using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml.Linq;
using UnityEngine;

namespace BuildYourFurniture
{
    // Parametric TV cabinet, measurements and plan coordinates are millimetres.
    public static class TvStandRenderer
    {
        public static readonly string[] Finishes={
            "Natúr tölgy","Hamilton tölgy","Sonoma tölgy","Dió","Fenyő",
            "Világos bükk","Fehér matt","Fekete matt","Antracit","Betonhatás"
        };
        public static readonly string[] Colors={
            "#B89160","#96704C","#C5A77E","#70503A","#D5B684",
            "#D8BC91","#F2F2EF","#252527","#414348","#9A9A96"
        };
        static readonly Dictionary<string,Texture2D> Cache=new Dictionary<string,Texture2D>();
        static float N(XElement e,string key,float fallback)
        {
            float n;return float.TryParse((string)e.Attribute(key),NumberStyles.Float,CultureInfo.InvariantCulture,out n)?n:fallback;
        }
        static string S(XElement e,string key,string fallback="")=>(string)e.Attribute(key)??fallback;
        static Texture2D Surface(int finish,Color color)
        {
            string key=finish+"-"+ColorUtility.ToHtmlStringRGB(color);
            Texture2D cached;if(Cache.TryGetValue(key,out cached)&&cached!=null)return cached;
            var t=new Texture2D(128,128,TextureFormat.RGBA32,true);
            t.wrapMode=TextureWrapMode.Repeat;t.filterMode=FilterMode.Trilinear;
            bool wood=finish<=5;
            for(int y=0;y<128;y++)for(int x=0;x<128;x++)
            {
                float fine=Mathf.PerlinNoise(x*.27f+finish*9,y*.24f+finish*3);
                float grain=Mathf.PerlinNoise(x*.013f+finish*8,y*.10f+finish*6);
                float stripe=Mathf.Sin(y*.17f+Mathf.Sin(x*.035f)*2.3f);
                float amount=wood?.90f+(grain-.5f)*.27f+stripe*.05f+(fine-.5f)*.045f:
                    finish==9?.93f+(fine-.5f)*.21f:.98f+(fine-.5f)*.04f;
                t.SetPixel(x,y,new Color(Mathf.Clamp01(color.r*amount),Mathf.Clamp01(color.g*amount),Mathf.Clamp01(color.b*amount),1));
            }
            t.Apply(true,false);Cache[key]=t;return t;
        }
        public static void Build(XDocument plan,Action<GameObject> register)
        {
            var stands=plan?.Root?.Element("tvStands");
            if(stands==null)return;
            foreach(var e in stands.Elements("stand"))
            {
                float w=Mathf.Max(.6f,N(e,"width",1400)/1000f);
                float d=Mathf.Max(.25f,N(e,"depth",420)/1000f);
                float h=Mathf.Max(.25f,N(e,"height",500)/1000f);
                int finish=Mathf.Clamp(Mathf.RoundToInt(N(e,"finish",0)),0,Finishes.Length-1);
                Color color;
                if(!ColorUtility.TryParseHtmlString(S(e,"color",Colors[finish]),out color))
                    ColorUtility.TryParseHtmlString(Colors[finish],out color);
                var root=new GameObject("TV stand "+S(e,"id","TV"));
                root.transform.position=new Vector3(N(e,"x",500)/1000f,0,N(e,"y",500)/1000f);
                root.transform.rotation=Quaternion.Euler(0,N(e,"rotation",0),0);
                register(root);
                var texture=Surface(finish,color);
                Action<string,Vector3,Vector3,bool> part=(name,pos,size,finished)=>
                {
                    var cube=GameObject.CreatePrimitive(PrimitiveType.Cube);
                    cube.name=name;cube.transform.SetParent(root.transform,false);
                    cube.transform.localPosition=pos;cube.transform.localScale=size;
                    var material=new Material(Shader.Find("Universal Render Pipeline/Lit")??Shader.Find("Standard"));
                    material.color=finished?Color.white:new Color(.13f,.14f,.15f);
                    if(finished)
                    {
                        material.mainTexture=texture;
                        if(material.HasProperty("_BaseMap"))material.SetTexture("_BaseMap",texture);
                    }
                    cube.GetComponent<Renderer>().material=material;
                };
                float thickness=.018f;
                float foot=.075f;
                float bodyBottom=foot,bodyTop=h;
                float innerH=Mathf.Max(.08f,bodyTop-bodyBottom-2*thickness);
                part("Top panel",new Vector3(0,h-thickness*.5f,0),new Vector3(w,thickness,d),true);
                part("Bottom panel",new Vector3(0,foot+thickness*.5f,0),new Vector3(w,thickness,d),true);
                part("Left side",new Vector3(-w*.5f+thickness*.5f,foot+thickness+innerH*.5f,0),new Vector3(thickness,innerH,d),true);
                part("Right side",new Vector3(w*.5f-thickness*.5f,foot+thickness+innerH*.5f,0),new Vector3(thickness,innerH,d),true);
                part("Back",new Vector3(0,foot+thickness+innerH*.5f,d*.5f-thickness*.5f),new Vector3(w-2*thickness,innerH,thickness),true);
                float usableW=w-2*thickness;
                float bayW=usableW/3f;
                for(int i=1;i<3;i++)
                    part("Partition "+i,new Vector3(-usableW*.5f+bayW*i,foot+thickness+innerH*.5f,0),
                        new Vector3(thickness,innerH,d-thickness),true);
                // Two cabinet doors, with a central open shelf for media equipment.
                foreach(int side in new[]{-1,1})
                {
                    float x=side*bayW;
                    part(side<0?"Left door":"Right door",
                        new Vector3(x,foot+thickness+innerH*.5f,-d*.5f-.003f),
                        new Vector3(bayW-.007f,innerH-.012f,.018f),true);
                    part("Door handle",new Vector3(x-side*bayW*.27f,foot+thickness+innerH*.55f,-d*.5f-.019f),
                        new Vector3(.012f,.055f,.012f),false);
                }
                part("Center shelf",new Vector3(0,foot+thickness+innerH*.5f,0),
                    new Vector3(bayW-.012f,thickness,d-thickness),true);
                foreach(int sx in new[]{-1,1})foreach(int sz in new[]{-1,1})
                    part("Leg",new Vector3(sx*(w*.5f-.09f),foot*.5f,sz*(d*.5f-.065f)),
                        new Vector3(.045f,foot,.045f),false);
            }
        }
    }
}