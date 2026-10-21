using System;
using System.Collections.Generic;
using UnityEngine;

namespace BuildYourFurniture
{
    // Extrudes an XML-defined, simple orthogonal polygon into one physical panel.
    // Vertices use millimetre-independent Unity world units (metres).
    public static class LShapePanelMesh
    {
        public static GameObject Create(string name, IList<Vector2> outline, float thickness,
            Vector3 origin, float rotation, Color color)
        {
            if(outline==null||outline.Count<3||thickness<=0)
                throw new ArgumentException("Invalid polygon panel");
            int count=outline.Count;
            float signedArea=0;
            for(int i=0;i<count;i++)
            {
                Vector2 a=outline[i],b=outline[(i+1)%count];
                signedArea+=a.x*b.y-b.x*a.y;
                if((b-a).sqrMagnitude<0.0000000001f)
                    throw new ArgumentException("Duplicate consecutive polygon points");
            }
            if(Mathf.Abs(signedArea)<0.0000001f)
                throw new ArgumentException("Zero-area polygon panel");
            // Ensure CCW in the X/Z projection.
            var points=new List<Vector2>(outline);
            if(signedArea<0)points.Reverse();
            var vertices=new Vector3[count*2];
            for(int i=0;i<count;i++)
            {
                vertices[i]=new Vector3(points[i].x,0,points[i].y);
                vertices[i+count]=new Vector3(points[i].x,thickness,points[i].y);
            }
            // Ear clipping correctly handles the re-entrant (inside) L corner.
            var remaining=new List<int>();
            for(int i=0;i<count;i++)remaining.Add(i);
            var cap=new List<int>();
            int guard=0;
            while(remaining.Count>3 && guard++<count*count)
            {
                bool clipped=false;
                for(int i=0;i<remaining.Count;i++)
                {
                    int prev=remaining[(i+remaining.Count-1)%remaining.Count];
                    int curr=remaining[i],next=remaining[(i+1)%remaining.Count];
                    Vector2 a=points[prev],b=points[curr],c=points[next];
                    if(Cross(b-a,c-b)<=0.00000001f)continue;
                    bool occupied=false;
                    foreach(int candidate in remaining)
                    {
                        if(candidate==prev||candidate==curr||candidate==next)continue;
                        if(InsideTriangle(points[candidate],a,b,c)){occupied=true;break;}
                    }
                    if(occupied)continue;
                    cap.Add(prev);cap.Add(curr);cap.Add(next);
                    remaining.RemoveAt(i);clipped=true;break;
                }
                if(!clipped)throw new ArgumentException("Polygon is non-simple or has invalid vertices");
            }
            if(remaining.Count!=3)throw new ArgumentException("Could not triangulate polygon");
            cap.AddRange(remaining);
            var triangles=new List<int>();
            for(int i=0;i<cap.Count;i+=3)
            {
                int a=cap[i],b=cap[i+1],c=cap[i+2];
                // Positive XY winding points downward when mapped to Unity XZ.
                triangles.Add(a+count);triangles.Add(b+count);triangles.Add(c+count);
                triangles.Add(a);triangles.Add(c);triangles.Add(b);
            }
            for(int i=0;i<count;i++)
            {
                int j=(i+1)%count;
                triangles.Add(i);triangles.Add(j+count);triangles.Add(j);
                triangles.Add(i);triangles.Add(i+count);triangles.Add(j+count);
            }
            var mesh=new Mesh{name=name+"_Mesh"};
            mesh.vertices=vertices;mesh.triangles=triangles.ToArray();
            mesh.RecalculateNormals();mesh.RecalculateBounds();
            var go=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer),typeof(MeshCollider));
            go.GetComponent<MeshFilter>().sharedMesh=mesh;
            go.GetComponent<MeshCollider>().sharedMesh=mesh;
            var shader=Shader.Find("Universal Render Pipeline/Lit")??Shader.Find("Standard");
            if(shader==null)throw new InvalidOperationException("No supported shader for L panel");
            var material=new Material(shader){color=color};
            go.GetComponent<MeshRenderer>().material=material;
            go.transform.position=origin;
            go.transform.rotation=Quaternion.Euler(0,rotation,0);
            return go;
        }
        static float Cross(Vector2 a,Vector2 b)=>a.x*b.y-a.y*b.x;
        static bool InsideTriangle(Vector2 p,Vector2 a,Vector2 b,Vector2 c)
        {
            const float epsilon=0.00000001f;
            float x=Cross(b-a,p-a),y=Cross(c-b,p-b),z=Cross(a-c,p-c);
            return x>=-epsilon&&y>=-epsilon&&z>=-epsilon;
        }
    }
}
