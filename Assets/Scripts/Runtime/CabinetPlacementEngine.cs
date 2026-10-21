using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using UnityEngine;

namespace BuildYourFurniture
{
    // Plan coordinates are millimetres, room coordinates are offsets from the room's first point.
    // A cabinet's X/Y is its local origin; docking can reference a named element's bounding face.
    public static class CabinetPlacementEngine
    {
        static string S(XElement e,string k,string fallback="")=>e==null?fallback:(string)e.Attribute(k)??fallback;
        static float N(XElement e,string k,float fallback=0){float v;return e!=null&&float.TryParse((string)e.Attribute(k),NumberStyles.Float,CultureInfo.InvariantCulture,out v)?v:fallback;}
        static string F(float v)=>Math.Round(v,3).ToString("0.###",CultureInfo.InvariantCulture);
        static Vector2 Rotate(Vector2 v,float degrees){float a=degrees*Mathf.Deg2Rad;return new Vector2(v.x*Mathf.Cos(a)+v.y*Mathf.Sin(a),-v.x*Mathf.Sin(a)+v.y*Mathf.Cos(a));}
        static Vector2 RoomOrigin(XDocument doc,string roomId)
        {
            var room=doc.Root.Element("rooms")?.Elements("room").FirstOrDefault(e=>S(e,"id")==roomId);
            var point=room?.Element("point");
            if(point==null)throw new InvalidOperationException("Unknown room: "+roomId);
            return new Vector2(N(point,"x"),N(point,"y"));
        }
        static Vector2 Position(XElement e,XDocument doc)
        {
            var node=e.Element("position");
            if(node==null)return new Vector2(N(e,"x"),N(e,"y"));
            string mode=S(node,"mode","plan");
            var fixedNode=node.Element(mode);
            if(fixedNode==null)throw new InvalidOperationException("Missing active position: "+S(e,"id"));
            Vector2 p=new Vector2(N(fixedNode,"x"),N(fixedNode,"y"));
            return mode=="room"?p+RoomOrigin(doc,S(node,"roomId",S(e,"roomId","R01"))):p;
        }
        static float Angle(XElement e)=>N(e.Element("orientation"),"rotation",N(e,"rotation"));
        static float Dim(XElement e,string key,float fallback=0)=>N(e,key,fallback);
        static Vector2 ElementCenter(XElement cabinet,string elementId)
        {
            if(string.IsNullOrEmpty(elementId)||elementId=="CABINET")return Vector2.zero;
            string typeId=S(cabinet,"typeId",S(cabinet,"typeRef",S(cabinet,"type")));
            var root=CabinetTypeEngine.Load(typeId).Root;
            var element=root.Element("elements")?.Elements("element").FirstOrDefault(p=>S(p,"id")==elementId);
            if(element==null)throw new InvalidOperationException("Unknown docking element "+elementId+" in "+S(cabinet,"id"));
            var vars=CabinetTypeEngine.Variables(root,cabinet);
            return new Vector2(CabinetTypeEngine.Evaluate(S(element,"x","0"),vars),
                CabinetTypeEngine.Evaluate(S(element,"z","0"),vars));
        }
        static Vector2 Face(XElement cabinet,string elementId,string face)
        {
            Vector2 center=ElementCenter(cabinet,elementId);
            if(string.IsNullOrEmpty(elementId)||elementId=="CABINET")
            {
                float width=Dim(cabinet,"width",Dim(cabinet,"leftLength",600));
                float depth=Dim(cabinet,"depth",Dim(cabinet,"rightLength",560));
                return center+FaceOffset(width,depth,face);
            }
            var root=CabinetTypeEngine.Load(S(cabinet,"typeId",S(cabinet,"typeRef",S(cabinet,"type")))).Root;
            var el=root.Element("elements").Elements("element").First(p=>S(p,"id")==elementId);
            var vars=CabinetTypeEngine.Variables(root,cabinet);
            float w=CabinetTypeEngine.Evaluate(S(el,"width"),vars),d=CabinetTypeEngine.Evaluate(S(el,"depth"),vars);
            return center+FaceOffset(w,d,face);
        }
        static Vector2 FaceOffset(float w,float d,string face)
        {
            switch(face){case "left":return new Vector2(-w/2,0);case "right":return new Vector2(w/2,0);
                case "front":return new Vector2(0,-d/2);case "back":return new Vector2(0,d/2);
                case "center":return Vector2.zero;default:throw new InvalidOperationException("Invalid docking face: "+face);}
        }
        public static bool Resolve(XDocument doc)
        {
            var cabinets=doc.Root.Element("cabinetry")?.Elements("cabinet").ToList();
            if(cabinets==null)return false;
            var byId=cabinets.ToDictionary(e=>S(e,"id"));
            var done=new Dictionary<string,Vector2>();var angles=new Dictionary<string,float>();
            var visiting=new HashSet<string>();
            Func<XElement,Vector2> solve=null;
            solve=e=>{
                string id=S(e,"id");if(done.ContainsKey(id))return done[id];
                if(!visiting.Add(id))throw new InvalidOperationException("Cyclic cabinet docking: "+id);
                float angle=Angle(e);
                var orientation=e.Element("orientation");
                if(orientation!=null&&S(orientation,"mode")=="alignment")
                {
                    var align=orientation.Element("alignment");
                    if(align==null)throw new InvalidOperationException("Missing orientation alignment: "+id);
                    XElement reference;if(!byId.TryGetValue(S(align,"cabinetId"),out reference))throw new InvalidOperationException("Unknown orientation reference: "+id);
                    solve(reference);angle=angles[S(reference,"id")]+N(align,"rotationOffset");
                }
                angles[id]=angle;
                Vector2 p;
                var dock=e.Element("docking");
                var position=e.Element("position");
                bool activeDock=dock!=null&&S(dock,"active","false")=="true";
                if(activeDock)
                {
                    XElement other;if(!byId.TryGetValue(S(dock,"cabinetId"),out other))throw new InvalidOperationException("Unknown docking cabinet: "+id);
                    Vector2 target=solve(other);
                    float otherAngle=angles[S(other,"id")];
                    string targetElement=S(dock,"targetElement","CABINET"), ownElement=S(dock,"element","CABINET");
                    Vector2 targetFace=Face(other,targetElement,S(dock,"targetFace","left"));
                    Vector2 ownFace=Face(e,ownElement,S(dock,"face","right"));
                    p=target+Rotate(targetFace,otherAngle)-Rotate(ownFace,angle);
                    p+=Rotate(new Vector2(N(dock,"offsetX"),N(dock,"offsetY")),otherAngle);
                }
                else if(position!=null)p=Position(e,doc);
                else if(!string.IsNullOrEmpty(S(e,"alignTo")))
                {
                    XElement other;if(!byId.TryGetValue(S(e,"alignTo"),out other))throw new InvalidOperationException("Unknown alignment target: "+id);
                    Vector2 target=solve(other);
                    float sign=S(e,"side","left")=="left"?-1:1;
                    float ow=Dim(other,"width",600),w=Dim(e,"width",600);
                    p=target+Rotate(new Vector2(sign*(ow+w)/2,0),angles[S(other,"id")]);
                    p+=new Vector2(N(e,"offsetX"),N(e,"offsetY"));
                }
                else p=new Vector2(N(e,"x"),N(e,"y"));
                visiting.Remove(id);done[id]=p;return p;
            };
            foreach(var e in cabinets)solve(e);
            bool changed=false;
            foreach(var e in cabinets)
            {
                string id=S(e,"id");Vector2 p=done[id];float angle=angles[id];
                changed|=Set(e,"x",F(p.x));changed|=Set(e,"y",F(p.y));changed|=Set(e,"rotation",F(angle));
                var position=e.Element("position");
                if(position!=null)
                {
                    string mode=S(position,"mode","plan");
                    if(mode!="plan"&&mode!="room")throw new InvalidOperationException("Invalid position mode: "+id);
                    Vector2 room=p-RoomOrigin(doc,S(position,"roomId",S(e,"roomId","R01")));
                    var planNode=Ensure(position,"plan");var roomNode=Ensure(position,"room");
                    changed|=Set(planNode,"x",F(p.x));changed|=Set(planNode,"y",F(p.y));
                    changed|=Set(roomNode,"x",F(room.x));changed|=Set(roomNode,"y",F(room.y));
                }
                var orientation=e.Element("orientation");
                if(orientation!=null)changed|=Set(orientation,"rotation",F(angle));
            }
            return changed;
        }
        static XElement Ensure(XElement parent,string name){var child=parent.Element(name);if(child==null){child=new XElement(name);parent.Add(child);}return child;}
        static bool Set(XElement e,string name,string value){if(S(e,name)==value)return false;e.SetAttributeValue(name,value);return true;}
    }
}
