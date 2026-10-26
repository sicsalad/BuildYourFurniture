using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml.Linq;
using UnityEngine;

namespace BuildYourFurniture
{
    // Five construction styles for each of three tabletop shapes, in dining and coffee heights.
    // Height changes affect only the legs/supports, never the tabletop thickness or outline.
    public static class TableRenderer
    {
        public static readonly string[] Shapes={"Téglalap","Kör","Ovális"};
        public static readonly string[] Styles={"Négy láb","Trapéz lábak","Központi talp","Keresztláb","Dupla pillér"};
        public static readonly string[] Finishes={
            "Fehér matt","Hamilton tölgy","Natúr tölgy","Sonoma tölgy","Dió",
            "Fenyő","Világos bükk","Fekete matt","Antracit","Márványhatás"
        };
        public static readonly string[] Colors={
            "#F5F5F2","#97704E","#B98E60","#C6AC84","#725039",
            "#D8B98C","#E0C89E","#252527","#505157","#DFDCD5"
        };
        static readonly Dictionary<string,Texture2D> Textures=new Dictionary<string,Texture2D>();
        static float N(XElement e,string key,float fallback)
        {
            float result;return float.TryParse((string)e.Attribute(key),NumberStyles.Float,CultureInfo.InvariantCulture,out result)?result:fallback;
        }
        static string S(XElement e,string key,string fallback="")=>(string)e.Attribute(key)??fallback;
        static Texture2D Texture(int finish,Color tint)
        {
            string key=finish+"-"+ColorUtility.ToHtmlStringRGB(tint);
            Texture2D cached;if(Textures.TryGetValue(key,out cached)&&cached!=null)return cached;
            var t=new Texture2D(128,128,TextureFormat.RGBA32,true);
            t.wrapMode=TextureWrapMode.Repeat;t.filterMode=FilterMode.Trilinear;
            for(int y=0;y<128;y++)for(int x=0;x<128;x++)
            {
                float fine=Mathf.PerlinNoise(x*.22f+finish*8,y*.24f+finish*4);
                float grain=Mathf.PerlinNoise(x*.015f+finish*6,y*.10f+finish*3);
                float marble=Mathf.Sin((x+y*.25f)*.1f+fine*6f);
                float v=finish==0||finish==7||finish==8?.97f+(fine-.5f)*.05f:
                    finish==9?.92f+marble*.10f:
                    .89f+(grain-.5f)*.28f+Mathf.Sin(y*.17f+fine*2)*.04f;
                t.SetPixel(x,y,new Color(Mathf.Clamp01(tint.r*v),Mathf.Clamp01(tint.g*v),Mathf.Clamp01(tint.b*v),1));
            }
            t.Apply(true,false);Textures[key]=t;return t;
        }
        static Mesh TopMesh(float width,float depth,float thickness,int shape)
        {
            if(shape==0)return null;
            int segments=64;
            var verts=new Vector3[(segments+1)*2+2];
            var uv=new Vector2[verts.Length];
            float rx=width*.5f,rz=depth*.5f;
            verts[0]=new Vector3(0,thickness*.5f,0);
            verts[segments+1]=new Vector3(0,-thickness*.5f,0);
            uv[0]=uv[segments+1]=new Vector2(.5f,.5f);
            for(int i=0;i<segments;i++)
            {
                float a=i*Mathf.PI*2f/segments;
                float x=Mathf.Cos(a)*rx,z=Mathf.Sin(a)*rz;
                verts[i+1]=new Vector3(x,thickness*.5f,z);
                verts[segments+i+2]=new Vector3(x,-thickness*.5f,z);
                uv[i+1]=uv[segments+i+2]=new Vector2(x/width+.5f,z/depth+.5f);
            }
            var triangles=new List<int>();
            for(int i=0;i<segments;i++)
            {
                int j=(i+1)%segments;
                triangles.Add(0);triangles.Add(i+1);triangles.Add(j+1);
                triangles.Add(segments+1);triangles.Add(segments+j+2);triangles.Add(segments+i+2);
                int a=i+1,b=j+1,c=segments+i+2,d=segments+j+2;
                triangles.Add(a);triangles.Add(c);triangles.Add(d);
                triangles.Add(a);triangles.Add(d);triangles.Add(b);
            }
            var mesh=new Mesh();
            mesh.vertices=verts;mesh.uv=uv;mesh.triangles=triangles.ToArray();
            mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }
        public static void Build(XDocument plan,Action<GameObject> register)
        {
            var collection=plan?.Root?.Element("tables");
            if(collection==null)return;
            foreach(var e in collection.Elements("table"))
            {
                int shape=Mathf.Clamp(Mathf.RoundToInt(N(e,"shape",0)),0,2);
                int style=Mathf.Clamp(Mathf.RoundToInt(N(e,"style",0)),0,4);
                int finish=Mathf.Clamp(Mathf.RoundToInt(N(e,"finish",0)),0,9);
                bool coffee=S(e,"kind","dining")=="coffee";
                float width=Mathf.Max(.4f,N(e,"width",1200)/1000f);
                float depth=Mathf.Max(.4f,N(e,"depth",shape==1?1200:800)/1000f);
                if(shape==1)depth=width; // circular top remains truly circular
                float height=Mathf.Max(.20f,N(e,"height",coffee?450:750)/1000f);
                const float thickness=.035f;
                float legHeight=Mathf.Max(.10f,height-thickness);
                Color tint;
                if(!ColorUtility.TryParseHtmlString(S(e,"color",Colors[finish]),out tint))
                    ColorUtility.TryParseHtmlString(Colors[finish],out tint);
                var root=new GameObject((coffee?"Coffee":"Dining")+" table "+S(e,"id","TABLE"));
                root.transform.position=new Vector3(N(e,"x",500)/1000f,0,N(e,"y",500)/1000f);
                root.transform.rotation=Quaternion.Euler(0,N(e,"rotation",0),0);
                register(root);
                var topMat=new Material(Shader.Find("Universal Render Pipeline/Lit")??Shader.Find("Standard"));
                topMat.color=Color.white;
                var tex=Texture(finish,tint);
                topMat.mainTexture=tex;
                if(topMat.HasProperty("_BaseMap"))topMat.SetTexture("_BaseMap",tex);
                var legMat=new Material(Shader.Find("Universal Render Pipeline/Lit")??Shader.Find("Standard"));
                legMat.color=style==0||style==2?tint:new Color(.18f,.19f,.20f);
                Action<string,Vector3,Vector3,Material> box=(name,pos,size,mat)=>
                {
                    var go=GameObject.CreatePrimitive(PrimitiveType.Cube);
                    go.name=name;go.transform.SetParent(root.transform,false);
                    go.transform.localPosition=pos;go.transform.localScale=size;
                    go.GetComponent<Renderer>().sharedMaterial=mat;
                };
                if(shape==0)box("Tabletop",new Vector3(0,height-thickness*.5f,0),new Vector3(width,thickness,depth),topMat);
                else
                {
                    var top=new GameObject(shape==1?"Round tabletop":"Oval tabletop");
                    top.transform.SetParent(root.transform,false);
                    top.transform.localPosition=new Vector3(0,height-thickness*.5f,0);
                    var filter=top.AddComponent<MeshFilter>();
                    filter.sharedMesh=TopMesh(width,depth,thickness,shape);
                    top.AddComponent<MeshRenderer>().sharedMaterial=topMat;
                    top.AddComponent<MeshCollider>().sharedMesh=filter.sharedMesh;
                }
                float x=Mathf.Max(.10f,width*.5f-.10f),z=Mathf.Max(.10f,depth*.5f-.10f);
                if(style==0)
                {
                    foreach(int sx in new[]{-1,1})foreach(int sz in new[]{-1,1})
                        box("Straight leg",new Vector3(sx*x,legHeight*.5f,sz*z),
                            new Vector3(.055f,legHeight,.055f),legMat);
                }
                else if(style==1)
                {
                    foreach(int sx in new[]{-1,1})
                    {
                        box("Trapezoid leg",new Vector3(sx*x*.85f,legHeight*.5f,0),
                            new Vector3(.065f,legHeight,depth*.68f),legMat);
                        box("Trapezoid foot",new Vector3(sx*x*.85f,.025f,0),
                            new Vector3(.09f,.05f,depth*.78f),legMat);
                    }
                }
                else if(style==2)
                {
                    box("Pedestal",new Vector3(0,legHeight*.5f,0),
                        new Vector3(.14f,legHeight,.14f),legMat);
                    box("Pedestal foot",new Vector3(0,.025f,0),
                        new Vector3(width*.42f,.05f,depth*.42f),legMat);
                }
                else if(style==3)
                {
                    foreach(int sx in new[]{-1,1})
                    {
                        var leg=GameObject.CreatePrimitive(PrimitiveType.Cube);
                        leg.name="Cross leg";leg.transform.SetParent(root.transform,false);
                        leg.transform.localPosition=new Vector3(sx*x*.5f,legHeight*.5f,0);
                        leg.transform.localScale=new Vector3(.065f,legHeight,depth*.55f);
                        leg.transform.localRotation=Quaternion.Euler(sx*25,0,0);
                        leg.GetComponent<Renderer>().sharedMaterial=legMat;
                    }
                }
                else
                {
                    foreach(int sx in new[]{-1,1})
                        box("Double pedestal",new Vector3(sx*width*.25f,legHeight*.5f,0),
                            new Vector3(.13f,legHeight,depth*.44f),legMat);
                }
            }
        }
    }
}