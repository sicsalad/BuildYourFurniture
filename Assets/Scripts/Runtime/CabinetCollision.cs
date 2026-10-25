using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BuildYourFurniture
{
    // Exact convex-prism SAT: triangulates each concave finished outline so L cutouts stay empty.
    // All distances in millimetres; touching faces are allowed.
    public static class CabinetCollision
    {
        const float Tolerance=.02f;
        static float Cross(Vector2 a,Vector2 b)=>a.x*b.y-a.y*b.x;
        static bool Inside(Vector2 p,Vector2 a,Vector2 b,Vector2 c)=>
            Cross(b-a,p-a)>=-0.0001f&&Cross(c-b,p-b)>=-0.0001f&&Cross(a-c,p-c)>=-0.0001f;
        static List<Vector2[]> Triangles(Vector2[] polygon)
        {
            if(polygon.Length<3)throw new ArgumentException("Invalid polygon");
            var points=polygon.ToArray();
            float area=0;
            for(int i=0;i<points.Length;i++)area+=Cross(points[i],points[(i+1)%points.Length]);
            if(area<0)Array.Reverse(points);
            var indices=Enumerable.Range(0,points.Length).ToList();
            var triangles=new List<Vector2[]>();
            int guard=0;
            while(indices.Count>3&&guard++<points.Length*points.Length)
            {
                bool found=false;
                for(int i=0;i<indices.Count;i++)
                {
                    int a=indices[(i+indices.Count-1)%indices.Count],b=indices[i],c=indices[(i+1)%indices.Count];
                    if(Cross(points[b]-points[a],points[c]-points[b])<.00001f)continue;
                    if(indices.Any(j=>j!=a&&j!=b&&j!=c&&Inside(points[j],points[a],points[b],points[c])))continue;
                    triangles.Add(new[]{points[a],points[b],points[c]});
                    indices.RemoveAt(i);found=true;break;
                }
                if(!found)throw new ArgumentException("Cannot triangulate cabinet outline");
            }
            if(indices.Count==3)triangles.Add(indices.Select(i=>points[i]).ToArray());
            return triangles;
        }
        static Vector3[] Prism(Vector2[] triangle,CabinetDesignerDocument.PanelElement panel)
        {
            var rotation=Quaternion.Euler(panel.Rotation);
            var points=new Vector3[6];
            for(int i=0;i<3;i++)
            {
                var basePoint=new Vector3(triangle[i].x,0,triangle[i].y);
                points[i]=panel.Position+rotation*basePoint;
                points[i+3]=panel.Position+rotation*(basePoint+Vector3.up*panel.Thickness);
            }
            return points;
        }
        static List<Vector3> Axes(Vector3[] vertices)
        {
            var axes=new List<Vector3>();
            var edges=new[]{vertices[1]-vertices[0],vertices[2]-vertices[1],
                vertices[0]-vertices[2],vertices[3]-vertices[0]};
            axes.Add(Vector3.Cross(edges[0],edges[1]));
            for(int i=0;i<3;i++)axes.Add(Vector3.Cross(edges[i],edges[3]));
            return axes;
        }
        static bool Overlap(Vector3[] a,Vector3[] b)
        {
            var axes=Axes(a);
            axes.AddRange(Axes(b));
            var ea=new[]{a[1]-a[0],a[2]-a[1],a[0]-a[2],a[3]-a[0]};
            var eb=new[]{b[1]-b[0],b[2]-b[1],b[0]-b[2],b[3]-b[0]};
            foreach(var x in ea)foreach(var y in eb)axes.Add(Vector3.Cross(x,y));
            foreach(var axis in axes)
            {
                float magnitude=axis.magnitude;
                if(magnitude<.000001f)continue;
                Vector3 n=axis/magnitude;
                float amin=float.PositiveInfinity,amax=float.NegativeInfinity;
                float bmin=float.PositiveInfinity,bmax=float.NegativeInfinity;
                foreach(var p in a){float v=Vector3.Dot(p,n);amin=Mathf.Min(amin,v);amax=Mathf.Max(amax,v);}
                foreach(var p in b){float v=Vector3.Dot(p,n);bmin=Mathf.Min(bmin,v);bmax=Mathf.Max(bmax,v);}
                if(amax<=bmin+Tolerance||bmax<=amin+Tolerance)return false;
            }
            return true;
        }
        static List<Vector3[]> Pieces(CabinetDesignerDocument doc,CabinetDesignerDocument.PanelElement panel)
        {
            // Finished outline includes the edge bands; this is the external collision envelope.
            var resolved=doc.Resolve(panel);
            return Triangles(resolved.Outline.Select(p=>p.Point).ToArray())
                .Select(t=>Prism(t,resolved)).ToList();
        }
        public static bool Intersects(CabinetDesignerDocument doc,int first,int second)
        {
            var a=Pieces(doc,doc.Elements[first]);
            var b=Pieces(doc,doc.Elements[second]);
            foreach(var x in a)foreach(var y in b)if(Overlap(x,y))return true;
            return false;
        }
        public static HashSet<int> FindAll(CabinetDesignerDocument doc)
        {
            var invalid=new HashSet<int>();
            for(int i=0;i<doc.Elements.Count;i++)
                for(int j=i+1;j<doc.Elements.Count;j++)
                    try{if(Intersects(doc,i,j)){invalid.Add(i);invalid.Add(j);}}
                    catch(Exception){invalid.Add(i);invalid.Add(j);}
            return invalid;
        }
        public static bool Collides(CabinetDesignerDocument doc,int index)
        {
            for(int j=0;j<doc.Elements.Count;j++)
                if(j!=index&&Intersects(doc,index,j))return true;
            return false;
        }
    }
}