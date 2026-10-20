using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace BuildYourFurniture
{
    // Procedural stainless-steel sink: two genuinely recessed basins, rims, drains and drainer.
    // Local X = sink width; local Z = sink depth. Dimensions are millimetres in the plan.
    public static class SinkModelRenderer
    {
        static float N(XElement e,string k,float fallback)
        {
            float v;
            return e!=null && float.TryParse((string)e.Attribute(k),NumberStyles.Float,CultureInfo.InvariantCulture,out v)?v:fallback;
        }
        static string S(XElement e,string k,string fallback="")=>e==null?fallback:(string)e.Attribute(k)??fallback;
        static Color C(string value,Color fallback){Color result;return ColorUtility.TryParseHtmlString(value,out result)?result:fallback;}
        static Material Mat(Color color,float smooth)
        {
            var shader=Shader.Find("Universal Render Pipeline/Lit")??Shader.Find("Standard");
            var mat=new Material(shader);
            mat.color=color;
            if(mat.HasProperty("_BaseColor"))mat.SetColor("_BaseColor",color);
            if(mat.HasProperty("_Smoothness"))mat.SetFloat("_Smoothness",smooth);
            if(mat.HasProperty("_Glossiness"))mat.SetFloat("_Glossiness",smooth);
            if(mat.HasProperty("_Metallic"))mat.SetFloat("_Metallic",.88f);
            return mat;
        }
        static void MeshPart(string name,List<Vector3> vertices,List<int> triangles,Material material,
            Func<Vector3,Vector3> tr,Action<GameObject> register)
        {
            var obj=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer));
            var mesh=new Mesh();
            var v=new Vector3[vertices.Count];
            for(int i=0;i<v.Length;i++)v[i]=tr(vertices[i]);
            mesh.vertices=v;mesh.triangles=triangles.ToArray();
            mesh.RecalculateNormals();mesh.RecalculateBounds();
            obj.GetComponent<MeshFilter>().sharedMesh=mesh;
            var renderer=obj.GetComponent<MeshRenderer>();
            renderer.sharedMaterial=material;
            renderer.shadowCastingMode=ShadowCastingMode.Off;
            renderer.receiveShadows=false;
            register(obj);
        }
        static void Ring(string name,float x,float z,float width,float depth,float top,float drop,
            float corner,Material metal,Func<Vector3,Vector3> tr,Action<GameObject> register)
        {
            // Rounded rectangle at the rim, tapering into a smaller flat basin floor.
            const int segments=12;
            var vertices=new List<Vector3>();
            var tris=new List<int>();
            float[] widths={width,width-.018f,width-.060f,width-.095f};
            float[] depths={depth,depth-.018f,depth-.060f,depth-.095f};
            float[] ys={top,top-.012f,top-drop*.77f,top-drop};
            for(int level=0;level<4;level++)
            {
                float hw=Mathf.Max(.012f,widths[level]*.5f);
                float hd=Mathf.Max(.012f,depths[level]*.5f);
                float r=Mathf.Min(corner,hw*.75f,hd*.75f);
                for(int side=0;side<4;side++)
                {
                    float sx=side==0||side==3?-1:1;
                    float sz=side<2?-1:1;
                    float centerX=sx*(hw-r),centerZ=sz*(hd-r);
                    float start=(side+2)*Mathf.PI*.5f;
                    for(int j=0;j<=segments;j++)
                    {
                        float a=start+j*Mathf.PI*.5f/segments;
                        vertices.Add(new Vector3(x+centerX+r*Mathf.Cos(a),ys[level],z+centerZ+r*Mathf.Sin(a)));
                    }
                }
            }
            int count=4*(segments+1);
            for(int level=0;level<3;level++)
            for(int i=0;i<count;i++)
            {
                int a=level*count+i,b=level*count+(i+1)%count;
                int c=(level+1)*count+i,d=(level+1)*count+(i+1)%count;
                tris.Add(a);tris.Add(b);tris.Add(c);tris.Add(b);tris.Add(d);tris.Add(c);
                tris.Add(c);tris.Add(b);tris.Add(a);tris.Add(c);tris.Add(d);tris.Add(b);
            }
            int baseIndex=vertices.Count;
            vertices.Add(new Vector3(x,top-drop,z));
            for(int i=0;i<count;i++)
            {
                tris.Add(baseIndex);tris.Add(3*count+i);tris.Add(3*count+(i+1)%count);
            }
            MeshPart(name,vertices,tris,metal,tr,register);
        }
        static void Disk(string name,float x,float y,float z,float radius,Material mat,
            Func<Vector3,Vector3> tr,Action<GameObject> register)
        {
            var vertices=new List<Vector3>{new Vector3(x,y,z)};
            var triangles=new List<int>();
            const int n=24;
            for(int i=0;i<n;i++)
            {
                float a=i*Mathf.PI*2/n;
                vertices.Add(new Vector3(x+radius*Mathf.Cos(a),y,z+radius*Mathf.Sin(a)));
            }
            for(int i=0;i<n;i++){triangles.Add(0);triangles.Add(1+i);triangles.Add(1+(i+1)%n);}
            MeshPart(name,vertices,triangles,mat,tr,register);
        }
        static void Bar(string name,float x,float y,float z,float length,float width,Material mat,
            Func<Vector3,Vector3> tr,Action<GameObject> register)
        {
            var vertices=new List<Vector3>{
                new Vector3(x-length*.5f,y,z-width*.5f),new Vector3(x+length*.5f,y,z-width*.5f),
                new Vector3(x+length*.5f,y,z+width*.5f),new Vector3(x-length*.5f,y,z+width*.5f)};
            MeshPart(name,vertices,new List<int>{0,2,1,0,3,2,0,1,2,0,2,3},mat,tr,register);
        }
        public static void Build(XElement fixture,XElement model,string id,float x,float z,
            float width,float depth,float surface,Func<float,float,float,Vector3> transform,
            Action<GameObject> register)
        {
            float basinDepth=N(model,"basinDepth",170)*.001f;
            float largeRatio=N(model,"largeBowlRatio",.43f);
            float smallRatio=N(model,"smallBowlRatio",.22f);
            float drainerRatio=N(model,"drainerRatio",.25f);
            float margin=.018f;
            Color steel=C(S(fixture,"color",S(model,"color","#C3C7C9")),Color.gray);
            var metal=Mat(steel,N(model,"smoothness",.82f));
            var dark=Mat(C(S(model,"drainColor","#53585A"),Color.gray),.6f);
            Func<Vector3,Vector3> tr=p=>transform(p.x,p.y,p.z);
            // Rim is a flat, solid strip around the perimeter; basins are cut through it.
            float usable=width-2*margin;
            float total=largeRatio+smallRatio+drainerRatio;
            float large=usable*largeRatio/total,small=usable*smallRatio/total;
            float left=x-width*.5f+margin;
            float lx=left+large*.5f;
            float sx=left+large+small*.5f;
            float dz=depth-.07f;
            Ring(id+"_largeBasin",lx,z,large-.012f,dz,surface,basinDepth,.065f,metal,tr,register);
            Ring(id+"_smallBasin",sx,z,small-.012f,dz,surface,basinDepth*.82f,.050f,metal,tr,register);
            Disk(id+"_largeDrain",lx,surface-basinDepth+.001f,z,.023f,dark,tr,register);
            Disk(id+"_smallDrain",sx,surface-basinDepth*.82f+.001f,z,.018f,dark,tr,register);
            float drainerStart=left+large+small+.012f;
            float drainerEnd=x+width*.5f-margin;
            float dw=drainerEnd-drainerStart;
            if(dw>.03f)
                for(int i=0;i<5;i++)
                {
                    float zz=z-depth*.38f+(i+.5f)*depth*.76f/5f;
                    Bar(id+"_drainerRidge"+i,(drainerStart+drainerEnd)*.5f,surface+.0015f,zz,dw*.83f,.008f,metal,tr,register);
                }
            // A top flange fills the cutout margin without covering the basin mouths.
            var v=new List<Vector3>();
            var triangles=new List<int>();
            Action<float,float,float,float> strip=(x0,z0,x1,z1)=>
            {
                int k=v.Count;
                v.Add(new Vector3(x0,surface,z0));v.Add(new Vector3(x1,surface,z0));
                v.Add(new Vector3(x1,surface,z1));v.Add(new Vector3(x0,surface,z1));
                triangles.Add(k);triangles.Add(k+2);triangles.Add(k+1);
                triangles.Add(k);triangles.Add(k+3);triangles.Add(k+2);
            };
            strip(x-width*.5f,z-depth*.5f,x+width*.5f,z-depth*.5f+margin);
            strip(x-width*.5f,z+depth*.5f-margin,x+width*.5f,z+depth*.5f);
            strip(x-width*.5f,z-depth*.5f+margin,x-width*.5f+margin,z+depth*.5f-margin);
            strip(x+width*.5f-margin,z-depth*.5f+margin,x+width*.5f,z+depth*.5f-margin);
            // The drainer is flat stainless steel, with five raised drainage ribs.
            if(dw>0)strip(drainerStart,z-depth*.5f+margin,drainerEnd,z+depth*.5f-margin);
            MeshPart(id+"_flangeAndDrainer",v,triangles,metal,tr,register);
        }
    }
}
