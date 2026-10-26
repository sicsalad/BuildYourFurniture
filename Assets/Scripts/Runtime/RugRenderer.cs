using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml.Linq;
using UnityEngine;

namespace BuildYourFurniture
{
    // Thin, floor-level area rug. All plan dimensions are millimetres.
    public static class RugRenderer
    {
        public static readonly string[] Patterns={
            "Egyszínű bézs","Modern geometrikus","Perzsa ornamentika","Skandináv csíkos",
            "Berber / marokkói","Vintage kopott","Juta természetes","Bohém színes",
            "Shaggy plüss","Antracit absztrakt"
        };
        public static readonly string[] Colors={
            "#CBB99F","#C9BFB1","#A25E51","#E1D7C6","#D7C5A6",
            "#B58D7A","#B89A6C","#B77762","#E4DFD5","#484A4D"
        };
        static readonly Dictionary<string,Texture2D> Cache=new Dictionary<string,Texture2D>();
        static float N(XElement e,string name,float fallback)
        {
            float v;return float.TryParse((string)e.Attribute(name),NumberStyles.Float,CultureInfo.InvariantCulture,out v)?v:fallback;
        }
        static string S(XElement e,string name,string fallback="")=>(string)e.Attribute(name)??fallback;
        static Texture2D Pattern(int pattern,Color tint)
        {
            string key=pattern+"-"+ColorUtility.ToHtmlStringRGB(tint);
            Texture2D cached;if(Cache.TryGetValue(key,out cached)&&cached!=null)return cached;
            const int size=256;
            var t=new Texture2D(size,size,TextureFormat.RGBA32,true);
            t.wrapMode=TextureWrapMode.Repeat;t.filterMode=FilterMode.Trilinear;
            for(int y=0;y<size;y++)for(int x=0;x<size;x++)
            {
                float u=(float)x/size,v=(float)y/size;
                float fiber=Mathf.PerlinNoise(x*.43f+pattern*5,y*.48f+pattern*3);
                float low=Mathf.PerlinNoise(x*.035f+pattern,y*.042f+pattern);
                float p=0f;
                switch(pattern)
                {
                    case 0:p=0;break;
                    case 1:p=((x/32+y/32)%2==0)?.22f:-.13f;break;
                    case 2:
                        float a=Mathf.Abs(Mathf.Sin(u*24f)*Mathf.Cos(v*24f));
                        p=a>.72f?.27f:-.09f;break;
                    case 3:p=(y%36<7)?-.22f:.07f;break;
                    case 4:
                        float diamond=Mathf.Abs(Mathf.Repeat(u*5+v*5,1)-.5f)+Mathf.Abs(Mathf.Repeat(u*5-v*5,1)-.5f);
                        p=diamond<.20f?-.31f:.07f;break;
                    case 5:p=(low-.5f)*.55f;break;
                    case 6:p=(Mathf.Sin(y*.41f)*.08f)+(low-.5f)*.15f;break;
                    case 7:p=((x/24+2*(y/24))%4==0)?.29f:(Mathf.Sin(u*21+v*17)*.13f);break;
                    case 8:p=(fiber-.5f)*.32f;break;
                    case 9:p=(Mathf.Sin(u*14+Mathf.Sin(v*9)*2)*.13f)+(low-.5f)*.28f;break;
                }
                float light=Mathf.Clamp(.91f+p+(fiber-.5f)*.14f,.35f,1.35f);
                t.SetPixel(x,y,new Color(Mathf.Clamp01(tint.r*light),Mathf.Clamp01(tint.g*light),
                    Mathf.Clamp01(tint.b*light),1));
            }
            t.Apply(true,false);Cache[key]=t;return t;
        }
        public static void Build(XDocument plan,Action<GameObject> register)
        {
            var rugs=plan?.Root?.Element("rugs");
            if(rugs==null)return;
            foreach(var e in rugs.Elements("rug"))
            {
                float width=Mathf.Max(200,N(e,"width",1600))/1000f;
                float length=Mathf.Max(200,N(e,"length",2400))/1000f;
                float thickness=Mathf.Clamp(N(e,"thickness",12),3,40)/1000f;
                int pattern=Mathf.Clamp(Mathf.RoundToInt(N(e,"pattern",0)),0,Patterns.Length-1);
                Color color;
                if(!ColorUtility.TryParseHtmlString(S(e,"color",Colors[pattern]),out color))
                    ColorUtility.TryParseHtmlString(Colors[pattern],out color);
                var root=new GameObject("Rug "+S(e,"id","RUG"));
                root.transform.position=new Vector3(N(e,"x",500)/1000f,0,N(e,"y",500)/1000f);
                root.transform.rotation=Quaternion.Euler(0,N(e,"rotation",0),0);
                register(root);
                var mesh=new GameObject("Rug fabric");
                mesh.transform.SetParent(root.transform,false);
                mesh.transform.localPosition=new Vector3(0,thickness*.5f+.002f,0);
                var cube=mesh.AddComponent<MeshFilter>();
                // UV mapping on the top surface is used by the procedural woven texture.
                cube.sharedMesh=CreateMesh(width,length,thickness);
                var renderer=mesh.AddComponent<MeshRenderer>();
                var material=new Material(Shader.Find("Universal Render Pipeline/Lit")??Shader.Find("Standard"));
                material.color=Color.white;
                var texture=Pattern(pattern,color);
                material.mainTexture=texture;
                if(material.HasProperty("_BaseMap"))material.SetTexture("_BaseMap",texture);
                renderer.material=material;
                mesh.AddComponent<MeshCollider>().sharedMesh=cube.sharedMesh;
            }
        }
        static Mesh CreateMesh(float width,float length,float thickness)
        {
            float x=width*.5f,z=length*.5f,h=thickness*.5f;
            var vertices=new Vector3[]{
                new Vector3(-x,h,-z),new Vector3(-x,h,z),new Vector3(x,h,z),new Vector3(x,h,-z),
                new Vector3(-x,-h,-z),new Vector3(-x,-h,z),new Vector3(x,-h,z),new Vector3(x,-h,-z)
            };
            var uv=new Vector2[]{new Vector2(0,0),new Vector2(0,1),new Vector2(1,1),new Vector2(1,0),
                new Vector2(0,0),new Vector2(0,1),new Vector2(1,1),new Vector2(1,0)};
            var triangles=new int[]{
                0,1,2,0,2,3, 7,6,5,7,5,4,
                4,5,1,4,1,0, 3,2,6,3,6,7,
                1,5,6,1,6,2, 4,0,3,4,3,7
            };
            var mesh=new Mesh{vertices=vertices,uv=uv,triangles=triangles};
            mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }
    }
}