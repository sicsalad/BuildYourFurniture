using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace BuildYourFurniture
{
    // Standalone/Play-mode cabinet editor, no UnityEditor dependency.
    public sealed class RuntimeCabinetDesigner : MonoBehaviour
    {
        public Action OnClose;
        CabinetDesignerDocument document;
        Camera camera3D;
        RenderTexture target;
        readonly List<GameObject> meshes=new List<GameObject>();
        readonly Dictionary<Collider,int> meshOwners=new Dictionary<Collider,int>();
        int selected=-1;
        Vector2 scroll,libraryScroll;
        bool libraryOpen,dirty;
        string error="";
        float yaw=40,pitch=30,zoom=2.5f;
        string editMode="Move";
        string[] modes={"Move","Resize","Rotate"};
        readonly Dictionary<string,string> fields=new Dictionary<string,string>();
        Rect viewport;
        int dragAxis=-1;
        bool dragging,orbiting;
        Vector2 previousMouse;
        bool initialized;
        public void Initialize()
        {
            if(initialized)return;
            initialized=true;
            document=CabinetDesignerDocument.New();
            var cameraObject=new GameObject("CabinetDesignerCamera");
            cameraObject.transform.SetParent(transform,false);
            camera3D=cameraObject.AddComponent<Camera>();
            camera3D.clearFlags=CameraClearFlags.SolidColor;
            camera3D.backgroundColor=new Color(.13f,.16f,.2f);
            camera3D.fieldOfView=40;
            camera3D.nearClipPlane=.01f;
            camera3D.farClipPlane=100;
            camera3D.cullingMask=1<<29;
            var lightObject=new GameObject("CabinetDesignerLight");
            lightObject.transform.SetParent(transform,false);
            var light=lightObject.AddComponent<Light>();
            light.type=LightType.Directional;light.intensity=1.3f;
            light.cullingMask=1<<29;
            lightObject.transform.rotation=Quaternion.Euler(45,25,0);
            SetTarget();
            Rebuild();
        }
        void SetTarget()
        {
            int w=Mathf.Max(256,Screen.width),h=Mathf.Max(256,Screen.height);
            if(target!=null&&target.width==w&&target.height==h)return;
            if(target!=null){camera3D.targetTexture=null;target.Release();Destroy(target);}
            target=new RenderTexture(w,h,24,RenderTextureFormat.ARGB32);
            target.Create();camera3D.targetTexture=target;
        }
        void OnDestroy()
        {
            ClearMeshes();
            if(camera3D!=null)camera3D.targetTexture=null;
            if(target!=null){target.Release();Destroy(target);}
        }
        void ClearMeshes()
        {
            foreach(var obj in meshes)
            {
                if(obj==null)continue;
                var filter=obj.GetComponent<MeshFilter>();
                if(filter!=null&&filter.sharedMesh!=null)Destroy(filter.sharedMesh);
                var renderer=obj.GetComponent<MeshRenderer>();
                if(renderer!=null&&renderer.material!=null)Destroy(renderer.material);
                Destroy(obj);
            }
            meshes.Clear();meshOwners.Clear();
        }
        void Rebuild()
        {
            ClearMeshes();error="";
            for(int i=0;i<document.Elements.Count;i++)
            {
                var p=document.Elements[i];
                try
                {
                    var resolved=document.Resolve(p);
                    var raw=CabinetDesignerDocument.RawOutline(resolved);
                    var finished=resolved.Outline.Select(v=>v.Point).ToArray();
                    Color color=ColorFor(p);
                    AddSolid(p.Name+" core",raw,p.Thickness,p.Position,p.Rotation,color,i);
                    for(int j=0;j<finished.Length;j++)
                    {
                        float band=resolved.Bands.ContainsKey(resolved.Outline[j].Id)?resolved.Bands[resolved.Outline[j].Id]:0;
                        if(band<=0)continue;
                        int next=(j+1)%finished.Length;
                        var strip=new List<Vector2>{finished[j],finished[next],raw[next],raw[j]};
                        for(int k=strip.Count-1;k>=1;k--)if((strip[k]-strip[k-1]).sqrMagnitude<.000001f)strip.RemoveAt(k);
                        if(strip.Count>2&&(strip[0]-strip[strip.Count-1]).sqrMagnitude<.000001f)strip.RemoveAt(strip.Count-1);
                        if(strip.Count<3)continue;
                        AddSolid(p.Name+" edge "+j,strip.ToArray(),p.Thickness,p.Position,p.Rotation,new Color(.16f,.48f,.75f),i);
                    }
                }
                catch(Exception ex){error=p.Name+": "+ex.Message;}
            }
        }
        Color ColorFor(CabinetDesignerDocument.PanelElement p)
        {
            var m=document.Materials.FirstOrDefault(x=>x.Id==p.MaterialId);
            var color=document.Colors.FirstOrDefault(x=>x.Id==m?.ColorId);
            Color c;return ColorUtility.TryParseHtmlString(color?.Hex??"#BFA17A",out c)?c:Color.gray;
        }
        void AddSolid(string name,Vector2[] outline,float thickness,Vector3 position,Vector3 rotation,Color color,int owner)
        {
            var obj=LShapePanelMesh.Create(name,outline.Select(p=>p*.001f).ToList(),thickness*.001f,
                position*.001f,0,color);
            obj.transform.rotation=Quaternion.Euler(rotation);
            obj.layer=29;
            meshes.Add(obj);
            var collider=obj.GetComponent<Collider>();
            if(collider!=null)meshOwners[collider]=owner;
        }
        void Save()
        {
            try{document.Save();dirty=false;error="Mentve: "+document.FilePath;}
            catch(Exception ex){error=ex.Message;}
        }
        void Open(string path)
        {
            try{document=CabinetDesignerDocument.Load(path);selected=-1;dirty=false;fields.Clear();libraryOpen=false;Rebuild();}
            catch(Exception ex){error=ex.Message;}
        }
        void New()
        {
            document=CabinetDesignerDocument.New();selected=-1;dirty=false;fields.Clear();Rebuild();
        }
        void Duplicate()
        {
            document=CabinetDesignerDocument.FromXml(document.ToXml());
            document.Id=Guid.NewGuid().ToString("N");document.Name+=" másolat";
            dirty=true;Save();
        }
        void Delete()
        {
            try{CabinetDesignerDocument.Delete(document);New();}
            catch(Exception ex){error=ex.Message;}
        }
        void OnGUI()
        {
            if(!initialized)Initialize();
            GUI.depth=-50;
            GUI.Box(new Rect(0,0,Screen.width,Screen.height),"");
            float bar=48;
            GUILayout.BeginArea(new Rect(8,4,Screen.width-16,bar));
            GUILayout.BeginHorizontal();
            if(GUILayout.Button("← Főmenü",GUILayout.Width(105),GUILayout.Height(34)))OnClose?.Invoke();
            if(GUILayout.Button("Új",GUILayout.Width(65),GUILayout.Height(34)))New();
            if(GUILayout.Button("Megnyitás",GUILayout.Width(95),GUILayout.Height(34)))libraryOpen=!libraryOpen;
            if(GUILayout.Button("Mentés",GUILayout.Width(75),GUILayout.Height(34)))Save();
            if(GUILayout.Button("Duplikálás",GUILayout.Width(100),GUILayout.Height(34)))Duplicate();
            if(GUILayout.Button("Törlés",GUILayout.Width(75),GUILayout.Height(34)))Delete();
            GUILayout.Label(dirty?"● Nem mentett":"",GUILayout.Width(130));
            GUILayout.EndHorizontal();GUILayout.EndArea();
            float left=Mathf.Clamp(Screen.width*.43f,330,650);
            GUILayout.BeginArea(new Rect(8,bar+6,left-12,Screen.height-bar-14),GUI.skin.box);
            scroll=GUILayout.BeginScrollView(scroll);
            DrawInspector();
            GUILayout.EndScrollView();GUILayout.EndArea();
            viewport=new Rect(left+5,bar+6,Screen.width-left-13,Screen.height-bar-14);
            GUI.Box(viewport,"");
            if(Event.current.type==EventType.Repaint&&target!=null)
                GUI.DrawTexture(new Rect(viewport.x+4,viewport.y+37,viewport.width-8,viewport.height-41),
                    target,ScaleMode.StretchToFill,false);
            GUILayout.BeginArea(new Rect(viewport.x+8,viewport.y+4,viewport.width-16,30));
            GUILayout.BeginHorizontal();
            foreach(string mode in modes)if(GUILayout.Toggle(editMode==mode,mode,"Button",GUILayout.Width(80)))editMode=mode;
            GUILayout.Label("X / Y / Z fogantyúk • jobb egér: forgatás");
            GUILayout.EndHorizontal();GUILayout.EndArea();
            DrawGizmos();
            if(libraryOpen)DrawLibrary();
            if(!string.IsNullOrEmpty(error))GUI.Box(new Rect(left+14,Screen.height-62,Mathf.Max(200,Screen.width-left-35),46),error);
        }
        void DrawLibrary()
        {
            var rect=new Rect(Mathf.Max(10,Screen.width*.23f),65,Mathf.Min(580,Screen.width*.54f),Mathf.Min(480,Screen.height-100));
            GUI.Box(rect,"Mentett szekrények");
            GUILayout.BeginArea(new Rect(rect.x+10,rect.y+30,rect.width-20,rect.height-40));
            libraryScroll=GUILayout.BeginScrollView(libraryScroll);
            foreach(string path in CabinetDesignerDocument.Files())
            {
                GUILayout.BeginHorizontal();
                if(GUILayout.Button(Path.GetFileNameWithoutExtension(path),GUILayout.Height(32)))Open(path);
                if(GUILayout.Button("Másolat",GUILayout.Width(85),GUILayout.Height(32)))
                {
                    Open(path);Duplicate();libraryOpen=true;
                }
                if(GUILayout.Button("Törlés",GUILayout.Width(65),GUILayout.Height(32)))
                {
                    try{File.Delete(path);}catch(Exception ex){error=ex.Message;}
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
            if(GUILayout.Button("Bezárás"))libraryOpen=false;
            GUILayout.EndArea();
        }
        string Field(string key,string label,string value)
        {
            string text;
            if(!fields.TryGetValue(key,out text))text=value;
            GUILayout.BeginHorizontal();
            GUILayout.Label(label,GUILayout.Width(100));
            string next=GUILayout.TextField(text);
            GUILayout.EndHorizontal();
            fields[key]=next;
            return next;
        }
        float FloatField(string key,string label,float value)
        {
            string input=Field(key,label,value.ToString("0.###",System.Globalization.CultureInfo.InvariantCulture));
            float parsed;
            if(float.TryParse(input,System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,out parsed)&&Mathf.Abs(parsed-value)>.00001f)
            {dirty=true;return parsed;}
            return value;
        }
        void DrawInspector()
        {
            GUILayout.Label("Szekrény",GUI.skin.box);
            string name=Field("cabinetName","Név",document.Name);
            if(name!=document.Name){document.Name=name;dirty=true;}
            GUILayout.Label("Méretek (mm)");
            foreach(var dim in document.Dimensions)
            {
                dim.Name=Field("dn"+dim.Id,dim.Id,dim.Name);
                float old=dim.Value;dim.Value=FloatField("dv"+dim.Id,dim.Name,dim.Value);
                if(old!=dim.Value)Rebuild();
            }
            if(GUILayout.Button("+ Nevesített méret"))
            {document.Dimensions.Add(new CabinetDesignerDocument.Dimension{Id="size"+Guid.NewGuid().ToString("N").Substring(0,5),Name="Méret",Value=100});dirty=true;}
            GUILayout.Label("Színek");
            foreach(var color in document.Colors)
            {
                string next=Field("color"+color.Id,color.Name,color.Hex);
                if(next!=color.Hex){color.Hex=next;dirty=true;Rebuild();}
            }
            if(GUILayout.Button("+ Szín"))
            {document.Colors.Add(new CabinetDesignerDocument.PaletteColor{Id="color"+Guid.NewGuid().ToString("N").Substring(0,5),Name="Új szín",Hex="#FFFFFF"});dirty=true;}
            GUILayout.Label("Anyagok");
            foreach(var material in document.Materials)
            {
                material.Name=Field("matname"+material.Id,material.Id,material.Name);
                material.ColorId=Field("matcolor"+material.Id,"Szín ID",material.ColorId);
                material.Texture=Field("mattex"+material.Id,"Textúra",material.Texture??"");
            }
            if(GUILayout.Button("+ Anyag"))
            {document.Materials.Add(new CabinetDesignerDocument.MaterialType{Id="material"+Guid.NewGuid().ToString("N").Substring(0,5),Name="Anyag",ColorId=document.Colors.FirstOrDefault()?.Id??""});dirty=true;}
            GUILayout.Label("Alkatrészek",GUI.skin.box);
            for(int i=0;i<document.Elements.Count;i++)
            {
                GUILayout.BeginHorizontal();
                if(GUILayout.Toggle(selected==i,document.Elements[i].Name,"Button")){if(selected!=i){selected=i;fields.Clear();}}
                if(GUILayout.Button("×",GUILayout.Width(28)))
                {document.Elements.RemoveAt(i);selected=-1;dirty=true;Rebuild();GUILayout.EndHorizontal();break;}
                GUILayout.EndHorizontal();
            }
            GUILayout.BeginHorizontal();
            if(GUILayout.Button("+ Bútorlap"))
            {
                var panel=CabinetDesignerDocument.PanelElement.Rectangle();
                panel.Name="Bútorlap "+(document.Elements.Count+1);
                document.Elements.Add(panel);selected=document.Elements.Count-1;fields.Clear();dirty=true;Rebuild();
            }
            if(GUILayout.Button("+ Bútorlap L alak"))
            {
                var panel=CabinetDesignerDocument.PanelElement.LShape();
                panel.Name="L alakú bútorlap "+(document.Elements.Count+1);
                document.Elements.Add(panel);
                selected=document.Elements.Count-1;
                fields.Clear();
                dirty=true;
                Rebuild();
            }
            if(GUILayout.Button("Elem duplikálása")&&selected>=0&&selected<document.Elements.Count)
            {
                document.Elements.Add(document.Elements[selected].Clone());selected=document.Elements.Count-1;fields.Clear();dirty=true;Rebuild();
            }
            GUILayout.EndHorizontal();
            if(selected<0||selected>=document.Elements.Count)return;
            var p=document.Elements[selected];
            GUILayout.Label("Kijelölt elem",GUI.skin.box);
            p.Name=Field("pname"+p.Id,"Név",p.Name);
            p.MaterialId=Field("pmat"+p.Id,"Anyag ID",p.MaterialId);
            var pos=p.Position;var rot=p.Rotation;float thick=p.Thickness;
            pos.x=FloatField("px","Pozíció X",pos.x);pos.y=FloatField("py","Pozíció Y",pos.y);pos.z=FloatField("pz","Pozíció Z",pos.z);
            rot.x=FloatField("rx","Forgatás X",rot.x);rot.y=FloatField("ry","Forgatás Y",rot.y);rot.z=FloatField("rz","Forgatás Z",rot.z);
            thick=Mathf.Max(.1f,FloatField("thick","Vastagság",thick));
            if(pos!=p.Position||rot!=p.Rotation||thick!=p.Thickness)
            {p.Position=pos;p.Rotation=rot;p.Thickness=thick;Rebuild();}
            GUILayout.Label("Kész körvonal (mm)");
            foreach(var v in p.Outline)
            {
                Vector2 old=v.Point;
                v.Point=new Vector2(FloatField(v.Id+"x",v.Id+" X",v.Point.x),FloatField(v.Id+"y",v.Id+" Y",v.Point.y));
                v.XFormula=Field(v.Id+"fx","X képlet",v.XFormula??"");
                v.YFormula=Field(v.Id+"fy","Y képlet",v.YFormula??"");
                if(old!=v.Point)Rebuild();
            }
            if(GUILayout.Button("+ Körvonalpont"))
            {
                p.Outline.Add(new CabinetDesignerDocument.Vertex{Id="P"+Guid.NewGuid().ToString("N").Substring(0,5),
                    Point=p.Outline.Last().Point+new Vector2(60,0)});dirty=true;Rebuild();
            }
            GUILayout.Label("Élfóliák (mm)");
            foreach(var v in p.Outline)
            {
                float old=p.Bands.ContainsKey(v.Id)?p.Bands[v.Id]:0;
                float value=Mathf.Max(0,FloatField("band"+v.Id,v.Id+" él",old));
                if(value!=old){p.Bands[v.Id]=value;Rebuild();}
            }
        }
        Vector3 Center()
        {
            if(document.Elements.Count==0)return new Vector3(.3f,.1f,.2f);
            return document.Elements.Aggregate(Vector3.zero,(sum,p)=>sum+p.Position)/(1000f*document.Elements.Count)+new Vector3(.25f,.1f,.2f);
        }
        void LateUpdate()
        {
            if(!initialized)return;
            SetTarget();
            var center=Center();
            var q=Quaternion.Euler(pitch,yaw,0);
            camera3D.transform.position=center+q*new Vector3(0,0,-zoom);
            camera3D.transform.LookAt(center);
            HandleMouse();
        }
        Vector2 ToViewport(Vector2 mouse)
        {
            var r=ImageRect();
            return new Vector2((mouse.x-r.x)/r.width,1-(mouse.y-r.y)/r.height);
        }
        Rect ImageRect()=>new Rect(viewport.x+4,viewport.y+37,viewport.width-8,viewport.height-41);
        Vector2 MouseGUI()
        {
#if ENABLE_INPUT_SYSTEM
            if(UnityEngine.InputSystem.Mouse.current!=null)
            {
                var p=UnityEngine.InputSystem.Mouse.current.position.ReadValue();
                return new Vector2(p.x,Screen.height-p.y);
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            return new Vector2(Input.mousePosition.x,Screen.height-Input.mousePosition.y);
#else
            return Vector2.zero;
#endif
        }
        bool MouseDown(int button)
        {
#if ENABLE_INPUT_SYSTEM
            if(UnityEngine.InputSystem.Mouse.current!=null)return button==0?
                UnityEngine.InputSystem.Mouse.current.leftButton.wasPressedThisFrame:
                UnityEngine.InputSystem.Mouse.current.rightButton.wasPressedThisFrame;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetMouseButtonDown(button);
#else
            return false;
#endif
        }
        bool MouseHeld(int button)
        {
#if ENABLE_INPUT_SYSTEM
            if(UnityEngine.InputSystem.Mouse.current!=null)return button==0?
                UnityEngine.InputSystem.Mouse.current.leftButton.isPressed:
                UnityEngine.InputSystem.Mouse.current.rightButton.isPressed;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetMouseButton(button);
#else
            return false;
#endif
        }
        float Wheel()
        {
#if ENABLE_INPUT_SYSTEM
            if(UnityEngine.InputSystem.Mouse.current!=null)return UnityEngine.InputSystem.Mouse.current.scroll.ReadValue().y/120f;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.mouseScrollDelta.y;
#else
            return 0;
#endif
        }
        Vector3 HandleOrigin()
        {
            var p=document.Elements[selected];
            var resolved=document.Resolve(p);
            float minX=resolved.Outline.Min(v=>v.Point.x),maxX=resolved.Outline.Max(v=>v.Point.x);
            float minZ=resolved.Outline.Min(v=>v.Point.y),maxZ=resolved.Outline.Max(v=>v.Point.y);
            return p.Position*.001f+Quaternion.Euler(p.Rotation)*new Vector3((minX+maxX)*.0005f,p.Thickness*.0005f,(minZ+maxZ)*.0005f);
        }
        Vector3 Axis(int i)=>i==0?Vector3.right:i==1?Vector3.up:Vector3.forward;
        Vector2 Project(Vector3 world)
        {
            var v=camera3D.WorldToViewportPoint(world);
            var r=ImageRect();
            return new Vector2(r.x+v.x*r.width,r.y+(1-v.y)*r.height);
        }
        float HandleLength()=>Mathf.Max(.14f,zoom*.15f);
        Vector2[] HandleLine(int i)
        {
            Vector3 o=HandleOrigin();
            Vector3 dir=Quaternion.Euler(document.Elements[selected].Rotation)*Axis(i);
            return new[]{Project(o),Project(o+dir*HandleLength())};
        }
        int HitHandle(Vector2 mouse)
        {
            if(selected<0||selected>=document.Elements.Count)return -1;
            int best=-1;float distance=14;
            for(int i=0;i<3;i++)
            {
                var line=HandleLine(i);var ab=line[1]-line[0];
                float t=Mathf.Clamp01(Vector2.Dot(mouse-line[0],ab)/Mathf.Max(.001f,ab.sqrMagnitude));
                float d=Vector2.Distance(mouse,line[0]+t*ab);
                if(d<distance){distance=d;best=i;}
            }
            return best;
        }
        void HandleMouse()
        {
            var mouse=MouseGUI();
            bool inside=ImageRect().Contains(mouse);
            if(inside&&Mathf.Abs(Wheel())>.001f)zoom=Mathf.Clamp(zoom-Wheel()*.2f,.3f,20);
            if(MouseDown(1)&&inside){orbiting=true;previousMouse=mouse;}
            if(!MouseHeld(1))orbiting=false;
            if(orbiting)
            {
                var delta=mouse-previousMouse;
                yaw+=delta.x*.4f;pitch=Mathf.Clamp(pitch-delta.y*.4f,-80,80);
                previousMouse=mouse;
            }
            if(MouseDown(0)&&inside)
            {
                dragAxis=HitHandle(mouse);dragging=dragAxis>=0;previousMouse=mouse;
                if(!dragging)
                {
                    var uv=ToViewport(mouse);
                    var ray=camera3D.ViewportPointToRay(new Vector3(uv.x,uv.y,0));
                    RaycastHit hit;
                    if(Physics.Raycast(ray,out hit,100,1<<29)&&meshOwners.ContainsKey(hit.collider))
                    {selected=meshOwners[hit.collider];fields.Clear();}
                }
            }
            if(!MouseHeld(0)){dragging=false;dragAxis=-1;}
            if(dragging&&selected>=0)
            {
                var delta=mouse-previousMouse;previousMouse=mouse;
                var line=HandleLine(dragAxis);var direction=line[1]-line[0];
                float projected=Vector2.Dot(delta,direction.normalized)/Mathf.Max(5,direction.magnitude);
                var p=document.Elements[selected];
                if(editMode=="Move")p.Position+=Quaternion.Euler(p.Rotation)*Axis(dragAxis)*(projected*HandleLength()*1000);
                else if(editMode=="Rotate")
                {
                    var rotation=p.Rotation;rotation[dragAxis]+=projected*90;p.Rotation=rotation;
                }
                else
                {
                    float amount=projected*HandleLength()*1000;
                    if(dragAxis==1)p.Thickness=Mathf.Max(.1f,p.Thickness+amount);
                    else
                    {
                        var resolved=document.Resolve(p);
                        var values=resolved.Outline.Select(v=>dragAxis==0?v.Point.x:v.Point.y).ToArray();
                        float min=values.Min(),span=Mathf.Max(1,values.Max()-min);
                        float factor=Mathf.Max(.05f,1+amount/span);
                        for(int i=0;i<p.Outline.Count;i++)
                        {
                            if(dragAxis==0){p.Outline[i].Point.x=min+(values[i]-min)*factor;p.Outline[i].XFormula="";}
                            else {p.Outline[i].Point.y=min+(values[i]-min)*factor;p.Outline[i].YFormula="";}
                        }
                    }
                }
                dirty=true;fields.Clear();Rebuild();
            }
        }
        void DrawGizmos()
        {
            if(selected<0||selected>=document.Elements.Count)return;
            var ev=Event.current;
            if(ev.type!=EventType.Repaint)return;
            try
            {
                var colors=new[]{Color.red,Color.green,Color.cyan};
                for(int i=0;i<3;i++)
                {
                    var line=HandleLine(i);
                    var texture=Texture2D.whiteTexture;
                    var a=line[0];var b=line[1];var d=b-a;
                    var old=GUI.color;GUI.color=colors[i];
                    Matrix4x4 matrix=GUI.matrix;
                    GUIUtility.RotateAroundPivot(Mathf.Atan2(d.y,d.x)*Mathf.Rad2Deg,a);
                    GUI.DrawTexture(new Rect(a.x,a.y-2,d.magnitude,4),texture);
                    GUI.matrix=matrix;
                    GUI.Box(new Rect(b.x-10,b.y-10,20,20),i==0?"X":i==1?"Y":"Z");
                    GUI.color=old;
                }
            }
            catch(Exception ex){error=ex.Message;}
        }
    }
}