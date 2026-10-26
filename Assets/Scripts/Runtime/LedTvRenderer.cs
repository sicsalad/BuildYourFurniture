using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml.Linq;
using UnityEngine;

namespace BuildYourFurniture
{
    // Wall-mounted LED TV. Screen diagonal is in inches, placement in millimetres.
    public static class LedTvRenderer
    {
        public static readonly string[] ScreenPresets={
            "Kikapcsolt fekete","Kék óceán","Hegyek és égbolt","Erdei táj",
            "Naplemente","Városi éjszaka","Absztrakt kék","Absztrakt lila",
            "Zöld természet","Színes geometria"
        };
        public static readonly string[] PresetColors={
            "#080A0D","#146CA5","#7AB1CB","#2F7153","#E68B4A",
            "#253C6C","#186FD1","#8646B2","#55A76A","#DA4D75"
        };
        static readonly Dictionary<int,Texture2D> Screens=new Dictionary<int,Texture2D>();
        static float N(XElement e,string key,float fallback)
        {
            float v;
            return float.TryParse((string)e.Attribute(key),NumberStyles.Float,CultureInfo.InvariantCulture,out v)?v:fallback;
        }
        static string S(XElement e,string key,string fallback="")=>(string)e.Attribute(key)??fallback;
        public static float WidthMm(float diagonalInches)=>diagonalInches*25.4f*16f/Mathf.Sqrt(337f);
        public static float HeightMm(float diagonalInches)=>diagonalInches*25.4f*9f/Mathf.Sqrt(337f);
        static Texture2D ScreenTexture(int preset)
        {
            Texture2D cached;
            if(Screens.TryGetValue(preset,out cached)&&cached!=null)return cached;
            const int w=256,h=144;
            var t=new Texture2D(w,h,TextureFormat.RGBA32,true);
            t.wrapMode=TextureWrapMode.Clamp;t.filterMode=FilterMode.Trilinear;
            Color baseColor;ColorUtility.TryParseHtmlString(PresetColors[preset],out baseColor);
            for(int y=0;y<h;y++)for(int x=0;x<w;x++)
            {
                float u=(float)x/w,v=(float)y/h;
                float noise=Mathf.PerlinNoise(u*7+preset*2,v*7+preset);
                float horizon=.36f+.09f*Mathf.Sin(u*10+preset);
                Color color=baseColor;
                if(preset==0)color=new Color(.015f,.018f,.025f);
                else if(preset==1)color=Color.Lerp(new Color(.015f,.12f,.27f),new Color(.19f,.69f,.86f),v*.75f+noise*.25f);
                else if(preset==2)color=v<horizon?new Color(.24f,.37f,.43f):Color.Lerp(new Color(.32f,.63f,.83f),Color.white,v*.5f);
                else if(preset==3)color=Color.Lerp(new Color(.025f,.15f,.07f),new Color(.27f,.55f,.17f),noise*.75f+v*.25f);
                else if(preset==4)color=Color.Lerp(new Color(.23f,.08f,.23f),new Color(1f,.62f,.16f),Mathf.Clamp01(v+.15f*noise));
                else if(preset==5)color=Color.Lerp(new Color(.01f,.025f,.08f),new Color(.16f,.26f,.52f),noise*.7f);
                else if(preset==6)color=Color.Lerp(new Color(.02f,.18f,.5f),new Color(.1f,.75f,.96f),Mathf.Sin(u*10+v*8)*.5f+.5f);
                else if(preset==7)color=Color.Lerp(new Color(.2f,.04f,.38f),new Color(.88f,.22f,.68f),Mathf.Sin(u*12-v*6)*.5f+.5f);
                else if(preset==8)color=Color.Lerp(new Color(.015f,.24f,.12f),new Color(.58f,.86f,.25f),noise);
                else color=Color.HSVToRGB(Mathf.Repeat(Mathf.Floor(u*8)/8f+Mathf.Floor(v*5)/10f,1),.7f,.85f);
                t.SetPixel(x,y,color);
            }
            t.Apply(true,false);Screens[preset]=t;return t;
        }
        public static void Build(XDocument plan,Action<GameObject> register)
        {
            var collection=plan?.Root?.Element("ledTvs");
            if(collection==null)return;
            foreach(var e in collection.Elements("tv"))
            {
                float diagonal=Mathf.Clamp(N(e,"inches",54),19,100);
                float width=WidthMm(diagonal)/1000f;
                float height=HeightMm(diagonal)/1000f;
                float bottom=N(e,"bottomHeight",900)/1000f;
                int preset=Mathf.Clamp(Mathf.RoundToInt(N(e,"screen",0)),0,ScreenPresets.Length-1);
                Color frame;
                if(!ColorUtility.TryParseHtmlString(S(e,"color","#1C1C1E"),out frame))
                    frame=new Color(.11f,.11f,.12f);
                var root=new GameObject("Wall LED TV "+S(e,"id","LEDTV"));
                root.transform.position=new Vector3(N(e,"x",500)/1000f,0,N(e,"y",500)/1000f);
                root.transform.rotation=Quaternion.Euler(0,N(e,"rotation",0),0);
                register(root);
                Action<string,Vector3,Vector3,Color,Texture2D> part=(name,position,size,tint,texture)=>
                {
                    var obj=GameObject.CreatePrimitive(PrimitiveType.Cube);
                    obj.name=name;obj.transform.SetParent(root.transform,false);
                    obj.transform.localPosition=position;obj.transform.localScale=size;
                    var shader=Shader.Find("Universal Render Pipeline/Lit")??Shader.Find("Standard");
                    var mat=new Material(shader);
                    mat.color=tint;
                    if(texture!=null)
                    {
                        mat.mainTexture=texture;
                        if(mat.HasProperty("_BaseMap"))mat.SetTexture("_BaseMap",texture);
                        // The screen is a textured face on the front of the thin box.
                    }
                    obj.GetComponent<Renderer>().material=mat;
                };
                float center=bottom+height*.5f;
                part("TV chassis",new Vector3(0,center,0),new Vector3(width+.014f,height+.014f,.045f),frame,null);
                // Front faces negative Z, as for the other furniture models.
                part("LED display",new Vector3(0,center,-.025f),new Vector3(width,height,.003f),Color.white,ScreenTexture(preset));
                part("Wall bracket plate",new Vector3(0,center,.052f),new Vector3(.22f,.16f,.012f),new Color(.20f,.20f,.22f),null);
                part("Wall mount arm",new Vector3(0,center,.028f),new Vector3(.065f,.065f,.055f),new Color(.15f,.15f,.16f),null);
                part("Status LED",new Vector3(width*.44f,bottom-.003f,-.026f),new Vector3(.007f,.005f,.003f),new Color(.18f,.75f,.22f),null);
            }
        }
    }
}