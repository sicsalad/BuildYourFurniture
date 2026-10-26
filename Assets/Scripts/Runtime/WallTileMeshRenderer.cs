using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace BuildYourFurniture
{
    public static class WallTileMeshRenderer
    {
        static float N(XElement e, string key, float fallback = 0f)
        {
            float v;
            return float.TryParse((string)e.Attribute(key), NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : fallback;
        }
        static string S(XElement e, string key, string fallback = "") => (string)e.Attribute(key) ?? fallback;
        static IEnumerable<XElement> Items(XElement root, string group, string item)
        {
            var parent = root.Element(group);
            return parent == null ? Enumerable.Empty<XElement>() : parent.Elements(item);
        }
        static Material Material(Color color, Texture texture, float smoothness)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = new Material(shader);
            m.color = color;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            if (texture)
            {
                m.mainTexture = texture;
                if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", texture);
            }
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smoothness);
            return m;
        }
        static Texture2D TextureFor(string path)
        {
            var tex = CabinetAppearanceLibrary.Load(path);
            if (tex) return tex;
            if (path == "Textures/tuzzi-tile-base64")
            {
                var source = Resources.Load<TextAsset>(path);
                if (source)
                {
                    try
                    {
                        var decoded = new Texture2D(2, 2, TextureFormat.RGB24, false);
                        if (decoded.LoadImage(Convert.FromBase64String(source.text.Trim()))) return decoded;
                    }
                    catch (Exception e) { Debug.LogWarning("Wall texture decode failed: " + e.Message); }
                }
            }
            return null;
        }
        static void Quad(List<Vector3> verts, List<Vector2> uv, List<int> indices,
            Vector2 a, Vector2 along, Vector2 inward, float depth,
            float x0, float x1, float y0, float y1, float u0, float u1, float v0, float v1)
        {
            int n = verts.Count;
            Vector2 p0 = a + along * x0 + inward * depth;
            Vector2 p1 = a + along * x1 + inward * depth;
            verts.Add(new Vector3(p0.x, y0, p0.y));
            verts.Add(new Vector3(p1.x, y0, p1.y));
            verts.Add(new Vector3(p1.x, y1, p1.y));
            verts.Add(new Vector3(p0.x, y1, p0.y));
            uv.Add(new Vector2(u0,v0)); uv.Add(new Vector2(u1,v0));
            uv.Add(new Vector2(u1,v1)); uv.Add(new Vector2(u0,v1));
            // Front-facing toward the room (positive inward normal).
            indices.Add(n); indices.Add(n+1); indices.Add(n+2);
            indices.Add(n); indices.Add(n+2); indices.Add(n+3);
        }
        static void Emit(string name, List<Vector3> vertices, List<Vector2> uv,
            List<int> triangles, Material material, Action<GameObject> register)
        {
            if (vertices.Count == 0) return;
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            var mesh = new Mesh { name = name + "_mesh" };
            if (vertices.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(vertices); mesh.SetUVs(0, uv); mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            register(go);
        }
        public static void Build(XDocument plan, Action<GameObject> register)
        {
            if (plan == null || plan.Root == null) return;
            var root = plan.Root;
            var openings = Items(root,"doors","door").Concat(Items(root,"windows","window")).ToList();
            foreach (var finish in Items(root,"wallFinishes","finish").Where(f => S(f,"type") == "tile"))
            {
                float width = N(finish,"tileWidth",600) / 1000f;
                float height = N(finish,"tileHeight",297) / 1000f;
                float startY = N(finish,"startHeight",800) / 1000f;
                int rows = Mathf.Clamp(Mathf.RoundToInt(N(finish,"rows",5)),0,100);
                float grout = Mathf.Max(0,N(finish,"groutWidth",2)) / 1000f;
                float depth = N(finish,"thickness",8) / 1000f;
                if (width <= 0 || height <= 0 || rows <= 0) continue;
                Color groutColor, tileColor;
                if (!ColorUtility.TryParseHtmlString(S(finish,"groutColor","#B6B0A9"),out groutColor)) groutColor=Color.gray;
                if (!ColorUtility.TryParseHtmlString(S(finish,"color","#FFFFFF"),out tileColor)) tileColor=Color.white;
                var tileTexture = TextureFor(S(finish,"texture","Textures/tuzzi-tile-base64"));
                float textureUnit=Mathf.Max(50,N(finish,"textureSize",400))/1000f;
                bool repeatTexture=S(finish,"textureMode","stretch")=="tile";
                bool rotateTexture=N(finish,"textureRotation")>=45;
                var tileMat = Material(tileColor,tileTexture,N(finish,"smoothness",.7f));
                var groutMat = Material(groutColor,null,0);
                foreach (var wall in Items(root,"walls","wall"))
                {
                    string wallId = S(wall,"id");
                    string selectedWalls=S(finish,"wallIds");
                    if(!string.IsNullOrEmpty(selectedWalls)&&
                       !selectedWalls.Split(',').Any(id=>id.Trim()==wallId))continue;
                    Vector2 a = new Vector2(N(wall,"x"),N(wall,"y"))/1000f;
                    Vector2 b = new Vector2(N(wall,"x1"),N(wall,"y1"))/1000f;
                    Vector2 diff = b-a;
                    float length=diff.magnitude;
                    if (length < .001f) continue;
                    Vector2 along=diff/length;
                    Vector2 inward=new Vector2(-along.y,along.x);
                    if(S(finish,"side")=="reverse")inward=-inward;
                    var cuts=openings.Where(o=>S(o,"wallId")==wallId).ToList();
                    var gv=new List<Vector3>(); var guv=new List<Vector2>(); var gi=new List<int>();
                    var tv=new List<Vector3>(); var tuv=new List<Vector2>(); var ti=new List<int>();
                    // All coordinates are relative to the finished interior wall face (at the wall line).
                    // Grout lies 0.5 mm outside that face; tile faces are 8 mm further inside the room.
                    float groutDepth=.0005f, tileDepth=depth+.0015f;
                    for (int row=0; row<rows; row++)
                    {
                        float y0=startY+row*height, y1=Mathf.Min(y0+height,N(finish,"endHeight",startY*1000f+rows*height*1000f)/1000f);
                        if(y1<=y0)break;
                        float shift=(row%2)*Mathf.Repeat(N(finish,"stagger",0),1f)*width;
                        for (int col=-1; col<Mathf.CeilToInt(length/width)+1; col++)
                        {
                            float x0=Mathf.Max(0,col*width+shift), x1=Mathf.Min(length,(col+1)*width+shift);
                            if(x1<=x0)continue;
                            var xs=new List<float>{x0,x1};
                            var ys=new List<float>{y0,y1};
                            foreach(var o in cuts)
                            {
                                float l=N(o,"offset")/1000f, r=l+N(o,"width")/1000f;
                                float d=N(o,"bottom")/1000f, u=d+N(o,"height")/1000f;
                                if(l>x0 && l<x1)xs.Add(l);
                                if(r>x0 && r<x1)xs.Add(r);
                                if(d>y0 && d<y1)ys.Add(d);
                                if(u>y0 && u<y1)ys.Add(u);
                            }
                            xs.Sort();ys.Sort();
                            for(int i=0;i<xs.Count-1;i++)
                            for(int j=0;j<ys.Count-1;j++)
                            {
                                float sx0=xs[i],sx1=xs[i+1],sy0=ys[j],sy1=ys[j+1];
                                if(sx1-sx0<.00001f || sy1-sy0<.00001f)continue;
                                float mx=(sx0+sx1)*.5f,my=(sy0+sy1)*.5f;
                                if(cuts.Any(o=>mx>N(o,"offset")/1000f && mx<(N(o,"offset")+N(o,"width"))/1000f &&
                                    my>N(o,"bottom")/1000f && my<(N(o,"bottom")+N(o,"height"))/1000f))continue;
                                Quad(gv,guv,gi,a,along,inward,groutDepth,sx0,sx1,sy0,sy1,0,1,0,1);
                                // Grout is only removed at the original tile borders, not at opening cuts.
                                float tx0=Mathf.Max(sx0,x0+grout*.5f);
                                float tx1=Mathf.Min(sx1,x1-grout*.5f);
                                float ty0=Mathf.Max(sy0,y0+grout*.5f);
                                float ty1=Mathf.Min(sy1,y1-grout*.5f);
                                if(tx1<=tx0 || ty1<=ty0)continue;
                                Quad(tv,tuv,ti,a,along,inward,tileDepth,tx0,tx1,ty0,ty1,
                                    rotateTexture?(ty0-y0)/height*(repeatTexture?height/textureUnit:1):(tx0-x0)/width*(repeatTexture?width/textureUnit:1),
                                    rotateTexture?(ty1-y0)/height*(repeatTexture?height/textureUnit:1):(tx1-x0)/width*(repeatTexture?width/textureUnit:1),
                                    rotateTexture?(tx0-x0)/width*(repeatTexture?width/textureUnit:1):(ty0-y0)/height*(repeatTexture?height/textureUnit:1),
                                    rotateTexture?(tx1-x0)/width*(repeatTexture?width/textureUnit:1):(ty1-y0)/height*(repeatTexture?height/textureUnit:1));
                            }
                        }
                    }
                    Emit("WallGrout_"+wallId,gv,guv,gi,groutMat,register);
                    Emit(S(finish,"id","Tile")+"_"+wallId,tv,tuv,ti,tileMat,register);
                }
            }
        }
    }
}
