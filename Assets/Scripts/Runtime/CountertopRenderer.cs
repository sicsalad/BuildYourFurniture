using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace BuildYourFurniture
{
    // Countertop meshes are subdivided at rectangular fixture openings.
    // Each top surface is a continuous UV mapping, so the wood grain does not restart per segment.
    public static class CountertopRenderer
    {
        static float N(XElement e,string key,float fallback=0)
        {
            float v;
            return e!=null && float.TryParse((string)e.Attribute(key),NumberStyles.Float,CultureInfo.InvariantCulture,out v)?v:fallback;
        }
        static string S(XElement e,string key,string fallback="")=>e==null?fallback:(string)e.Attribute(key)??fallback;
        static float M(float v)=>v/1000f;
        static Color C(string s,Color fallback){Color c;return ColorUtility.TryParseHtmlString(s,out c)?c:fallback;}
        static Texture2D Load(string path)
        {
            if(string.IsNullOrEmpty(path))return null;
            var tex=Resources.Load<Texture2D>(path);
            if(tex)return tex;
            var asset=Resources.Load<TextAsset>(path);
            if(!asset)return null;
            try
            {
                tex=new Texture2D(2,2,TextureFormat.RGBA32,true);
                if(tex.LoadImage(Convert.FromBase64String(asset.text.Trim())))
                {tex.wrapMode=TextureWrapMode.Repeat;tex.filterMode=FilterMode.Trilinear;return tex;}
                UnityEngine.Object.Destroy(tex);
            }
            catch(Exception ex){Debug.LogWarning("Countertop texture "+path+": "+ex.Message);}
            return null;
        }
        static Material Material(Color color,Texture2D tex,float smoothness)
        {
            var shader=Shader.Find("Universal Render Pipeline/Lit")??Shader.Find("Standard");
            var mat=new Material(shader);
            mat.color=color;
            if(mat.HasProperty("_BaseColor"))mat.SetColor("_BaseColor",color);
            if(tex){mat.mainTexture=tex;if(mat.HasProperty("_BaseMap"))mat.SetTexture("_BaseMap",tex);}
            if(mat.HasProperty("_Smoothness"))mat.SetFloat("_Smoothness",smoothness);
            if(mat.HasProperty("_Glossiness"))mat.SetFloat("_Glossiness",smoothness);
            return mat;
        }
        static void MeshObject(string name,Vector3[] v,Vector2[] uv,int[] triangles,Material mat,Action<GameObject> register)
        {
            var go=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer));
            var mesh=new Mesh{vertices=v,uv=uv,triangles=triangles};
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            go.GetComponent<MeshFilter>().sharedMesh=mesh;
            var r=go.GetComponent<MeshRenderer>();
            r.sharedMaterial=mat;
            r.shadowCastingMode=ShadowCastingMode.Off;
            r.receiveShadows=false;
            register(go);
        }
        static void Quad(string name,Func<float,float,float,Vector3> tr,
            float x0,float z0,float x1,float z1,float y,Material mat,
            float fullW,float fullD,Action<GameObject> register)
        {
            MeshObject(name,new[]{tr(x0,y,z0),tr(x0,y,z1),tr(x1,y,z1),tr(x1,y,z0)},
                new[]{new Vector2((x0+fullW*.5f)/fullW,(z0+fullD*.5f)/fullD),
                      new Vector2((x0+fullW*.5f)/fullW,(z1+fullD*.5f)/fullD),
                      new Vector2((x1+fullW*.5f)/fullW,(z1+fullD*.5f)/fullD),
                      new Vector2((x1+fullW*.5f)/fullW,(z0+fullD*.5f)/fullD)},
                new[]{0,1,2,0,2,3},mat,register);
        }
        // A fixture photo must fill its own rectangle, not inherit the whole countertop UVs.
        // textureRotation is clockwise in degrees when viewed from above.
        static void FixturePhoto(string name,Func<float,float,float,Vector3> tr,
            float x,float z,float width,float depth,float y,int rotation,Material mat,Action<GameObject> register)
        {
            int turns=((rotation%360)+360)%360/90;
            if(rotation%90!=0)Debug.LogWarning(name+": textureRotation must be 0, 90, 180 or 270.");
            var uv=new[]{new Vector2(0,0),new Vector2(0,1),new Vector2(1,1),new Vector2(1,0)};
            for(int i=0;i<uv.Length;i++)
            {
                Vector2 v=uv[i]-new Vector2(.5f,.5f);
                for(int t=0;t<turns;t++)v=new Vector2(v.y,-v.x);
                uv[i]=v+new Vector2(.5f,.5f);
            }
            MeshObject(name,new[]{
                tr(x-width*.5f,y,z-depth*.5f),tr(x-width*.5f,y,z+depth*.5f),
                tr(x+width*.5f,y,z+depth*.5f),tr(x+width*.5f,y,z-depth*.5f)},
                uv,new[]{0,1,2,0,2,3},mat,register);
        }
        struct Opening {public float x0,x1,z0,z1;public XElement fixture;}
        public static void Build(XDocument plan,Action<GameObject> register,
            Action<string,Vector3,Vector3,Color,float> cube)
        {
            var cabinetry=plan?.Root?.Element("cabinetry");
            if(cabinetry==null)return;
            var tops=cabinetry.Element("countertops");
            if(tops==null)return;
            var fixtures=cabinetry.Element("fixtures")?.Elements("fixture").ToList()??new List<XElement>();
            foreach(var top in tops.Elements("countertop"))
            {
                string id=S(top,"id");
                float w=M(N(top,"width")),d=M(N(top,"depth"));
                float thickness=M(N(top,"thickness",40)),bottom=M(N(top,"bottom",860));
                if(w<=0||d<=0||thickness<=0)continue;
                float angle=N(top,"rotation"),rad=angle*Mathf.Deg2Rad;
                float cx=M(N(top,"x")),cz=M(N(top,"y"));
                Func<float,float,float,Vector3> tr=(x,y,z)=>new Vector3(cx+Mathf.Cos(rad)*x+Mathf.Sin(rad)*z,y,cz-Mathf.Sin(rad)*x+Mathf.Cos(rad)*z);
                var cuts=new List<Opening>();
                foreach(var fixture in fixtures.Where(f=>S(f,"countertopId")==id && S(f,"cutout")=="true"))
                {
                    float fw=M(N(fixture,"cutoutWidth",N(fixture,"width",S(fixture,"type")=="sink"?750:560)));
                    float fd=M(N(fixture,"cutoutDepth",N(fixture,"depth",S(fixture,"type")=="sink"?480:490)));
                    float fx=M(N(fixture,"offsetX")),fz=M(N(fixture,"offsetY"));
                    if(fw<=0||fd<=0)continue;
                    var opening=new Opening{x0=fx-fw*.5f,x1=fx+fw*.5f,z0=fz-fd*.5f,z1=fz+fd*.5f,fixture=fixture};
                    if(opening.x0< -w*.5f||opening.x1>w*.5f||opening.z0< -d*.5f||opening.z1>d*.5f)
                    {Debug.LogWarning("Fixture opening outside countertop: "+S(fixture,"id"));continue;}
                    cuts.Add(opening);
                }
                var xs=new List<float>{-w*.5f,w*.5f};
                var zs=new List<float>{-d*.5f,d*.5f};
                foreach(var cut in cuts){xs.Add(cut.x0);xs.Add(cut.x1);zs.Add(cut.z0);zs.Add(cut.z1);}
                xs=xs.Distinct().OrderBy(v=>v).ToList();
                zs=zs.Distinct().OrderBy(v=>v).ToList();
                var material=Material(C(S(top,"color","#FFFFFF"),Color.white),Load(S(top,"texture")),N(top,"smoothness",.25f));
                var edgeMat=Material(C(S(top,"edgeColor","#B28D69"),Color.white),null,.2f);
                for(int i=0;i<xs.Count-1;i++)
                for(int j=0;j<zs.Count-1;j++)
                {
                    float x0=xs[i],x1=xs[i+1],z0=zs[j],z1=zs[j+1];
                    float mx=(x0+x1)*.5f,mz=(z0+z1)*.5f;
                    if(cuts.Any(o=>mx>o.x0 && mx<o.x1 && mz>o.z0 && mz<o.z1))continue;
                    Quad(id+"_top_"+i+"_"+j,tr,x0,z0,x1,z1,bottom+thickness+.0007f,material,w,d,register);
                    // Individual underside pieces avoid a solid slab filling the opening.
                    Vector3 p=tr(mx,bottom+thickness*.5f,mz);
                    cube(id+"_core_"+i+"_"+j,p,new Vector3(x1-x0,thickness,z1-z0),
                        C(S(top,"edgeColor","#B28D69"),Color.white),angle);
                }
                foreach(var fixture in fixtures.Where(f=>S(f,"countertopId")==id))
                {
                    string type=S(fixture,"type");
                    float fw=M(N(fixture,"width",type=="sink"?780:600));
                    float fd=M(N(fixture,"depth",type=="sink"?500:520));
                    float fx=M(N(fixture,"offsetX")),fz=M(N(fixture,"offsetY"));
                    if(type=="gasHob")
                    {
                        float h=M(N(fixture,"height",130));
                        cube(S(fixture,"id")+"_hob",tr(fx,bottom+thickness+.012f,fz),
                            new Vector3(fw,.024f,fd),C(S(fixture,"color","#202024"),Color.black),angle);
                        var hobTexture=Load(S(fixture,"texture"));
                        if(hobTexture)
                        {
                            var hobMat=Material(Color.white,hobTexture,.25f);
                            FixturePhoto(S(fixture,"id")+"_photo",tr,fx,fz,fw,fd,
                                bottom+thickness+.025f,(int)N(fixture,"textureRotation",0),hobMat,register);
                        }
                    }
                    else if(type=="sink")
                    {
                        string modelId=S(fixture,"modelId");
                        var model=plan.Root.Element("models")?.Elements("model")
                            .FirstOrDefault(m=>S(m,"id")==modelId);
                        if(model==null || S(model,"type")!="stainlessDoubleSink")
                        {
                            Debug.LogWarning("Sink model not found: "+modelId);
                            continue;
                        }
                        SinkModelRenderer.Build(fixture,model,S(fixture,"id"),fx,fz,fw,fd,
                            bottom+thickness+.006f,tr,register);
                    }
                }
            }
        }
    }
}
