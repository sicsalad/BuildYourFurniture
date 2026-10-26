using System;
using System.Globalization;
using System.Xml.Linq;
using UnityEngine;

namespace BuildYourFurniture
{
    // A single combined horizontal mesh per floor finish, with correctly oriented UVs.
    // The grout is one uninterrupted surface underneath, not cube side faces.
    public static class FloorTileRenderer
    {
        static float N(XElement e, string key, float fallback = 0f)
        {
            float n;
            return float.TryParse((string)e.Attribute(key), NumberStyles.Float, CultureInfo.InvariantCulture, out n) ? n : fallback;
        }
        static string S(XElement e, string key, string fallback = "") => (string)e.Attribute(key) ?? fallback;

        public static void Build(XDocument plan, Action<GameObject> register)
        {
            if (plan == null || plan.Root == null) return;
            var finishes = plan.Root.Element("floorFinishes");
            var rooms = plan.Root.Element("rooms");
            if (finishes == null || rooms == null) return;
            foreach (var finish in finishes.Elements("finish"))
            foreach (var room in rooms.Elements("room"))
            {
                if (S(room, "id") != S(finish, "roomId")) continue;
                float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
                foreach (var point in room.Elements("point"))
                {
                    float x = N(point, "x") / 1000f, z = N(point, "y") / 1000f;
                    minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x);
                    minZ = Mathf.Min(minZ, z); maxZ = Mathf.Max(maxZ, z);
                }
                if (maxX <= minX || maxZ <= minZ) continue;
                bool alongX = S(finish, "direction", "x").ToLowerInvariant() != "y";
                float tileLong = N(finish, "tileWidth", 600f) / 1000f;
                float tileShort = N(finish, "tileHeight", 200f) / 1000f;
                float thickness = N(finish, "thickness", 8.3f) / 1000f;
                float grout = Mathf.Max(0f, N(finish, "groutWidth", 2f)) / 1000f;
                float stagger = Mathf.Repeat(N(finish, "stagger", .5f), 1f);
                if (tileLong <= .001f || tileShort <= .001f) continue;
                Color groutColor;
                if (!ColorUtility.TryParseHtmlString(S(finish, "groutColor", "#B6B0A9"), out groutColor))
                    groutColor = Color.gray;
                Color tileColor;
                if (!ColorUtility.TryParseHtmlString(S(finish, "color", "#FFFFFF"), out tileColor))
                    tileColor = Color.white;
                float y = N(room, "floorHeight") / 1000f + thickness;
                Material groutMat = MakeMaterial(groutColor, null, 0f);
                // Top of grout is below the tile surface by 1 mm, avoiding depth fighting.
                AddQuad("FloorGrout_" + S(room, "id"), minX, maxX, minZ, maxZ,
                    y - .001f, Vector2.zero, Vector2.one, groutMat, register);
                Texture2D texture = Resources.Load<Texture2D>(S(finish, "texture", "Textures/helios"));
                if (!texture) Debug.LogWarning("Floor texture missing: " + S(finish, "texture", "Textures/helios"));
                Material tileMat = MakeMaterial(tileColor, texture, N(finish, "smoothness", .12f));
                RemoteSurfaceTexture.Attach(tileMat,S(finish,"textureUrl"),register);
                float mainMin = alongX ? minX : minZ, mainMax = alongX ? maxX : maxZ;
                float crossMin = alongX ? minZ : minX, crossMax = alongX ? maxZ : maxX;
                int row = 0;
                for (float cross = crossMin; cross < crossMax - .0001f; cross += tileShort, row++)
                {
                    float crossEnd = Mathf.Min(crossMax, cross + tileShort);
                    float shift = row % 2 == 0 ? 0f : tileLong * stagger;
                    int first = Mathf.FloorToInt(-shift / tileLong);
                    for (int col = first; ; col++)
                    {
                        float rawStart = mainMin + shift + col * tileLong;
                        if (rawStart >= mainMax - .0001f) break;
                        float rawEnd = rawStart + tileLong;
                        float start = Mathf.Max(mainMin, rawStart), end = Mathf.Min(mainMax, rawEnd);
                        if (end - start <= grout) continue;
                        // Half the grout on each edge, with cut tiles clipped at room boundary.
                        float s0 = start + (start > mainMin + .0001f ? grout * .5f : 0f);
                        float s1 = end - (end < mainMax - .0001f ? grout * .5f : 0f);
                        float t0 = cross + (cross > crossMin + .0001f ? grout * .5f : 0f);
                        float t1 = crossEnd - (crossEnd < crossMax - .0001f ? grout * .5f : 0f);
                        if (s1 <= s0 || t1 <= t0) continue;
                        float x0 = alongX ? s0 : t0, x1 = alongX ? s1 : t1;
                        float z0 = alongX ? t0 : s0, z1 = alongX ? t1 : s1;
                        // UVs correspond to the uncut original tile, not the clipped fragment.
                        float u0 = (s0 - rawStart) / tileLong, u1 = (s1 - rawStart) / tileLong;
                        float v0 = (t0 - cross) / tileShort, v1 = (t1 - cross) / tileShort;
                        AddQuad(S(finish, "id", "Floor") + "_" + row + "_" + col,
                            x0, x1, z0, z1, y, new Vector2(u0, v0), new Vector2(u1, v1),
                            tileMat, register, !alongX);
                    }
                }
            }
        }

        static Material MakeMaterial(Color color, Texture2D texture, float smoothness)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material = new Material(shader);
            material.color = color;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (texture)
            {
                material.mainTexture = texture;
                if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
            }
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
            if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", smoothness);
            return material;
        }

        static void AddQuad(string name, float x0, float x1, float z0, float z1, float y,
            Vector2 uv0, Vector2 uv1, Material material, Action<GameObject> register, bool swapUv = false)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            var mesh = new Mesh();
            mesh.name = name + "_mesh";
            mesh.vertices = new[]
            {
                new Vector3(x0,y,z0), new Vector3(x0,y,z1),
                new Vector3(x1,y,z1), new Vector3(x1,y,z0)
            };
            mesh.triangles = new[] { 0,1,2, 0,2,3 };
            mesh.uv = swapUv
                ? new[] { new Vector2(uv0.y,uv0.x), new Vector2(uv1.y,uv0.x),
                          new Vector2(uv1.y,uv1.x), new Vector2(uv0.y,uv1.x) }
                : new[] { new Vector2(uv0.x,uv0.y), new Vector2(uv0.x,uv1.y),
                          new Vector2(uv1.x,uv1.y), new Vector2(uv1.x,uv0.y) };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            register(go);
        }
    }
}
