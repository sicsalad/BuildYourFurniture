using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using UnityEngine;

namespace BuildYourFurniture
{
    // Laminate cladding for floors and room walls, in millimetres.
    public static class LaminateRenderer
    {
        public static readonly string[] Finishes={"Fehérített tölgy","Hamilton tölgy","Natúr tölgy","Sonoma tölgy",
            "Dió","Füstölt tölgy","Szürke tölgy","Világos bükk","Fenyő","Antracit tölgy",
            "Rusztikus tölgy","Fehér matt"};
        public static readonly string[] Colors={"#E2D6C0","#987451","#C39A6A","#D0B58B",
            "#78543D","#69594D","#92918C","#E0C69B","#DCC19B","#4B4D4C","#AE835B","#EFEDE7"};
        static float N(XElement e,string key,float fallback=0)
        {
            float v;return e!=null&&float.TryParse((string)e.Attribute(key),NumberStyles.Float,CultureInfo.InvariantCulture,out v)?v:fallback;
        }
        static string S(XElement e,string key,string fallback="")=>e==null?fallback:(string)e.Attribute(key)??fallback;
        static Material Mat(Color c,Texture2D texture=null)
        {
            var m=new Material(Shader.Find("Universal Render Pipeline/Lit")??Shader.Find("Standard"));
            m.color=c;
            if(m.HasProperty("_BaseColor"))m.SetColor("_BaseColor",c);
            if(texture!=null){m.mainTexture=texture;if(m.HasProperty("_BaseMap"))m.SetTexture("_BaseMap",texture);}
            return m;
        }
        static Texture2D Wood(int finish)
        {
            var t=new Texture2D(128,128,TextureFormat.RGBA32,true);
            t.wrapMode=TextureWrapMode.Repeat;
            for(int y=0;y<128;y++)for(int x=0;x<128;x++)
            {
                float g=Mathf.PerlinNoise(x*.019f+finish*4,y*.12f+finish*3);
                float n=Mathf.PerlinNoise(x*.22f,y*.28f+finish);
                float v=finish==11?.97f+(n-.5f)*.04f:.88f+(g-.5f)*.25f+Mathf.Sin(y*.22f+g*4)*.055f;
                t.SetPixel(x,y,new Color(v,v,v,1));
            }
            t.Apply(true,false);return t;
        }
        static void Quad(GameObject parent,string name,Vector3 position,Vector3 scale,Material material)
        {
            var obj=GameObject.CreatePrimitive(PrimitiveType.Cube);
            obj.name=name;obj.transform.SetParent(parent.transform,false);
            obj.transform.localPosition=position;obj.transform.localScale=scale;
            obj.GetComponent<Renderer>().sharedMaterial=material;
        }
        static bool InsideRoom(float x,float z,List<Vector2> polygon)
        {
            bool inside=false;
            for(int i=0,j=polygon.Count-1;i<polygon.Count;j=i++)
            {
                Vector2 a=polygon[i],b=polygon[j];
                if((a.y>z)!=(b.y>z)&&x<(b.x-a.x)*(z-a.y)/(b.y-a.y)+a.x)inside=!inside;
            }
            return inside;
        }
        public static bool HasFloor(XDocument plan,string roomId)
        {
            return plan?.Root?.Element("laminates")?.Elements("laminate").Any(x=>S(x,"target")=="floor"&&S(x,"roomId")==roomId)==true;
        }
        public static void Build(XDocument plan,Action<GameObject> register)
        {
            var finishes=plan?.Root?.Element("laminates");
            if(finishes==null)return;
            foreach(var item in finishes.Elements("laminate"))
            {
                string target=S(item,"target","floor"),roomId=S(item,"roomId");
                float boardLength=Mathf.Max(100,N(item,"boardLength",1200))/1000f;
                float boardWidth=Mathf.Max(40,N(item,"boardWidth",190))/1000f;
                float gap=Mathf.Clamp(N(item,"grout",1),0,20)/1000f;
                float thickness=Mathf.Clamp(N(item,"thickness",8),2,25)/1000f;
                float offset=S(item,"layout","offset")=="grid"?0:.5f;
                int finish=Mathf.Clamp(Mathf.RoundToInt(N(item,"finish",0)),0,Finishes.Length-1);
                Color color,grout;
                if(!ColorUtility.TryParseHtmlString(S(item,"color",Colors[finish]),out color))ColorUtility.TryParseHtmlString(Colors[finish],out color);
                if(!ColorUtility.TryParseHtmlString(S(item,"groutColor","#777777"),out grout))grout=Color.gray;
                var surface=Mat(color,Wood(finish));var joint=Mat(grout);
                var root=new GameObject("Laminate "+S(item,"id","L"));
                register(root);
                if(target=="floor")
                {
                    foreach(var room in plan.Root.Element("rooms").Elements("room").Where(x=>roomId==""||S(x,"id")==roomId))
                    {
                        var polygon=room.Elements("point").Select(p=>new Vector2(N(p,"x")/1000f,N(p,"y")/1000f)).ToList();
                        if(polygon.Count<3)continue;
                        float minX=polygon.Min(p=>p.x),maxX=polygon.Max(p=>p.x);
                        float minZ=polygon.Min(p=>p.y),maxZ=polygon.Max(p=>p.y);
                        float floor=N(room,"floorHeight")/1000f;
                        Quad(root,"Grout base",new Vector3((minX+maxX)*.5f,floor+.002f,(minZ+maxZ)*.5f),
                            new Vector3(maxX-minX,.003f,maxZ-minZ),joint);
                        int row=0;
                        for(float z=minZ;z<maxZ;z+=boardWidth,row++)
                        {
                            float zEnd=Mathf.Min(maxZ,z+boardWidth);
                            float start=minX-(row%2)*offset*boardLength;
                            for(float x=start;x<maxX;x+=boardLength)
                            {
                                float a=Mathf.Max(x,minX),b=Mathf.Min(x+boardLength,maxX);
                                if(b-a<=gap||zEnd-z<=gap)continue;
                                // Clip non-rectangular room outlines by checking board centre.
                                if(!InsideRoom((a+b)*.5f,(z+zEnd)*.5f,polygon))continue;
                                Quad(root,"Floor plank",new Vector3((a+b)*.5f,floor+thickness*.5f+.004f,(z+zEnd)*.5f),
                                    new Vector3(b-a-gap,thickness,zEnd-z-gap),surface);
                            }
                        }
                    }
                }
                else
                {
                    var walls=plan.Root.Element("walls");
                    if(walls==null)continue;
                    string selected=S(item,"wallId","all");
                    foreach(var wall in walls.Elements("wall").Where(w=>selected=="all"||S(w,"id")==selected))
                    {
                        Vector2 a=new Vector2(N(wall,"x"),N(wall,"y"))/1000f;
                        Vector2 b=new Vector2(N(wall,"x1"),N(wall,"y1"))/1000f;
                        Vector2 delta=b-a;float length=delta.magnitude;
                        if(length<.001f)continue;
                        float from=Mathf.Max(N(wall,"heightStart"),N(item,"fromHeight",0))/1000f;
                        float to=Mathf.Min(N(wall,"heightEnd",3000),N(item,"toHeight",3000))/1000f;
                        if(to<=from)continue;
                        Vector2 along=delta/length;
                        Vector2 inward=new Vector2(-along.y,along.x);
                        float wallThickness=N(wall,"thickness",120)/1000f;
                        // Front face is offset into the room by half the wall thickness.
                        Vector2 face=a+inward*(wallThickness*.5f+.008f);
                        var wallRoot=new GameObject("Wall "+S(wall,"id"));
                        wallRoot.transform.SetParent(root.transform,false);
                        wallRoot.transform.position=new Vector3(face.x,0,face.y);
                        wallRoot.transform.rotation=Quaternion.Euler(0,-Mathf.Atan2(along.y,along.x)*Mathf.Rad2Deg,0);
                        float layer=0.004f;
                        Quad(wallRoot,"Wall grout",new Vector3(length*.5f,(from+to)*.5f,layer),
                            new Vector3(length,to-from,.002f),joint);
                        var openings=plan.Root.Elements().Where(x=>x.Name=="doors"||x.Name=="windows")
                            .SelectMany(x=>x.Elements()).Where(x=>S(x,"wallId")==S(wall,"id")).ToList();
                        int row=0;
                        for(float y=from;y<to;y+=boardWidth,row++)
                        {
                            float upper=Mathf.Min(to,y+boardWidth);
                            for(float x=-(row%2)*offset*boardLength;x<length;x+=boardLength)
                            {
                                float left=Mathf.Max(0,x),right=Mathf.Min(length,x+boardLength);
                                if(right-left<=gap||upper-y<=gap)continue;
                                // Split planks around door/window openings.
                                var spans=new List<Vector2>{new Vector2(left,right)};
                                foreach(var opening in openings)
                                {
                                    float oy=N(opening,"bottom")/1000f,ot=oy+N(opening,"height")/1000f;
                                    if(upper<=oy||y>=ot)continue;
                                    float ol=N(opening,"offset")/1000f,orr=ol+N(opening,"width")/1000f;
                                    var next=new List<Vector2>();
                                    foreach(var span in spans)
                                    {
                                        if(span.x<ol)next.Add(new Vector2(span.x,Mathf.Min(span.y,ol)));
                                        if(span.y>orr)next.Add(new Vector2(Mathf.Max(span.x,orr),span.y));
                                    }
                                    spans=next;
                                }
                                foreach(var span in spans)
                                    if(span.y-span.x>gap)
                                        Quad(wallRoot,"Wall plank",new Vector3((span.x+span.y)*.5f,(y+upper)*.5f,layer+.005f),
                                            new Vector3(span.y-span.x-gap,upper-y-gap,thickness),surface);
                            }
                        }
                    }
                }
            }
        }
    }
}