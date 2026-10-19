using System;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using UnityEngine;

namespace BuildYourFurniture
{
    // Parametric fridge: all dimensions, materials, position and orientation come from plan XML.
    public static class FurnitureModelRenderer
    {
        static float N(XElement e, string key, float fallback = 0f)
        {
            float value;
            return float.TryParse((string)e.Attribute(key), NumberStyles.Float,
                CultureInfo.InvariantCulture, out value) ? value : fallback;
        }
        static string S(XElement e, string key, string fallback = "") => (string)e.Attribute(key) ?? fallback;
        static Color C(XElement e, string key, string fallback)
        {
            Color result;
            return ColorUtility.TryParseHtmlString(S(e, key, fallback), out result) ? result : Color.gray;
        }
        public static void Build(XDocument plan, Action<string, Vector3, Vector3, Color, float> cube)
        {
            if (plan == null || plan.Root == null) return;
            var models = plan.Root.Element("models");
            var furniture = plan.Root.Element("furniture");
            if (models == null || furniture == null) return;
            foreach (var item in furniture.Elements("item"))
            {
                string modelId = S(item, "modelId");
                var model = models.Elements("model").FirstOrDefault(m => S(m, "id") == modelId);
                if (model == null) continue;
                string modelType = S(model, "type");
                if (modelType == "texturedAppliance" || modelType == "texturedHob" || modelType == "texturedCountertop")
                {
                    float width = N(model, "width", 600f) / 1000f;
                    float height = N(model, "height", 600f) / 1000f;
                    float depth = N(model, "depth", 600f) / 1000f;
                    float baseHeight = N(item, "bottom") / 1000f;
                    Color tint = C(model, "color", "#333333");
                    cube(S(item, "id") + "_body",
                        new Vector3(N(item, "x") / 1000f, baseHeight + height * .5f, N(item, "y") / 1000f),
                        new Vector3(width, height, depth), tint, N(item, "rotation"));
                    continue;
                }
                if (modelType != "bottomFreezerRefrigerator") continue;
                string id = S(item, "id", "Fridge");
                float w = N(model, "width", 550) / 1000f;
                float h = N(model, "height", 1824) / 1000f;
                float d = N(model, "depth", 557) / 1000f;
                float x = N(item, "x") / 1000f, z = N(item, "y") / 1000f;
                float angle = N(item, "rotation", 0);
                float rad = angle * Mathf.Deg2Rad;
                Color body = C(model, "color", "#77777B");
                Color sides = C(model, "sideColor", "#66666B");
                Color handle = C(model, "handleColor", "#252529");
                Color feet = C(model, "footColor", "#29292B");
                // Local X = width, local Z = depth; negative Z is the door/front.
                Action<string, float, float, float, float, float, float, Color> part =
                    (name, px, py, pz, sx, sy, sz, color) =>
                    {
                        float worldX = x + Mathf.Cos(rad) * px + Mathf.Sin(rad) * pz;
                        float worldZ = z - Mathf.Sin(rad) * px + Mathf.Cos(rad) * pz;
                        cube(id + "_" + name, new Vector3(worldX, py, worldZ),
                            new Vector3(sx, sy, sz), color, angle);
                    };
                part("cabinet", 0, h*.5f, .008f, w, h-.022f, d-.018f, sides);
                float doorDepth = .014f;
                float front = -d*.5f - doorDepth*.5f + .006f;
                float upper = N(model, "topDoorHeight", 940)/1000f;
                float lower = N(model, "bottomDoorHeight", 800)/1000f;
                float gap = N(model, "doorGap", 22)/1000f;
                float bottom = .025f;
                part("freezer_door", 0, bottom+lower*.5f, front, w-.014f, lower, doorDepth, body);
                float upperBottom = bottom+lower+gap;
                part("fridge_door", 0, upperBottom+upper*.5f, front, w-.014f, upper, doorDepth, body);
                // Recessed dark separation between the two doors, as in the reference.
                part("door_recess", 0, bottom+lower+gap*.5f, front+.006f, w-.01f, gap, .003f, handle);
                part("lower_handle", 0, bottom+lower-.025f, front-.008f, w*.84f, .008f, .008f, handle);
                part("upper_handle", 0, upperBottom+.025f, front-.008f, w*.84f, .008f, .008f, handle);
                part("top_edge", 0, h-.007f, 0, w, .014f, d, sides);
                for (int i=0;i<4;i++)
                    part("foot_"+i, (i%2==0?-1:1)*(w*.5f-.045f), .014f,
                        (i<2?-1:1)*(d*.5f-.055f), .035f, .028f, .035f, feet);
            }
        }
    }
}
