using System;
using System.Globalization;
using System.Xml.Linq;
using UnityEngine;

namespace BuildYourFurniture
{
    public static class FloorTileRenderer
    {
        static float N(XElement e, string key, float fallback = 0f)
        {
            float n;
            return float.TryParse((string)e.Attribute(key), NumberStyles.Float,
                CultureInfo.InvariantCulture, out n) ? n : fallback;
        }
        static string S(XElement e, string key, string fallback = "") => (string)e.Attribute(key) ?? fallback;
        public static void Build(XDocument plan, Action<string, Vector3, Vector3, Color, float> cube)
        {
            if (plan == null || plan.Root == null) return;
            var finishes = plan.Root.Element("floorFinishes");
            if (finishes == null) return;
            var rooms = plan.Root.Element("rooms");
            if (rooms == null) return;
            foreach (var finish in finishes.Elements("finish"))
            {
                var room = rooms.Elements("room");
                foreach (var r in room)
                {
                    if (S(r, "id") != S(finish, "roomId")) continue;
                    float minX = float.MaxValue, maxX = float.MinValue;
                    float minY = float.MaxValue, maxY = float.MinValue;
                    foreach (var point in r.Elements("point"))
                    {
                        float x = N(point, "x") / 1000f, y = N(point, "y") / 1000f;
                        minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x);
                        minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y);
                    }
                    if (maxX <= minX || maxY <= minY) continue;
                    bool xDirection = S(finish, "direction", "x").ToLowerInvariant() != "y";
                    float longSide = N(finish, "tileWidth", 600) / 1000f;
                    float shortSide = N(finish, "tileHeight", 200) / 1000f;
                    float thickness = N(finish, "thickness", 8.3f) / 1000f;
                    float grout = Mathf.Max(0, N(finish, "groutWidth", 2f)) / 1000f;
                    float stagger = Mathf.Repeat(N(finish, "stagger", .5f), 1f);
                    Color groutColor;
                    if (!ColorUtility.TryParseHtmlString(S(finish, "groutColor", "#AAA59F"), out groutColor))
                        groutColor = Color.gray;
                    // Entire room is covered by a grout backing at the tile underside.
                    cube("FloorGrout_" + S(r, "id"), new Vector3((minX + maxX) / 2, thickness * .16f, (minY + maxY) / 2),
                        new Vector3(maxX - minX, thickness * .32f, maxY - minY), groutColor, 0);
                    float mainMin = xDirection ? minX : minY;
                    float mainMax = xDirection ? maxX : maxY;
                    float crossMin = xDirection ? minY : minX;
                    float crossMax = xDirection ? maxY : maxX;
                    int row = 0;
                    for (float cross = crossMin; cross < crossMax - .0001f; cross += shortSide, row++)
                    {
                        float endCross = Mathf.Min(crossMax, cross + shortSide);
                        float shift = (row % 2) * longSide * stagger;
                        int first = Mathf.FloorToInt(-shift / longSide);
                        for (int col = first; ; col++)
                        {
                            float start = mainMin + shift + col * longSide;
                            if (start >= mainMax - .0001f) break;
                            float end = Mathf.Min(mainMax, start + longSide);
                            start = Mathf.Max(mainMin, start);
                            if (end - start <= .0001f) continue;
                            float length = Mathf.Max(.0005f, end - start - grout);
                            float width = Mathf.Max(.0005f, endCross - cross - grout);
                            float centerMain = (start + end) / 2f;
                            float centerCross = (cross + endCross) / 2f;
                            Vector3 position = xDirection
                                ? new Vector3(centerMain, thickness * .65f, centerCross)
                                : new Vector3(centerCross, thickness * .65f, centerMain);
                            Vector3 size = xDirection
                                ? new Vector3(length, thickness * .70f, width)
                                : new Vector3(width, thickness * .70f, length);
                            cube("FL01_" + row + "_" + col, position, size, Color.white, 0);
                        }
                    }
                }
            }
        }
    }
}
