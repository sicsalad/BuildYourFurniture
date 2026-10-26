using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml.Linq;
using UnityEngine;

namespace BuildYourFurniture
{
    // Open-backed room-divider shelf. Built from fixed-pitch modules and clipped at the
    // requested envelope; no whole-object scaling or stretched shelf openings.
    public static class DividerShelfRenderer
    {
        public static readonly string[] Finishes={
            "Fehér matt","Hamilton tölgy","Natúr tölgy","Sonoma tölgy","Dió",
            "Fenyő","Világos bükk","Fekete matt","Antracit","Betonhatás"
        };
        public static readonly string[] Colors={
            "#F4F4F1","#97704E","#BE986B","#C9AE87","#765039",
            "#D9BB8D","#E0C89E","#292A2C","#515257","#AAA9A4"
        };
        const float Panel=0.018f, Bay=0.35f, Row=0.40f;
        static readonly Dictionary<string,Texture2D> Cache=new Dictionary<string,Texture2D>();
        static float N(XElement e,string key,float fallback)
        {
            float result;
            return float.TryParse((string)e.Attribute(key),NumberStyles.Float,CultureInfo.InvariantCulture,out result)?result:fallback;
        }
        static string S(XElement e,string key,string fallback="")=>(string)e.Attribute(key)??fallback;
        static Texture2D Surface(int finish,Color tint)
        {
            string key=finish+"-"+ColorUtility.ToHtmlStringRGB(tint);
            Texture2D result;if(Cache.TryGetValue(key,out result)&&result!=null)return result;
            result=new Texture2D(128,128,TextureFormat.RGBA32,true);
            result.wrapMode=TextureWrapMode.Repeat;result.filterMode=FilterMode.Trilinear;
            for(int y=0;y<128;y++)for(int x=0;x<128;x++)
            {
                float fine=Mathf.PerlinNoise(x*.23f+finish*9,y*.28f+finish*4);
                float grain=Mathf.PerlinNoise(x*.013f+finish*5,y*.095f+finish*3);
                float wave=Mathf.Sin(y*.15f+Mathf.Sin(x*.032f)*2.5f);
                float value=finish==0||finish==7||finish==8?
                    .97f+(fine-.5f)*.045f:finish==9?
                    .93f+(fine-.5f)*.22f:
                    .89f+(grain-.5f)*.29f+wave*.05f+(fine-.5f)*.03f;
                result.SetPixel(x,y,new Color(Mathf.Clamp01(tint.r*value),
                    Mathf.Clamp01(tint.g*value),Mathf.Clamp01(tint.b*value),1f));
            }
            result.Apply(true,false);Cache[key]=result;return result;
        }
        public static void Build(XDocument plan,Action<GameObject> register)
        {
            var collection=plan?.Root?.Element("dividerShelves");
            if(collection==null)return;
            foreach(var e in collection.Elements("shelf"))
            {
                float width=Mathf.Max(.25f,N(e,"width",1400)/1000f);
                float height=Mathf.Max(.25f,N(e,"height",1600)/1000f);
                float depth=Mathf.Max(.15f,N(e,"depth",350)/1000f);
                int finish=Mathf.Clamp(Mathf.RoundToInt(N(e,"finish",0)),0,Finishes.Length-1);
                Color tint;
                if(!ColorUtility.TryParseHtmlString(S(e,"color",Colors[finish]),out tint))
                    ColorUtility.TryParseHtmlString(Colors[finish],out tint);
                var root=new GameObject("Divider shelf "+S(e,"id","DIV"));
                root.transform.position=new Vector3(N(e,"x",500)/1000f,0,N(e,"y",500)/1000f);
                root.transform.rotation=Quaternion.Euler(0,N(e,"rotation",0),0);
                register(root);
                var material=new Material(Shader.Find("Universal Render Pipeline/Lit")??Shader.Find("Standard"));
                material.color=Color.white;
                var texture=Surface(finish,tint);
                material.mainTexture=texture;
                if(material.HasProperty("_BaseMap"))material.SetTexture("_BaseMap",texture);

                // Clip the larger, regular 350x400 mm module grid to the chosen size.
                // All panels retain their true 18 mm thickness; truncated end bays
                // become narrower rather than scaling the entire cabinet.
                Action<string,float,float,float,float,float,float> panel=(name,x0,x1,y0,y1,z0,z1)=>
                {
                    x0=Mathf.Max(0,x0);x1=Mathf.Min(width,x1);
                    y0=Mathf.Max(0,y0);y1=Mathf.Min(height,y1);
                    z0=Mathf.Max(0,z0);z1=Mathf.Min(depth,z1);
                    if(x1-x0<.001f||y1-y0<.001f||z1-z0<.001f)return;
                    var part=GameObject.CreatePrimitive(PrimitiveType.Cube);
                    part.name=name;part.transform.SetParent(root.transform,false);
                    part.transform.localPosition=new Vector3((x0+x1-width)*.5f,(y0+y1)*.5f,(z0+z1-depth)*.5f);
                    part.transform.localScale=new Vector3(x1-x0,y1-y0,z1-z0);
                    part.GetComponent<Renderer>().sharedMaterial=material;
                };
                panel("Bottom",0,width,0,Panel,0,depth);
                panel("Top",0,width,height-Panel,height,0,depth);
                panel("Left side",0,Panel,Panel,height-Panel,0,depth);
                panel("Right side",width-Panel,width,Panel,height-Panel,0,depth);
                for(int i=1;i*Bay<width-Panel;i++)
                {
                    float x=i*Bay;
                    panel("Vertical divider "+i,x-Panel*.5f,x+Panel*.5f,Panel,height-Panel,0,depth);
                }
                for(int j=1;j*Row<height-Panel;j++)
                {
                    float y=j*Row;
                    panel("Horizontal shelf "+j,Panel,width-Panel,y-Panel*.5f,y+Panel*.5f,0,depth);
                }
            }
        }
    }
}