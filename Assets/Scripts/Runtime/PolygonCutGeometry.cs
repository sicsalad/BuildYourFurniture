using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using UnityEngine;

namespace BuildYourFurniture
{
    // Edge banding of an orthogonal polygon: each FINISHED edge is offset inward.
    // The intersections of adjacent offset lines form the raw CNC outline.
    public static class PolygonCutGeometry
    {
        static string S(XElement e,string name,string fallback="")=>(string)e?.Attribute(name)??fallback;
        static float N(XElement e,string name)=>float.Parse(S(e,name,"0"),CultureInfo.InvariantCulture);
        public static List<Vector2> Finished(XElement part)
        {
            var outline=part.Element("outline")??throw new InvalidOperationException("Missing polygon outline");
            return outline.Elements("point").Select(p=>new Vector2(N(p,"x"),N(p,"z"))).ToList();
        }
        public static List<Vector2> Raw(XElement part)
        {
            var vertices=Finished(part);
            var pointIds=part.Element("outline").Elements("point").Select(p=>S(p,"id")).ToList();
            int n=vertices.Count;
            if(n<4)throw new InvalidOperationException("Polygon needs at least four vertices");
            var bands=part.Element("edgeBands")?.Elements("edge").ToList()??new List<XElement>();
            float area=0;
            for(int i=0;i<n;i++)area+=vertices[i].x*vertices[(i+1)%n].y-vertices[(i+1)%n].x*vertices[i].y;
            if(Math.Abs(area)<.001f)throw new InvalidOperationException("Degenerate polygon");
            var lines=new List<Tuple<Vector2,Vector2>>();
            for(int i=0;i<n;i++)
            {
                int next=(i+1)%n;
                Vector2 a=vertices[i],b=vertices[next],direction=b-a;
                if(direction.sqrMagnitude<.00001f)throw new InvalidOperationException("Zero length polygon edge");
                var band=bands.FirstOrDefault(e=>S(e,"from")==pointIds[i]&&S(e,"to")==pointIds[next]);
                float thickness=band==null?0:N(band,"thickness");
                if(thickness<0)throw new InvalidOperationException("Negative edge band");
                Vector2 inward=area>0?new Vector2(-direction.y,direction.x):new Vector2(direction.y,-direction.x);
                inward.Normalize();
                lines.Add(Tuple.Create(a+inward*thickness,b+inward*thickness));
            }
            var result=new List<Vector2>();
            for(int i=0;i<n;i++)
            {
                var previous=lines[(i+n-1)%n];
                var current=lines[i];
                Vector2 u=previous.Item2-previous.Item1,v=current.Item2-current.Item1;
                float cross=u.x*v.y-u.y*v.x;
                if(Math.Abs(cross)<.00001f)throw new InvalidOperationException("Adjacent edges are parallel");
                Vector2 delta=current.Item1-previous.Item1;
                float t=(delta.x*v.y-delta.y*v.x)/cross;
                result.Add(previous.Item1+u*t);
            }
            return result;
        }
        public static string RawOutline(XElement part)
        {
            return string.Join(" | ",Raw(part).Select(p=>p.x.ToString("0.###",CultureInfo.InvariantCulture)+","+
                p.y.ToString("0.###",CultureInfo.InvariantCulture)).ToArray());
        }
    }
}
