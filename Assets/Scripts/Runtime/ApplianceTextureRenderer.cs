using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace BuildYourFurniture
{
    // Photo-textured appliance fronts and countertop / hob tops.
    // Geometry, orientation, dimensions and texture names come from plan XML.
    public static class ApplianceTextureRenderer
    {
        static readonly Dictionary<string, Texture2D> TextureCache = new Dictionary<string, Texture2D>();
        static float N(XElement e,string key,float fallback=0f)
        {
            float v;
            return float.TryParse((string)e.Attribute(key),NumberStyles.Float,CultureInfo.InvariantCulture,out v)?v:fallback;
        }
        static string S(XElement e,string key,string fallback="") => (string)e.Attribute(key) ?? fallback;
        static Color C(XElement e,string key,string fallback)
        {
            Color v;
            return ColorUtility.TryParseHtmlString(S(e,key,fallback),out v)?v:Color.white;
        }
        static Texture2D LoadTexture(string resource)
        {
            if (TextureCache.ContainsKey(resource)) return TextureCache[resource];
            var tex = Resources.Load<Texture2D>(resource);
            if (!tex)
            {
                var encoded = Resources.Load<TextAsset>(resource);
                if (encoded)
                {
                    try
                    {
                        tex = new Texture2D(2,2,TextureFormat.RGB24,true);
                        if (!tex.LoadImage(Convert.FromBase64String(encoded.text.Trim())))
                        {
                            UnityEngine.Object.Destroy(tex);
                            tex=null;
                        }
                        else
                        {
                            tex.filterMode=FilterMode.Bilinear;
                            tex.wrapMode=TextureWrapMode.Clamp;
                        }
                    }
                    catch(Exception e)
                    {
                        Debug.LogWarning("Appliance texture "+resource+": "+e.Message);
                        if (tex) UnityEngine.Object.Destroy(tex);
                        tex=null;
                    }
                }
            }
            if (!tex) Debug.LogWarning("Appliance texture not found: "+resource);
            TextureCache[resource]=tex;
            return tex;
        }
        static Material Mat(Color color, Texture2D texture, float smoothness)
        {
            var shader=Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var mat=new Material(shader);
            mat.color=color;
            if(mat.HasProperty("_BaseColor"))mat.SetColor("_BaseColor",color);
            if(texture)
            {
                mat.mainTexture=texture;
                if(mat.HasProperty("_BaseMap"))mat.SetTexture("_BaseMap",texture);
            }
            if(mat.HasProperty("_Smoothness"))mat.SetFloat("_Smoothness",smoothness);
            if(mat.HasProperty("_Glossiness"))mat.SetFloat("_Glossiness",smoothness);
            if(mat.HasProperty("_Cull"))mat.SetFloat("_Cull",0f);
            return mat;
        }
        static void Face(string name,Vector3[] vertices,Material material,Action<GameObject> register)
        {
            var go=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer));
            var mesh=new Mesh();
            mesh.vertices=vertices;
            mesh.uv=new[]{new Vector2(0,0),new Vector2(1,0),new Vector2(1,1),new Vector2(0,1)};
            mesh.triangles=new[]{0,1,2,0,2,3,2,1,0,3,2,0};
            mesh.RecalculateNormals();mesh.RecalculateBounds();
            go.GetComponent<MeshFilter>().sharedMesh=mesh;
            var renderer=go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial=material;
            renderer.shadowCastingMode=ShadowCastingMode.Off;
            renderer.receiveShadows=false;
            register(go);
        }
        public static void Build(XDocument plan,Action<GameObject> register)
        {
            if(plan==null || plan.Root==null)return;
            var models=plan.Root.Element("models");
            var furniture=plan.Root.Element("furniture");
            if(models==null || furniture==null)return;
            foreach(var item in furniture.Elements("item"))
            {
                var model=models.Elements("model").FirstOrDefault(m=>S(m,"id")==S(item,"modelId"));
                if(model==null)continue;
                string type=S(model,"type");
                if(type!="texturedAppliance" && type!="texturedHob" && type!="texturedCountertop")continue;
                float w=N(model,"width",600)/1000f;
                float h=N(model,"height",600)/1000f;
                float d=N(model,"depth",600)/1000f;
                float x=N(item,"x")/1000f;
                float z=N(item,"y")/1000f;
                float bottom=N(item,"bottom")/1000f;
                Quaternion rotation=Quaternion.Euler(0,N(item,"rotation"),0);
                Vector3 center=new Vector3(x,bottom+h*.5f,z);
                Func<Vector3,Vector3> transform=p=>center+rotation*p;
                var texture=LoadTexture(S(model,"texture"));
                var mat=Mat(C(model,"textureTint","#FFFFFF"),texture,N(model,"smoothness",.25f));
                if(type=="texturedAppliance")
                {
                    float front=-d*.5f-.002f;
                    Face(S(item,"id")+"_front",new[]{
                        transform(new Vector3(-w*.5f,-h*.5f,front)),
                        transform(new Vector3(w*.5f,-h*.5f,front)),
                        transform(new Vector3(w*.5f,h*.5f,front)),
                        transform(new Vector3(-w*.5f,h*.5f,front))
                    },mat,register);
                }
                else
                {
                    float top=h*.5f+.002f;
                    Face(S(item,"id")+"_top",new[]{
                        transform(new Vector3(-w*.5f,top,-d*.5f)),
                        transform(new Vector3(w*.5f,top,-d*.5f)),
                        transform(new Vector3(w*.5f,top,d*.5f)),
                        transform(new Vector3(-w*.5f,top,d*.5f))
                    },mat,register);
                }
            }
        }
    }
}
