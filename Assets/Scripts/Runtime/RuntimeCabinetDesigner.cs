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
        bool elementExpanded=true;
        bool cabinetExpanded=true,colorsExpanded=true,materialsExpanded=true;
        readonly HashSet<string> openPanels=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        HashSet<int> collisions=new HashSet<int>();
        string collisionWarning="";
        Vector2 scroll,libraryScroll;
        bool libraryOpen,dirty;
        bool colorPopup,texturePopup;
        int editingColor=-1,editingMaterial=-1;
        string colorInput="#383E42",ralInput="RAL 7016",textureSource="";
        Vector2 paletteScroll,textureScroll;
        readonly Dictionary<string,Texture2D> textureCache=new Dictionary<string,Texture2D>();
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
        bool showPointLabels=true,pointEditMode;
        int firstPoint=-1,secondPoint=-1;
        string scrubKey="";
        float scrubStart;
        Vector2 scrubMouse;
        bool scrubActive;
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
            foreach(var entry in textureCache)if(entry.Key.StartsWith("imported:")&&entry.Value!=null)Destroy(entry.Value);
            textureCache.Clear();
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
            document.EnsurePanelIds();
            collisions=CabinetCollision.FindAll(document);
            collisionWarning=collisions.Count>0?"Ütközés: "+string.Join(", ",collisions.OrderBy(i=>i).Select(i=>document.Elements[i].Name).ToArray()):"";
            for(int i=0;i<document.Elements.Count;i++)
            {
                var p=document.Elements[i];
                try
                {
                    var resolved=document.Resolve(p);
                    var raw=CabinetDesignerDocument.RawOutline(resolved);
                    var finished=resolved.Outline.Select(v=>v.Point).ToArray();
                    Color color=ColorFor(p);
                    AddSolid(p.Name+" core",raw,p.Thickness,p.Position,p.Rotation,color,i,p.MaterialId);
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
        void AddSolid(string name,Vector2[] outline,float thickness,Vector3 position,Vector3 rotation,Color color,int owner,string materialId=null)
        {
            var obj=LShapePanelMesh.Create(name,outline.Select(p=>p*.001f).ToList(),thickness*.001f,
                position*.001f,0,color);
            obj.transform.rotation=Quaternion.Euler(rotation);
            if(materialId!=null)
            {
                var material=document.Materials.FirstOrDefault(x=>x.Id==materialId);
                var id=material?.Texture;
                if(!string.IsNullOrWhiteSpace(id))
                {
                    Texture2D texture;
                    if(!textureCache.TryGetValue(id,out texture))
                    {texture=CabinetAppearanceLibrary.Load(id);textureCache[id]=texture;}
                    if(texture!=null)
                    {
                        var renderer=obj.GetComponent<MeshRenderer>();
                        renderer.material.mainTexture=texture;
                    }
                }
            }
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
            if(GUILayout.Toggle(showPointLabels,"Pontok","Button",GUILayout.Width(72))!=showPointLabels)showPointLabels=!showPointLabels;
            if(GUILayout.Toggle(pointEditMode,"Pontpár","Button",GUILayout.Width(72))!=pointEditMode)
            {pointEditMode=!pointEditMode;firstPoint=-1;secondPoint=-1;}
            foreach(string mode in modes)if(GUILayout.Toggle(editMode==mode,mode,"Button",GUILayout.Width(80)))editMode=mode;
            GUILayout.Label("X / Y / Z fogantyúk • jobb egér: forgatás");
            GUILayout.EndHorizontal();GUILayout.EndArea();
            DrawGizmos();
            DrawPointLabels();
            if(pointEditMode)DrawPointPairEditor();
            if(libraryOpen)DrawLibrary();
            if(colorPopup)DrawColorPopup();
            if(texturePopup)DrawTexturePopup();
            if(!string.IsNullOrEmpty(error)||!string.IsNullOrEmpty(collisionWarning))
                GUI.Box(new Rect(left+14,Screen.height-75,Mathf.Max(200,Screen.width-left-35),60),
                    (string.IsNullOrEmpty(collisionWarning)?"":collisionWarning+"\\n")+error);
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
        void OpenColorPicker(int index)
        {
            editingColor=index;colorInput=document.Colors[index].Hex;
            ralInput="";colorPopup=true;texturePopup=false;
        }
        void ApplyColor(string hex,string name=null)
        {
            Color parsed;
            if(!ColorUtility.TryParseHtmlString(hex,out parsed))
            {error="Érvénytelen HEX szín: "+hex;return;}
            if(editingColor<0||editingColor>=document.Colors.Count)return;
            var color=document.Colors[editingColor];
            color.Hex="#"+ColorUtility.ToHtmlStringRGB(parsed);
            if(!string.IsNullOrEmpty(name))color.Name=name;
            colorInput=color.Hex;dirty=true;Rebuild();
        }
        void DrawColorPopup()
        {
            var rect=new Rect(Mathf.Max(5,(Screen.width-540)*.5f),Mathf.Max(5,(Screen.height-560)*.5f),
                Mathf.Min(540,Screen.width-10),Mathf.Min(560,Screen.height-10));
            GUI.Box(rect,"Színválasztó / RAL");
            GUILayout.BeginArea(new Rect(rect.x+12,rect.y+30,rect.width-24,rect.height-42));
            if(editingColor>=0&&editingColor<document.Colors.Count)
            {
                var item=document.Colors[editingColor];
                item.Name=Field("colorName"+item.Id,"Szín neve",item.Name);
                GUILayout.BeginHorizontal();
                GUILayout.Label("HEX",GUILayout.Width(60));
                colorInput=GUILayout.TextField(colorInput);
                if(GUILayout.Button("Alkalmaz",GUILayout.Width(90)))ApplyColor(colorInput);
                GUILayout.EndHorizontal();
                Color color;
                if(!ColorUtility.TryParseHtmlString(colorInput,out color))color=Color.white;
                float red=GUILayout.HorizontalSlider(color.r,0,1);
                float green=GUILayout.HorizontalSlider(color.g,0,1);
                float blue=GUILayout.HorizontalSlider(color.b,0,1);
                GUILayout.Label("R: "+Mathf.RoundToInt(red*255)+"   G: "+Mathf.RoundToInt(green*255)+"   B: "+Mathf.RoundToInt(blue*255));
                if(!Mathf.Approximately(red,color.r)||!Mathf.Approximately(green,color.g)||!Mathf.Approximately(blue,color.b))
                    ApplyColor("#"+ColorUtility.ToHtmlStringRGB(new Color(red,green,blue)));
                Color old=GUI.backgroundColor;GUI.backgroundColor=color;
                GUILayout.Box("Aktuális szín",GUILayout.Height(42),GUILayout.ExpandWidth(true));
                GUI.backgroundColor=old;
                GUILayout.BeginHorizontal();
                GUILayout.Label("RAL kód",GUILayout.Width(70));
                ralInput=GUILayout.TextField(ralInput);
                if(GUILayout.Button("RAL keresés",GUILayout.Width(100)))
                {
                    string code=ralInput.Trim().ToUpperInvariant().Replace(" ","");
                    var match=CabinetAppearanceLibrary.Presets.FirstOrDefault(s=>s.Name.Replace(" ","").ToUpperInvariant().StartsWith(code));
                    if(!string.IsNullOrEmpty(match.Hex))ApplyColor(match.Hex,match.Name);
                    else error="Ez a RAL kód nincs a beépített palettában; HEX/RGB szabadon megadható.";
                }
                GUILayout.EndHorizontal();
                GUILayout.Label("Választható RAL és alapszínek (képernyős közelítések):");
                paletteScroll=GUILayout.BeginScrollView(paletteScroll,GUILayout.Height(Mathf.Min(205,rect.height*.37f)));
                foreach(var swatch in CabinetAppearanceLibrary.Presets)
                {
                    Color sw;ColorUtility.TryParseHtmlString(swatch.Hex,out sw);
                    GUILayout.BeginHorizontal();
                    Color prev=GUI.backgroundColor;GUI.backgroundColor=sw;
                    if(GUILayout.Button("      ",GUILayout.Width(70)))ApplyColor(swatch.Hex,swatch.Name);
                    GUI.backgroundColor=prev;
                    if(GUILayout.Button(swatch.Name+"  "+swatch.Hex))ApplyColor(swatch.Hex,swatch.Name);
                    GUILayout.EndHorizontal();
                }
                GUILayout.EndScrollView();
            }
            if(GUILayout.Button("Bezárás"))colorPopup=false;
            GUILayout.EndArea();
        }
        void SelectTexture(string id)
        {
            if(editingMaterial<0||editingMaterial>=document.Materials.Count)return;
            document.Materials[editingMaterial].Texture=id;
            dirty=true;Rebuild();texturePopup=false;
        }
        void DrawTexturePopup()
        {
            var rect=new Rect(Mathf.Max(5,(Screen.width-600)*.5f),Mathf.Max(5,(Screen.height-550)*.5f),
                Mathf.Min(600,Screen.width-10),Mathf.Min(550,Screen.height-10));
            GUI.Box(rect,"Textúraválasztó");
            GUILayout.BeginArea(new Rect(rect.x+12,rect.y+30,rect.width-24,rect.height-42));
            if(GUILayout.Button("Textúra nélkül",GUILayout.Height(28)))SelectTexture("");
            GUILayout.Label("A projektben lévő és korábban importált textúrák:");
            textureScroll=GUILayout.BeginScrollView(textureScroll,GUILayout.Height(Mathf.Min(285,rect.height*.52f)));
            foreach(string resource in CabinetAppearanceLibrary.ResourceTextures())
                TextureChoice("resource:"+resource,resource);
            foreach(string imported in CabinetAppearanceLibrary.ImportedTextures())
                TextureChoice("imported:"+imported,imported);
            GUILayout.EndScrollView();
            GUILayout.Label("Új PNG/JPG textúra feltöltése helyi fájlból:");
            GUILayout.BeginHorizontal();
            textureSource=GUILayout.TextField(textureSource);
            if(GUILayout.Button("Tallózás",GUILayout.Width(85)))BrowseTexture();
            GUILayout.EndHorizontal();
            if(GUILayout.Button("Fájl importálása és kiválasztása"))
            {
                try{SelectTexture(CabinetAppearanceLibrary.Import(textureSource));}
                catch(Exception ex){error=ex.Message;}
            }
            if(GUILayout.Button("Bezárás"))texturePopup=false;
            GUILayout.EndArea();
        }
        void TextureChoice(string id,string name)
        {
            GUILayout.BeginHorizontal();
            Texture2D texture;
            if(!textureCache.TryGetValue(id,out texture))
            {texture=CabinetAppearanceLibrary.Load(id);textureCache[id]=texture;}
            if(texture!=null)GUILayout.Label(texture,GUILayout.Width(48),GUILayout.Height(40));
            if(GUILayout.Button(name,GUILayout.Height(40)))SelectTexture(id);
            GUILayout.EndHorizontal();
        }
        void BrowseTexture()
        {
#if UNITY_EDITOR
            textureSource=UnityEditor.EditorUtility.OpenFilePanel("Textúra kiválasztása","","png,jpg,jpeg");
#elif UNITY_STANDALONE_WIN
            // File dialog is asynchronous so the render loop remains responsive.
            var thread=new System.Threading.Thread(()=>{
                try
                {
                    var command="Add-Type -AssemblyName System.Windows.Forms; $d=New-Object System.Windows.Forms.OpenFileDialog; $d.Filter='Képek (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg'; if($d.ShowDialog() -eq 'OK') {[Console]::Write($d.FileName)}";
                    var psi=new System.Diagnostics.ProcessStartInfo("powershell.exe",
                        "-NoProfile -STA -Command "+(char)34+command+(char)34);
                    psi.UseShellExecute=false;psi.RedirectStandardOutput=true;psi.CreateNoWindow=true;
                    using(var process=System.Diagnostics.Process.Start(psi))
                    {
                        var path=process.StandardOutput.ReadToEnd().Trim();process.WaitForExit();
                        if(!string.IsNullOrEmpty(path))pendingTexturePath=path;
                    }
                }
                catch(Exception ex){pendingTextureError=ex.Message;}
            });
            thread.IsBackground=true;thread.SetApartmentState(System.Threading.ApartmentState.STA);thread.Start();
#else
            error="A tallózás Windows buildben és Unity Editorban érhető el; add meg a fájl útvonalát.";
#endif
        }
        volatile string pendingTexturePath,pendingTextureError;
        void Update()
        {
            if(pendingTexturePath!=null){textureSource=pendingTexturePath;pendingTexturePath=null;}
            if(pendingTextureError!=null){error=pendingTextureError;pendingTextureError=null;}
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
            var ev=Event.current;
            bool rotation=label.StartsWith("Forgatás ");
            GUILayout.BeginHorizontal();
            Rect labelRect=GUILayoutUtility.GetRect(new GUIContent(label+" ↔"),GUI.skin.label,GUILayout.Width(100));
            GUI.Label(labelRect,label+" ↔");
            int control=GUIUtility.GetControlID(key.GetHashCode(),FocusType.Passive,labelRect);
            if(ev.type==EventType.MouseDown&&ev.button==0&&labelRect.Contains(ev.mousePosition))
            {
                GUIUtility.hotControl=control; scrubKey=key; scrubStart=value;
                scrubMouse=ev.mousePosition; scrubActive=true; ev.Use();
            }
            if(scrubActive&&scrubKey==key)
            {
                if(ev.type==EventType.MouseDrag&&GUIUtility.hotControl==control)
                {
                    float dx=ev.mousePosition.x-scrubMouse.x;
                    float next=rotation?Mathf.Round((scrubStart+dx*.9f)/90f)*90f:
                        scrubStart+dx*(ev.shift?0.1f:1f);
                    fields[key]=next.ToString("0.###",System.Globalization.CultureInfo.InvariantCulture);
                    dirty=true;ev.Use();
                }
                if(ev.type==EventType.KeyDown&&ev.keyCode==KeyCode.Escape)
                {
                    fields[key]=scrubStart.ToString("0.###",System.Globalization.CultureInfo.InvariantCulture);
                    scrubActive=false;GUIUtility.hotControl=0;ev.Use();
                }
                if(ev.type==EventType.MouseUp&&ev.button==0)
                {scrubActive=false;GUIUtility.hotControl=0;ev.Use();}
            }
            string input;
            if(!fields.TryGetValue(key,out input))
                input=value.ToString("0.###",System.Globalization.CultureInfo.InvariantCulture);
            GUI.SetNextControlName(key);
            string nextText=GUILayout.TextField(input);
            bool enter=ev.type==EventType.KeyDown&&(ev.keyCode==KeyCode.Return||ev.keyCode==KeyCode.KeypadEnter)&&GUI.GetNameOfFocusedControl()==key;
            if(enter)
            {
                try
                {
                    float evaluated=CabinetFormulaResolver.Evaluate(document,nextText);
                    nextText=evaluated.ToString("0.###",System.Globalization.CultureInfo.InvariantCulture);
                    GUI.FocusControl("");ev.Use();
                }
                catch(Exception ex){error="Érték: "+ex.Message;}
            }
            fields[key]=nextText;
            GUILayout.EndHorizontal();
            float parsed;
            if(float.TryParse(nextText,System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,out parsed)&&Mathf.Abs(parsed-value)>.00001f)
            {dirty=true;return parsed;}
            return value;
        }
        float FormulaField(CabinetDesignerDocument.PanelElement panel,CabinetDesignerDocument.Vertex vertex,bool x)
        {
            string key=panel.Id+"."+vertex.Id+(x?"X":"Y");
            string fieldKey="expr"+key;
            float current=x?vertex.Point.x:vertex.Point.y;
            string formula=x?vertex.XFormula:vertex.YFormula;
            string display=string.IsNullOrWhiteSpace(formula)?
                current.ToString("0.###",System.Globalization.CultureInfo.InvariantCulture):formula;
            GUILayout.BeginHorizontal();
            GUILayout.Label(vertex.Id+(x?" X":" Y"),GUILayout.Width(100));
            string input;
            if(!fields.TryGetValue(fieldKey,out input))input=display;
            GUI.SetNextControlName(fieldKey);
            string next=GUILayout.TextField(input);
            fields[fieldKey]=next;
            bool enter=Event.current.type==EventType.KeyDown &&
                (Event.current.keyCode==KeyCode.Return||Event.current.keyCode==KeyCode.KeypadEnter) &&
                GUI.GetNameOfFocusedControl()==fieldKey;
            bool apply=GUILayout.Button("✓",GUILayout.Width(28));
            if(apply||enter)
            {
                try
                {
                    if(string.IsNullOrWhiteSpace(next))throw new FormatException("Üres képlet");
                    float result=CabinetFormulaResolver.Evaluate(document,next);
                    float literalValue;
                    bool literal=float.TryParse(next,System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,out literalValue);
                    if(x){vertex.Point.x=result;vertex.XFormula=literal?"":next;}
                    else {vertex.Point.y=result;vertex.YFormula=literal?"":next;}
                    fields[fieldKey]=literal?result.ToString("0.###",System.Globalization.CultureInfo.InvariantCulture):next;
                    dirty=true;Rebuild();
                    if(enter){GUI.FocusControl("");Event.current.Use();}
                }
                catch(Exception ex){error="Képlet ("+key+"): "+ex.Message;}
            }
            GUILayout.EndHorizontal();
            return x?vertex.Point.x:vertex.Point.y;
        }
        void DrawInspector()
        {
            if(GUILayout.Button((cabinetExpanded?"▼ ":"▶ ")+"Szekrény"))cabinetExpanded=!cabinetExpanded;
            if(cabinetExpanded){
            string name=Field("cabinetName","Név",document.Name);
            if(name!=document.Name){document.Name=name;dirty=true;}
            GUILayout.Label("Méretek (mm)");
            foreach(var dim in document.Dimensions)
            {
                dim.Name=Field("dn"+dim.Id,dim.Id,dim.Name);
                float original=dim.Value,instance=dim.EffectiveValue;
                dim.Value=FloatField("dv"+dim.Id,dim.Name+" alap",dim.Value);
                if(float.IsNaN(dim.InstanceValue))dim.InstanceValue=original;
                dim.InstanceValue=FloatField("di"+dim.Id,"Példány",instance);
                GUILayout.Label(dim.Id+".delta = "+dim.Delta.ToString("0.###",System.Globalization.CultureInfo.InvariantCulture)+" mm");
                if(original!=dim.Value||instance!=dim.EffectiveValue){dirty=true;Rebuild();}
            }
            if(GUILayout.Button("+ Nevesített méret"))
            {document.Dimensions.Add(new CabinetDesignerDocument.Dimension{Id="size"+Guid.NewGuid().ToString("N").Substring(0,5),Name="Méret",Value=100});dirty=true;}
            }
            if(GUILayout.Button((colorsExpanded?"▼ ":"▶ ")+"Színek"))colorsExpanded=!colorsExpanded;
            if(colorsExpanded){
            for(int i=0;i<document.Colors.Count;i++)
            {
                var color=document.Colors[i];
                GUILayout.BeginHorizontal();
                GUILayout.Label(color.Name,GUILayout.Width(105));
                Color swatch;
                if(!ColorUtility.TryParseHtmlString(color.Hex,out swatch))swatch=Color.magenta;
                Color old=GUI.backgroundColor;GUI.backgroundColor=swatch;
                if(GUILayout.Button("      ",GUILayout.Width(55),GUILayout.Height(26)))OpenColorPicker(i);
                GUI.backgroundColor=old;
                GUILayout.Label(color.Hex,GUILayout.Width(88));
                if(GUILayout.Button("Színválasztó...",GUILayout.Width(115)))OpenColorPicker(i);
                GUILayout.EndHorizontal();
            }
            if(GUILayout.Button("+ Szín"))
            {
                document.Colors.Add(new CabinetDesignerDocument.PaletteColor{
                    Id="color"+Guid.NewGuid().ToString("N").Substring(0,5),Name="Új szín",Hex="#FFFFFF"});
                dirty=true;OpenColorPicker(document.Colors.Count-1);
            }
            }
            if(GUILayout.Button((materialsExpanded?"▼ ":"▶ ")+"Anyagok"))materialsExpanded=!materialsExpanded;
            if(materialsExpanded){
            for(int i=0;i<document.Materials.Count;i++)
            {
                var material=document.Materials[i];
                material.Name=Field("matname"+material.Id,material.Id,material.Name);
                material.ColorId=Field("matcolor"+material.Id,"Szín ID",material.ColorId);
                GUILayout.BeginHorizontal();
                GUILayout.Label("Textúra",GUILayout.Width(100));
                GUILayout.Label(string.IsNullOrEmpty(material.Texture)?"Nincs":material.Texture);
                if(GUILayout.Button("Választás...",GUILayout.Width(100)))
                {editingMaterial=i;texturePopup=true;colorPopup=false;}
                GUILayout.EndHorizontal();
            }
            if(GUILayout.Button("+ Anyag"))
            {document.Materials.Add(new CabinetDesignerDocument.MaterialType{Id="material"+Guid.NewGuid().ToString("N").Substring(0,5),Name="Anyag",ColorId=document.Colors.FirstOrDefault()?.Id??""});dirty=true;}
            }
            if(GUILayout.Button((elementExpanded?"▼ ":"▶ ")+"Alkatrészek ("+document.Elements.Count+")",GUILayout.Height(32)))elementExpanded=!elementExpanded;
            if(elementExpanded)
            {
            for(int i=0;i<document.Elements.Count;i++)
            {
                GUILayout.BeginHorizontal();
                var previousColor=GUI.backgroundColor;
                if(collisions.Contains(i))GUI.backgroundColor=new Color(1f,.32f,.32f);
                var item=document.Elements[i];
                bool opened=openPanels.Contains(item.Id);
                if(GUILayout.Button((opened?"▼ ":"▶ ")+(collisions.Contains(i)?"⚠ ":"")+item.Id+" "+item.Name))
                {
                    if(opened)openPanels.Remove(item.Id);else openPanels.Add(item.Id);
                    selected=i;fields.Clear();
                }
                GUI.backgroundColor=previousColor;
                if(GUILayout.Button("×",GUILayout.Width(28)))
                {document.Elements.RemoveAt(i);selected=-1;dirty=true;Rebuild();GUILayout.EndHorizontal();break;}
                GUILayout.EndHorizontal();
            }
            GUILayout.BeginHorizontal();
            if(GUILayout.Button("+ Bútorlap"))
            {
                var panel=CabinetDesignerDocument.PanelElement.Rectangle();
                panel.Name="Bútorlap "+(document.Elements.Count+1);
                document.Elements.Add(panel);document.EnsurePanelIds();openPanels.Add(panel.Id);selected=document.Elements.Count-1;fields.Clear();dirty=true;Rebuild();
            }
            if(GUILayout.Button("+ Bútorlap L alak"))
            {
                var panel=CabinetDesignerDocument.PanelElement.LShape();
                panel.Name="L alakú bútorlap "+(document.Elements.Count+1);
                document.Elements.Add(panel);
                document.EnsurePanelIds();openPanels.Add(panel.Id);
                selected=document.Elements.Count-1;
                fields.Clear();
                dirty=true;
                Rebuild();
            }
            if(GUILayout.Button("Elem duplikálása")&&selected>=0&&selected<document.Elements.Count)
            {
                document.Elements.Add(document.Elements[selected].Clone());document.EnsurePanelIds();openPanels.Add(document.Elements.Last().Id);selected=document.Elements.Count-1;fields.Clear();dirty=true;Rebuild();
            }
            GUILayout.EndHorizontal();
            }
            foreach(var p in document.Elements.Where(item=>openPanels.Contains(item.Id)).ToArray())
            {
            GUILayout.Label("Bútorlap "+p.Id,GUI.skin.box);
            string newId=Field("panelid"+p.Id,"ID",p.Id).Trim();
            if(newId!=p.Id)
            {
                if(System.Text.RegularExpressions.Regex.IsMatch(newId,@"^[A-Za-z_][A-Za-z0-9_]*$") &&
                    !document.Elements.Any(other=>other!=p&&string.Equals(other.Id,newId,StringComparison.OrdinalIgnoreCase)))
                {openPanels.Remove(p.Id);p.Id=newId;openPanels.Add(newId);fields.Clear();dirty=true;Rebuild();}
                else error="Érvénytelen vagy már foglalt bútorlap ID.";
            }

            p.Name=Field("pname"+p.Id,"Név",p.Name);
            p.MaterialId=Field("pmat"+p.Id,"Anyag ID",p.MaterialId);
            var pos=p.Position;var rot=p.Rotation;float thick=p.Thickness;
            pos.x=FloatField("px","Pozíció X",pos.x);pos.y=FloatField("py","Pozíció Y",pos.y);pos.z=FloatField("pz","Pozíció Z",pos.z);
            rot.x=FloatField("rx","Forgatás X",rot.x);rot.y=FloatField("ry","Forgatás Y",rot.y);rot.z=FloatField("rz","Forgatás Z",rot.z);
            thick=Mathf.Max(.1f,FloatField("thick","Vastagság",thick));
            if(pos!=p.Position||rot!=p.Rotation||thick!=p.Thickness)
            {p.Position=pos;p.Rotation=rot;p.Thickness=thick;dirty=true;Rebuild();}
            GUILayout.Label("Kész körvonal (mm)");
            foreach(var v in p.Outline)
            {
                Vector2 old=v.Point;
                v.Point=new Vector2(FormulaField(p,v,true),FormulaField(p,v,false));
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
                if(pointEditMode&&selected>=0&&SelectPointAt(mouse))return;
                dragAxis=pointEditMode?-1:HitHandle(mouse);dragging=dragAxis>=0;previousMouse=mouse;
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
                var moveStart=p.Position;
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
                // Stop at the first contact. Positions are in mm; a tiny 0.1 mm
                // clearance prevents floating-point SAT tolerance from leaving a red collision.
                if(editMode=="Move"&&dragAxis>=0)
                {
                    Vector3 destination=p.Position;
                    Vector3 moveDirection=destination-moveStart;
                    float distance=moveDirection.magnitude;
                    if(distance>0.00001f)
                    {
                        bool startedOverlapping;
                        p.Position=moveStart;
                        startedOverlapping=CabinetCollision.Collides(document,selected);
                        if(startedOverlapping)
                        {
                            // An already invalid position must not be treated as a safe boundary.
                            // Permit motion out of the collision; otherwise restore the start.
                            p.Position=destination;
                            if(CabinetCollision.Collides(document,selected))p.Position=moveStart;
                        }
                        else
                        {
                            float safe=0f,blocked=distance;
                            bool hit=false;
                            int steps=Mathf.Clamp(Mathf.CeilToInt(distance),1,16384);
                            for(int step=1;step<=steps;step++)
                            {
                                float traveled=distance*step/steps;
                                p.Position=moveStart+moveDirection*(traveled/distance);
                                if(CabinetCollision.Collides(document,selected))
                                {blocked=traveled;hit=true;break;}
                                safe=traveled;
                            }
                            if(hit)
                            {
                                // Binary search the actual contact boundary to sub-mm precision.
                                for(int iteration=0;iteration<24;iteration++)
                                {
                                    float mid=(safe+blocked)*.5f;
                                    p.Position=moveStart+moveDirection*(mid/distance);
                                    if(CabinetCollision.Collides(document,selected))blocked=mid;
                                    else safe=mid;
                                }
                                safe=Mathf.Max(0f,safe-.1f);
                            }
                            p.Position=moveStart+moveDirection*(safe/distance);
                            if(!hit)p.Position=destination;
                            // Defensive correction if the geometry/tolerance is degenerate.
                            if(CabinetCollision.Collides(document,selected))p.Position=moveStart;
                        }
                    }
                }
                dirty=true;fields.Clear();Rebuild();
            }
        }
        void DrawPointLabels()
        {
            if(!showPointLabels&&!pointEditMode)return;
            if(selected<0||selected>=document.Elements.Count)return;
            if(Event.current.type!=EventType.Repaint)return;
            var panel=document.Elements[selected];
            var resolved=document.Resolve(panel);
            for(int i=0;i<resolved.Outline.Count;i++)
            {
                var vertex=resolved.Outline[i];
                var world=panel.Position*.001f+Quaternion.Euler(panel.Rotation)*
                    new Vector3(vertex.Point.x*.001f,panel.Thickness*.001f,vertex.Point.y*.001f);
                if(camera3D.WorldToViewportPoint(world).z<=0)continue;
                var screen=Project(world);
                var previous=GUI.backgroundColor;
                GUI.backgroundColor=i==firstPoint||i==secondPoint?Color.yellow:new Color(.6f,.85f,1f);
                GUI.Box(new Rect(screen.x-24,screen.y-14,48,28),vertex.Id);
                GUI.backgroundColor=previous;
            }
        }
        bool SelectPointAt(Vector2 mouse)
        {
            var panel=document.Elements[selected];
            var resolved=document.Resolve(panel);
            float closest=23f;int hit=-1;
            for(int i=0;i<resolved.Outline.Count;i++)
            {
                var point=resolved.Outline[i].Point;
                var world=panel.Position*.001f+Quaternion.Euler(panel.Rotation)*
                    new Vector3(point.x*.001f,panel.Thickness*.001f,point.y*.001f);
                if(camera3D.WorldToViewportPoint(world).z<=0)continue;
                float distance=Vector2.Distance(Project(world),mouse);
                if(distance<closest){closest=distance;hit=i;}
            }
            if(hit<0)return false;
            if(firstPoint<0||secondPoint>=0){firstPoint=hit;secondPoint=-1;}
            else if(hit!=firstPoint)secondPoint=hit;
            fields.Remove("pairX");fields.Remove("pairY");
            return true;
        }
        void DrawPointPairEditor()
        {
            if(selected<0||selected>=document.Elements.Count)return;
            var panel=document.Elements[selected];
            var rect=new Rect(viewport.x+12,viewport.y+48,Mathf.Min(330,viewport.width-24),178);
            GUI.Box(rect,"Pontpár szerkesztése");
            GUILayout.BeginArea(new Rect(rect.x+8,rect.y+24,rect.width-16,rect.height-30));
            if(firstPoint<0)GUILayout.Label("Kattints az első körvonalpontra.");
            else if(secondPoint<0)GUILayout.Label(panel.Outline[firstPoint].Id+" kiválasztva. Kattints a második pontra.");
            else
            {
                var first=panel.Outline[firstPoint];var second=panel.Outline[secondPoint];
                GUILayout.Label(first.Id+" → "+second.Id+" (csak a második pont változik)");
                float dx=second.Point.x-first.Point.x,dy=second.Point.y-first.Point.y;
                float nextX=FloatField("pairX","Eltérés X",dx);
                float nextY=FloatField("pairY","Eltérés Y",dy);
                if(!Mathf.Approximately(nextX,dx)||!Mathf.Approximately(nextY,dy))
                {
                    second.Point=new Vector2(first.Point.x+nextX,first.Point.y+nextY);
                    second.XFormula="";second.YFormula="";
                    dirty=true;Rebuild();
                }
            }
            if(GUILayout.Button("Kijelölés törlése"))
            {firstPoint=-1;secondPoint=-1;fields.Remove("pairX");fields.Remove("pairY");}
            GUILayout.EndArea();
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