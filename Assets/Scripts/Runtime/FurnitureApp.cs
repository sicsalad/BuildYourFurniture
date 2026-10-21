using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

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
        float cameraYaw, cameraPitch;
        Vector3 cameraHome;
        Quaternion cameraHomeRotation;
        string status = "";
        readonly XmlDesignEditor xmlEditor = new XmlDesignEditor();
        Texture2D tileTexture;
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
            var editedSample = Path.Combine(Application.persistentDataPath,"XML","Plans","sample.xml");
            if(File.Exists(editedSample)) LoadPlan(File.ReadAllText(editedSample));
            else if (sample != null) LoadPlan(sample.text);
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
            CabinetPlacementEngine.Resolve(candidate);
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
            var tr = worldCamera.transform;
            float horizontal = 0f, forward = 0f, vertical = 0f, wheel = 0f;
            float lookX = 0f, lookY = 0f;
            bool rightDown = false, reset = false, fast = false;
#if ENABLE_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            if (keyboard != null)
            {
                horizontal = (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f);
                forward = (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f);
                vertical = (keyboard.eKey.isPressed ? 1f : 0f) - (keyboard.qKey.isPressed ? 1f : 0f);
                fast = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
                reset = keyboard.rKey.wasPressedThisFrame;
            }
            if (mouse != null)
            {
                rightDown = mouse.rightButton.isPressed;
                var delta = mouse.delta.ReadValue();
                lookX = delta.x; lookY = delta.y;
                wheel = mouse.scroll.ReadValue().y / 120f;
            }
#elif ENABLE_LEGACY_INPUT_MANAGER
            horizontal = (Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f);
            forward = (Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f);
            vertical = (Input.GetKey(KeyCode.E) ? 1f : 0f) - (Input.GetKey(KeyCode.Q) ? 1f : 0f);
            fast = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            reset = Input.GetKeyDown(KeyCode.R);
            rightDown = Input.GetMouseButton(1);
            lookX = Input.GetAxisRaw("Mouse X"); lookY = Input.GetAxisRaw("Mouse Y");
            wheel = Input.mouseScrollDelta.y;
#endif
            if (reset) { tr.position = cameraHome; tr.rotation = cameraHomeRotation; SyncCameraAngles(); }
            if (rightDown)
            {
                // Mouse deltas are pixels with the new Input System and axis values with legacy input.
#if ENABLE_INPUT_SYSTEM
                float sensitivity = .15f;
#else
                float sensitivity = 3f;
#endif
                cameraYaw += lookX * sensitivity;
                cameraPitch = Mathf.Clamp(cameraPitch - lookY * sensitivity, -89f, 89f);
                tr.rotation = Quaternion.Euler(cameraPitch, cameraYaw, 0f);
            }
            float speed = (fast ? 9f : 3f) * Time.deltaTime;
            var planarForward = Vector3.ProjectOnPlane(tr.forward, Vector3.up).normalized;
            var planarRight = Vector3.ProjectOnPlane(tr.right, Vector3.up).normalized;
            var direction = planarRight * horizontal + planarForward * forward + Vector3.up * vertical;
            if (direction.sqrMagnitude > 1f) direction.Normalize();
            tr.position += direction * speed;
            if (Mathf.Abs(wheel) > .001f) tr.position += tr.forward * wheel * .45f;
        }
        void SyncCameraAngles()
        {
            if (!worldCamera) return;
            var angles = worldCamera.transform.eulerAngles;
            cameraYaw = angles.y;
            cameraPitch = Mathf.DeltaAngle(0f, angles.x);
        }
        void ApplyViewerStart(Transform cameraTransform)
        {
            if (plan == null || plan.Root == null) return;
            var start = plan.Root.Element("viewerStart");
            if (start == null) return; // Backwards compatibility with older plans.
            var rooms = plan.Root.Element("rooms");
            if (rooms == null) return;
            XElement room = null;
            foreach (var candidate in rooms.Elements("room"))
                if (S(candidate, "id") == S(start, "roomId")) { room = candidate; break; }
            if (room == null) return;
            var points = new List<Vector2>();
            foreach (var point in room.Elements("point"))
                points.Add(new Vector2(N(point, "x") / 1000f, N(point, "y") / 1000f));
            if (points.Count < 3) return;
            Vector2 min = points[0], max = points[0];
            foreach (var p in points) { min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
            Vector2 center = (min + max) * .5f;
            float height = N(start, "height", 1500f) / 1000f;
            Vector3 position = new Vector3(center.x, height, center.y);
            if (S(start, "mode") == "door")
            {
                var doors = plan.Root.Element("doors");
                var walls = plan.Root.Element("walls");
                if (doors != null && walls != null)
                {
                    XElement door = null, wall = null;
                    foreach (var d in doors.Elements("door"))
                        if (S(d, "id") == S(start, "doorId")) { door = d; break; }
                    if (door != null)
                        foreach (var w in walls.Elements("wall"))
                            if (S(w, "id") == S(door, "wallId")) { wall = w; break; }
                    if (door != null && wall != null)
                    {
                        Vector2 a = new Vector2(N(wall, "x"), N(wall, "y")) / 1000f;
                        Vector2 b = new Vector2(N(wall, "x1"), N(wall, "y1")) / 1000f;
                        Vector2 direction = (b - a).normalized;
                        Vector2 midpoint = a + direction * ((N(door, "offset") + N(door, "width") * .5f) / 1000f);
                        float offset = N(start, "inwardOffset", 100f) / 1000f;
                        Vector2 inward = (center - midpoint).normalized;
                        Vector2 eye = midpoint + inward * offset;
                        position = new Vector3(eye.x, height, eye.y);
                    }
                }
            }
            else if (S(start, "mode") == "position")
            {
                position = new Vector3(N(start, "x") / 1000f, height, N(start, "y") / 1000f);
            }
            cameraTransform.position = position;
            Vector3 target = new Vector3(center.x, N(start, "lookHeight", 1500f) / 1000f, center.y);
            if (S(start, "lookAt", "roomCenter") == "position")
                target = new Vector3(N(start, "targetX") / 1000f, N(start, "targetHeight", 1500f) / 1000f, N(start, "targetY") / 1000f);
            if ((target - position).sqrMagnitude > .000001f)
                cameraTransform.LookAt(target);
        }
        void BuildGeometry()
        {
            var go = new GameObject("Viewer Camera",typeof(Camera),typeof(AudioListener));
            worldCamera=go.GetComponent<Camera>(); worldCamera.depth=0;
            go.transform.position=new Vector3(1.33f,2.8f,-4.5f);
            go.transform.LookAt(new Vector3(1.33f,1.1f,1.1f));
            ApplyViewerStart(go.transform);
            cameraHome = go.transform.position;
            cameraHomeRotation = go.transform.rotation;
            SyncCameraAngles();
            // Soft room illumination is configured in the plan XML, not hard-coded.
            var lighting = plan != null && plan.Root != null ? plan.Root.Element("lighting") : null;
            Color ambientColor = new Color(.88f,.89f,.91f);
            Color keyColor = Color.white;
            if (lighting != null)
            {
                Color parsed;
                if (ColorUtility.TryParseHtmlString(S(lighting,"ambientColor","#E1E3E8"),out parsed)) ambientColor=parsed;
                if (ColorUtility.TryParseHtmlString(S(lighting,"color","#FFFFFF"),out parsed)) keyColor=parsed;
            }
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = ambientColor * (lighting == null ? .85f : N(lighting,"ambientIntensity",.85f));
            var light=new GameObject("Viewer Light",typeof(Light)); geometry.Add(light);
            var roomLight=light.GetComponent<Light>();
            roomLight.type=LightType.Directional;
            roomLight.color=keyColor;
            roomLight.intensity=lighting == null ? .35f : N(lighting,"intensity",.35f);
            roomLight.shadows=LightShadows.None;
            light.transform.rotation=Quaternion.Euler(
                lighting == null ? 50f : N(lighting,"rotationX",50f),
                lighting == null ? -30f : N(lighting,"rotationY",-30f),0f);
            if (plan == null) return;
            foreach(var room in plan.Root.Element("rooms").Elements("room"))
            {
                var pts=new List<Vector2>();
                foreach(var p in room.Elements("point")) pts.Add(new Vector2(N(p,"x")/1000f,N(p,"y")/1000f));
                if (pts.Count < 3) continue;
                var min=pts[0]; var max=pts[0]; foreach(var p in pts){ min=Vector2.Min(min,p);max=Vector2.Max(max,p); }
                Cube("Floor",new Vector3((min.x+max.x)/2,-.05f,(min.y+max.y)/2),new Vector3(max.x-min.x,.1f,max.y-min.y),Color.gray);
            }
            FloorTileRenderer.Build(plan, element => geometry.Add(element));
            WallTileMeshRenderer.Build(plan, element => geometry.Add(element));
            ApplianceTextureRenderer.Build(plan, element => geometry.Add(element));
            ApplianceTextureRenderer.BuildCabinetFronts(plan, element => geometry.Add(element));
            CountertopRenderer.Build(plan, element => geometry.Add(element), (name, position, size, color, rotation) =>
            {
                var element = Cube(name, position, size, color);
                element.transform.rotation = Quaternion.Euler(0f, rotation, 0f);
            });
            PlanGeometryRenderer.Build(plan, (name, position, size, color, rotation) =>
            {
                var element = Cube(name, position, size, color);
                element.transform.rotation = Quaternion.Euler(0f, rotation, 0f);
            });
        }
        GameObject Cube(string name,Vector3 pos,Vector3 scale,Color color)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube); go.name=name; go.transform.position=pos; go.transform.localScale=scale;
            var mat=new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            mat.color=color;
            if (name.StartsWith("TF01_"))
            {
                if (!tileTexture)
                {
                    var asset = Resources.Load<TextAsset>("Textures/tuzzi-tile-base64");
                    if (asset)
                    {
                        try
                        {
                            tileTexture = new Texture2D(2, 2, TextureFormat.RGB24, false);
                            tileTexture.LoadImage(Convert.FromBase64String(asset.text.Trim()));
                            tileTexture.wrapMode = TextureWrapMode.Repeat;
                            tileTexture.filterMode = FilterMode.Bilinear;
                        }
                        catch (Exception ex) { Debug.LogWarning("TUZZI texture decode failed: " + ex.Message); }
                    }
                }
                if (tileTexture)
                {
                    mat.mainTexture = tileTexture;
                    mat.color = Color.white;
                    if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tileTexture);
                    if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", .76f);
                    if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", .76f);
                }
            }
            if (name.StartsWith("FL01_"))
            {
                var floorTexture = Resources.Load<Texture2D>("Textures/helios");
                if (floorTexture)
                {
                    mat.mainTexture = floorTexture;
                    if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", floorTexture);
                }
                else mat.color = new Color(.68f, .67f, .65f);
                if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", .12f);
                if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", .12f);
            }
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
        void OnGUI() { if(current=="editor") xmlEditor.Draw(); if(!string.IsNullOrEmpty(status)) GUI.Label(new Rect(12,Screen.height-32,Screen.width-24,25),status); }
    }
}
