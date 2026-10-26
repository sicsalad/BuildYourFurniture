using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml.Linq;
using UnityEngine;

namespace BuildYourFurniture
{
    // Parametric electric standing desk with two monitors. XML dimensions in millimetres.
    public static class StandingDeskRenderer
    {
        public static readonly string[] Finishes={
            "Natúr tölgy","Diófa","Világos bükk","Fekete műanyag / laminált","Fehérített tölgy"
        };
        static readonly string[] DefaultColors={"#BA9669","#725039","#D6B98B","#242629","#D8CBB1"};
        static readonly Dictionary<string,Texture2D> TextureCache=new Dictionary<string,Texture2D>();
        static float N(XElement e,string key,float fallback)
        {
            float value;
            return float.TryParse((string)e.Attribute(key),NumberStyles.Float,CultureInfo.InvariantCulture,out value)
                ?value:fallback;
        }
        static string S(XElement e,string key,string fallback="")=>(string)e.Attribute(key)??fallback;
        public static string DefaultColor(int finish)=>DefaultColors[Mathf.Clamp(finish,0,DefaultColors.Length-1)];

        static Texture2D MakeTexture(int finish,Color tint)
        {
            string key=finish+"-"+ColorUtility.ToHtmlStringRGB(tint);
            Texture2D cached;
            if(TextureCache.TryGetValue(key,out cached)&&cached!=null)return cached;
            const int side=128;
            var texture=new Texture2D(side,side,TextureFormat.RGBA32,true);
            texture.wrapMode=TextureWrapMode.Repeat;
            texture.filterMode=FilterMode.Trilinear;
            for(int y=0;y<side;y++)for(int x=0;x<side;x++)
            {
                float grain=Mathf.PerlinNoise(y*.085f+finish*12f,x*.008f+finish*7f);
                float detail=Mathf.PerlinNoise(x*.23f+5f,y*.31f+8f);
                float streak=Mathf.Sin(y*.18f+Mathf.Sin(x*.035f)*2f);
                float value=finish==3
                    ? .94f+(detail-.5f)*.065f
                    : .88f+(grain-.5f)*.27f+streak*.045f+(detail-.5f)*.035f;
                texture.SetPixel(x,y,new Color(Mathf.Clamp01(tint.r*value),
                    Mathf.Clamp01(tint.g*value),Mathf.Clamp01(tint.b*value),1));
            }
            texture.Apply(true,false);
            TextureCache[key]=texture;
            return texture;
        }

        public static void Build(XDocument plan,Action<GameObject> register)
        {
            var desks=plan?.Root?.Element("standingDesks");
            if(desks==null)return;
            foreach(var e in desks.Elements("desk"))
            {
                float width=Mathf.Max(900f,N(e,"width",1200f))/1000f;
                float depth=Mathf.Max(500f,N(e,"depth",700f))/1000f;
                float height=Mathf.Clamp(N(e,"height",740f),620f,1250f)/1000f;
                float x=N(e,"x",500f)/1000f,z=N(e,"y",500f)/1000f;
                float rotation=N(e,"rotation",0f);
                int finish=Mathf.Clamp(Mathf.RoundToInt(N(e,"finish",0)),0,Finishes.Length-1);
                Color tint;
                if(!ColorUtility.TryParseHtmlString(S(e,"color",DefaultColor(finish)),out tint))
                    ColorUtility.TryParseHtmlString(DefaultColor(finish),out tint);
                var root=new GameObject("Standing desk "+S(e,"id","DESK"));
                root.transform.position=new Vector3(x,0,z);
                root.transform.rotation=Quaternion.Euler(0,rotation,0);
                register(root);
                var topTexture=MakeTexture(finish,tint);
                Color metal=new Color(.13f,.15f,.17f);
                Color screen=new Color(.055f,.095f,.145f);
                Action<string,Vector3,Vector3,Color,bool> box=(name,position,size,color,wood)=>
                {
                    var part=GameObject.CreatePrimitive(PrimitiveType.Cube);
                    part.name=name;
                    part.transform.SetParent(root.transform,false);
                    part.transform.localPosition=position;
                    part.transform.localScale=size;
                    var shader=Shader.Find("Universal Render Pipeline/Lit")??Shader.Find("Standard");
                    var material=new Material(shader);
                    material.color=wood?Color.white:color;
                    if(wood)
                    {
                        material.mainTexture=topTexture;
                        if(material.HasProperty("_BaseMap"))material.SetTexture("_BaseMap",topTexture);
                    }
                    part.GetComponent<Renderer>().material=material;
                };
                const float topThickness=.028f;
                box("Desk tabletop",new Vector3(0,height-topThickness*.5f,0),
                    new Vector3(width,topThickness,depth),tint,true);
                // Two-stage telescopic lifting columns and T-shaped feet.
                float legX=width*.5f-.115f;
                foreach(float side in new[]{-1f,1f})
                {
                    float sx=side*legX;
                    box("Desk foot",new Vector3(sx,.035f,0),
                        new Vector3(.085f,.07f,depth*.78f),metal,false);
                    float fixedHeight=Mathf.Min(.52f,height*.72f);
                    box("Lower telescopic column",new Vector3(sx,.07f+fixedHeight*.5f,0),
                        new Vector3(.083f,fixedHeight,.083f),metal,false);
                    float upperHeight=Mathf.Max(.12f,height-.11f-fixedHeight);
                    box("Upper telescopic column",new Vector3(sx,.07f+fixedHeight+upperHeight*.5f,0),
                        new Vector3(.065f,upperHeight,.065f),new Color(.28f,.30f,.32f),false);
                    box("Top mounting bracket",new Vector3(sx,height-.055f,0),
                        new Vector3(.19f,.055f,.14f),metal,false);
                }
                box("Desk crossbar",new Vector3(0,height-.17f,0),
                    new Vector3(Mathf.Max(.25f,width-.23f),.055f,.055f),metal,false);
                box("Height control panel",new Vector3(-width*.36f,height-.055f,-depth*.45f),
                    new Vector3(.11f,.028f,.065f),new Color(.09f,.10f,.11f),false);

                // Two independent screens, with stands, bezels and front-facing panels.
                float monitorWidth=Mathf.Min(.48f,(width-.16f)*.5f);
                float monitorHeight=monitorWidth*.565f;
                float gap=.018f;
                for(int i=0;i<2;i++)
                {
                    float mx=(i==0?-1f:1f)*(monitorWidth+gap)*.5f;
                    float mz=depth*.23f;
                    float baseY=height+.012f;
                    box("Monitor "+(i+1)+" stand foot",new Vector3(mx,baseY+.009f,mz),
                        new Vector3(.16f,.018f,.12f),metal,false);
                    box("Monitor "+(i+1)+" stand",new Vector3(mx,baseY+.095f,mz),
                        new Vector3(.022f,.18f,.025f),metal,false);
                    float centerY=height+.16f+monitorHeight*.5f;
                    box("Monitor "+(i+1)+" bezel",new Vector3(mx,centerY,mz+.025f),
                        new Vector3(monitorWidth,monitorHeight,.035f),metal,false);
                    // Front is toward negative Z (the seated user).
                    box("Monitor "+(i+1)+" screen",new Vector3(mx,centerY,mz+.004f),
                        new Vector3(monitorWidth-.025f,monitorHeight-.025f,.003f),screen,false);
                }
                box("Keyboard",new Vector3(0,height+.008f,-depth*.31f),
                    new Vector3(Mathf.Min(.42f,width*.43f),.016f,.14f),metal,false);
                box("Mouse",new Vector3(width*.30f,height+.012f,-depth*.30f),
                    new Vector3(.07f,.024f,.10f),metal,false);
            }
        }
    }
}