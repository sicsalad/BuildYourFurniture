using System;
using System.Globalization;
using System.Xml.Linq;
using UnityEngine;

namespace BuildYourFurniture
{
    // Lightweight procedural decor: runs in desktop Unity and on standalone Quest.
    public static class DecorationRenderer
    {
        static float N(XElement e,string k,float d=0)
        {
            float f;return float.TryParse((string)e.Attribute(k),NumberStyles.Float,
                CultureInfo.InvariantCulture,out f)?f:d;
        }
        static string S(XElement e,string k,string d="")=>(string)e.Attribute(k)??d;
        static Color Tint(XElement e,Color fallback)
        {
            Color c;return ColorUtility.TryParseHtmlString(S(e,"color"),out c)?c:fallback;
        }
        static GameObject Shape(string name,PrimitiveType type,Transform parent,
            Vector3 pos,Vector3 scale,Color color)
        {
            var obj=GameObject.CreatePrimitive(type);obj.name=name;
            obj.transform.SetParent(parent,false);
            obj.transform.localPosition=pos;obj.transform.localScale=scale;
            var material=new Material(Shader.Find("Universal Render Pipeline/Lit")??Shader.Find("Standard"));
            material.color=color;obj.GetComponent<Renderer>().sharedMaterial=material;
            return obj;
        }
        public static void Build(XDocument plan,Action<GameObject> register)
        {
            var collection=plan?.Root?.Element("decorations");
            if(collection==null)return;
            foreach(var e in collection.Elements())
            {
                string kind=e.Name.LocalName,variant=S(e,"variant","0");
                float w=Mathf.Max(.03f,N(e,"width",200)/1000f);
                float d=Mathf.Max(.03f,N(e,"depth",200)/1000f);
                float h=Mathf.Max(.03f,N(e,"height",300)/1000f);
                var root=new GameObject("Decoration "+S(e,"id","DEC"));
                root.transform.position=new Vector3(N(e,"x")/1000f,N(e,"z")/1000f,N(e,"y")/1000f);
                root.transform.rotation=Quaternion.Euler(0,N(e,"rotation"),0);
                register(root);
                Color accent=Tint(e,new Color(.2f,.48f,.2f));
                if(kind=="book")
                {
                    var cover=Shape("Book cover",PrimitiveType.Cube,root.transform,
                        new Vector3(0,h*.5f,0),new Vector3(w,h,d),accent);
                    Shape("Pages",PrimitiveType.Cube,root.transform,
                        new Vector3(0,h*.5f,-d*.025f),new Vector3(w*.91f,h*.82f,d*.96f),
                        new Color(.9f,.86f,.72f));
                    continue;
                }
                // Ceramic pot with visible rim and soil.
                Color pot=new Color(.66f,.36f,.25f);
                Shape("Pot",PrimitiveType.Cylinder,root.transform,
                    new Vector3(0,h*.16f,0),new Vector3(w*.75f,h*.16f,d*.75f),pot);
                Shape("Pot rim",PrimitiveType.Cylinder,root.transform,
                    new Vector3(0,h*.32f,0),new Vector3(w*.86f,h*.035f,d*.86f),pot);
                Shape("Soil",PrimitiveType.Cylinder,root.transform,
                    new Vector3(0,h*.35f,0),new Vector3(w*.65f,h*.012f,d*.65f),
                    new Color(.22f,.16f,.11f));
                if(kind=="flower")
                {
                    Shape("Stem",PrimitiveType.Cylinder,root.transform,
                        new Vector3(0,h*.59f,0),new Vector3(w*.055f,h*.25f,d*.055f),Color.green);
                    for(int i=0;i<6;i++)
                    {
                        float a=i*Mathf.PI/3f;
                        Shape("Petal",PrimitiveType.Sphere,root.transform,
                            new Vector3(Mathf.Cos(a)*w*.23f,h*.87f,Mathf.Sin(a)*d*.23f),
                            new Vector3(w*.28f,h*.16f,d*.28f),accent);
                    }
                    Shape("Flower center",PrimitiveType.Sphere,root.transform,
                        new Vector3(0,h*.87f,0),new Vector3(w*.24f,h*.12f,d*.24f),Color.yellow);
                    continue;
                }
                int type=Mathf.Clamp((int)N(e,"variant",0),0,4);
                if(type==0||type==4)
                    Shape("Trunk",PrimitiveType.Cylinder,root.transform,
                        new Vector3(0,h*.58f,0),new Vector3(w*.10f,h*.27f,d*.10f),
                        new Color(.35f,.25f,.13f));
                int count=type==1?8:type==2?5:type==3?12:6;
                for(int i=0;i<count;i++)
                {
                    float angle=i*2f*Mathf.PI/count;
                    float spread=type==1?.25f:type==2?.1f:type==3?.34f:.18f;
                    float level=.53f+(i%3)*.13f;
                    var leaf=Shape("Plant leaf "+i,PrimitiveType.Sphere,root.transform,
                        new Vector3(Mathf.Cos(angle)*w*spread,h*level,Mathf.Sin(angle)*d*spread),
                        new Vector3(w*(type==2?.13f:.30f),h*(type==1?.42f:.23f),
                            d*(type==3?.15f:.30f)),accent);
                    leaf.transform.localRotation=Quaternion.Euler(0,-angle*Mathf.Rad2Deg,30);
                }
                if(type==4)
                    Shape("Tree crown",PrimitiveType.Sphere,root.transform,
                        new Vector3(0,h*.83f,0),new Vector3(w*.85f,h*.34f,d*.85f),accent);
            }
        }
    }
}