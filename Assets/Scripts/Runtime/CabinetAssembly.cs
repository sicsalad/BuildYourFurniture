using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using UnityEngine;

namespace BuildYourFurniture
{
    // XML constraints resolve local panel centers from named attachment points.
    // Coordinates are mm, before the cabinet instance's rotation and translation.
    public static class CabinetAssembly
    {
        static string S(XElement e,string name,string fallback="")=>e==null?fallback:(string)e.Attribute(name)??fallback;
        static Vector3 P(XElement e,Dictionary<string,float> vars)
        {
            return new Vector3(CabinetTypeEngine.Evaluate(S(e,"x","0"),vars),
                CabinetTypeEngine.Evaluate(S(e,"y","0"),vars),
                CabinetTypeEngine.Evaluate(S(e,"z","0"),vars));
        }
        static Vector3 Offset(string face,Vector3 size)
        {
            switch(face)
            {
                case "Center":return Vector3.zero;
                case "Bottom":return Vector3.down*size.y*.5f;
                case "Top":return Vector3.up*size.y*.5f;
                case "Left":return Vector3.left*size.x*.5f;
                case "Right":return Vector3.right*size.x*.5f;
                case "Front":return Vector3.back*size.z*.5f;
                case "Back":return Vector3.forward*size.z*.5f;
                default:throw new InvalidOperationException("Unknown face: "+face);
            }
        }
        static Vector3 Size(XElement part,Dictionary<string,float> vars)
        {
            return new Vector3(CabinetTypeEngine.Evaluate(S(part,"width"),vars),
                CabinetTypeEngine.Evaluate(S(part,"height"),vars),
                CabinetTypeEngine.Evaluate(S(part,"depth"),vars));
        }
        public static Dictionary<string,Vector3> Resolve(XElement type,XElement cabinet,Dictionary<string,float> vars)
        {
            var parts=type.Element("elements").Elements("element").ToDictionary(e=>S(e,"id"));
            var result=new Dictionary<string,Vector3>();
            var visiting=new HashSet<string>();
            Func<string,Vector3> solve=null;
            solve=id=>
            {
                Vector3 known;
                if(result.TryGetValue(id,out known))return known;
                XElement part;
                if(!parts.TryGetValue(id,out part))throw new InvalidOperationException("Unknown assembly element: "+id);
                if(!visiting.Add(id))throw new InvalidOperationException("Cyclic assembly constraint at "+id);
                var change=cabinet.Element("elements")?.Elements("element").FirstOrDefault(e=>S(e,"id")==id);
                var placement=change?.Element("placement")??part.Element("placement");
                Vector3 center;
                if(placement==null)center=P(part,vars);
                else
                {
                    var attach=placement.Element("attach");
                    if(attach==null)throw new InvalidOperationException("Missing attach in "+id);
                    string reference=S(attach,"to");
                    var segments=reference.Split('.');
                    if(segments.Length!=2)throw new InvalidOperationException("Invalid attachment "+reference);
                    var target=parts[segments[0]];
                    var targetCenter=solve(segments[0]);
                    Vector3 targetSize=Size(target,vars);
                    Vector3 targetPoint;
                    var anchor=target.Element("anchors")?.Elements("anchor").FirstOrDefault(e=>S(e,"id")==segments[1]);
                    if(anchor!=null)targetPoint=targetCenter+P(anchor,vars);
                    else
                    {
                        if(S(target,"kind")=="polygonPanel"||S(target,"kind")=="lShapePanel")
                        {
                            if(segments[1]=="Top")targetPoint=targetCenter+Vector3.up*targetSize.y;
                            else if(segments[1]=="Bottom")targetPoint=targetCenter;
                            else targetPoint=targetCenter+Offset(segments[1],targetSize);
                        }
                        else targetPoint=targetCenter+Offset(segments[1],targetSize);
                    }
                    Vector3 ownSize=Size(part,vars);
                    Vector3 ownPoint=Offset(S(attach,"my","Center"),ownSize);
                    center=targetPoint-ownPoint+P(attach,vars);
                }
                visiting.Remove(id);
                result[id]=center;
                return center;
            };
            foreach(string id in parts.Keys)solve(id);
            return result;
        }
    }
}
