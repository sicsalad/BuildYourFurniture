using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using UnityEngine;

namespace BuildYourFurniture
{
    public static class PlanGeometryRenderer
    {
        static float N(XElement e, string key, float fallback = 0f)
        {
            float value;
            return float.TryParse((string)e.Attribute(key), NumberStyles.Float,
                CultureInfo.InvariantCulture, out value) ? value : fallback;
        }
        static string S(XElement e, string key, string fallback = "") => (string)e.Attribute(key) ?? fallback;
        static IEnumerable<XElement> Items(XElement root, string group, string name)
        {
            var parent = root.Element(group);
            return parent == null ? Enumerable.Empty<XElement>() : parent.Elements(name);
        }
        static Color GetColor(XElement e, Color fallback)
        {
            Color color;
            return ColorUtility.TryParseHtmlString(S(e, "color"), out color) ? color : fallback;
        }
        static bool Inside(float x, float a, float b) => x >= a - 0.00001f && x <= b + 0.00001f;

        public static void Build(XDocument plan, Action<string, Vector3, Vector3, Color, float> cube)
        {
            if (plan == null || plan.Root == null) return;
            XElement root = plan.Root;
            var openings = Items(root, "doors", "door").Concat(Items(root, "windows", "window")).ToList();
            foreach (var wall in Items(root, "walls", "wall"))
            {
                string wallId = S(wall, "id");
                Vector2 a = new Vector2(N(wall, "x"), N(wall, "y")) / 1000f;
                Vector2 b = new Vector2(N(wall, "x1"), N(wall, "y1")) / 1000f;
                Vector2 delta = b - a;
                float length = delta.magnitude;
                if (length < .0001f) continue;
                Vector2 along = delta / length;
                Vector2 inward = new Vector2(-along.y, along.x);
                float angle = -Mathf.Atan2(along.y, along.x) * Mathf.Rad2Deg;
                float thickness = N(wall, "thickness", 120f) / 1000f;
                float bottom = N(wall, "heightStart") / 1000f;
                float top = N(wall, "heightEnd", 3000f) / 1000f;
                var cuts = openings.Where(o => S(o, "wallId") == wallId).ToList();
                var xs = new List<float> { 0f, length };
                var ys = new List<float> { bottom, top };
                foreach (var o in cuts)
                {
                    float x0 = N(o, "offset") / 1000f, x1 = x0 + N(o, "width") / 1000f;
                    float y0 = N(o, "bottom") / 1000f, y1 = y0 + N(o, "height") / 1000f;
                    if (x0 < 0 || x1 > length + .0001f || y0 < bottom || y1 > top + .0001f)
                    {
                        Debug.LogWarning("Opening outside wall: " + S(o, "id"));
                        continue;
                    }
                    xs.Add(x0); xs.Add(x1); ys.Add(y0); ys.Add(y1);
                }
                xs.Sort(); ys.Sort();
                for (int i = 0; i < xs.Count - 1; i++)
                for (int j = 0; j < ys.Count - 1; j++)
                {
                    float x0 = xs[i], x1 = xs[i + 1], y0 = ys[j], y1 = ys[j + 1];
                    if (x1 - x0 < .0001f || y1 - y0 < .0001f) continue;
                    float mx = (x0 + x1) * .5f, my = (y0 + y1) * .5f;
                    if (cuts.Any(o =>
                        Inside(mx, N(o, "offset") / 1000f, (N(o, "offset") + N(o, "width")) / 1000f) &&
                        Inside(my, N(o, "bottom") / 1000f, (N(o, "bottom") + N(o, "height")) / 1000f))) continue;
                    Vector2 p = a + along * mx - inward * thickness * .5f;
                    cube(wallId + "_segment", new Vector3(p.x, my, p.y),
                        new Vector3(x1 - x0, y1 - y0, thickness), Color.white, angle);
                }
                foreach (var o in cuts)
                {
                    if (S(o, "fill", "empty") != "solid") continue;
                    float x = (N(o, "offset") + N(o, "width") * .5f) / 1000f;
                    float y = (N(o, "bottom") + N(o, "height") * .5f) / 1000f;
                    Vector2 p = a + along * x - inward * thickness * .5f;
                    cube(S(o, "id"), new Vector3(p.x, y, p.y),
                        new Vector3(N(o, "width") / 1000f, N(o, "height") / 1000f, .035f),
                        GetColor(o, new Color(.55f, .75f, .85f)), angle);
                }

            }
            RoomDetailRenderer.Build(plan, cube);
            FurnitureModelRenderer.Build(plan, cube);
        }
    }
}
