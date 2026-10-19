using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace BuildYourFurniture
{
    // Add this component to one empty GameObject in an empty Unity scene.
    // The entire UI is constructed at runtime from Resources/UI/*.xml.
    public sealed class FurnitureApp : MonoBehaviour
    {
        Canvas canvas;
        Font font;
        string current = "main";
        XDocument plan;
        readonly List<GameObject> generated = new List<GameObject>();
        readonly List<GameObject> geometry = new List<GameObject>();
        Camera worldCamera;
        bool mouseLook;
        string status = "";
        static float N(XElement e, string key, float fallback = 0) => float.TryParse((string)e.Attribute(key), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;
        static string S(XElement e, string key, string fallback = "") => (string)e.Attribute(key) ?? fallback;

        void Start()
        {
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var go = new GameObject("Generated UI Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            EnsureEventSystem();
            go.GetComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            go.GetComponent<CanvasScaler>().referenceResolution = new Vector2(1920, 1080);
            var sample = Resources.Load<TextAsset>("Plans/sample");
            if (sample != null) LoadPlan(sample.text);
            Show("main");
        }
        void EnsureEventSystem()
        {
            if (EventSystem.current != null) return;
            var eventObject = new GameObject("UI EventSystem", typeof(EventSystem));
            // Use the new Input System module when installed; otherwise use legacy input.
            var inputModuleType = Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            if (inputModuleType != null) eventObject.AddComponent(inputModuleType);
            else eventObject.AddComponent<StandaloneInputModule>();
        }
        static Sprite LoadUiSprite(string resource)
        {
            if (string.IsNullOrWhiteSpace(resource)) return null;
            var sprite = Resources.Load<Sprite>(resource);
            if (sprite != null) return sprite;
            var texture = Resources.Load<Texture2D>(resource);
            if (texture != null) return Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(.5f,.5f));
            Debug.LogWarning("Missing UI image: Assets/Resources/" + resource + ".png");
            return null;
        }
        void Clear()
        {
            foreach (var g in generated) if (g) Destroy(g);
            generated.Clear();
            foreach (var g in geometry) if (g) Destroy(g);
            geometry.Clear();
            if (worldCamera) Destroy(worldCamera.gameObject);
            worldCamera = null;
        }
        GameObject UIObject(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            generated.Add(go);
            return go;
        }
        RectTransform Rect(GameObject go, XElement e)
        {
            var r = go.GetComponent<RectTransform>();
            r.anchorMin = r.anchorMax = new Vector2(0.5f + N(e,"x"), 0.5f + N(e,"y"));
            r.sizeDelta = new Vector2(N(e,"width",0.3f)*1920, N(e,"height",0.1f)*1080);
            return r;
        }
        Text Label(string name, string value, XElement e)
        {
            var go = UIObject(name, canvas.transform);
            Rect(go,e);
            var t = go.AddComponent<Text>();
            t.font = font; t.text = value; t.fontSize = (int)N(e,"size",24);
            t.alignment = TextAnchor.MiddleCenter; t.color = Color.white;
            return t;
        }
        void Show(string screen)
        {
            Clear(); current = screen;
            var asset = Resources.Load<TextAsset>("UI/"+screen);
            if (!asset) { Debug.LogError("Missing UI xml: "+screen); return; }
            var root = XDocument.Parse(asset.text).Root;
            var bg = UIObject("Background",canvas.transform);
            var rect = bg.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
            var image = bg.AddComponent<Image>();
            ColorUtility.TryParseHtmlString(S(root,"background","#172332"),out var color); image.color = color;
            var backgroundSprite = LoadUiSprite(S(root,"backgroundImage"));
            if (backgroundSprite) { image.sprite = backgroundSprite; image.color = Color.white; image.type = Image.Type.Simple; image.preserveAspect = false; }
            if (screen == "viewer") { image.color = Color.clear; image.raycastTarget = false; }
            else image.raycastTarget = false;
            foreach (var e in root.Elements())
            {
                if (e.Name.LocalName == "label")
                {
                    var value = S(e,"text");
                    if (S(e,"id") == "planName") value = "Aktuális terv: " + (plan == null ? "nincs" : S(plan.Root,"name"));
                    if (S(e,"id") == "materialsTable") value = MaterialsCsv().Replace(';','\t');
                    Label(S(e,"id","Label"),value,e);
                }
                else if (e.Name.LocalName == "button")
                {
                    var go = UIObject("Button",canvas.transform); Rect(go,e);
                    var img = go.AddComponent<Image>(); img.color = new Color(.15f,.35f,.49f,.95f);
                    var sprite = LoadUiSprite(S(e,"image"));
                    if (sprite) { img.sprite = sprite; img.color = Color.white; img.preserveAspect = false; }
                    var b = go.AddComponent<Button>();
                    var action = S(e,"action");
                    b.onClick.AddListener(()=>Action(action));
                    var label = new GameObject("Caption",typeof(RectTransform),typeof(Text));
                    label.transform.SetParent(go.transform,false);
                    var lr = label.GetComponent<RectTransform>();
                    lr.anchorMin = Vector2.zero; lr.anchorMax = Vector2.one; lr.offsetMin = lr.offsetMax = Vector2.zero;
                    var t = label.GetComponent<Text>(); t.font=font; t.text=S(e,"text"); t.color=Color.white;
                    t.fontSize=24; t.fontStyle=FontStyle.Bold; t.alignment=TextAnchor.MiddleCenter; t.raycastTarget = false;
                }
            }
            if (screen == "viewer") BuildGeometry();
        }
        void Action(string action)
        {
            if (action.StartsWith("scene:")) Show(action.Substring(6));
            else if (action == "upload") { status = "Válassz egy XML tervet a fájlválasztóval (Windows build)."; OpenPlanDialog(); }
            else if (action == "export") Export();
        }
        public void LoadPlan(string xml)
        {
            var candidate = XDocument.Parse(xml);
            if (candidate.Root == null || candidate.Root.Name != "plan") throw new InvalidDataException("Invalid plan root");
            if (candidate.Root.Element("rooms") == null || candidate.Root.Element("walls") == null) throw new InvalidDataException("Missing rooms or walls");
            plan = candidate;
            if (current == "viewer" || current == "main" || current == "materials") Show(current);
        }
        void OpenPlanDialog()
        {
#if UNITY_EDITOR
            var path = UnityEditor.EditorUtility.OpenFilePanel("Terv XML kiválasztása","","xml");
            if (!string.IsNullOrEmpty(path)) TryImport(path);
#elif UNITY_STANDALONE_WIN
            // Windows native dialog via PowerShell; only local user-selected XML is read.
            var thread = new System.Threading.Thread(() => {
                try {
                    var psi = new System.Diagnostics.ProcessStartInfo("powershell.exe",
                        "-NoProfile -STA -Command \"Add-Type -AssemblyName System.Windows.Forms; $d=New-Object System.Windows.Forms.OpenFileDialog; $d.Filter='XML plans (*.xml)|*.xml'; if($d.ShowDialog() -eq 'OK') {[Console]::Write($d.FileName)}\"");
                    psi.UseShellExecute=false; psi.RedirectStandardOutput=true; psi.CreateNoWindow=true;
                    using (var p=System.Diagnostics.Process.Start(psi)) { var path=p.StandardOutput.ReadToEnd().Trim(); p.WaitForExit(); if (!string.IsNullOrEmpty(path)) pendingPath=path; }
                } catch(Exception ex) { pendingError=ex.Message; }
            });
            thread.IsBackground=true; thread.SetApartmentState(System.Threading.ApartmentState.STA); thread.Start();
#else
            status="File dialog is currently supported in Windows builds and Unity Editor.";
#endif
        }
        volatile string pendingPath, pendingError;
        void TryImport(string path) { try { LoadPlan(File.ReadAllText(path)); status="Terv betöltve."; } catch(Exception ex) { status=ex.Message; Debug.LogException(ex); } }
        void Update()
        {
            if (pendingPath != null) { var p=pendingPath; pendingPath=null; TryImport(p); }
            if (pendingError != null) { status=pendingError; pendingError=null; }
            if (current != "viewer" || !worldCamera) return;
            var tr=worldCamera.transform;
            if (Input.GetMouseButtonDown(1)) mouseLook=true;
            if (Input.GetMouseButtonUp(1)) mouseLook=false;
            if (mouseLook) { tr.Rotate(Vector3.up,Input.GetAxis("Mouse X")*3,Space.World); tr.Rotate(Vector3.right,-Input.GetAxis("Mouse Y")*3,Space.Self); }
            var speed=3f*Time.deltaTime;
            tr.Translate(new Vector3(Input.GetAxis("Horizontal"),(Input.GetKey(KeyCode.E)?1:0)-(Input.GetKey(KeyCode.Q)?1:0),Input.GetAxis("Vertical"))*speed,Space.Self);
            tr.Translate(Vector3.forward*Input.mouseScrollDelta.y*.4f,Space.Self);
        }
        void BuildGeometry()
        {
            var go = new GameObject("Viewer Camera",typeof(Camera),typeof(AudioListener));
            worldCamera=go.GetComponent<Camera>(); worldCamera.depth=0;
            go.transform.position=new Vector3(1.33f,2.8f,-4.5f);
            go.transform.LookAt(new Vector3(1.33f,1.1f,1.1f));
            var light=new GameObject("Viewer Light",typeof(Light)); geometry.Add(light);
            light.GetComponent<Light>().type=LightType.Directional; light.transform.rotation=Quaternion.Euler(50,-30,0);
            if (plan == null) return;
            foreach(var room in plan.Root.Element("rooms").Elements("room"))
            {
                var pts=new List<Vector2>();
                foreach(var p in room.Elements("point")) pts.Add(new Vector2(N(p,"x")/1000f,N(p,"y")/1000f));
                if (pts.Count < 3) continue;
                var min=pts[0]; var max=pts[0]; foreach(var p in pts){ min=Vector2.Min(min,p);max=Vector2.Max(max,p); }
                Cube("Floor",new Vector3((min.x+max.x)/2,-.05f,(min.y+max.y)/2),new Vector3(max.x-min.x,.1f,max.y-min.y),Color.gray);
            }
            foreach(var w in plan.Root.Element("walls").Elements("wall"))
            {
                var a=new Vector2(N(w,"x")/1000f,N(w,"y")/1000f);
                var b=new Vector2(N(w,"x1")/1000f,N(w,"y1")/1000f);
                var d=b-a; var bottom=N(w,"heightStart")/1000f; var top=N(w,"heightEnd",2700)/1000f;
                var cube=Cube("Wall",new Vector3((a.x+b.x)/2,(bottom+top)/2,(a.y+b.y)/2),
                    new Vector3(d.magnitude,N(w,"heightEnd",2700)/1000f-bottom,N(w,"thickness",120)/1000f),Color.white);
                cube.transform.rotation=Quaternion.Euler(0,-Mathf.Atan2(d.y,d.x)*Mathf.Rad2Deg,0);
            }
        }
        GameObject Cube(string name,Vector3 pos,Vector3 scale,Color color)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube); go.name=name; go.transform.position=pos; go.transform.localScale=scale;
            var mat=new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard")); mat.color=color;
            go.GetComponent<Renderer>().material=mat; geometry.Add(go); return go;
        }
        string MaterialsCsv()
        {
            var lines=new List<string>{"Elem;Darab;Hossz (mm);Magasság (mm);Vastagság (mm)"};
            if(plan != null) foreach(var w in plan.Root.Element("walls").Elements("wall"))
            {
                float dx = N(w, "x1") - N(w, "x");
                float dy = N(w, "y1") - N(w, "y");
                lines.Add("Fal;1;"+Mathf.RoundToInt(Mathf.Sqrt(dx*dx+dy*dy))+";"+(N(w,"heightEnd")-N(w,"heightStart"))+";"+N(w,"thickness"));
            }
            return string.Join("\n",lines);
        }
        void Export()
        {
            var path=Path.Combine(Application.persistentDataPath,"anyaglista.csv");
            File.WriteAllText(path,"\uFEFF"+MaterialsCsv(),System.Text.Encoding.UTF8);
            status="Export: "+path; Debug.Log(status);
        }
        void OnGUI() { if(!string.IsNullOrEmpty(status)) GUI.Label(new Rect(12,Screen.height-32,Screen.width-24,25),status); }
    }
}
