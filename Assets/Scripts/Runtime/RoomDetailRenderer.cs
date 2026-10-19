using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using UnityEngine;

namespace BuildYourFurniture
{
    // Geometry is generated at runtime; no imported prefab is required.
    public static class RoomDetailRenderer
    {
        static float N(XElement e, string key, float fallback = 0f)
        {
            float n;
            return float.TryParse((string)e.Attribute(key), NumberStyles.Float, CultureInfo.InvariantCulture, out n) ? n : fallback;
        }
        static string S(XElement e, string key, string fallback = "") => (string)e.Attribute(key) ?? fallback;
        static IEnumerable<XElement> Items(XElement root, string group, string item)
        {
            var parent = root.Element(group);
            return parent == null ? Enumerable.Empty<XElement>() : parent.Elements(item);
        }
        static bool Intersects(float a0, float a1, float b0, float b1) => a0 < b1 - .00001f && a1 > b0 + .00001f;
        static Color TileColor(int column, int row)
        {
            // Subtle warm-white cement variation, deterministic across rebuilds.
            float noise = Mathf.PerlinNoise(column * .71f + 3.1f, row * .83f + 2.4f);
            float v = Mathf.Lerp(.85f, .98f, noise);
            return new Color(v, v * .989f, v * .969f);
        }

        public static void Build(XDocument plan, Action<string, Vector3, Vector3, Color, float> cube)
        {
            if (plan == null || plan.Root == null) return;
            var root = plan.Root;
            var cuts = Items(root, "doors", "door").Concat(Items(root, "windows", "window")).ToList();
            var finish = Items(root, "wallFinishes", "finish").FirstOrDefault(f => S(f, "type") == "tile");
            foreach (var wall in Items(root, "walls", "wall"))
            {
                string wallId = S(wall, "id");
                Vector2 a = new Vector2(N(wall, "x"), N(wall, "y")) / 1000f;
                Vector2 b = new Vector2(N(wall, "x1"), N(wall, "y1")) / 1000f;
                Vector2 delta = b - a;
                float length = delta.magnitude;
                if (length < .00001f) continue;
                Vector2 along = delta / length;
                Vector2 normal = new Vector2(-along.y, along.x);
                float angle = -Mathf.Atan2(along.y, along.x) * Mathf.Rad2Deg;
                float wallThickness = N(wall, "thickness", 120) / 1000f;
                var openings = cuts.Where(o => S(o, "wallId") == wallId).ToList();

                if (finish != null)
                {
                    float tileWidth = N(finish, "tileWidth", 600f) / 1000f;
                    float tileHeight = N(finish, "tileHeight", 297f) / 1000f;
                    float tileDepth = N(finish, "thickness", 8f) / 1000f;
                    float startHeight = N(finish, "startHeight", 800f) / 1000f;
                    int rows = Mathf.Clamp(Mathf.RoundToInt(N(finish, "rows", 5)), 0, 50);
                    if (tileWidth > 0f && tileHeight > 0f)
                    for (int row = 0; row < rows; row++)
                    {
                        float y0 = startHeight + row * tileHeight;
                        float y1 = y0 + tileHeight;
                        int columns = Mathf.CeilToInt(length / tileWidth);
                        for (int col = 0; col < columns; col++)
                        {
                            float x0 = col * tileWidth, x1 = Mathf.Min(length, x0 + tileWidth);
                            // Split tile at all opening edges, preserving the opening itself.
                            var edges = new List<float> { x0, x1 };
                            foreach (var o in openings)
                            {
                                float left = N(o, "offset") / 1000f;
                                float right = left + N(o, "width") / 1000f;
                                if (left > x0 && left < x1) edges.Add(left);
                                if (right > x0 && right < x1) edges.Add(right);
                            }
                            edges.Sort();
                            for (int k = 0; k < edges.Count - 1; k++)
                            {
                                float sx0 = edges[k], sx1 = edges[k + 1];
                                if (sx1 - sx0 < .001f) continue;
                                float middle = (sx0 + sx1) * .5f;
                                var verticalEdges = new List<float> { y0, y1 };
                                foreach (var o in openings)
                                {
                                    float left = N(o, "offset") / 1000f;
                                    float right = left + N(o, "width") / 1000f;
                                    if (middle < left || middle > right) continue;
                                    float lower = N(o, "bottom") / 1000f;
                                    float upper = lower + N(o, "height") / 1000f;
                                    if (lower > y0 && lower < y1) verticalEdges.Add(lower);
                                    if (upper > y0 && upper < y1) verticalEdges.Add(upper);
                                }
                                verticalEdges.Sort();
                                for (int v = 0; v < verticalEdges.Count - 1; v++)
                                {
                                    float sy0 = verticalEdges[v], sy1 = verticalEdges[v + 1];
                                    if (sy1 - sy0 < .001f) continue;
                                    float ym = (sy0 + sy1) * .5f;
                                    if (openings.Any(o => Intersects(sx0, sx1, N(o, "offset") / 1000f,
                                        (N(o, "offset") + N(o, "width")) / 1000f) &&
                                        ym > N(o, "bottom") / 1000f &&
                                        ym < (N(o, "bottom") + N(o, "height")) / 1000f)) continue;
                                    float grout = .0025f;
                                    Vector2 p = a + along * ((sx0 + sx1) * .5f) + normal * (tileDepth * .5f + .001f);
                                    cube(S(finish, "id", "TILE") + "_" + wallId + "_" + row + "_" + col + "_" + k + "_" + v,
                                        new Vector3(p.x, (sy0 + sy1) * .5f, p.y),
                                        new Vector3(Mathf.Max(.001f, sx1 - sx0 - grout), Mathf.Max(.001f, sy1 - sy0 - grout), tileDepth),
                                        TileColor(col, row), angle);
                                }
                            }
                        }
                    }
                }

                foreach (var radiator in Items(root, "radiators", "radiator").Where(r => S(r, "wallId") == wallId))
                {
                    float width = N(radiator, "length", 1200f) / 1000f;
                    float height = N(radiator, "height", 730f) / 1000f;
                    float depth = N(radiator, "depth", 210f) / 1000f;
                    float left = N(radiator, "offset") / 1000f;
                    float bottom = N(radiator, "bottom") / 1000f;
                    float center = left + width * .5f;
                    string id = S(radiator, "id", "RAD");
                    Color cream = new Color(.84f, .82f, .75f);
                    int sections = Mathf.Max(2, Mathf.RoundToInt(N(radiator, "sections", 12)));
                    float pitch = width / sections;
                    Action<string, float, float, float, float, float, float, Color> part =
                        (suffix, x, y, z, sx, sy, sz, color) =>
                        {
                            Vector2 pos = a + along * x + normal * (wallThickness * .5f + .025f + z);
                            cube(id + "_" + suffix, new Vector3(pos.x, y, pos.y),
                                new Vector3(sx, sy, sz), color, angle);
                        };
                    // Each column consists of a vertical cast-iron section with thicker rounded-looking ends.
                    for (int i = 0; i < sections; i++)
                    {
                        float x = left + pitch * (i + .5f);
                        float rib = Mathf.Min(pitch * .69f, .068f);
                        part("rib_" + i, x, bottom + height * .5f, depth * .5f, rib, height * .91f, depth * .62f, cream);
                        part("front_" + i, x, bottom + height * .5f, depth * .87f, rib * .77f, height * .81f, depth * .20f, cream);
                        part("top_" + i, x, bottom + height * .96f, depth * .5f, rib * 1.13f, height * .08f, depth * .86f, cream);
                        part("foot_" + i, x, bottom + height * .04f, depth * .5f, rib * 1.07f, height * .08f, depth * .84f, cream);
                    }
                    part("top_pipe", center, bottom + height * .89f, depth * .42f, width * .96f, .032f, .032f, cream);
                    part("bottom_pipe", center, bottom + height * .11f, depth * .42f, width * .96f, .032f, .032f, cream);
                    part("valve_left", left - .027f, bottom + height * .88f, depth * .5f, .052f, .05f, .05f, cream);
                    part("valve_right", left + width + .027f, bottom + height * .88f, depth * .5f, .052f, .05f, .05f, cream);
                }
            }
        }
    }
}
