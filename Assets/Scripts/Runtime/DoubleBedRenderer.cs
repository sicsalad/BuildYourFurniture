using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml.Linq;
using UnityEngine;

namespace BuildYourFurniture
{
    // Double bed with frame, headboard, slatted base, mattress and two pillows.
    // Plan coordinates and dimensions are millimetres.
    public static class DoubleBedRenderer
    {
        public static readonly string[] Finishes={"Fenyő","Hamilton tölgy","Fehér"};
        public static readonly string[] Colors={"#D5B684","#97704E","#F5F4F0"};
        static readonly Dictionary<string,Texture2D> Textures=new Dictionary<string,Texture2D>();
        static float N(XElement e,string name,float fallback)
        {
            float result;
            return float.TryParse((string)e.Attribute(name),NumberStyles.Float,CultureInfo.InvariantCulture,out result)?result:fallback;
        }
        static string S(XElement e,string name,string fallback="")=>(string)e.Attribute(name)??fallback;
        static Texture2D Texture(int finish,Color tint)
        {
            string key=finish+"-"+ColorUtility.ToHtmlStringRGB(tint);
            Texture2D existing;
            if(Textures.TryGetValue(key,out existing)&&existing!=null)return existing;
            const int size=128;
            var result=new Texture2D(size,size,TextureFormat.RGBA32,true);
            result.wrapMode=TextureWrapMode.Repeat;
            result.filterMode=FilterMode.Trilinear;
            for(int y=0;y<size;y++)for(int x=0;x<size;x++)
            {
                float grain=Mathf.PerlinNoise(y*.095f+finish*17f,x*.012f+3f);
                float wave=Mathf.Sin(y*.12f+Mathf.Sin(x*.028f)*2.8f);
                float detail=Mathf.PerlinNoise(x*.26f,y*.28f);
                float intensity=finish==2?.98f+(detail-.5f)*.035f:
                    .88f+(grain-.5f)*(finish==0?.23f:.34f)+wave*.055f+(detail-.5f)*.025f;
                result.SetPixel(x,y,new Color(Mathf.Clamp01(tint.r*intensity),
                    Mathf.Clamp01(tint.g*intensity),Mathf.Clamp01(tint.b*intensity),1f));
            }
            result.Apply(true,false);
            Textures[key]=result;
            return result;
        }
        public static void Build(XDocument plan,Action<GameObject> register)
        {
            var beds=plan?.Root?.Element("doubleBeds");
            if(beds==null)return;
            foreach(var bed in beds.Elements("bed"))
            {
                float width=Mathf.Max(900f,N(bed,"width",1400f))/1000f;
                float length=Mathf.Max(1600f,N(bed,"length",2000f))/1000f;
                float frameHeight=Mathf.Clamp(N(bed,"frameHeight",350f),200f,650f)/1000f;
                float headboardHeight=Mathf.Clamp(N(bed,"headboardHeight",950f),500f,1600f)/1000f;
                int finish=Mathf.Clamp(Mathf.RoundToInt(N(bed,"finish",0)),0,2);
                Color tint;
                if(!ColorUtility.TryParseHtmlString(S(bed,"color",Colors[finish]),out tint))
                    ColorUtility.TryParseHtmlString(Colors[finish],out tint);
                var root=new GameObject("Double bed "+S(bed,"id","BED"));
                root.transform.position=new Vector3(N(bed,"x",500f)/1000f,0,N(bed,"y",500f)/1000f);
                root.transform.rotation=Quaternion.Euler(0,N(bed,"rotation",0),0);
                register(root);
                var finishTexture=Texture(finish,tint);
                Action<string,Vector3,Vector3,Color,bool> box=(name,pos,size,color,wood)=>
                {
                    var cube=GameObject.CreatePrimitive(PrimitiveType.Cube);
                    cube.name=name;cube.transform.SetParent(root.transform,false);
                    cube.transform.localPosition=pos;cube.transform.localScale=size;
                    var material=new Material(Shader.Find("Universal Render Pipeline/Lit")??Shader.Find("Standard"));
                    material.color=wood?Color.white:color;
                    if(wood)
                    {
                        material.mainTexture=finishTexture;
                        if(material.HasProperty("_BaseMap"))material.SetTexture("_BaseMap",finishTexture);
                    }
                    cube.GetComponent<Renderer>().material=material;
                };
                float rail=.035f;
                box("Left frame",new Vector3(-width*.5f+rail*.5f,frameHeight*.5f,0),
                    new Vector3(rail,frameHeight,length),tint,true);
                box("Right frame",new Vector3(width*.5f-rail*.5f,frameHeight*.5f,0),
                    new Vector3(rail,frameHeight,length),tint,true);
                box("Footboard",new Vector3(0,frameHeight*.5f,-length*.5f+rail*.5f),
                    new Vector3(width,frameHeight,rail),tint,true);
                box("Headboard",new Vector3(0,headboardHeight*.5f,length*.5f-rail*.5f),
                    new Vector3(width,headboardHeight,rail*1.7f),tint,true);
                box("Slatted base",new Vector3(0,frameHeight-.075f,0),
                    new Vector3(width-.075f,.04f,length-.095f),tint,true);
                box("Mattress",new Vector3(0,frameHeight+.055f,-.01f),
                    new Vector3(width-.085f,.22f,length-.11f),new Color(.91f,.91f,.87f),false);
                float pillowWidth=(width-.15f)*.46f;
                for(int i=0;i<2;i++)
                {
                    float px=(i==0?-1f:1f)*width*.245f;
                    box("Pillow "+(i+1),new Vector3(px,frameHeight+.205f,length*.33f),
                        new Vector3(pillowWidth,.085f,.34f),new Color(.96f,.96f,.94f),false);
                }
                foreach(float side in new[]{-1f,1f})
                    foreach(float end in new[]{-1f,1f})
                        box("Bed leg",new Vector3(side*(width*.5f-.095f),.065f,end*(length*.5f-.095f)),
                            new Vector3(.075f,.13f,.075f),tint,true);
            }
        }
    }
}