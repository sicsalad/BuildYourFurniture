using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using UnityEngine;

namespace BuildYourFurniture
{
    // Sofa dimensions and positions are in millimetres in the plan XML.
    public static class SofaRenderer
    {
        public static readonly string[] Fabrics={
            "Len","Pamut","Bársony","Bordázott kord","Zsenília",
            "Buklé","Tweed","Halszálka","Vászon","Mikroszálas"
        };
        static readonly Dictionary<string,Texture2D> Cache=new Dictionary<string,Texture2D>();
        static float N(XElement e,string name,float fallback)
        {
            float v;return float.TryParse((string)e.Attribute(name),System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,out v)?v:fallback;
        }
        static string S(XElement e,string name,string fallback="")=>(string)e.Attribute(name)??fallback;
        static Texture2D Fabric(int index,Color baseColor)
        {
            string key=index+"_"+ColorUtility.ToHtmlStringRGB(baseColor);
            Texture2D cached;if(Cache.TryGetValue(key,out cached)&&cached!=null)return cached;
            const int size=128;
            var texture=new Texture2D(size,size,TextureFormat.RGBA32,true);
            texture.wrapMode=TextureWrapMode.Repeat;texture.filterMode=FilterMode.Bilinear;
            for(int y=0;y<size;y++)for(int x=0;x<size;x++)
            {
                float noise=Mathf.PerlinNoise(x*.29f+index*5.1f,y*.31f+index*7.3f);
                float pattern=0f;
                switch(index)
                {
                    case 0: pattern=((x%5==0)||(y%5==0))?-.12f:.025f;break;
                    case 1: pattern=((x+y)%4==0)?-.09f:.02f;break;
                    case 2: pattern=Mathf.PerlinNoise(x*.065f,y*.065f)*.20f-.1f;break;
                    case 3: pattern=(x%12<3)?-.18f:.04f;break;
                    case 4: pattern=Mathf.Sin(x*.55f+y*.36f)*.075f;break;
                    case 5: pattern=(Mathf.Sin(x*.62f)*Mathf.Cos(y*.62f))*.13f;break;
                    case 6: pattern=((x/6+y/6)%2==0)?-.11f:.07f;break;
                    case 7: pattern=(((x/7+y/7)%2==0)^(x%14<7))?-.14f:.06f;break;
                    case 8: pattern=(x%3==0?-.08f:0)+(y%4==0?-.07f:0);break;
                    default: pattern=(noise-.5f)*.11f;break;
                }
                float brightness=Mathf.Clamp(.9f+(noise-.5f)*.20f+pattern,.48f,1.15f);
                texture.SetPixel(x,y,new Color(Mathf.Clamp01(baseColor.r*brightness),
                    Mathf.Clamp01(baseColor.g*brightness),Mathf.Clamp01(baseColor.b*brightness),1));
            }
            texture.Apply(true,false);Cache[key]=texture;return texture;
        }
        public static void Build(XDocument plan,Action<GameObject> register)
        {
            var sofas=plan?.Root?.Element("sofas");
            if(sofas==null)return;
            foreach(var sofa in sofas.Elements("sofa"))
            {
                float width=Mathf.Max(600,N(sofa,"width",1400))/1000f;
                float depth=Mathf.Max(500,N(sofa,"depth",850))/1000f;
                float height=Mathf.Max(550,N(sofa,"height",850))/1000f;
                float x=N(sofa,"x",0)/1000f,z=N(sofa,"y",0)/1000f;
                float rotation=N(sofa,"rotation",0);
                Color color;if(!ColorUtility.TryParseHtmlString(S(sofa,"color","#777C83"),out color))
                    color=new Color(.47f,.49f,.51f);
                int fabric=Mathf.Clamp((int)N(sofa,"fabric",0),0,Fabrics.Length-1);
                var root=new GameObject("Sofa "+S(sofa,"id","SOFA"));
                root.transform.position=new Vector3(x,0,z);
                root.transform.rotation=Quaternion.Euler(0,rotation,0);
                register(root);
                var texture=Fabric(fabric,color);
                Action<string,Vector3,Vector3,bool> part=(name,center,size,upholstered)=>
                {
                    var cube=GameObject.CreatePrimitive(PrimitiveType.Cube);
                    cube.name=name;cube.transform.SetParent(root.transform,false);
                    cube.transform.localPosition=center;cube.transform.localScale=size;
                    var material=new Material(Shader.Find("Universal Render Pipeline/Lit")??Shader.Find("Standard"));
                    material.color=upholstered?Color.white:new Color(.18f,.17f,.16f);
                    if(upholstered)
                    {
                        material.mainTexture=texture;
                        if(material.HasProperty("_BaseMap"))material.SetTexture("_BaseMap",texture);
                    }
                    cube.GetComponent<Renderer>().material=material;
                };
                float seatHeight=.43f;
                float arm=.12f;
                part("Sofa base",new Vector3(0,.29f,0),new Vector3(width-.05f,.22f,depth-.07f),true);
                part("Seat left",new Vector3(-width*.23f,seatHeight,depth*.05f),
                    new Vector3(width*.46f-.06f,.15f,depth*.68f),true);
                part("Seat right",new Vector3(width*.23f,seatHeight,depth*.05f),
                    new Vector3(width*.46f-.06f,.15f,depth*.68f),true);
                part("Back frame",new Vector3(0,height*.63f,depth*.43f),
                    new Vector3(width,.54f,depth*.14f),true);
                part("Back cushion left",new Vector3(-width*.23f,height*.67f,depth*.31f),
                    new Vector3(width*.45f,.39f,.18f),true);
                part("Back cushion right",new Vector3(width*.23f,height*.67f,depth*.31f),
                    new Vector3(width*.45f,.39f,.18f),true);
                part("Left arm",new Vector3(-width*.5f+arm*.5f,.51f,-.015f),
                    new Vector3(arm,.45f,depth),true);
                part("Right arm",new Vector3(width*.5f-arm*.5f,.51f,-.015f),
                    new Vector3(arm,.45f,depth),true);
                foreach(float side in new[]{-1f,1f})
                    foreach(float front in new[]{-1f,1f})
                        part("Sofa leg",new Vector3(side*(width*.5f-.12f),.085f,front*(depth*.5f-.12f)),
                            new Vector3(.065f,.17f,.065f),false);
            }
        }
    }
}