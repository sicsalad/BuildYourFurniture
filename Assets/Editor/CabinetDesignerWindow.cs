#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BuildYourFurniture
{
    public sealed class CabinetDesignerWindow : EditorWindow
    {
        CabinetDesignerDocument document;
        int selected=-1;
        Vector2 scroll;
        PreviewRenderUtility preview;
        readonly List<GameObject> previewObjects=new List<GameObject>();
        float orbit=35,tilt=25,distance=2.3f;
        Vector2 mouseDown;
        int activeAxis=-1;
        readonly Color[] axisColors={new Color(.95f,.30f,.30f),new Color(.35f,.95f,.45f),new Color(.35f,.65f,1f)};
        enum Mode { Move, Resize, Rotate, Orbit }
        Mode mode=Mode.Move;
        bool dirty;
        [MenuItem("BuildYourFurniture/Szekrény készítő",priority=100)]
        public static void Open()=>GetWindow<CabinetDesignerWindow>("Szekrény készítő");
        [MenuItem("Window/BuildYourFurniture/Szekrény készítő",priority=1100)]
        public static void OpenFromWindowMenu()=>Open();
        void OnEnable(){document=CabinetDesignerDocument.New();InitializePreview();}
        void OnDisable(){DisposePreview();}
        void InitializePreview()
        {
            if(preview!=null)return;
            preview=new PreviewRenderUtility();
            preview.cameraFieldOfView=35;
            preview.camera.nearClipPlane=.01f;
            preview.camera.farClipPlane=100;
            preview.lights[0].intensity=1.2f;
            preview.lights[0].transform.rotation=Quaternion.Euler(35,35,0);
            preview.ambientColor=Color.gray;
        }
        void DisposePreview()
        {
            ClearObjects();
            if(preview!=null){preview.Cleanup();preview=null;}
        }
        void ClearObjects()
        {
            foreach(var o in previewObjects)if(o!=null)DestroyImmediate(o);
            previewObjects.Clear();
        }
        void Changed(){dirty=true;Repaint();}
        void OnGUI()
        {
            if(document==null)document=CabinetDesignerDocument.New();
            Toolbar();
            Rect bounds=GUILayoutUtility.GetRect(0,10000,0,10000,GUILayout.ExpandWidth(true),GUILayout.ExpandHeight(true));
            float left=Mathf.Clamp(bounds.width*.44f,300,520);
            var inspector=new Rect(bounds.x,bounds.y,left,bounds.height);
            var viewport=new Rect(bounds.x+left+4,bounds.y,bounds.width-left-4,bounds.height);
            GUILayout.BeginArea(inspector,EditorStyles.helpBox);
            scroll=EditorGUILayout.BeginScrollView(scroll);
            Inspector();
            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();
            Preview(viewport);
        }
        void Toolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            if(GUILayout.Button("Új",EditorStyles.toolbarButton,GUILayout.Width(42)))NewDocument();
            if(GUILayout.Button("Megnyitás",EditorStyles.toolbarButton,GUILayout.Width(78)))OpenDocument();
            if(GUILayout.Button("Mentés",EditorStyles.toolbarButton,GUILayout.Width(65)))SaveDocument();
            if(GUILayout.Button("Duplikálás",EditorStyles.toolbarButton,GUILayout.Width(85)))DuplicateDocument();
            if(GUILayout.Button("Törlés",EditorStyles.toolbarButton,GUILayout.Width(55)))DeleteDocument();
            GUILayout.FlexibleSpace();
            GUILayout.Label(dirty?"● Nem mentett":"Mentve",EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
        }
        bool ConfirmDiscard()=>!dirty||EditorUtility.DisplayDialog("Nem mentett változások",
            "A jelenlegi szekrény módosításai elvesznek. Folytatod?","Igen","Mégse");
        void NewDocument()
        {
            if(!ConfirmDiscard())return;
            document=CabinetDesignerDocument.New();selected=-1;dirty=true;Repaint();
        }
        void OpenDocument()
        {
            if(!ConfirmDiscard())return;
            var files=CabinetDesignerDocument.Files();
            if(files.Length==0){EditorUtility.DisplayDialog("Szekrények","Még nincs mentett szekrény.","OK");return;}
            var menu=new GenericMenu();
            foreach(string file in files)
            {
                string captured=file;
                string title;
                try{title=CabinetDesignerDocument.Load(file).Name;}catch{title=Path.GetFileNameWithoutExtension(file);}
                menu.AddItem(new GUIContent(title+" ("+Path.GetFileNameWithoutExtension(file)+")"),false,()=>{
                    try{document=CabinetDesignerDocument.Load(captured);selected=-1;dirty=false;Repaint();}
                    catch(Exception ex){EditorUtility.DisplayDialog("XML hiba",ex.Message,"OK");}
                });
            }
            menu.ShowAsContext();
        }
        void SaveDocument()
        {
            try{document.Save();dirty=false;}
            catch(Exception ex){EditorUtility.DisplayDialog("Mentési hiba",ex.Message,"OK");}
        }
        void DuplicateDocument()
        {
            document=CabinetDesignerDocument.FromXml(document.ToXml());
            document.Id=Guid.NewGuid().ToString("N");document.Name+=" - másolat";
            selected=-1;dirty=true;SaveDocument();
        }
        void DeleteDocument()
        {
            if(!EditorUtility.DisplayDialog("Szekrény törlése","Biztosan törlöd a mentett szekrényt?","Törlés","Mégse"))return;
            CabinetDesignerDocument.Delete(document);
            document=CabinetDesignerDocument.New();selected=-1;dirty=false;
        }
        void Inspector()
        {
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.LabelField("Szekrény",EditorStyles.boldLabel);
            document.Name=EditorGUILayout.TextField("Név",document.Name);
            EditorGUILayout.SelectableLabel(document.Id,EditorStyles.textField,GUILayout.Height(19));
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Nevesített méretek (mm)",EditorStyles.boldLabel);
            for(int i=0;i<document.Dimensions.Count;i++)
            {
                var d=document.Dimensions[i];
                EditorGUILayout.BeginHorizontal();
                d.Id=EditorGUILayout.TextField(d.Id,GUILayout.Width(75));
                d.Name=EditorGUILayout.TextField(d.Name);
                d.Value=EditorGUILayout.FloatField(d.Value,GUILayout.Width(70));
                if(GUILayout.Button("×",GUILayout.Width(22))){document.Dimensions.RemoveAt(i);Changed();break;}
                EditorGUILayout.EndHorizontal();
            }
            if(GUILayout.Button("+ Méret")){document.Dimensions.Add(new CabinetDesignerDocument.Dimension{
                Id="size"+(document.Dimensions.Count+1),Name="Új méret",Value=100});Changed();}
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Színek",EditorStyles.boldLabel);
            foreach(var color in document.Colors)
            {
                EditorGUILayout.BeginHorizontal();
                color.Id=EditorGUILayout.TextField(color.Id,GUILayout.Width(90));
                color.Name=EditorGUILayout.TextField(color.Name);
                Color parsed;
                if(!ColorUtility.TryParseHtmlString(color.Hex,out parsed))parsed=Color.white;
                var updated=EditorGUILayout.ColorField(parsed,GUILayout.Width(75));
                color.Hex="#"+ColorUtility.ToHtmlStringRGB(updated);
                EditorGUILayout.EndHorizontal();
            }
            if(GUILayout.Button("+ Szín")){document.Colors.Add(new CabinetDesignerDocument.PaletteColor{
                Id="color"+(document.Colors.Count+1),Name="Új szín",Hex="#FFFFFF"});Changed();}
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Anyagok",EditorStyles.boldLabel);
            foreach(var m in document.Materials)
            {
                m.Id=EditorGUILayout.TextField("Anyag ID",m.Id);
                m.Name=EditorGUILayout.TextField("Elnevezés",m.Name);
                m.ColorId=EditorGUILayout.TextField("Szín ID",m.ColorId);
                m.Texture=EditorGUILayout.TextField("Textúra útvonal",m.Texture);
            }
            if(GUILayout.Button("+ Anyag")){document.Materials.Add(new CabinetDesignerDocument.MaterialType{
                Id="material"+(document.Materials.Count+1),Name="Új anyag",ColorId=document.Colors.FirstOrDefault()?.Id??""});Changed();}
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Elemek",EditorStyles.boldLabel);
            for(int i=0;i<document.Elements.Count;i++)
            {
                EditorGUILayout.BeginHorizontal();
                if(GUILayout.Toggle(selected==i,document.Elements[i].Name+" ["+document.Elements[i].Kind+"]","Button"))
                    selected=i;
                if(GUILayout.Button("×",GUILayout.Width(24))){document.Elements.RemoveAt(i);selected=-1;Changed();break;}
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.BeginHorizontal();
            if(GUILayout.Button("+ Bútorlap"))
            {
                var p=CabinetDesignerDocument.PanelElement.Rectangle();
                p.Outline[1].XFormula="$width";
                p.Outline[2].XFormula="$width";
                p.Outline[2].YFormula="$depth";
                p.Outline[3].YFormula="$depth";
                p.Name="Bútorlap "+(document.Elements.Count+1);
                document.Elements.Add(p);selected=document.Elements.Count-1;Changed();
            }
            using(new EditorGUI.DisabledScope(selected<0||selected>=document.Elements.Count))
                if(GUILayout.Button("Elem duplikálása"))
                {
                    document.Elements.Insert(selected+1,document.Elements[selected].Clone());selected++;Changed();
                }
            EditorGUILayout.EndHorizontal();
            if(selected>=0&&selected<document.Elements.Count)PanelInspector(document.Elements[selected]);
            if(EditorGUI.EndChangeCheck())Changed();
        }
        void PanelInspector(CabinetDesignerDocument.PanelElement p)
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Kijelölt bútorlap",EditorStyles.boldLabel);
            p.Name=EditorGUILayout.TextField("Név",p.Name);
            EditorGUILayout.SelectableLabel(p.Id,EditorStyles.textField,GUILayout.Height(19));
            p.MaterialId=EditorGUILayout.TextField("Anyag ID",p.MaterialId);
            p.Position=EditorGUILayout.Vector3Field("Pozíció (mm)",p.Position);
            p.Rotation=EditorGUILayout.Vector3Field("Forgatás (°)",p.Rotation);
            p.Thickness=Mathf.Max(.1f,EditorGUILayout.FloatField("Vastagság (mm)",p.Thickness));
            EditorGUILayout.LabelField("Kész körvonal pontjai (mm)",EditorStyles.boldLabel);
            for(int i=0;i<p.Outline.Count;i++)
            {
                var vertex=p.Outline[i];
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label(vertex.Id,GUILayout.Width(30));
                vertex.Point=EditorGUILayout.Vector2Field("",vertex.Point);
                if(p.Outline.Count>3&&GUILayout.Button("×",GUILayout.Width(24)))
                {p.Bands.Remove(vertex.Id);p.Outline.RemoveAt(i);Changed();break;}
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.BeginHorizontal();
                vertex.XFormula=EditorGUILayout.TextField("X képlet",vertex.XFormula);
                vertex.YFormula=EditorGUILayout.TextField("Y képlet",vertex.YFormula);
                EditorGUILayout.EndHorizontal();
            }
            if(GUILayout.Button("+ Körvonalpont"))
            {
                p.Outline.Add(new CabinetDesignerDocument.Vertex{Id="P"+(p.Outline.Count+1)+"_"+Guid.NewGuid().ToString("N").Substring(0,4),
                    Point=p.Outline.Last().Point+new Vector2(50,50)});Changed();
            }
            EditorGUILayout.LabelField("Élfólia (mm) – az él kezdőpontja szerint",EditorStyles.boldLabel);
            foreach(var vertex in p.Outline)
            {
                float value=p.Bands.ContainsKey(vertex.Id)?p.Bands[vertex.Id]:0;
                p.Bands[vertex.Id]=Mathf.Max(0,EditorGUILayout.FloatField(vertex.Id+" → következő",value));
            }
            try
            {
                var raw=CabinetDesignerDocument.RawOutline(document.Resolve(p));
                EditorGUILayout.HelpBox("Kész méret állandó. Szabáskoordináták: "+
                    string.Join("; ",raw.Select(v=>"("+v.x.ToString("0.##")+","+v.y.ToString("0.##")+")").ToArray()),MessageType.Info);
            }
            catch(Exception ex){EditorGUILayout.HelpBox(ex.Message,MessageType.Warning);}
        }
        Color ElementColor(CabinetDesignerDocument.PanelElement p)
        {
            var m=document.Materials.FirstOrDefault(x=>x.Id==p.MaterialId);
            var color=document.Colors.FirstOrDefault(x=>x.Id==m?.ColorId);
            Color parsed;
            return ColorUtility.TryParseHtmlString(color?.Hex??"#BFA17A",out parsed)?parsed:Color.gray;
        }
        void RebuildPreview()
        {
            ClearObjects();
            foreach(var p in document.Elements)
            {
                if(p.Outline.Count<3)continue;
                try
                {
                    var resolved=document.Resolve(p);
                    foreach(var go in CabinetDesignerPreviewGeometry.Build(resolved,ElementColor(p),new Color(.19f,.43f,.65f)))
                    {
                        previewObjects.Add(go);
                        preview.AddSingleGO(go);
                    }
                }
                catch(Exception ex){Debug.LogWarning("Cabinet preview "+p.Name+": "+ex.Message);}
            }
        }
        void Preview(Rect rect)
        {
            GUI.Box(rect,GUIContent.none);
            Rect tools=new Rect(rect.x+8,rect.y+8,rect.width-16,23);
            GUILayout.BeginArea(tools);
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            foreach(Mode m in Enum.GetValues(typeof(Mode)))
                if(GUILayout.Toggle(mode==m,m.ToString(),"Button",GUILayout.Width(65)))mode=m;
            GUILayout.FlexibleSpace();
            GUILayout.Label("X/Y/Z fogantyú: húzás · jobb egér: kamera",EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
            GUILayout.EndArea();
            Rect view=new Rect(rect.x+4,rect.y+38,rect.width-8,rect.height-42);
            if(view.width<10||view.height<10)return;
            InitializePreview();
            ConfigureCamera();
            var ev=Event.current;
            if(view.Contains(ev.mousePosition))
            {
                if(ev.type==EventType.ScrollWheel)
                {
                    distance=Mathf.Clamp(distance+ev.delta.y*.12f,.3f,12);
                    ev.Use();Repaint();
                }
                if(ev.type==EventType.MouseDown)
                {
                    mouseDown=ev.mousePosition;
                    activeAxis=-1;
                    if(ev.button==0)
                    {
                        activeAxis=HitAxis(view,ev.mousePosition);
                        if(activeAxis<0)SelectAt(view,ev.mousePosition);
                    }
                    ev.Use();Repaint();
                }
                if(ev.type==EventType.MouseDrag)
                {
                    Vector2 delta=ev.mousePosition-mouseDown;mouseDown=ev.mousePosition;
                    if(ev.button==1||mode==Mode.Orbit)
                    {
                        orbit+=delta.x*.45f;
                        tilt=Mathf.Clamp(tilt-delta.y*.45f,-80,80);
                    }
                    else if(ev.button==0&&activeAxis>=0&&selected>=0&&selected<document.Elements.Count)
                        DragAxis(view,delta,activeAxis);
                    ev.Use();Repaint();
                }
                if(ev.type==EventType.MouseUp)
                {
                    activeAxis=-1;
                    ev.Use();
                }
            }
            if(ev.type!=EventType.Repaint)return;
            InitializePreview();
            RebuildPreview();
            preview.BeginPreview(view,GUIStyle.none);
            var cam=preview.camera;
            cam.Render();
            var texture=preview.EndPreview();
            GUI.DrawTexture(view,texture,ScaleMode.StretchToFill,false);
            DrawGizmo(view);
            if(selected>=0&&selected<document.Elements.Count)
                GUI.Label(new Rect(view.x+8,view.y+8,view.width-16,20),
                    "Kijelölve: "+document.Elements[selected].Name,EditorStyles.whiteLabel);
        }
        void ConfigureCamera()
        {
            var cam=preview.camera;
            Vector3 center=Vector3.zero;
            if(document.Elements.Count>0)
                center=document.Elements.Aggregate(Vector3.zero,(sum,p)=>sum+p.Position)/(document.Elements.Count*1000f);
            var orientation=Quaternion.Euler(tilt,orbit,0);
            cam.transform.position=center+orientation*new Vector3(0,0,-distance);
            cam.transform.LookAt(center);
            cam.clearFlags=CameraClearFlags.SolidColor;
            cam.backgroundColor=new Color(.15f,.17f,.20f);
        }
        Vector3 Axis(int axis)=>axis==0?Vector3.right:axis==1?Vector3.up:Vector3.forward;
        Vector3 GizmoOrigin()
        {
            var p=document.Elements[selected];
            var resolved=document.Resolve(p);
            var points=resolved.Outline.Select(v=>v.Point).ToArray();
            float minX=points.Min(v=>v.x),maxX=points.Max(v=>v.x);
            float minZ=points.Min(v=>v.y),maxZ=points.Max(v=>v.y);
            Vector3 local=new Vector3((minX+maxX)*.0005f,p.Thickness*.0005f,(minZ+maxZ)*.0005f);
            return p.Position*.001f+Quaternion.Euler(p.Rotation)*local;
        }
        Vector2 Project(Rect view,Vector3 world)
        {
            var p=preview.camera.WorldToViewportPoint(world);
            return new Vector2(view.x+p.x*view.width,view.y+(1-p.y)*view.height);
        }
        float GizmoLength()=>Mathf.Max(.13f,distance*.16f);
        Vector2[] AxisPoints(Rect view,int axis)
        {
            Vector3 origin=GizmoOrigin();
            var rotation=Quaternion.Euler(document.Elements[selected].Rotation);
            var direction=rotation*Axis(axis);
            return new[]{Project(view,origin),Project(view,origin+direction*GizmoLength())};
        }
        static float DistanceToSegment(Vector2 p,Vector2 a,Vector2 b)
        {
            var ab=b-a;
            float t=Mathf.Clamp01(Vector2.Dot(p-a,ab)/Mathf.Max(.0001f,ab.sqrMagnitude));
            return Vector2.Distance(p,a+ab*t);
        }
        int HitAxis(Rect view,Vector2 mouse)
        {
            if(mode==Mode.Orbit||selected<0||selected>=document.Elements.Count)return -1;
            float best=12;int found=-1;
            for(int axis=0;axis<3;axis++)
            {
                var ends=AxisPoints(view,axis);
                float d=DistanceToSegment(mouse,ends[0],ends[1]);
                if(d<best){best=d;found=axis;}
            }
            return found;
        }
        void DragAxis(Rect view,Vector2 mouseDelta,int axis)
        {
            var ends=AxisPoints(view,axis);
            Vector2 screen=ends[1]-ends[0];
            float screenLength=screen.magnitude;
            if(screenLength<2)return;
            float normalized=Vector2.Dot(mouseDelta,screen/screenLength)/screenLength;
            float millimetres=normalized*GizmoLength()*1000;
            var p=document.Elements[selected];
            Vector3 localAxis=Axis(axis);
            if(mode==Mode.Move)
                p.Position+=Quaternion.Euler(p.Rotation)*localAxis*millimetres;
            else if(mode==Mode.Rotate)
            {
                var euler=p.Rotation;
                euler[axis]+=normalized*90;
                p.Rotation=euler;
            }
            else if(mode==Mode.Resize)
            {
                if(axis==1)p.Thickness=Mathf.Max(.1f,p.Thickness+millimetres);
                else
                {
                    var resolved=document.Resolve(p);
                    float min=axis==0?resolved.Outline.Min(v=>v.Point.x):resolved.Outline.Min(v=>v.Point.y);
                    float max=axis==0?resolved.Outline.Max(v=>v.Point.x):resolved.Outline.Max(v=>v.Point.y);
                    float span=Mathf.Max(.01f,max-min);
                    float scale=Mathf.Max(.02f,(span+millimetres)/span);
                    for(int i=0;i<p.Outline.Count;i++)
                    {
                        var vertex=p.Outline[i];
                        var point=resolved.Outline[i].Point;
                        if(axis==0){vertex.Point.x=min+(point.x-min)*scale;vertex.XFormula="";}
                        else {vertex.Point.y=min+(point.y-min)*scale;vertex.YFormula="";}
                    }
                }
            }
            Changed();
        }
        void DrawGizmo(Rect view)
        {
            if(mode==Mode.Orbit||selected<0||selected>=document.Elements.Count)return;
            try
            {
                for(int axis=0;axis<3;axis++)
                {
                    var ends=AxisPoints(view,axis);
                    Handles.BeginGUI();
                    Handles.color=axisColors[axis];
                    Handles.DrawAAPolyLine(activeAxis==axis?5:3,ends[0],ends[1]);
                    Handles.EndGUI();
                    var marker=new Rect(ends[1].x-9,ends[1].y-9,18,18);
                    Color old=GUI.color;GUI.color=axisColors[axis];
                    GUI.Box(marker,axis==0?"X":axis==1?"Y":"Z");
                    GUI.color=old;
                }
            }
            catch(Exception ex){Debug.LogWarning("Gizmo: "+ex.Message);}
        }
        void SelectAt(Rect view,Vector2 mouse)
        {
            // Hit-test against the current preview's mesh colliders.
            if(preview==null)return;
            var cam=preview.camera;
            float u=(mouse.x-view.x)/view.width;
            float v=1-(mouse.y-view.y)/view.height;
            var ray=cam.ViewportPointToRay(new Vector3(u,v,0));
            float nearest=float.MaxValue;int found=-1;
            for(int i=0;i<previewObjects.Count;i++)
            {
                var collider=previewObjects[i].GetComponent<MeshCollider>();
                RaycastHit hit;
                if(collider!=null&&collider.Raycast(ray,out hit,100)&&hit.distance<nearest)
                {nearest=hit.distance;found=i;}
            }
            if(found>=0)
            {
                var item=previewObjects[found];
                selected=document.Elements.FindIndex(p=>item.name.StartsWith(p.Name+"_core",StringComparison.Ordinal)||item.name.StartsWith(p.Name+"_band_",StringComparison.Ordinal));
                Repaint();
            }
        }
    }
}
#endif