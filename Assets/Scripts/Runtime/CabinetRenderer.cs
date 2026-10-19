using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using UnityEngine;

namespace BuildYourFurniture
{
    // All lengths in the XML are millimetres; geometry is generated in Unity metres.
    // The cabinet's local -Z is its front. Its local +X is the right side when viewed from the front.
    public static class CabinetRenderer
    {
        static float N(XElement e,string key,float fallback=0f)
        {
            if(e==null)return fallback;
            float value;
            return float.TryParse((string)e.Attribute(key),NumberStyles.Float,CultureInfo.InvariantCulture,out value)?value:fallback;
        }
        static string S(XElement e,string key,string fallback="") => e==null?fallback:(string)e.Attribute(key)??fallback;
        static Color ColorOf(string text,Color fallback)
        {
            Color c;
            return ColorUtility.TryParseHtmlString(text,out c)?c:fallback;
        }
        static float M(float mm)=>mm*.001f;

        public static void Build(XDocument plan,Action<string,Vector3,Vector3,Color,float> cube)
        {
            if(plan?.Root==null)return;
            var root=plan.Root;
            var section=root.Element("cabinetry");
            if(section==null)return;
            var defaults=section.Element("defaults");
            var cabinets=section.Elements("cabinet").ToList();
            var byId=cabinets.ToDictionary(e=>S(e,"id"));
            var placement=new Dictionary<string,Vector2>();
            var angles=new Dictionary<string,float>();
            var resolving=new HashSet<string>();
            Color defaultBody=ColorOf(S(defaults,"bodyColor","#20385B"),new Color(.12f,.22f,.36f));
            Color defaultFront=ColorOf(S(defaults,"frontColor","#20385B"),defaultBody);
            Color defaultPlinth=ColorOf(S(defaults,"plinthColor","#A8A9AA"),Color.gray);
            Color defaultHandle=ColorOf(S(defaults,"handleColor","#101114"),Color.black);
            float tDefault=M(N(defaults,"boardThickness",18));
            float backDefault=M(N(defaults,"backThickness",6));
            float braceDefault=M(N(defaults,"braceWidth",100));
            float gapDefault=M(N(defaults,"drawerGap",2));
            float bandDefault=M(N(defaults,"edgeBandThickness",2));
            foreach(var e in cabinets)
            {
                Resolve(e,byId,placement,angles,resolving);
            }
            foreach(var e in cabinets)
            {
                string id=S(e,"id");
                Vector2 origin=placement[id];
                float angle=angles[id], rad=angle*Mathf.Deg2Rad;
                float w=M(N(e,"width",600)),h=M(N(e,"height",860)),d=M(N(e,"depth",560));
                float plinth=M(N(e,"plinth",S(e,"type")=="empty"?0:80));
                float t=M(N(e,"boardThickness",tDefault*1000));
                float back=M(N(e,"backThickness",backDefault*1000));
                float brace=M(N(e,"braceWidth",braceDefault*1000));
                float gap=M(N(e,"drawerGap",gapDefault*1000));
                float band=M(N(e,"edgeBandThickness",bandDefault*1000));
                float bodyHeight=h-plinth;
                if(w<=2*t||h<=plinth||d<=back) {Debug.LogWarning("Invalid cabinet dimensions: "+id);continue;}
                Color body=ColorOf(S(e,"bodyColor"),defaultBody);
                Color front=ColorOf(S(e,"frontColor"),defaultFront);
                Color plinthColor=ColorOf(S(e,"plinthColor"),defaultPlinth);
                Color handle=ColorOf(S(e,"handleColor"),defaultHandle);
                Func<float,float,float,Vector3> position=(x,y,z)=>new Vector3(
                    origin.x+Mathf.Cos(rad)*x+Mathf.Sin(rad)*z,
                    y,
                    origin.y-Mathf.Sin(rad)*x+Mathf.Cos(rad)*z);
                Action<string,float,float,float,float,float,float,Color> box=(name,x,y,z,sx,sy,sz,color)=>
                    cube(id+"_"+name,position(x,y,z),new Vector3(sx,sy,sz),color,angle);
                // The exposed edge bands add up to the nominal outside dimensions.
                box("leftSide", -w*.5f+t*.5f,plinth+bodyHeight*.5f,0,t,bodyHeight,d,body);
                box("rightSide",w*.5f-t*.5f,plinth+bodyHeight*.5f,0,t,bodyHeight,d,body);
                box("backboard",0,plinth+bodyHeight*.5f,d*.5f-back*.5f,w-2*t,bodyHeight,back,ColorOf(S(e,"backColor","#B5AB98"),body));
                bool empty=S(e,"type")=="empty";
                if(!empty)
                    box("bottomPanel",0,plinth+t*.5f,0,w-2*t,t,d-back,body);
                // Two 100 mm upper cross braces, front and rear.
                box("frontTopBrace",0,h-t*.5f,-d*.5f+brace*.5f,w-2*t,t,brace,body);
                box("rearTopBrace",0,h-t*.5f,d*.5f-brace*.5f,w-2*t,t,brace,body);
                if(plinth>0)
                    box("plinth",0,plinth*.5f,-d*.5f+.015f,w,plinth,.018f,plinthColor);
                var sections=e.Element("sections")?.Elements("section").ToList()??new List<XElement>();
                float sectionBottom=plinth;
                for(int i=0;i<sections.Count;i++)
                {
                    var part=sections[i];
                    float sectionHeight=M(N(part,"height"));
                    if(sectionHeight<=0)continue;
                    string type=S(part,"type","drawer");
                    float top=sectionBottom+sectionHeight;
                    if(type=="drawer")
                    {
                        Color drawerColor=ColorOf(S(part,"color"),front);
                        box("drawerFront"+i,0,sectionBottom+sectionHeight*.5f,-d*.5f-.009f,
                            w-gap,Mathf.Max(.005f,sectionHeight-gap),.018f,drawerColor);
                        float handleWidth=M(N(part,"handleWidth",N(defaults,"handleWidth",120)));
                        float handleDiameter=M(N(part,"handleDiameter",N(defaults,"handleDiameter",10)));
                        Color handleColor=ColorOf(S(part,"handleColor"),handle);
                        bool vertical=S(part,"handleOrientation",S(e,"handleOrientation","horizontal"))=="vertical";
                        box("handle"+i,0,sectionBottom+sectionHeight*.5f,-d*.5f-.028f,
                            vertical?handleDiameter:handleWidth,vertical?handleWidth:handleDiameter,
                            handleDiameter,handleColor);
                    }
                    else if(type=="oven")
                    {
                        // Two full-depth horizontal shelves replace cross braces above/below an oven.
                        box("ovenLowerShelf"+i,0,sectionBottom+t*.5f,0,w-2*t,t,d-back,body);
                        box("ovenUpperShelf"+i,0,top-t*.5f,0,w-2*t,t,d-back,body);
                        box("ovenBody"+i,0,sectionBottom+sectionHeight*.5f,0,w-2*t,Mathf.Max(.01f,sectionHeight-2*t),d-back,
                            ColorOf(S(part,"color","#151515"),Color.black));
                    }
                    else if(type=="dishwasher"||type=="appliance")
                    {
                        box("applianceBody"+i,0,sectionBottom+sectionHeight*.5f,0,w-.012f,sectionHeight,d-.02f,
                            ColorOf(S(part,"color","#474747"),Color.gray));
                    }
                    if(i<sections.Count-1 && type!="oven" && S(sections[i+1],"type")!="oven")
                        box("sectionBrace"+i,0,top-t*.5f,-d*.5f+brace*.5f,w-2*t,t,brace,body);
                    sectionBottom=top;
                }
                if(empty)
                {
                    var appliance=e.Element("appliance");
                    if(appliance!=null)
                    {
                        float aw=M(N(appliance,"width",595)),ah=M(N(appliance,"height",845)),ad=M(N(appliance,"depth",510));
                        box("insertedAppliance",0,ah*.5f,0,aw,ah,ad,
                            ColorOf(S(appliance,"color","#343438"),Color.gray));
                    }
                }
                // XML edge-band declarations are retained as manufacturing metadata.
                // Rendering visible edge-band strips will be implemented with dedicated meshes.
                if(band<0)Debug.LogWarning("Negative edge band: "+id);
            }

        }
        static void Resolve(XElement e,Dictionary<string,XElement> byId,
            Dictionary<string,Vector2> placements,Dictionary<string,float> angles,HashSet<string> resolving)
        {
            string id=S(e,"id");
            if(placements.ContainsKey(id))return;
            if(!resolving.Add(id))throw new InvalidOperationException("Circular cabinet placement: "+id);
            float angle=N(e,"rotation");
            Vector2 p=new Vector2(M(N(e,"x")),M(N(e,"y")));
            string reference=S(e,"alignTo");
            if(reference.Length>0)
            {
                XElement other;
                if(!byId.TryGetValue(reference,out other))throw new InvalidOperationException("Unknown cabinet: "+reference);
                Resolve(other,byId,placements,angles,resolving);
                float otherAngle=angles[reference]*Mathf.Deg2Rad;
                float otherWidth=M(N(other,"width"));
                float ownWidth=M(N(e,"width"));
                float side=S(e,"side","left")=="left"?-1f:1f;
                // Relative alignment is in the referenced cabinet's own left/right axis.
                Vector2 localRight=new Vector2(Mathf.Cos(otherAngle),-Mathf.Sin(otherAngle));
                p=placements[reference]+localRight*(side*(otherWidth+ownWidth)*.5f);
                // A corner may explicitly override rotation and provide a position offset.
                p+=new Vector2(M(N(e,"offsetX")),M(N(e,"offsetY")));
                angle=N(e,"rotation",angles[reference]);
            }
            placements[id]=p;
            angles[id]=angle;
            resolving.Remove(id);
        }
    }
}
