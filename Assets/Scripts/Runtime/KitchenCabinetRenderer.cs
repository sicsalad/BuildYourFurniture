using System;
using System.Globalization;
using System.Xml.Linq;
using UnityEngine;
namespace BuildYourFurniture
{
    // Dedicated A-N kitchen modules. XML dimensions and coordinates are millimetres.
    public static class KitchenCabinetRenderer
    {
        static float N(XElement e,string key,float fallback=0) {float v;return float.TryParse((string)e?.Attribute(key),NumberStyles.Float,CultureInfo.InvariantCulture,out v)?v:fallback;}
        static string S(XElement e,string key,string fallback="") => (string)e?.Attribute(key)??fallback;
        static Color C(string value,Color fallback){Color result;return ColorUtility.TryParseHtmlString(value,out result)?result:fallback;}
        public static void Build(XElement e,XElement defaults,Action<string,Vector3,Vector3,Color,float> cube,Action<GameObject> register=null)
        {
            string id=S(e,"id"),style=S(e,"style");
            float w=N(e,"width")/1000f,d=N(e,"depth")/1000f,h=N(e,"height")/1000f;
            float bottom=N(e,"bottom")/1000f,plinth=N(e,"plinth")/1000f;
            float angle=N(e,"rotation"),r=angle*Mathf.Deg2Rad;
            float ox=N(e,"x")/1000f,oz=N(e,"y")/1000f,t=.018f,frontT=.02f;
            Color body=C(S(defaults,"bodyColor","#536F85"),Color.gray),front=C(S(defaults,"frontColor","#536F85"),Color.gray);
            Color dark=C("#16191C",Color.black),metal=C("#AEB6BA",Color.gray);
            Action<string,float,float,float,float,float,float,Color> box=(name,x,y,z,sx,sy,sz,color)=>
                cube(id+"_"+name,new Vector3(ox+Mathf.Cos(r)*x+Mathf.Sin(r)*z,bottom+y,oz-Mathf.Sin(r)*x+Mathf.Cos(r)*z),
                    new Vector3(sx,sy,sz),color,angle);
            if(style=="diagonalCorner")
            {
                // Five-sided corner carcass: the front-left 300x300 mm corner is removed.
                // Local -Z faces the kitchen; the diagonal door joins the two exposed edges.
                float cut=Mathf.Min(.30f,Mathf.Min(w,d)*.48f);
                Vector2[] outline={
                    new Vector2(-w*.5f+cut,-d*.5f),
                    new Vector2(w*.5f,-d*.5f),
                    new Vector2(w*.5f,d*.5f),
                    new Vector2(-w*.5f,d*.5f),
                    new Vector2(-w*.5f,-d*.5f+cut)
                };
                if(register!=null)
                {
                    var vertices=new Vector3[outline.Length*2];
                    for(int i=0;i<outline.Length;i++)
                    {
                        float x=outline[i].x,cornerZ=outline[i].y;
                        float px=ox+Mathf.Cos(r)*x+Mathf.Sin(r)*cornerZ;
                        float pz=oz-Mathf.Sin(r)*x+Mathf.Cos(r)*cornerZ;
                        vertices[i]=new Vector3(px,bottom,pz);
                        vertices[i+outline.Length]=new Vector3(px,bottom+h,pz);
                    }
                    var triangles=new System.Collections.Generic.List<int>();
                    for(int i=1;i<outline.Length-1;i++)
                    {
                        triangles.Add(0);triangles.Add(i+1);triangles.Add(i);
                        triangles.Add(outline.Length);triangles.Add(outline.Length+i);triangles.Add(outline.Length+i+1);
                    }
                    for(int i=0;i<outline.Length;i++)
                    {
                        int j=(i+1)%outline.Length;
                        triangles.Add(i);triangles.Add(j);triangles.Add(outline.Length+j);
                        triangles.Add(i);triangles.Add(outline.Length+j);triangles.Add(outline.Length+i);
                    }
                    var go=new GameObject(id+"_clippedCorner",typeof(MeshFilter),typeof(MeshRenderer));
                    var mesh=new Mesh{vertices=vertices,triangles=triangles.ToArray()};
                    mesh.RecalculateNormals();mesh.RecalculateBounds();
                    go.GetComponent<MeshFilter>().sharedMesh=mesh;
                    var shader=Shader.Find("Universal Render Pipeline/Lit")??Shader.Find("Standard");
                    var mat=new Material(shader);mat.color=body;
                    if(mat.HasProperty("_BaseColor"))mat.SetColor("_BaseColor",body);
                    go.GetComponent<MeshRenderer>().sharedMaterial=mat;
                    register(go);
                }
                // Solid visible side panels even when adjacent cabinet N is hidden.
                box("cornerRightFrontSide",cut*.5f,h*.5f,-d*.5f+t*.5f,w-cut,h,t,body);
                box("cornerLeftSide",-w*.5f+t*.5f,h*.5f,cut*.5f,t,h,d-cut,body);
                float doorLength=Mathf.Sqrt(2f)*cut;
                float doorX=-w*.5f+cut*.5f,doorZ=-d*.5f+cut*.5f;
                float doorAngle=angle+45f;
                cube(id+"_diagonalDoor",
                    new Vector3(ox+Mathf.Cos(r)*doorX+Mathf.Sin(r)*doorZ,
                        bottom+h*.5f,oz-Mathf.Sin(r)*doorX+Mathf.Cos(r)*doorZ),
                    new Vector3(doorLength-.004f,h-.004f,frontT),front,doorAngle);
                return;
            }
            if(id=="E" && N(e,"fillGap",0)>0)
            {
                float extra=N(e,"fillGap")/1000f;
                // Extend E's visible filler front toward the sink unit D.
                box("gapFill",-(w+extra)*.5f,h*.5f,-d*.5f-.01f,extra,h,.02f,front);
            }
            if(style=="fridge")
            {
                box("inoxBody",0,h*.5f,0,w,h,d,metal);
                box("upperDoor",0,1.30f,-d*.5f-.012f,w-.012f,1.08f,.025f,metal);
                box("freezerDoor",0,.38f,-d*.5f-.012f,w-.012f,.70f,.025f,metal);
                box("handles",w*.34f,1.17f,-d*.5f-.04f,.012f,.48f,.014f,dark);
                return;
            }
            if(plinth>0)box("plinth",0,plinth*.5f,-d*.5f+.02f,w,plinth,.018f,body);
            float y0=plinth,inner=h-plinth;
            box("left", -w*.5f+t*.5f,y0+inner*.5f,0,t,inner,d,body);
            box("right",w*.5f-t*.5f,y0+inner*.5f,0,t,inner,d,body);
            box("bottom",0,y0+t*.5f,0,w-2*t,t,d,body);
            box("top",0,h-t*.5f,0,w-2*t,t,d,body);
            box("back",0,y0+inner*.5f,d*.5f-.003f,w-2*t,inner,.006f,body);
            int shelves=(int)N(e,"shelves");
            for(int i=1;i<=shelves;i++)box("shelf"+i,0,y0+inner*i/(shelves+1),0,w-2*t,t,d-.025f,body);
            float z=-d*.5f-frontT*.5f;
            if(style=="oven")
            {
                box("drawer",0,y0+.075f,z,w-.004f,.148f,frontT,front);
                box("blackOven",0,y0+.15f+(inner-.15f)*.5f,z,w-.005f,inner-.16f,.025f,dark);
                box("ovenGlass",0,y0+.15f+(inner-.15f)*.48f,z-.015f,w-.09f,inner-.29f,.005f,C("#30373C",dark));
                box("ovenHandle",0,h-.08f,z-.025f,w-.11f,.012f,.025f,metal);
            }
            else if(style=="drawers"||style=="sink")
            {
                int count=style=="sink"?2:3;
                float[] heights=style=="sink"?new[]{.60f,.18f}:new[]{.26f,.26f,inner-.52f};
                float yy=y0;
                for(int i=0;i<count;i++)
                {
                    float hh=heights[i];float dw=style=="sink"?N(e,"drawerFrontWidth",340)/1000f:w-.004f;float dx=style=="sink"?-w*.5f+dw*.5f+.002f:0;box("drawer"+i,dx,yy+hh*.5f,z,dw,hh-.003f,frontT,front);
                    box("handle"+i,style=="sink"?-w*.5f+N(e,"drawerFrontWidth",340)/2000f:0,yy+hh*.5f,z-.02f,.12f,.009f,.012f,dark);yy+=hh;
                }
            }
            else if(style=="double")
            {
                box("doorLeft",-w*.25f,y0+inner*.5f,z,w*.5f-.003f,inner-.004f,frontT,front);
                box("doorRight",w*.25f,y0+inner*.5f,z,w*.5f-.003f,inner-.004f,frontT,front);
            }
            else
                box("front",0,y0+inner*.5f,z,w-.004f,inner-.004f,frontT,front);
        }
    }
}