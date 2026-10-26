using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml.Linq;
using UnityEngine;

namespace BuildYourFurniture
{
    public static class MirroredWardrobeRenderer
    {
        public static readonly string[] Finishes={
            "Fehér matt","Hamilton tölgy","Natúr tölgy","Sonoma tölgy","Dió",
            "Fenyő","Világos bükk","Fekete matt","Antracit","Világosszürke"
        };
        public static readonly string[] Colors={
            "#F4F4F1","#97704E","#B98E60","#C8AD85","#74503B",
            "#D8B88B","#E0C99F","#292A2C","#525359","#C8C9C8"
        };
        static readonly Dictionary<string,Texture2D> Textures=new Dictionary<string,Texture2D>();
        static float N(XElement e,string key,float fallback)
        {
            float v;return float.TryParse((string)e.Attribute(key),NumberStyles.Float,CultureInfo.InvariantCulture,out v)?v:fallback;
        }
        static string S(XElement e,string key,string fallback="")=>(string)e.Attribute(key)??fallback;
        static Texture2D FinishTexture(int finish,Color color)
        {
            string key=finish+"-"+ColorUtility.ToHtmlStringRGB(color);
            Texture2D t;if(Textures.TryGetValue(key,out t)&&t!=null)return t;
            t=new Texture2D(128,128,TextureFormat.RGBA32,true);
            t.wrapMode=TextureWrapMode.Repeat;
            for(int y=0;y<128;y++)for(int x=0;x<128;x++)
            {
                float noise=Mathf.PerlinNoise(x*.025f+finish*3,y*.12f+finish*5);
                float detail=Mathf.PerlinNoise(x*.29f+finish,y*.26f+finish);
                float v=(finish==0||finish>=7)?.97f+(detail-.5f)*.045f:
                    .90f+(noise-.5f)*.30f+Mathf.Sin(y*.2f+noise*3f)*.04f;
                t.SetPixel(x,y,new Color(Mathf.Clamp01(color.r*v),Mathf.Clamp01(color.g*v),Mathf.Clamp01(color.b*v)));
            }
            t.Apply(true,false);Textures[key]=t;return t;
        }
        static Material DoorMaterial(XElement e,int side,int fallbackFinish)
        {
            string prefix=side==0?"leftDoor":"rightDoor";
            int finish=Mathf.Clamp(Mathf.RoundToInt(N(e,prefix+"Finish",
                N(e,"doorFinish",fallbackFinish))),0,Finishes.Length-1);
            Color tint;
            if(!ColorUtility.TryParseHtmlString(S(e,prefix+"Color",
                S(e,"doorColor",Colors[finish])),out tint))
                ColorUtility.TryParseHtmlString(Colors[finish],out tint);
            var material=new Material(Shader.Find("Universal Render Pipeline/Lit")??Shader.Find("Standard"));
            var texture=FinishTexture(finish,tint);
            string asset=S(e,prefix+"Texture",S(e,"doorTexture"));
            if(!string.IsNullOrEmpty(asset))
            {
                string path=asset.Replace("\\","/");
                if(path.StartsWith("Textures/",StringComparison.OrdinalIgnoreCase))path=path.Substring(9);
                var loaded=Resources.Load<Texture2D>("Textures/"+path);
                if(loaded!=null)texture=loaded;
            }
            material.color=Color.white;
            material.mainTexture=texture;
            if(material.HasProperty("_BaseMap"))material.SetTexture("_BaseMap",texture);
            return material;
        }
        static void SetDoorUv(GameObject panel,XElement e,int side,float width,float height)
        {
            string prefix=side==0?"leftDoor":"rightDoor";
            string mode=S(e,prefix+"TextureMode",S(e,"doorTextureMode","tile"));
            float tileMm=Mathf.Max(25,N(e,prefix+"TileMm",N(e,"doorTileMm",400)));
            bool rotated=N(e,prefix+"TextureRotation",N(e,"doorTextureRotation",0))>=45;
            float u=mode=="stretch"?1:Mathf.Max(.01f,width*1000f/tileMm);
            float v=mode=="stretch"?1:Mathf.Max(.01f,height*1000f/tileMm);
            var mesh=panel.GetComponent<MeshFilter>().mesh;
            var uv=mesh.uv;
            // The front face of a Unity cube uses UVs at indices 4..7.
            // Map both broad faces, keeping side faces independent.
            for(int face=0;face<6;face++)
            {
                int offset=face*4;
                if(offset+3>=uv.Length)break;
                for(int k=0;k<4;k++)
                {
                    Vector2 original=uv[offset+k];
                    uv[offset+k]=rotated?
                        new Vector2(original.y*v,(1f-original.x)*u):
                        new Vector2(original.x*u,original.y*v);
                }
            }
            mesh.uv=uv;
        }
        public static void Build(XDocument plan,Action<GameObject> register)
        {
            var collection=plan?.Root?.Element("mirroredWardrobes");
            if(collection==null)return;
            foreach(var e in collection.Elements("wardrobe"))
            {
                float w=Mathf.Max(.6f,N(e,"width",1000)/1000f);
                float h=Mathf.Max(1f,N(e,"height",1850)/1000f);
                float d=Mathf.Max(.25f,N(e,"depth",450)/1000f);
                float opening=Mathf.Clamp01(N(e,"open",0));
                int finish=Mathf.Clamp(Mathf.RoundToInt(N(e,"finish",0)),0,9);
                Color color;
                if(!ColorUtility.TryParseHtmlString(S(e,"color",Colors[finish]),out color))
                    ColorUtility.TryParseHtmlString(Colors[finish],out color);
                int doorFinish=Mathf.Clamp(Mathf.RoundToInt(N(e,"doorFinish",finish)),0,9);
                Color doorColor;
                if(!ColorUtility.TryParseHtmlString(S(e,"doorColor",Colors[doorFinish]),out doorColor))
                    ColorUtility.TryParseHtmlString(Colors[doorFinish],out doorColor);
                var root=new GameObject("Sliding wardrobe "+S(e,"id","WARD"));
                root.transform.position=new Vector3(N(e,"x",500)/1000f,0,N(e,"y",500)/1000f);
                root.transform.rotation=Quaternion.Euler(0,N(e,"rotation",0),0);
                register(root);
                var wood=new Material(Shader.Find("Universal Render Pipeline/Lit")??Shader.Find("Standard"));
                wood.color=Color.white;
                var texture=FinishTexture(finish,color);
                wood.mainTexture=texture;
                if(wood.HasProperty("_BaseMap"))wood.SetTexture("_BaseMap",texture);

                var metal=new Material(Shader.Find("Universal Render Pipeline/Lit")??Shader.Find("Standard"));
                metal.color=new Color(.45f,.46f,.48f);
                if(metal.HasProperty("_Metallic"))metal.SetFloat("_Metallic",.85f);
                Action<string,Vector3,Vector3,Material,Transform> box=(name,pos,size,mat,parent)=>
                {
                    var obj=GameObject.CreatePrimitive(PrimitiveType.Cube);
                    obj.name=name;obj.transform.SetParent(parent,false);
                    obj.transform.localPosition=pos;obj.transform.localScale=size;
                    obj.GetComponent<Renderer>().sharedMaterial=mat;
                };
                const float p=.018f;
                box("Left panel",new Vector3(-w*.5f+p*.5f,h*.5f,0),new Vector3(p,h,d),wood,root.transform);
                box("Right panel",new Vector3(w*.5f-p*.5f,h*.5f,0),new Vector3(p,h,d),wood,root.transform);
                box("Top",new Vector3(0,h-p*.5f,0),new Vector3(w,p,d),wood,root.transform);
                box("Bottom",new Vector3(0,p*.5f,0),new Vector3(w,p,d),wood,root.transform);
                box("Back",new Vector3(0,h*.5f,d*.5f-p*.5f),new Vector3(w-2*p,h-2*p,p),wood,root.transform);
                box("Shelf",new Vector3(0,h*.78f,0),new Vector3(w-2*p,p,d-p),wood,root.transform);
                box("Hanging rail",new Vector3(0,h*.68f,0),new Vector3(w-3*p,.02f,.02f),metal,root.transform);
                float doorW=(w-2*p)*.52f;
                bool openWardrobe=N(e,"wardrobeType",0)>=3;
                for(int i=0;i<(openWardrobe?0:2);i++)
                {
                    var door=new GameObject(i==0?"Left sliding door":"Right sliding door");
                    door.transform.SetParent(root.transform,false);
                    float x=(i==0?-1:1)*doorW*.49f+(i==0?opening*doorW*.7f:-opening*doorW*.7f);
                    door.transform.localPosition=new Vector3(x,0,-d*.5f-(i==0?.018f:.035f));
                    box("Door frame",new Vector3(0,h*.5f,0),new Vector3(doorW,h-.022f,.024f),metal,door.transform);
                    bool mirrored=N(e,i==0?"leftMirror":"rightMirror",1)>.5f;
                    if(mirrored)
                    {
                        var mirror=GameObject.CreatePrimitive(PrimitiveType.Quad);
                        mirror.name="Full door live mirror";
                        mirror.transform.SetParent(door.transform,false);
                        mirror.transform.localPosition=new Vector3(0,h*.5f,-.0135f);
                        mirror.transform.localRotation=Quaternion.identity;
                        mirror.transform.localScale=new Vector3(doorW-.006f,h-.028f,1);
                        var component=mirror.AddComponent<PlanarWardrobeMirror>();
                        component.Initialize();
                    }
                    else
                    {
                        var surface=GameObject.CreatePrimitive(PrimitiveType.Quad);
                        surface.name=i==0?"Left door finish":"Right door finish";
                        surface.transform.SetParent(door.transform,false);
                        surface.transform.localPosition=new Vector3(0,h*.5f,-.019f);
                        surface.transform.localScale=new Vector3(doorW-.006f,h-.028f,1);
                        var material=DoorMaterial(e,i,doorFinish);
                        surface.GetComponent<Renderer>().sharedMaterial=material;
                        string prefix=i==0?"leftDoor":"rightDoor";
                        string mode=S(e,prefix+"TextureMode",S(e,"doorTextureMode","tile"));
                        float tileMm=Mathf.Max(25,N(e,prefix+"TileMm",N(e,"doorTileMm",400)));
                        bool rotated=N(e,prefix+"TextureRotation",N(e,"doorTextureRotation",0))>=45;
                        float u=mode=="stretch"?1:(doorW-.006f)*1000f/tileMm;
                        float v=mode=="stretch"?1:(h-.028f)*1000f/tileMm;
                        var mesh=surface.GetComponent<MeshFilter>().mesh;
                        var uv=mesh.uv;
                        for(int k=0;k<uv.Length;k++)
                        {
                            var t=uv[k];
                            uv[k]=rotated?new Vector2(t.y*v,(1f-t.x)*u):
                                new Vector2(t.x*u,t.y*v);
                        }
                        mesh.uv=uv;
                    }
                }
                if(!openWardrobe)box("Top sliding rail",new Vector3(0,h-.023f,-d*.5f-.018f),new Vector3(w,.022f,.048f),metal,root.transform);
                if(!openWardrobe)box("Bottom sliding rail",new Vector3(0,.024f,-d*.5f-.018f),new Vector3(w,.022f,.048f),metal,root.transform);
            }
        }
    }
}