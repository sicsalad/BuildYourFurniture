using System;
using System.Globalization;
using System.Xml.Linq;
using UnityEngine;

namespace BuildYourFurniture
{
    // L-shaped corner cabinet, all XML dimensions are finished outer dimensions in mm.
    // Local X/Z define the L footprint; local origin is its outside back corner.
    public static class CornerCabinetRenderer
    {
        static float N(XElement e,string k,float fallback=0)
        {float v;return e!=null&&float.TryParse((string)e.Attribute(k),NumberStyles.Float,CultureInfo.InvariantCulture,out v)?v:fallback;}
        static string S(XElement e,string k,string fallback="")=>e==null?fallback:(string)e.Attribute(k)??fallback;
        static Color C(string s,Color fallback){Color c;return ColorUtility.TryParseHtmlString(s,out c)?c:fallback;}
        public static void Build(XElement e,XElement defaults,Action<string,Vector3,Vector3,Color,float> cube)
        {
            string id=S(e,"id");
            float l=N(e,"leftLength",950)*.001f,r=N(e,"rightLength",950)*.001f;
            float ld=N(e,"leftDepth",560)*.001f,rd=N(e,"rightDepth",590)*.001f;
            float h=N(e,"height",860)*.001f,p=N(e,"plinth",80)*.001f;
            float t=N(e,"boardThickness",N(defaults,"boardThickness",18))*.001f;
            float brace=N(e,"braceWidth",100)*.001f;
            if(l<=ld+t||r<=rd+t||h<=p+t+brace){Debug.LogWarning("Invalid corner cabinet dimensions: "+id);return;}
            float angle=N(e,"rotation"),a=angle*Mathf.Deg2Rad;
            Vector3 origin=new Vector3(N(e,"x")*.001f,0,N(e,"y")*.001f);
            Color body=C(S(e,"bodyColor",S(defaults,"bodyColor","#20385B")),Color.blue);
            Color front=C(S(e,"frontColor",S(defaults,"frontColor","#20385B")),body);
            Color plinth=C(S(e,"plinthColor",S(defaults,"plinthColor","#A8A9AA")),Color.gray);
            Func<float,float,float,Vector3> point=(x,y,z)=>origin+new Vector3(Mathf.Cos(a)*x+Mathf.Sin(a)*z,y,-Mathf.Sin(a)*x+Mathf.Cos(a)*z);
            Action<string,float,float,float,float,float,float,Color> box=(n,x,y,z,sx,sy,sz,c)=>cube(id+"_"+n,point(x,y,z),new Vector3(sx,sy,sz),c,angle);
            // Footprint: X arm extends l, Z arm extends r, overlap is ld x rd.
            // A single L-shaped bottom is represented by two non-overlapping rectangular panels.
            float baseY=p+t*.5f;
            box("bottomLeftArm",l*.5f,baseY,rd*.5f,l,t,rd,body);
            box("bottomRightArm",ld*.5f,baseY,rd+(r-rd)*.5f,ld,t,r-rd,body);
            float sideH=h-p-t;
            box("leftSide",l-t*.5f,p+t+sideH*.5f,rd*.5f,t,sideH,rd,body);
            box("rightSide",ld*.5f,p+t+sideH*.5f,r-t*.5f,ld,sideH,t,body);
            // R1/R2: upright internal corner posts, 100 mm face, stopping below top braces.
            float postH=h-p-t-brace;
            box("R1",ld-brace*.5f,p+t+postH*.5f,rd-t*.5f,brace,postH,t,body);
            box("R2",ld-t*.5f,p+t+postH*.5f,rd-brace*.5f,t,postH,brace,body);
            // K1/K2 vertical-plane top rear braces; S1/S2 horizontal-plane front braces.
            box("K1",l*.5f,h-brace*.5f,t*.5f,l-t,brace,t,body);
            box("K2",t*.5f,h-brace*.5f,r*.5f,t,brace,r-2*t,body);
            box("S1",(l+t)*.5f,h-t*.5f,rd-t*.5f,l-t,t,brace,body);
            box("S2",ld-t*.5f,h-t*.5f,(r+rd)*.5f,brace,t,r-rd,body);
            if(p>0)
            {
                box("plinthLeft",l*.5f,p*.5f,rd-.018f,l,p,.018f,plinth);
                box("plinthRight",ld-.018f,p*.5f,rd+(r-rd)*.5f,.018f,p,r-rd,plinth);
            }
            // Preserve the former F05 drawer as a configurable front on the right arm.
            var drawer=e.Element("drawer");
            if(drawer!=null)
            {
                float width=N(drawer,"width",390)*.001f;
                float dh=Mathf.Min(N(drawer,"height",780)*.001f,sideH-.004f);
                float gap=N(defaults,"drawerGap",2)*.001f;
                float start=N(drawer,"offset",0)*.001f;
                box("cornerDrawerFront",ld-.014f,p+t+dh*.5f,rd+start+width*.5f,
                    .018f,dh-gap,width-gap,front);
                box("cornerDrawerHandle",ld-.028f,p+t+dh*.5f,rd+start+width*.5f,
                    .012f,.01f,.12f,C(S(defaults,"handleColor","#101114"),Color.black));
            }
        }
    }
}
