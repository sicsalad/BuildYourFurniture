using UnityEngine;

namespace BuildYourFurniture
{
    // A single six-vertex concave L footprint extruded as one closed solid.
    // Local coordinates: inner corner (0,0), long arm along X, second arm along Z.
    public static class LShapePanelMesh
    {
        public static GameObject Create(string name, float leftLength, float rightLength,
            float leftDepth, float rightDepth, float thickness, Vector3 origin, float rotation, Color color)
        {
            if(leftLength<=leftDepth||rightLength<=rightDepth||leftDepth<=0||rightDepth<=0||thickness<=0)
                throw new System.ArgumentException("Invalid L panel dimensions");
            // Outline CCW in X/Z: (0,0) -> (leftLength,0) -> (leftLength,rightDepth)
            // -> (leftDepth,rightDepth) -> (leftDepth,rightLength) -> (0,rightLength).
            Vector2[] p={
                new Vector2(0,0),new Vector2(leftLength,0),new Vector2(leftLength,rightDepth),
                new Vector2(leftDepth,rightDepth),new Vector2(leftDepth,rightLength),new Vector2(0,rightLength)
            };
            var vertices=new Vector3[12];
            for(int i=0;i<6;i++)
            {
                vertices[i]=new Vector3(p[i].x,0,p[i].y);
                vertices[i+6]=new Vector3(p[i].x,thickness,p[i].y);
            }
            // Four top triangles tile the two rectangles without filling the missing corner.
            int[] top={6,7,9,7,8,9,6,9,11,9,10,11};
            var triangles=new System.Collections.Generic.List<int>();
            // XZ coordinate system reverses winding when seen from above.
            for(int i=0;i<top.Length;i+=3)
            {
                triangles.Add(top[i]);triangles.Add(top[i+2]);triangles.Add(top[i+1]);
                triangles.Add(top[i]-6);triangles.Add(top[i+1]-6);triangles.Add(top[i+2]-6);
            }
            for(int i=0;i<6;i++)
            {
                int j=(i+1)%6;
                triangles.Add(i);triangles.Add(j);triangles.Add(j+6);
                triangles.Add(i);triangles.Add(j+6);triangles.Add(i+6);
            }
            var mesh=new Mesh {name=name+"_Mesh"};
            mesh.vertices=vertices;
            mesh.triangles=triangles.ToArray();
            mesh.RecalculateNormals();mesh.RecalculateBounds();
            var go=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer),typeof(MeshCollider));
            go.GetComponent<MeshFilter>().sharedMesh=mesh;
            go.GetComponent<MeshCollider>().sharedMesh=mesh;
            var shader=Shader.Find("Universal Render Pipeline/Lit")??Shader.Find("Standard");
            var material=new Material(shader);
            material.color=color;
            go.GetComponent<MeshRenderer>().material=material;
            go.transform.position=origin;
            go.transform.rotation=Quaternion.Euler(0,rotation,0);
            return go;
        }
    }
}
