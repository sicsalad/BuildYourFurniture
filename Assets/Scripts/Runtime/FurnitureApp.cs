using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
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
        RuntimeCabinetDesigner runtimeCabinetDesigner;
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
        bool cabinetVisibilityOpen;
        bool apartmentOverview=true;
        bool roomNavigationOpen;
        XElement selectedFurniture;
        string furnitureEditError="";
        bool furnitureDragging;
        int activeGizmoHandle=-1;
        Vector2 gizmoStartPointer;
        float gizmoStartValue;
        bool rebuildingViewer;
        Vector2 furnitureDragOffset;
        Vector2 lastInteriorPosition=new Vector2(1.33f,1.1f);
        bool sofaEditorOpen;
        bool deskEditorOpen;
        bool bedEditorOpen;
        bool tvStandEditorOpen;
        bool ledTvEditorOpen;
        bool rugEditorOpen;
        bool dividerEditorOpen;
        bool tableEditorOpen;
        bool wardrobeEditorOpen;
        bool laminateEditorOpen;
        bool deleteModelsOpen;
        Vector2 laminateEditorScroll;
        Vector2 deleteModelsScroll;
        Vector2 wardrobeEditorScroll;
        Vector2 tableEditorScroll;
        Vector2 dividerEditorScroll;
        Vector2 rugEditorScroll;
        Vector2 ledTvEditorScroll;
        Vector2 tvStandEditorScroll;
        Vector2 bedEditorScroll;
        Vector2 deskEditorScroll;
        Vector2 sofaEditorScroll;
        string sofaError="";
        Vector2 cabinetVisibilityScroll;
        readonly Dictionary<string,bool> cabinetVisibility=new Dictionary<string,bool>();
        string VisibilityPath=>Path.Combine(Application.persistentDataPath,"XML","Plans","sample.xml");
        void ReadCabinetVisibility()
        {
            cabinetVisibility.Clear();
            var cabinetry=plan?.Root?.Element("cabinetry");
            if(cabinetry==null)return;
            var settings=cabinetry.Element("visibility");
            foreach(var cabinet in cabinetry.Elements("cabinet"))
            {
                string id=S(cabinet,"id");
                if(string.IsNullOrEmpty(id))continue;
                var entry=settings?.Elements("cabinet").FirstOrDefault(x=>S(x,"id")==id);
                cabinetVisibility[id]=entry==null||S(entry,"visible","true")!="false";
            }
        }
        void SaveCabinetVisibility()
        {
            if(plan?.Root==null)return;
            var cabinetry=plan.Root.Element("cabinetry");
            if(cabinetry==null)return;
            var settings=cabinetry.Element("visibility");
            if(settings==null){settings=new XElement("visibility");cabinetry.AddFirst(settings);}
            settings.RemoveNodes();
            foreach(var cabinet in cabinetry.Elements("cabinet"))
            {
                string id=S(cabinet,"id");
                bool visible;
                if(!cabinetVisibility.TryGetValue(id,out visible))visible=true;
                settings.Add(new XElement("cabinet",new XAttribute("id",id),
                    new XAttribute("visible",visible?"true":"false")));
            }
            try
            {
#if UNITY_EDITOR
                string path=Path.Combine(Application.dataPath,"Resources","Plans","sample.xml");
#else
                string path=VisibilityPath;
#endif
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path,plan.ToString(),System.Text.Encoding.UTF8);
#if UNITY_EDITOR
                UnityEditor.AssetDatabase.Refresh();
#endif
                status="Szekrények láthatósága mentve a minta.xml-be.";
            }
            catch(Exception ex){status="Láthatóság mentési hiba: "+ex.Message;Debug.LogException(ex);}
        }
        XDocument VisibleCabinetPlan()
        {
            var copy=new XDocument(plan);
            var cabinetry=copy.Root?.Element("cabinetry");
            if(cabinetry==null)return copy;
            foreach(var cabinet in cabinetry.Elements("cabinet").ToList())
            {
                bool visible;
                if(cabinetVisibility.TryGetValue(S(cabinet,"id"),out visible)&&!visible)
                    cabinet.Remove();
            }
            // All rendering paths receive the same filtered plan, including appliance fronts.
            return copy;
        }
        void RefreshViewerGeometry()
        {
            if(current!="viewer")return;
            Vector3 previousPosition=worldCamera?worldCamera.transform.position:Vector3.zero;
            Quaternion previousRotation=worldCamera?worldCamera.transform.rotation:Quaternion.identity;
            bool previousOrthographic=worldCamera&&worldCamera.orthographic;
            float previousSize=worldCamera?worldCamera.orthographicSize:5f;
            foreach(var g in geometry)if(g)Destroy(g);
            geometry.Clear();
            if(worldCamera)Destroy(worldCamera.gameObject);
            worldCamera=null;
            rebuildingViewer=true;
            try{BuildGeometry();}finally{rebuildingViewer=false;}
            if(worldCamera)
            {
                worldCamera.transform.position=previousPosition;
                worldCamera.transform.rotation=previousRotation;
                worldCamera.orthographic=previousOrthographic;
                worldCamera.orthographicSize=previousSize;
                SyncCameraAngles();
            }
        }
        bool addMenuOpen;
        bool furnitureResizeMode;
        int furnitureEditMode; // 0 Move, 1 Resize, 2 Rotate
        readonly string[] addKinds={"Gardrób / szekrény","Íróasztal","Franciaágy","Kanapé","TV-állvány","LED TV","Térelválasztó polc","Étkezőasztal","Dohányzóasztal","Szőnyeg","Könyv","Cserepes virág","Növény"};
        void DrawCabinetVisibilityMenu()
        {
            float barHeight=Mathf.Max(48,Screen.height*.085f);
            GUI.Box(new Rect(0,0,Screen.width,barHeight),"");
            if(GUI.Button(new Rect(12,8,140,barHeight-16),addMenuOpen?"Bezárás":"+ Hozzáadás"))
            {addMenuOpen=!addMenuOpen;roomNavigationOpen=false;}
            string[] editModes={"Mozgatás","Méretezés","Forgatás"};
            if(GUI.Button(new Rect(162,8,155,barHeight-16),editModes[furnitureEditMode]))
            {
                furnitureEditMode=(furnitureEditMode+1)%3;
                furnitureResizeMode=furnitureEditMode==1;
            }
            if(GUI.Button(new Rect(Mathf.Max(325,Screen.width-525),8,165,barHeight-16),"Belépés szobába"))
            {roomNavigationOpen=!roomNavigationOpen;addMenuOpen=false;}
            if(GUI.Button(new Rect(Mathf.Max(500,Screen.width-350),8,165,barHeight-16),"Teljes alaprajz"))
                SetApartmentOverview(true);
            if(GUI.Button(new Rect(Mathf.Max(675,Screen.width-175),8,165,barHeight-16),"Konyha nézet"))
                SetApartmentOverview(false);
            if(addMenuOpen)
            {
                float panelHeight=Mathf.Min(Screen.height-barHeight-45,addKinds.Length*38+45);
                GUI.Box(new Rect(12,barHeight+4,280,panelHeight),"Bútor hozzáadása");
                GUILayout.BeginArea(new Rect(20,barHeight+30,264,panelHeight-35));
                for(int i=0;i<addKinds.Length;i++)
                    if(GUILayout.Button("+ "+addKinds[i],GUILayout.Height(33)))
                    {AddFurnitureKind(i);addMenuOpen=false;break;}
                GUILayout.EndArea();
            }
            if(roomNavigationOpen)
            {
                var rooms=plan?.Root?.Element("rooms");
                if(rooms!=null)
                {
                    int index=0;
                    foreach(var room in rooms.Elements("room"))
                    {
                        var chosen=room;
                        if(GUI.Button(new Rect(Mathf.Max(325,Screen.width-525),barHeight+5+index*38,165,34),S(room,"name",S(room,"id"))))
                            EnterRoom(chosen);
                        index++;
                    }
                }
            }
            if(!addMenuOpen)DrawSelectedFurniture(barHeight);
        }
        void AddFurnitureKind(int kind)
        {
            if(plan==null)return;
            string[] groups={"mirroredWardrobes","standingDesks","doubleBeds","sofas","tvStands","ledTvs","dividerShelves","tables","tables","rugs","decorations","decorations","decorations"};
            string[] tags={"wardrobe","desk","bed","sofa","stand","tv","shelf","table","table","rug","book","flower","plant"};
            string[] prefixes={"WARD","DESK","BED","S","TV","LED","DIV","TABLE","TABLE","RUG","BOOK","FLOWER","PLANT"};
            if(kind<0||kind>=groups.Length)return;
            var group=plan.Root.Element(groups[kind]);
            if(group==null){group=new XElement(groups[kind]);plan.Root.Add(group);}
            int id=1;
            while(group.Elements().Any(e=>S(e,"id")==prefixes[kind]+id))id++;
            var item=new XElement(tags[kind],new XAttribute("id",prefixes[kind]+id),
                new XAttribute("x",0),new XAttribute("y",0),new XAttribute("rotation",0));
            int[] widths={1000,1200,1400,1400,1400,1210,1400,1200,1200,1600,200,350,550};
            int[] depths={450,700,2000,850,420,80,350,800,800,2400,150,350,550};
            int[] heights={1850,740,950,850,500,700,1600,750,450,12,280,450,1000};
            item.SetAttributeValue("width",widths[kind]);
            item.SetAttributeValue(kind==2?"length":"depth",depths[kind]);
            item.SetAttributeValue(kind==9?"thickness":"height",heights[kind]);
            if(kind==0){item.SetAttributeValue("leftMirror",1);item.SetAttributeValue("rightMirror",1);}
            if(kind==2){item.SetAttributeValue("frameHeight",350);item.SetAttributeValue("headboardHeight",950);}
            if(kind==5){item.SetAttributeValue("inches",54);item.SetAttributeValue("bottomHeight",900);}
            if(kind==7||kind==8)
            {item.SetAttributeValue("kind",kind==7?"dining":"coffee");item.SetAttributeValue("shape",0);item.SetAttributeValue("style",0);}
            if(kind>=10){item.SetAttributeValue("collision","none");item.SetAttributeValue("variant","0");item.SetAttributeValue("color",kind==10?"#964B32":kind==11?"#D9699B":"#367B40");}
            if(kind==0){item.SetAttributeValue("finish","0");item.SetAttributeValue("doorFinish","0");item.SetAttributeValue("color",MirroredWardrobeRenderer.Colors[0]);item.SetAttributeValue("doorColor",MirroredWardrobeRenderer.Colors[0]);}
            group.Add(item);
            if(AddFurnitureToActiveRoom(item)){SaveLaminates();RefreshViewerGeometry();}
        }
        void SaveSofas()
        {
            if(plan==null)return;
            try
            {
#if UNITY_EDITOR
                string path=Path.Combine(Application.dataPath,"Resources","Plans","sample.xml");
#else
                string path=VisibilityPath;
#endif
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path,plan.ToString(),System.Text.Encoding.UTF8);
#if UNITY_EDITOR
                UnityEditor.AssetDatabase.Refresh();
#endif
                status="Kanapé mentve a tervbe.";
            }
            catch(Exception ex){sofaError=ex.Message;}
        }
        static string SofaNumber(float value)=>value.ToString("0.###",CultureInfo.InvariantCulture);
        void DrawSofaEditor(float barHeight)
        {
            var rect=new Rect(Mathf.Max(8,Screen.width-385),barHeight+5,375,Mathf.Max(100,Screen.height-barHeight-48));
            GUI.Box(rect,"Kanapék a tervben");
            GUILayout.BeginArea(new Rect(rect.x+10,rect.y+24,rect.width-20,rect.height-34));
            sofaEditorScroll=GUILayout.BeginScrollView(sofaEditorScroll);
            if(plan!=null)
            {
                var sofas=plan.Root.Element("sofas");
                if(sofas==null){sofas=new XElement("sofas");plan.Root.Add(sofas);}
                if(GUILayout.Button("+ Kanapé hozzáadása (140 cm)",GUILayout.Height(34)))
                {
                    int index=1;
                    while(sofas.Elements("sofa").Any(e=>S(e,"id")=="S"+index))index++;
                    sofas.Add(new XElement("sofa",
                        new XAttribute("id","S"+index),new XAttribute("width","1400"),
                        new XAttribute("depth","850"),new XAttribute("height","850"),
                        new XAttribute("x","500"),new XAttribute("y","500"),
                        new XAttribute("rotation","0"),new XAttribute("color","#777C83"),
                        new XAttribute("fabric","0")));
                    if(AddFurnitureToActiveRoom(sofas.Elements("sofa").Last())){SaveSofas();RefreshViewerGeometry();}
                }
                foreach(var sofa in sofas.Elements("sofa").ToList())
                {
                    GUILayout.Label("Kanapé "+S(sofa,"id"),GUI.skin.box);
                    bool changed=false;
                    foreach(var dimension in new[]{"width","depth","height","x","y","rotation"})
                    {
                        GUILayout.BeginHorizontal();
                        GUILayout.Label(dimension+" (mm)",GUILayout.Width(115));
                        string previous=S(sofa,dimension,"0");
                        string next=GUILayout.TextField(previous);
                        float parsed;
                        if(next!=previous&&float.TryParse(next,NumberStyles.Float,CultureInfo.InvariantCulture,out parsed))
                        {
                            if(dimension=="width")parsed=Mathf.Max(600,parsed);
                            if(dimension=="depth")parsed=Mathf.Max(500,parsed);
                            if(dimension=="height")parsed=Mathf.Max(550,parsed);
                            sofa.SetAttributeValue(dimension,SofaNumber(parsed));changed=true;
                        }
                        GUILayout.EndHorizontal();
                    }
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Szín HEX",GUILayout.Width(115));
                    string oldColor=S(sofa,"color","#777C83");
                    string newColor=GUILayout.TextField(oldColor);
                    Color parsedColor;
                    if(newColor!=oldColor&&ColorUtility.TryParseHtmlString(newColor,out parsedColor))
                    {sofa.SetAttributeValue("color",newColor);changed=true;}
                    GUILayout.EndHorizontal();
                    GUILayout.Label("Kárpit textúrája");
                    int selected=Mathf.Clamp((int)N(sofa,"fabric",0),0,SofaRenderer.Fabrics.Length-1);
                    for(int i=0;i<SofaRenderer.Fabrics.Length;i++)
                    {
                        if(GUILayout.Toggle(i==selected,SofaRenderer.Fabrics[i],"Button")&&i!=selected)
                        {sofa.SetAttributeValue("fabric",i);selected=i;changed=true;}
                    }
                    if(GUILayout.Button("Kanapé törlése"))
                    {sofa.Remove();changed=true;}
                    if(changed){SaveSofas();RefreshViewerGeometry();}
                }
            }
            if(!string.IsNullOrEmpty(sofaError))GUILayout.Label(sofaError);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }
        void DrawLaminateEditor(float barHeight)
        {
            var rect=new Rect(Mathf.Max(8,Screen.width-425),barHeight+5,415,Mathf.Max(110,Screen.height-barHeight-48));
            GUI.Box(rect,"Laminált parketta / falburkolat");
            GUILayout.BeginArea(new Rect(rect.x+10,rect.y+24,rect.width-20,rect.height-34));
            laminateEditorScroll=GUILayout.BeginScrollView(laminateEditorScroll);
            if(plan?.Root!=null)
            {
                var group=plan.Root.Element("laminates");
                if(group==null){group=new XElement("laminates");plan.Root.Add(group);}
                foreach(string target in new[]{"floor","wall"})
                    if(GUILayout.Button(target=="floor"?"+ Laminált padló":"+ Laminált falburkolat"))
                    {
                        int number=1;
                        while(group.Elements("laminate").Any(e=>S(e,"id")=="LAM"+number))number++;
                        var room=plan.Root.Element("rooms")?.Elements("room").FirstOrDefault();
                        group.Add(new XElement("laminate",new XAttribute("id","LAM"+number),
                            new XAttribute("target",target),new XAttribute("roomId",room==null?"":S(room,"id")),
                            new XAttribute("wallId","all"),new XAttribute("boardLength","1200"),
                            new XAttribute("boardWidth","190"),new XAttribute("thickness","8"),
                            new XAttribute("layout","offset"),new XAttribute("grout","1"),
                            new XAttribute("groutColor","#777777"),new XAttribute("finish","0"),
                            new XAttribute("color",LaminateRenderer.Colors[0]),
                            new XAttribute("fromHeight","0"),new XAttribute("toHeight","3000")));
                        SaveLaminates();RefreshViewerGeometry();
                    }
                foreach(var item in group.Elements("laminate").ToList())
                {
                    bool changed=false;
                    bool wall=S(item,"target")=="wall";
                    GUILayout.Label(S(item,"id")+" – "+(wall?"Fal":"Padló"),GUI.skin.box);
                    GUILayout.Label("Kiosztás");
                    string layout=S(item,"layout","offset");
                    foreach(string option in new[]{"grid","offset"})
                        if(GUILayout.Toggle(layout==option,option=="grid"?"Hálós":"Eltolt (50%)","Button")&&layout!=option)
                        {item.SetAttributeValue("layout",option);layout=option;changed=true;}
                    foreach(string key in wall?
                        new[]{"boardLength","boardWidth","thickness","grout","fromHeight","toHeight"}:
                        new[]{"boardLength","boardWidth","thickness","grout"})
                    {
                        GUILayout.BeginHorizontal();
                        string label=key=="boardLength"?"Elem hossza (mm)":
                            key=="boardWidth"?"Elem szélessége (mm)":key=="thickness"?"Vastagság (mm)":
                            key=="grout"?"Fuga (mm)":key=="fromHeight"?"Kezdőmagasság (mm)":"Végmagasság (mm)";
                        GUILayout.Label(label,GUILayout.Width(200));
                        string old=S(item,key,"0");
                        string next=GUILayout.TextField(old);
                        float number;
                        if(next!=old&&float.TryParse(next,NumberStyles.Float,CultureInfo.InvariantCulture,out number))
                        {
                            if(key=="boardLength")number=Mathf.Max(100,number);
                            if(key=="boardWidth")number=Mathf.Max(40,number);
                            if(key=="thickness")number=Mathf.Clamp(number,2,25);
                            if(key=="grout")number=Mathf.Clamp(number,0,20);
                            item.SetAttributeValue(key,SofaNumber(number));changed=true;
                        }
                        GUILayout.EndHorizontal();
                    }
                    if(wall)
                    {
                        GUILayout.Label("Fal kiválasztása");
                        string wallId=S(item,"wallId","all");
                        var ids=new List<string>{"all"};
                        var walls=plan.Root.Element("walls");
                        if(walls!=null)ids.AddRange(walls.Elements("wall").Select(w=>S(w,"id")));
                        foreach(string id in ids)
                            if(GUILayout.Toggle(wallId==id,id=="all"?"Összes fal":id,"Button")&&wallId!=id)
                            {item.SetAttributeValue("wallId",id);wallId=id;changed=true;}
                    }
                    else
                    {
                        GUILayout.Label("Szoba padlója");
                        var rooms=plan.Root.Element("rooms");
                        if(rooms!=null)foreach(var room in rooms.Elements("room"))
                        {
                            string id=S(room,"id");
                            if(GUILayout.Toggle(S(item,"roomId")==id,id+" "+S(room,"name"),"Button")&&S(item,"roomId")!=id)
                            {item.SetAttributeValue("roomId",id);changed=true;}
                        }
                    }
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Fuga színe HEX",GUILayout.Width(200));
                    string oldGrout=S(item,"groutColor","#777777");
                    string newGrout=GUILayout.TextField(oldGrout);
                    Color parsed;
                    if(newGrout!=oldGrout&&ColorUtility.TryParseHtmlString(newGrout,out parsed))
                    {item.SetAttributeValue("groutColor",newGrout);changed=true;}
                    GUILayout.EndHorizontal();
                    GUILayout.Label("Laminált textúrák");
                    int finish=Mathf.Clamp(Mathf.RoundToInt(N(item,"finish",0)),0,LaminateRenderer.Finishes.Length-1);
                    for(int i=0;i<LaminateRenderer.Finishes.Length;i++)
                        if(GUILayout.Toggle(finish==i,LaminateRenderer.Finishes[i],"Button")&&finish!=i)
                        {item.SetAttributeValue("finish",i);item.SetAttributeValue("color",LaminateRenderer.Colors[i]);finish=i;changed=true;}
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Burkolat színe HEX",GUILayout.Width(200));
                    string oldColor=S(item,"color",LaminateRenderer.Colors[finish]);
                    string newColor=GUILayout.TextField(oldColor);
                    if(newColor!=oldColor&&ColorUtility.TryParseHtmlString(newColor,out parsed))
                    {item.SetAttributeValue("color",newColor);changed=true;}
                    GUILayout.EndHorizontal();
                    if(GUILayout.Button("Burkolat törlése")){item.Remove();changed=true;}
                    if(changed){SaveLaminates();RefreshViewerGeometry();}
                }
            }
            GUILayout.EndScrollView();GUILayout.EndArea();
        }
        void DrawModelDeletion(float barHeight)
        {
            var rect=new Rect(Mathf.Max(8,Screen.width-405),barHeight+5,395,Mathf.Max(110,Screen.height-barHeight-48));
            GUI.Box(rect,"Terv elemeinek törlése");
            GUILayout.BeginArea(new Rect(rect.x+10,rect.y+24,rect.width-20,rect.height-34));
            deleteModelsScroll=GUILayout.BeginScrollView(deleteModelsScroll);
            if(plan?.Root!=null)
            {
                foreach(string groupName in new[]{"sofas","standingDesks","doubleBeds","tvStands","ledTvs",
                    "rugs","dividerShelves","tables","mirroredWardrobes","laminates","cabinetry",
                    "appliances","furniture","objects"})
                {
                    var group=plan.Root.Element(groupName);
                    if(group==null)continue;
                    foreach(var item in group.Elements().Where(e=>e.Attribute("id")!=null).ToList())
                    {
                        GUILayout.BeginHorizontal();
                        GUILayout.Label(groupName+" / "+S(item,"id"),GUILayout.Width(250));
                        if(GUILayout.Button("Törlés",GUILayout.Width(90)))
                        {item.Remove();SaveLaminates();RefreshViewerGeometry();}
                        GUILayout.EndHorizontal();
                    }
                }
            }
            GUILayout.EndScrollView();GUILayout.EndArea();
        }
        void SaveLaminates()
        {
            if(plan==null)return;
            try
            {
#if UNITY_EDITOR
                string path=Path.Combine(Application.dataPath,"Resources","Plans","sample.xml");
#else
                string path=VisibilityPath;
#endif
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path,plan.ToString(),System.Text.Encoding.UTF8);
#if UNITY_EDITOR
                UnityEditor.AssetDatabase.Refresh();
#endif
                status="Terv mentve.";
            }
            catch(Exception ex){status="Mentési hiba: "+ex.Message;}
        }
        void DrawWardrobeEditor(float barHeight)
        {
            var rect=new Rect(Mathf.Max(8,Screen.width-405),barHeight+5,395,Mathf.Max(100,Screen.height-barHeight-48));
            GUI.Box(rect,"Tolóajtós gardróbok");
            GUILayout.BeginArea(new Rect(rect.x+10,rect.y+24,rect.width-20,rect.height-34));
            wardrobeEditorScroll=GUILayout.BeginScrollView(wardrobeEditorScroll);
            if(plan!=null)
            {
                var wardrobes=plan.Root.Element("mirroredWardrobes");
                if(wardrobes==null){wardrobes=new XElement("mirroredWardrobes");plan.Root.Add(wardrobes);}
                foreach(bool mirrorDefault in new[]{true,false})
                if(GUILayout.Button(mirrorDefault?"+ Teljesen tükrös gardrób":"+ Nem tükrös gardrób",GUILayout.Height(34)))
                {
                    int id=1;while(wardrobes.Elements("wardrobe").Any(e=>S(e,"id")=="WARD"+id))id++;
                    wardrobes.Add(new XElement("wardrobe",new XAttribute("id","WARD"+id),
                        new XAttribute("width","1000"),new XAttribute("height","1850"),
                        new XAttribute("depth","450"),new XAttribute("x","500"),
                        new XAttribute("y","500"),new XAttribute("rotation","0"),
                        new XAttribute("open","0"),new XAttribute("leftMirror",mirrorDefault?1:0),
                        new XAttribute("rightMirror",mirrorDefault?1:0),
                        new XAttribute("doorFinish","0"),new XAttribute("doorColor",MirroredWardrobeRenderer.Colors[0]),
                        new XAttribute("finish","0"),
                        new XAttribute("color",MirroredWardrobeRenderer.Colors[0])));
                    if(AddFurnitureToActiveRoom(wardrobes.Elements("wardrobe").Last())){SaveWardrobes();RefreshViewerGeometry();}
                }
                foreach(var wardrobe in wardrobes.Elements("wardrobe").ToList())
                {
                    GUILayout.Label("Gardrób "+S(wardrobe,"id"),GUI.skin.box);
                    bool changed=false;
                    foreach(string key in new[]{"width","height","depth","x","y","rotation","open"})
                    {
                        GUILayout.BeginHorizontal();
                        string label=key=="width"?"Szélesség (mm)":key=="height"?"Magasság (mm)":
                            key=="depth"?"Mélység (mm)":key=="x"?"X pozíció (mm)":
                            key=="y"?"Y pozíció (mm)":key=="rotation"?"Forgatás (°)":"Ajtónyitás (0–1)";
                        GUILayout.Label(label,GUILayout.Width(185));
                        string previous=S(wardrobe,key,"0");
                        string next=GUILayout.TextField(previous);
                        float value;
                        if(next!=previous&&float.TryParse(next,NumberStyles.Float,CultureInfo.InvariantCulture,out value))
                        {
                            if(key=="width")value=Mathf.Max(600,value);
                            if(key=="height")value=Mathf.Max(1000,value);
                            if(key=="depth")value=Mathf.Max(250,value);
                            if(key=="open")value=Mathf.Clamp01(value);
                            wardrobe.SetAttributeValue(key,SofaNumber(value));changed=true;
                        }
                        GUILayout.EndHorizontal();
                    }
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Bútorlap színe (HEX)",GUILayout.Width(185));
                    string oldColor=S(wardrobe,"color",MirroredWardrobeRenderer.Colors[0]);
                    string newColor=GUILayout.TextField(oldColor);
                    Color parsed;
                    if(newColor!=oldColor&&ColorUtility.TryParseHtmlString(newColor,out parsed))
                    {wardrobe.SetAttributeValue("color",newColor);changed=true;}
                    GUILayout.EndHorizontal();
                    GUILayout.Label("Bútorlap textúra (10)");
                    int selected=Mathf.Clamp(Mathf.RoundToInt(N(wardrobe,"finish",0)),0,9);
                    for(int i=0;i<MirroredWardrobeRenderer.Finishes.Length;i++)
                        if(GUILayout.Toggle(i==selected,MirroredWardrobeRenderer.Finishes[i],"Button")&&i!=selected)
                        {wardrobe.SetAttributeValue("finish",i);wardrobe.SetAttributeValue("color",MirroredWardrobeRenderer.Colors[i]);selected=i;changed=true;}
                    GUILayout.Label("Ajtók teljes felülete");
                    foreach(string side in new[]{"leftMirror","rightMirror"})
                    {
                        bool previous=N(wardrobe,side,1)>.5f;
                        bool next=GUILayout.Toggle(previous,side=="leftMirror"?"Bal ajtó tükör":"Jobb ajtó tükör");
                        if(previous!=next){wardrobe.SetAttributeValue(side,next?1:0);changed=true;}
                    }
                    GUILayout.Label("Nem tükrös ajtó textúrája");
                    int doorFinish=Mathf.Clamp(Mathf.RoundToInt(N(wardrobe,"doorFinish",0)),0,9);
                    for(int i=0;i<MirroredWardrobeRenderer.Finishes.Length;i++)
                        if(GUILayout.Toggle(i==doorFinish,MirroredWardrobeRenderer.Finishes[i],"Button")&&i!=doorFinish)
                        {wardrobe.SetAttributeValue("doorFinish",i);wardrobe.SetAttributeValue("doorColor",MirroredWardrobeRenderer.Colors[i]);doorFinish=i;changed=true;}
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Ajtó színe HEX",GUILayout.Width(185));
                    string oldDoor=S(wardrobe,"doorColor",MirroredWardrobeRenderer.Colors[doorFinish]);
                    string newDoor=GUILayout.TextField(oldDoor);
                    Color parsedDoor;
                    if(newDoor!=oldDoor&&ColorUtility.TryParseHtmlString(newDoor,out parsedDoor))
                    {wardrobe.SetAttributeValue("doorColor",newDoor);changed=true;}
                    GUILayout.EndHorizontal();
                    if(GUILayout.Button("Gardrób törlése")){wardrobe.Remove();changed=true;}
                    if(changed){SaveWardrobes();RefreshViewerGeometry();}
                }
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }
        void SaveWardrobes()
        {
            if(plan==null)return;
            try
            {
#if UNITY_EDITOR
                string path=Path.Combine(Application.dataPath,"Resources","Plans","sample.xml");
#else
                string path=VisibilityPath;
#endif
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path,plan.ToString(),System.Text.Encoding.UTF8);
#if UNITY_EDITOR
                UnityEditor.AssetDatabase.Refresh();
#endif
                status="Tükrös gardrób mentve a tervbe.";
            }
            catch(Exception ex){status="Gardrób mentési hiba: "+ex.Message;}
        }
        void DrawTableEditor(float barHeight)
        {
            var rect=new Rect(Mathf.Max(8,Screen.width-405),barHeight+5,395,Mathf.Max(100,Screen.height-barHeight-48));
            GUI.Box(rect,"Étkező- és dohányzóasztalok");
            GUILayout.BeginArea(new Rect(rect.x+10,rect.y+24,rect.width-20,rect.height-34));
            tableEditorScroll=GUILayout.BeginScrollView(tableEditorScroll);
            if(plan!=null)
            {
                var tables=plan.Root.Element("tables");
                if(tables==null){tables=new XElement("tables");plan.Root.Add(tables);}
                foreach(string kind in new[]{"dining","coffee"})
                {
                    if(GUILayout.Button(kind=="dining"?"+ Étkezőasztal (120 cm)":"+ Dohányzóasztal (120 cm)",GUILayout.Height(30)))
                    {
                        int id=1;while(tables.Elements("table").Any(e=>S(e,"id")=="TABLE"+id))id++;
                        tables.Add(new XElement("table",new XAttribute("id","TABLE"+id),
                            new XAttribute("kind",kind),new XAttribute("shape","0"),
                            new XAttribute("style","0"),new XAttribute("width","1200"),
                            new XAttribute("depth","800"),new XAttribute("height",kind=="dining"?"750":"450"),
                            new XAttribute("x","500"),new XAttribute("y","500"),
                            new XAttribute("rotation","0"),new XAttribute("finish","0"),
                            new XAttribute("color",TableRenderer.Colors[0])));
                        if(AddFurnitureToActiveRoom(tables.Elements("table").Last())){SaveTables();RefreshViewerGeometry();}
                    }
                }
                foreach(var table in tables.Elements("table").ToList())
                {
                    GUILayout.Label("Asztal "+S(table,"id"),GUI.skin.box);
                    bool changed=false;
                    bool coffee=S(table,"kind","dining")=="coffee";
                    bool nextCoffee=GUILayout.Toggle(coffee,"Dohányzóasztal (kikapcsolva: étkezőasztal)");
                    if(nextCoffee!=coffee)
                    {
                        table.SetAttributeValue("kind",nextCoffee?"coffee":"dining");
                        table.SetAttributeValue("height",nextCoffee?"450":"750");changed=true;
                    }
                    int shape=Mathf.Clamp(Mathf.RoundToInt(N(table,"shape",0)),0,2);
                    GUILayout.Label("Forma");
                    for(int i=0;i<TableRenderer.Shapes.Length;i++)
                        if(GUILayout.Toggle(i==shape,TableRenderer.Shapes[i],"Button")&&i!=shape)
                        {table.SetAttributeValue("shape",i);shape=i;changed=true;}
                    int style=Mathf.Clamp(Mathf.RoundToInt(N(table,"style",0)),0,4);
                    GUILayout.Label("Lábkialakítás – 5 modell");
                    for(int i=0;i<TableRenderer.Styles.Length;i++)
                        if(GUILayout.Toggle(i==style,TableRenderer.Styles[i],"Button")&&i!=style)
                        {table.SetAttributeValue("style",i);style=i;changed=true;}
                    foreach(string key in new[]{"width","depth","height","x","y","rotation"})
                    {
                        GUILayout.BeginHorizontal();
                        string label=key=="width"?"Szélesség / átmérő (mm)":
                            key=="depth"?"Mélység (mm)":key=="height"?"Magasság (mm)":
                            key=="x"?"X (mm)":key=="y"?"Y (mm)":"Forgatás (°)";
                        GUILayout.Label(label,GUILayout.Width(185));
                        string old=S(table,key,"0");
                        string next=GUILayout.TextField(old);
                        float value;
                        if(next!=old&&float.TryParse(next,NumberStyles.Float,CultureInfo.InvariantCulture,out value))
                        {
                            if(key=="width"||key=="depth")value=Mathf.Max(400,value);
                            if(key=="height")value=Mathf.Max(200,value);
                            table.SetAttributeValue(key,SofaNumber(value));changed=true;
                        }
                        GUILayout.EndHorizontal();
                    }
                    if(shape==1)GUILayout.Label("Kör esetén a mélység = átmérő.");
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Szín HEX",GUILayout.Width(185));
                    string oldColor=S(table,"color",TableRenderer.Colors[0]);
                    string newColor=GUILayout.TextField(oldColor);
                    Color parsed;
                    if(newColor!=oldColor&&ColorUtility.TryParseHtmlString(newColor,out parsed))
                    {table.SetAttributeValue("color",newColor);changed=true;}
                    GUILayout.EndHorizontal();
                    GUILayout.Label("Asztallap textúra (10)");
                    int finish=Mathf.Clamp(Mathf.RoundToInt(N(table,"finish",0)),0,9);
                    for(int i=0;i<TableRenderer.Finishes.Length;i++)
                        if(GUILayout.Toggle(i==finish,TableRenderer.Finishes[i],"Button")&&i!=finish)
                        {table.SetAttributeValue("finish",i);table.SetAttributeValue("color",TableRenderer.Colors[i]);finish=i;changed=true;}
                    if(GUILayout.Button("Asztal törlése")){table.Remove();changed=true;}
                    if(changed){SaveTables();RefreshViewerGeometry();}
                }
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }
        void SaveTables()
        {
            if(plan==null)return;
            try
            {
#if UNITY_EDITOR
                string path=Path.Combine(Application.dataPath,"Resources","Plans","sample.xml");
#else
                string path=VisibilityPath;
#endif
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path,plan.ToString(),System.Text.Encoding.UTF8);
#if UNITY_EDITOR
                UnityEditor.AssetDatabase.Refresh();
#endif
                status="Asztalok mentve a tervbe.";
            }
            catch(Exception ex){status="Asztal mentési hiba: "+ex.Message;}
        }
        void DrawDividerEditor(float barHeight)
        {
            var rect=new Rect(Mathf.Max(8,Screen.width-385),barHeight+5,375,Mathf.Max(100,Screen.height-barHeight-48));
            GUI.Box(rect,"Térelválasztó polcos szekrények");
            GUILayout.BeginArea(new Rect(rect.x+10,rect.y+24,rect.width-20,rect.height-34));
            dividerEditorScroll=GUILayout.BeginScrollView(dividerEditorScroll);
            if(plan!=null)
            {
                var shelves=plan.Root.Element("dividerShelves");
                if(shelves==null){shelves=new XElement("dividerShelves");plan.Root.Add(shelves);}
                if(GUILayout.Button("+ Térelválasztó (140 × 160 × 35 cm)",GUILayout.Height(34)))
                {
                    int number=1;
                    while(shelves.Elements("shelf").Any(e=>S(e,"id")=="DIV"+number))number++;
                    shelves.Add(new XElement("shelf",new XAttribute("id","DIV"+number),
                        new XAttribute("width","1400"),new XAttribute("height","1600"),
                        new XAttribute("depth","350"),new XAttribute("x","500"),
                        new XAttribute("y","500"),new XAttribute("rotation","0"),
                        new XAttribute("finish","0"),new XAttribute("color",DividerShelfRenderer.Colors[0])));
                    if(AddFurnitureToActiveRoom(shelves.Elements("shelf").Last())){SaveDividers();RefreshViewerGeometry();}
                }
                foreach(var shelf in shelves.Elements("shelf").ToList())
                {
                    GUILayout.Label("Térelválasztó "+S(shelf,"id"),GUI.skin.box);
                    bool changed=false;
                    foreach(string key in new[]{"width","height","depth","x","y","rotation"})
                    {
                        GUILayout.BeginHorizontal();
                        string label=key=="width"?"Szélesség (mm)":key=="height"?"Magasság (mm)":
                            key=="depth"?"Mélység (mm)":key=="x"?"X pozíció (mm)":
                            key=="y"?"Y pozíció (mm)":"Forgatás (°)";
                        GUILayout.Label(label,GUILayout.Width(155));
                        string oldValue=S(shelf,key,"0");
                        string next=GUILayout.TextField(oldValue);
                        float number;
                        if(next!=oldValue&&float.TryParse(next,NumberStyles.Float,CultureInfo.InvariantCulture,out number))
                        {
                            if(key=="width"||key=="height")number=Mathf.Max(250,number);
                            if(key=="depth")number=Mathf.Max(150,number);
                            shelf.SetAttributeValue(key,SofaNumber(number));changed=true;
                        }
                        GUILayout.EndHorizontal();
                    }
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Szín (HEX)",GUILayout.Width(155));
                    string oldColor=S(shelf,"color",DividerShelfRenderer.Colors[0]);
                    string nextColor=GUILayout.TextField(oldColor);
                    Color parsed;
                    if(nextColor!=oldColor&&ColorUtility.TryParseHtmlString(nextColor,out parsed))
                    {shelf.SetAttributeValue("color",nextColor);changed=true;}
                    GUILayout.EndHorizontal();
                    GUILayout.Label("Bútorlap textúra (10)");
                    int selected=Mathf.Clamp(Mathf.RoundToInt(N(shelf,"finish",0)),0,DividerShelfRenderer.Finishes.Length-1);
                    for(int i=0;i<DividerShelfRenderer.Finishes.Length;i++)
                    {
                        if(GUILayout.Toggle(i==selected,DividerShelfRenderer.Finishes[i],"Button")&&i!=selected)
                        {shelf.SetAttributeValue("finish",i);shelf.SetAttributeValue("color",DividerShelfRenderer.Colors[i]);selected=i;changed=true;}
                    }
                    if(GUILayout.Button("Térelválasztó törlése")){shelf.Remove();changed=true;}
                    if(changed){SaveDividers();RefreshViewerGeometry();}
                }
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }
        void SaveDividers()
        {
            if(plan==null)return;
            try
            {
#if UNITY_EDITOR
                string path=Path.Combine(Application.dataPath,"Resources","Plans","sample.xml");
#else
                string path=VisibilityPath;
#endif
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path,plan.ToString(),System.Text.Encoding.UTF8);
#if UNITY_EDITOR
                UnityEditor.AssetDatabase.Refresh();
#endif
                status="Térelválasztó mentve a tervbe.";
            }
            catch(Exception ex){status="Térelválasztó mentési hiba: "+ex.Message;}
        }
        void DrawRugEditor(float barHeight)
        {
            var rect=new Rect(Mathf.Max(8,Screen.width-385),barHeight+5,375,Mathf.Max(100,Screen.height-barHeight-48));
            GUI.Box(rect,"Szőnyegek a tervben");
            GUILayout.BeginArea(new Rect(rect.x+10,rect.y+24,rect.width-20,rect.height-34));
            rugEditorScroll=GUILayout.BeginScrollView(rugEditorScroll);
            if(plan!=null)
            {
                var rugs=plan.Root.Element("rugs");
                if(rugs==null){rugs=new XElement("rugs");plan.Root.Add(rugs);}
                if(GUILayout.Button("+ Szőnyeg (160 × 240 cm)",GUILayout.Height(34)))
                {
                    int id=1;
                    while(rugs.Elements("rug").Any(e=>S(e,"id")=="RUG"+id))id++;
                    rugs.Add(new XElement("rug",new XAttribute("id","RUG"+id),
                        new XAttribute("width","1600"),new XAttribute("length","2400"),
                        new XAttribute("thickness","12"),new XAttribute("x","500"),
                        new XAttribute("y","500"),new XAttribute("rotation","0"),
                        new XAttribute("pattern","0"),new XAttribute("color",RugRenderer.Colors[0])));
                    if(AddFurnitureToActiveRoom(rugs.Elements("rug").Last())){SaveRugs();RefreshViewerGeometry();}
                }
                foreach(var rug in rugs.Elements("rug").ToList())
                {
                    GUILayout.Label("Szőnyeg "+S(rug,"id"),GUI.skin.box);
                    bool changed=false;
                    foreach(string property in new[]{"width","length","thickness","x","y","rotation"})
                    {
                        GUILayout.BeginHorizontal();
                        string label=property=="width"?"Szélesség (mm)":
                            property=="length"?"Hosszúság (mm)":
                            property=="thickness"?"Vastagság (mm)":
                            property=="x"?"X pozíció (mm)":property=="y"?"Y pozíció (mm)":"Forgatás (°)";
                        GUILayout.Label(label,GUILayout.Width(155));
                        string oldValue=S(rug,property,"0");
                        string next=GUILayout.TextField(oldValue);
                        float parsed;
                        if(next!=oldValue&&float.TryParse(next,NumberStyles.Float,CultureInfo.InvariantCulture,out parsed))
                        {
                            if(property=="width"||property=="length")parsed=Mathf.Max(200,parsed);
                            if(property=="thickness")parsed=Mathf.Clamp(parsed,3,40);
                            rug.SetAttributeValue(property,SofaNumber(parsed));changed=true;
                        }
                        GUILayout.EndHorizontal();
                    }
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Szín (HEX)",GUILayout.Width(155));
                    string oldColor=S(rug,"color",RugRenderer.Colors[0]);
                    string newColor=GUILayout.TextField(oldColor);
                    Color parsedColor;
                    if(newColor!=oldColor&&ColorUtility.TryParseHtmlString(newColor,out parsedColor))
                    {rug.SetAttributeValue("color",newColor);changed=true;}
                    GUILayout.EndHorizontal();
                    GUILayout.Label("Szőnyegminták (10)");
                    int selected=Mathf.Clamp(Mathf.RoundToInt(N(rug,"pattern",0)),0,RugRenderer.Patterns.Length-1);
                    for(int i=0;i<RugRenderer.Patterns.Length;i++)
                    {
                        if(GUILayout.Toggle(i==selected,RugRenderer.Patterns[i],"Button")&&i!=selected)
                        {rug.SetAttributeValue("pattern",i);rug.SetAttributeValue("color",RugRenderer.Colors[i]);selected=i;changed=true;}
                    }
                    if(GUILayout.Button("Szőnyeg törlése")){rug.Remove();changed=true;}
                    if(changed){SaveRugs();RefreshViewerGeometry();}
                }
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }
        void SaveRugs()
        {
            if(plan==null)return;
            try
            {
#if UNITY_EDITOR
                string path=Path.Combine(Application.dataPath,"Resources","Plans","sample.xml");
#else
                string path=VisibilityPath;
#endif
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path,plan.ToString(),System.Text.Encoding.UTF8);
#if UNITY_EDITOR
                UnityEditor.AssetDatabase.Refresh();
#endif
                status="Szőnyeg mentve a tervbe.";
            }
            catch(Exception ex){status="Szőnyeg mentési hiba: "+ex.Message;}
        }
        void DrawLedTvEditor(float barHeight)
        {
            var rect=new Rect(Mathf.Max(8,Screen.width-385),barHeight+5,375,Mathf.Max(100,Screen.height-barHeight-48));
            GUI.Box(rect,"Falra szerelhető LED TV-k");
            GUILayout.BeginArea(new Rect(rect.x+10,rect.y+24,rect.width-20,rect.height-34));
            ledTvEditorScroll=GUILayout.BeginScrollView(ledTvEditorScroll);
            if(plan!=null)
            {
                var tvs=plan.Root.Element("ledTvs");
                if(tvs==null){tvs=new XElement("ledTvs");plan.Root.Add(tvs);}
                if(GUILayout.Button("+ LED TV (54 col)",GUILayout.Height(34)))
                {
                    int id=1;
                    while(tvs.Elements("tv").Any(e=>S(e,"id")=="LED"+id))id++;
                    tvs.Add(new XElement("tv",new XAttribute("id","LED"+id),
                        new XAttribute("inches","54"),new XAttribute("bottomHeight","900"),
                        new XAttribute("x","500"),new XAttribute("y","500"),
                        new XAttribute("rotation","0"),new XAttribute("screen","0"),
                        new XAttribute("color","#1C1C1E")));
                    if(AddFurnitureToActiveRoom(tvs.Elements("tv").Last())){SaveLedTvs();RefreshViewerGeometry();}
                }
                foreach(var tv in tvs.Elements("tv").ToList())
                {
                    GUILayout.Label("LED TV "+S(tv,"id"),GUI.skin.box);
                    bool changed=false;
                    foreach(string property in new[]{"inches","bottomHeight","x","y","rotation"})
                    {
                        GUILayout.BeginHorizontal();
                        string label=property=="inches"?"Képátló (col)":
                            property=="bottomHeight"?"Alsó él magassága (mm)":
                            property=="x"?"X pozíció (mm)":property=="y"?"Y pozíció (mm)":"Forgatás (°)";
                        GUILayout.Label(label,GUILayout.Width(160));
                        string oldValue=S(tv,property,"0");
                        string next=GUILayout.TextField(oldValue);
                        float parsed;
                        if(next!=oldValue&&float.TryParse(next,NumberStyles.Float,CultureInfo.InvariantCulture,out parsed))
                        {
                            if(property=="inches")parsed=Mathf.Clamp(parsed,19,100);
                            if(property=="bottomHeight")parsed=Mathf.Max(0,parsed);
                            tv.SetAttributeValue(property,SofaNumber(parsed));changed=true;
                        }
                        GUILayout.EndHorizontal();
                    }
                    float inches=N(tv,"inches",54);
                    GUILayout.Label("Képernyő: "+LedTvRenderer.WidthMm(inches).ToString("0")+" × "+
                        LedTvRenderer.HeightMm(inches).ToString("0")+" mm (16:9)");
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Keret színe (HEX)",GUILayout.Width(160));
                    string oldColor=S(tv,"color","#1C1C1E");
                    string newColor=GUILayout.TextField(oldColor);
                    Color parsedColor;
                    if(newColor!=oldColor&&ColorUtility.TryParseHtmlString(newColor,out parsedColor))
                    {tv.SetAttributeValue("color",newColor);changed=true;}
                    GUILayout.EndHorizontal();
                    GUILayout.Label("Kijelző textúrája (10)");
                    int selected=Mathf.Clamp(Mathf.RoundToInt(N(tv,"screen",0)),0,LedTvRenderer.ScreenPresets.Length-1);
                    for(int i=0;i<LedTvRenderer.ScreenPresets.Length;i++)
                    {
                        if(GUILayout.Toggle(i==selected,LedTvRenderer.ScreenPresets[i],"Button")&&i!=selected)
                        {tv.SetAttributeValue("screen",i);selected=i;changed=true;}
                    }
                    if(GUILayout.Button("LED TV törlése")){tv.Remove();changed=true;}
                    if(changed){SaveLedTvs();RefreshViewerGeometry();}
                }
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }
        void SaveLedTvs()
        {
            if(plan==null)return;
            try
            {
#if UNITY_EDITOR
                string path=Path.Combine(Application.dataPath,"Resources","Plans","sample.xml");
#else
                string path=VisibilityPath;
#endif
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path,plan.ToString(),System.Text.Encoding.UTF8);
#if UNITY_EDITOR
                UnityEditor.AssetDatabase.Refresh();
#endif
                status="LED TV mentve a tervbe.";
            }
            catch(Exception ex){status="LED TV mentési hiba: "+ex.Message;}
        }
        void DrawTvStandEditor(float barHeight)
        {
            var area=new Rect(Mathf.Max(8,Screen.width-385),barHeight+5,375,Mathf.Max(100,Screen.height-barHeight-48));
            GUI.Box(area,"TV-állványok");
            GUILayout.BeginArea(new Rect(area.x+10,area.y+24,area.width-20,area.height-34));
            tvStandEditorScroll=GUILayout.BeginScrollView(tvStandEditorScroll);
            if(plan!=null)
            {
                var collection=plan.Root.Element("tvStands");
                if(collection==null){collection=new XElement("tvStands");plan.Root.Add(collection);}
                if(GUILayout.Button("+ TV-állvány (140 cm)"))
                {
                    int i=1;
                    while(collection.Elements("stand").Any(s=>S(s,"id")=="TV"+i))i++;
                    collection.Add(new XElement("stand",new XAttribute("id","TV"+i),
                        new XAttribute("width",1400),new XAttribute("depth",420),new XAttribute("height",500),
                        new XAttribute("x",500),new XAttribute("y",500),new XAttribute("rotation",0),
                        new XAttribute("finish",0),new XAttribute("color",TvStandRenderer.Colors[0])));
                    if(AddFurnitureToActiveRoom(collection.Elements("stand").Last())){SaveTvStands();RefreshViewerGeometry();}
                }
                foreach(var stand in collection.Elements("stand").ToList())
                {
                    GUILayout.Label(S(stand,"id"),GUI.skin.box);
                    bool changed=false;
                    foreach(string key in new[]{"width","depth","height","x","y","rotation"})
                    {
                        GUILayout.BeginHorizontal();
                        GUILayout.Label(key,GUILayout.Width(110));
                        string previous=S(stand,key,"0");
                        string next=GUILayout.TextField(previous);
                        float number;
                        if(next!=previous&&float.TryParse(next,NumberStyles.Float,CultureInfo.InvariantCulture,out number))
                        {
                            if(key=="width")number=Mathf.Max(600,number);
                            if(key=="depth"||key=="height")number=Mathf.Max(250,number);
                            stand.SetAttributeValue(key,SofaNumber(number));changed=true;
                        }
                        GUILayout.EndHorizontal();
                    }
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Szín HEX",GUILayout.Width(110));
                    string color=S(stand,"color",TvStandRenderer.Colors[0]);
                    string nextColor=GUILayout.TextField(color);
                    Color parsed;
                    if(nextColor!=color&&ColorUtility.TryParseHtmlString(nextColor,out parsed))
                    {stand.SetAttributeValue("color",nextColor);changed=true;}
                    GUILayout.EndHorizontal();
                    int selected=Mathf.Clamp(Mathf.RoundToInt(N(stand,"finish",0)),0,9);
                    foreach(int i in System.Linq.Enumerable.Range(0,TvStandRenderer.Finishes.Length))
                    {
                        if(GUILayout.Toggle(i==selected,TvStandRenderer.Finishes[i],"Button")&&i!=selected)
                        {
                            stand.SetAttributeValue("finish",i);
                            stand.SetAttributeValue("color",TvStandRenderer.Colors[i]);
                            changed=true;selected=i;
                        }
                    }
                    if(GUILayout.Button("TV-állvány törlése")){stand.Remove();changed=true;}
                    if(changed){SaveTvStands();RefreshViewerGeometry();}
                }
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }
        void SaveTvStands()
        {
            if(plan==null)return;
            try
            {
#if UNITY_EDITOR
                string path=Path.Combine(Application.dataPath,"Resources","Plans","sample.xml");
#else
                string path=VisibilityPath;
#endif
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path,plan.ToString(),System.Text.Encoding.UTF8);
#if UNITY_EDITOR
                UnityEditor.AssetDatabase.Refresh();
#endif
                status="TV-állvány mentve.";
            }
            catch(Exception ex){status=ex.Message;}
        }
        void DrawBedEditor(float barHeight)
        {
            var rect=new Rect(Mathf.Max(8,Screen.width-385),barHeight+5,375,Mathf.Max(100,Screen.height-barHeight-48));
            GUI.Box(rect,"Franciaágyak a tervben");
            GUILayout.BeginArea(new Rect(rect.x+10,rect.y+24,rect.width-20,rect.height-34));
            bedEditorScroll=GUILayout.BeginScrollView(bedEditorScroll);
            if(plan!=null)
            {
                var beds=plan.Root.Element("doubleBeds");
                if(beds==null){beds=new XElement("doubleBeds");plan.Root.Add(beds);}
                if(GUILayout.Button("+ Franciaágy (140 × 200 cm)",GUILayout.Height(36)))
                {
                    int number=1;
                    while(beds.Elements("bed").Any(e=>S(e,"id")=="BED"+number))number++;
                    beds.Add(new XElement("bed",
                        new XAttribute("id","BED"+number),
                        new XAttribute("width","1400"),new XAttribute("length","2000"),
                        new XAttribute("frameHeight","350"),new XAttribute("headboardHeight","950"),
                        new XAttribute("x","500"),new XAttribute("y","500"),
                        new XAttribute("rotation","0"),new XAttribute("finish","0"),
                        new XAttribute("color",DoubleBedRenderer.Colors[0])));
                    if(AddFurnitureToActiveRoom(beds.Elements("bed").Last())){SaveBeds();RefreshViewerGeometry();}
                }
                foreach(var bed in beds.Elements("bed").ToList())
                {
                    GUILayout.Label("Franciaágy "+S(bed,"id"),GUI.skin.box);
                    bool changed=false;
                    foreach(var property in new[]{"width","length","frameHeight","headboardHeight","x","y","rotation"})
                    {
                        GUILayout.BeginHorizontal();
                        string label=property=="width"?"Szélesség (mm)":
                            property=="length"?"Hosszúság (mm)":
                            property=="frameHeight"?"Ágykeret magasság":
                            property=="headboardHeight"?"Fejtámla magasság":
                            property=="rotation"?"Forgatás (°)":property=="x"?"X pozíció (mm)":"Y pozíció (mm)";
                        GUILayout.Label(label,GUILayout.Width(155));
                        string oldValue=S(bed,property,"0");
                        string next=GUILayout.TextField(oldValue);
                        float number;
                        if(next!=oldValue&&float.TryParse(next,NumberStyles.Float,CultureInfo.InvariantCulture,out number))
                        {
                            if(property=="width")number=Mathf.Max(900,number);
                            if(property=="length")number=Mathf.Max(1600,number);
                            if(property=="frameHeight")number=Mathf.Clamp(number,200,650);
                            if(property=="headboardHeight")number=Mathf.Clamp(number,500,1600);
                            bed.SetAttributeValue(property,SofaNumber(number));changed=true;
                        }
                        GUILayout.EndHorizontal();
                    }
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Szín (HEX)",GUILayout.Width(155));
                    string previous=S(bed,"color",DoubleBedRenderer.Colors[0]);
                    string entered=GUILayout.TextField(previous);
                    Color parsed;
                    if(entered!=previous&&ColorUtility.TryParseHtmlString(entered,out parsed))
                    {bed.SetAttributeValue("color",entered);changed=true;}
                    GUILayout.EndHorizontal();
                    GUILayout.Label("Ágykeret textúrája");
                    int selected=Mathf.Clamp(Mathf.RoundToInt(N(bed,"finish",0)),0,2);
                    for(int i=0;i<DoubleBedRenderer.Finishes.Length;i++)
                    {
                        if(GUILayout.Toggle(i==selected,DoubleBedRenderer.Finishes[i],"Button")&&i!=selected)
                        {
                            bed.SetAttributeValue("finish",i);
                            bed.SetAttributeValue("color",DoubleBedRenderer.Colors[i]);
                            selected=i;changed=true;
                        }
                    }
                    if(GUILayout.Button("Franciaágy törlése")){bed.Remove();changed=true;}
                    if(changed){SaveBeds();RefreshViewerGeometry();}
                }
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }
        void SaveBeds()
        {
            if(plan==null)return;
            try
            {
#if UNITY_EDITOR
                string path=Path.Combine(Application.dataPath,"Resources","Plans","sample.xml");
#else
                string path=VisibilityPath;
#endif
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path,plan.ToString(),System.Text.Encoding.UTF8);
#if UNITY_EDITOR
                UnityEditor.AssetDatabase.Refresh();
#endif
                status="Franciaágy mentve a tervbe.";
            }
            catch(Exception ex){status="Franciaágy mentési hiba: "+ex.Message;}
        }
        void DrawDeskEditor(float barHeight)
        {
            var rect=new Rect(Mathf.Max(8,Screen.width-385),barHeight+5,375,Mathf.Max(100,Screen.height-barHeight-48));
            GUI.Box(rect,"Állítható íróasztalok");
            GUILayout.BeginArea(new Rect(rect.x+10,rect.y+24,rect.width-20,rect.height-34));
            deskEditorScroll=GUILayout.BeginScrollView(deskEditorScroll);
            if(plan!=null)
            {
                var desks=plan.Root.Element("standingDesks");
                if(desks==null){desks=new XElement("standingDesks");plan.Root.Add(desks);}
                if(GUILayout.Button("+ Íróasztal (120 cm, 2 monitor)",GUILayout.Height(34)))
                {
                    int index=1;
                    while(desks.Elements("desk").Any(d=>S(d,"id")=="DESK"+index))index++;
                    desks.Add(new XElement("desk",
                        new XAttribute("id","DESK"+index),new XAttribute("width","1200"),
                        new XAttribute("depth","700"),new XAttribute("height","740"),
                        new XAttribute("x","500"),new XAttribute("y","500"),
                        new XAttribute("rotation","0"),new XAttribute("finish","0"),
                        new XAttribute("color",StandingDeskRenderer.DefaultColor(0))));
                    if(AddFurnitureToActiveRoom(desks.Elements("desk").Last())){SaveDesks();RefreshViewerGeometry();}
                }
                foreach(var desk in desks.Elements("desk").ToList())
                {
                    GUILayout.Label("Íróasztal "+S(desk,"id"),GUI.skin.box);
                    bool changed=false;
                    foreach(var property in new[]{"width","depth","height","x","y","rotation"})
                    {
                        GUILayout.BeginHorizontal();
                        GUILayout.Label(property+" (mm, rotation °)",GUILayout.Width(155));
                        string oldValue=S(desk,property,"0");
                        string next=GUILayout.TextField(oldValue);
                        float number;
                        if(next!=oldValue&&float.TryParse(next,NumberStyles.Float,CultureInfo.InvariantCulture,out number))
                        {
                            if(property=="width")number=Mathf.Max(900f,number);
                            if(property=="depth")number=Mathf.Max(500f,number);
                            if(property=="height")number=Mathf.Clamp(number,620f,1250f);
                            desk.SetAttributeValue(property,SofaNumber(number));changed=true;
                        }
                        GUILayout.EndHorizontal();
                    }
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Szín HEX",GUILayout.Width(155));
                    string oldColor=S(desk,"color","#BA9669");
                    string newColor=GUILayout.TextField(oldColor);
                    Color parsed;
                    if(newColor!=oldColor&&ColorUtility.TryParseHtmlString(newColor,out parsed))
                    {desk.SetAttributeValue("color",newColor);changed=true;}
                    GUILayout.EndHorizontal();
                    GUILayout.Label("Felület / textúra");
                    int selected=Mathf.Clamp(Mathf.RoundToInt(N(desk,"finish",0)),0,StandingDeskRenderer.Finishes.Length-1);
                    for(int i=0;i<StandingDeskRenderer.Finishes.Length;i++)
                    {
                        if(GUILayout.Toggle(i==selected,StandingDeskRenderer.Finishes[i],"Button")&&i!=selected)
                        {
                            desk.SetAttributeValue("finish",i);
                            desk.SetAttributeValue("color",StandingDeskRenderer.DefaultColor(i));
                            changed=true;selected=i;
                        }
                    }
                    if(GUILayout.Button("Íróasztal törlése")){desk.Remove();changed=true;}
                    if(changed){SaveDesks();RefreshViewerGeometry();}
                }
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }
        void SaveDesks()
        {
            if(plan==null)return;
            try
            {
#if UNITY_EDITOR
                string path=Path.Combine(Application.dataPath,"Resources","Plans","sample.xml");
#else
                string path=VisibilityPath;
#endif
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path,plan.ToString(),System.Text.Encoding.UTF8);
#if UNITY_EDITOR
                UnityEditor.AssetDatabase.Refresh();
#endif
                status="Íróasztal mentve a tervbe.";
            }
            catch(Exception ex){status="Íróasztal mentési hiba: "+ex.Message;}
        }
        static float N(XElement e, string key, float fallback = 0) => float.TryParse((string)e.Attribute(key), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;
        static string S(XElement e, string key, string fallback = "") => (string)e.Attribute(key) ?? fallback;

        readonly List<string> selectablePlanPaths=new List<string>();
        string PlanSelectionPath=>Path.Combine(Application.persistentDataPath,"XML","Plans","selected-plan.txt");
        void RememberPlanSelection(string value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PlanSelectionPath));
            File.WriteAllText(PlanSelectionPath,value);
        }
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
            string selection=File.Exists(PlanSelectionPath)?File.ReadAllText(PlanSelectionPath).Trim():"bundled";
            try
            {
                if(selection!="bundled"&&File.Exists(selection))LoadPlan(File.ReadAllText(selection));
                else if(sample!=null)
                {
                    string local=VisibilityPath;
                    bool useSaved=false;
                    if(File.Exists(local))
                    {
                        try
                        {
                            var saved=XDocument.Load(local);
                            useSaved=saved.Root?.Element("cameraState")!=null
                                &&(saved.Root.Element("rooms")?.Elements("room").Count()??0)>=5;
                        }
                        catch(Exception ex){Debug.LogWarning("Saved plan unreadable: "+ex.Message);}
                    }
                    LoadPlan(useSaved?File.ReadAllText(local):sample.text);
                }
            }
            catch(Exception ex)
            {
                Debug.LogWarning("Could not open selected plan; loading bundled apartment: "+ex.Message);
                if(sample!=null)LoadPlan(sample.text);
            }
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
        void SaveViewerCamera()
        {
            if(plan?.Root==null||worldCamera==null)return;
            var tr=worldCamera.transform;
            var view=plan.Root.Element("cameraState");
            if(view==null){view=new XElement("cameraState");plan.Root.Add(view);}
            Action<string,float> set=(key,value)=>view.SetAttributeValue(key,value.ToString("R",CultureInfo.InvariantCulture));
            set("x",tr.position.x);set("y",tr.position.y);set("z",tr.position.z);
            set("pitch",tr.eulerAngles.x);set("yaw",tr.eulerAngles.y);set("roll",tr.eulerAngles.z);
            set("orthographicSize",worldCamera.orthographicSize);
            view.SetAttributeValue("orthographic",worldCamera.orthographic?"true":"false");
            try
            {
                string selected=File.Exists(PlanSelectionPath)?File.ReadAllText(PlanSelectionPath).Trim():"bundled";
                string path=selected!="bundled"&&Path.IsPathRooted(selected)?selected:VisibilityPath;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path,plan.ToString(),System.Text.Encoding.UTF8);
            }
            catch(Exception ex){Debug.LogWarning("Kameranézet mentési hiba: "+ex.Message);}
        }
        bool RestoreViewerCamera()
        {
            var state=plan?.Root?.Element("cameraState");
            if(state==null||worldCamera==null)return false;
            var tr=worldCamera.transform;
            tr.position=new Vector3(N(state,"x"),N(state,"y"),N(state,"z"));
            tr.rotation=Quaternion.Euler(N(state,"pitch"),N(state,"yaw"),N(state,"roll"));
            worldCamera.orthographic=S(state,"orthographic")=="true";
            worldCamera.orthographicSize=Mathf.Max(.1f,N(state,"orthographicSize",5));
            apartmentOverview=worldCamera.orthographic;
            return true;
        }
        void OnApplicationQuit(){if(current=="viewer")SaveViewerCamera();}
        void OnApplicationPause(bool paused){if(paused&&current=="viewer")SaveViewerCamera();}
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
            if(runtimeCabinetDesigner!=null)
            {
                Destroy(runtimeCabinetDesigner.gameObject);
                runtimeCabinetDesigner=null;
            }
            if(current=="viewer"&&screen!="viewer")SaveViewerCamera();
            Clear(); current = screen;
            if(screen=="cabinetDesigner")
            {
                canvas.gameObject.SetActive(false);
                var designerObject=new GameObject("Runtime Cabinet Designer");
                runtimeCabinetDesigner=designerObject.AddComponent<RuntimeCabinetDesigner>();
                runtimeCabinetDesigner.OnClose=()=>Show("main");
                runtimeCabinetDesigner.Initialize();
                return;
            }
            canvas.gameObject.SetActive(true);
            var asset = Resources.Load<TextAsset>("UI/"+screen);
            if (!asset) { Debug.LogError("Missing UI xml: "+screen); return; }
            var root = XDocument.Parse(asset.text).Root;
            if(screen=="planPicker")PopulatePlanPicker(root);
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
            if (screen == "viewer") { cabinetVisibilityOpen=false; BuildGeometry(); }
        }
        void Action(string action)
        {
            if (action.StartsWith("scene:")) Show(action.Substring(6));
            else if (action == "upload") { status = "Válassz egy XML tervet a fájlválasztóval (Windows build)."; OpenPlanDialog(); }
            else if (action == "export") Export();
            else if (action == "loadBundledApartment") LoadBundledApartment();
            else if (action == "choosePlan") Show("planPicker");
            else if (action.StartsWith("choosePlanIndex:"))
            {
                int index;
                if(int.TryParse(action.Substring("choosePlanIndex:".Length),out index)
                    &&index>=0&&index<selectablePlanPaths.Count)
                    SelectSavedPlan(selectablePlanPaths[index]);
            }
        }
        void PopulatePlanPicker(XElement root)
        {
            selectablePlanPaths.Clear();
            var folder=Path.GetDirectoryName(VisibilityPath);
            selectablePlanPaths.Add("bundled");
            if(Directory.Exists(folder))
                selectablePlanPaths.AddRange(Directory.GetFiles(folder,"*.xml")
                    .OrderBy(x=>Path.GetFileName(x),StringComparer.OrdinalIgnoreCase));
            root.Add(new XElement("label",new XAttribute("text","Válassz egy tervet"),
                new XAttribute("x",0),new XAttribute("y",.35),new XAttribute("width",.8),
                new XAttribute("height",.09),new XAttribute("size",30)));
            int count=Mathf.Min(selectablePlanPaths.Count,9);
            for(int i=0;i<count;i++)
            {
                string path=selectablePlanPaths[i];
                string title=path=="bundled"?"Teljes lakás (5 helyiség) – alapértelmezett":Path.GetFileNameWithoutExtension(path);
                root.Add(new XElement("button",new XAttribute("text",title),
                    new XAttribute("action","choosePlanIndex:"+i),
                    new XAttribute("x",0),new XAttribute("y",.21-i*.09),
                    new XAttribute("width",.72),new XAttribute("height",.075)));
            }
            root.Add(new XElement("button",new XAttribute("text","Másik XML megnyitása..."),
                new XAttribute("action","upload"),new XAttribute("x",-.19),
                new XAttribute("y",-.72),new XAttribute("width",.35),new XAttribute("height",.09)));
            root.Add(new XElement("button",new XAttribute("text","Vissza"),
                new XAttribute("action","scene:main"),new XAttribute("x",.22),
                new XAttribute("y",-.72),new XAttribute("width",.23),new XAttribute("height",.09)));
        }
        void SelectSavedPlan(string path)
        {
            try
            {
                if(path=="bundled")
                {
                    var sample=Resources.Load<TextAsset>("Plans/sample");
                    if(sample==null)throw new FileNotFoundException("A teljes lakásterv hiányzik.");
                    LoadPlan(sample.text);
                }
                else LoadPlan(File.ReadAllText(path));
                RememberPlanSelection(path);
                apartmentOverview=true;
                status="Terv kiválasztva: "+(path=="bundled"?"Teljes lakás":Path.GetFileName(path));
                Show("main");
            }
            catch(Exception ex){status="Terv betöltési hiba: "+ex.Message;Debug.LogException(ex);}
        }
        void LoadBundledApartment()
        {
            try
            {
                var bundled=Resources.Load<TextAsset>("Plans/sample");
                if(bundled==null)throw new FileNotFoundException("A teljes mintaterv nem található a Resources/Plans/sample.xml fájlban.");
                var candidate=XDocument.Parse(bundled.text);
                int count=candidate.Root?.Element("rooms")?.Elements("room").Count()??0;
                if(count<5)throw new InvalidDataException("A beépített mintaterv csak "+count+" helyiséget tartalmaz. Frissítsd a projektet, majd indítsd újra a programot.");
                string path=VisibilityPath;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                if(File.Exists(path))
                {
                    string backup=Path.Combine(Path.GetDirectoryName(path),
                        "sample-backup-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".xml");
                    File.Copy(path,backup,false);
                }
                // Validate before replacing the active plan or its persisted override.
                LoadPlan(bundled.text);
                File.WriteAllText(path,bundled.text,System.Text.Encoding.UTF8);
                RememberPlanSelection("bundled");
                apartmentOverview=true;
                status="Teljes lakásterv betöltve ("+count+" helyiség). A korábbi terv biztonsági másolata megmaradt.";
                Show("main");
            }
            catch(Exception ex)
            {
                status="Lakásterv betöltési hiba: "+ex.Message;
                Debug.LogException(ex);
            }
        }
        public void LoadPlan(string xml)
        {
            var candidate = XDocument.Parse(xml);
            if (candidate.Root == null || candidate.Root.Name != "plan") throw new InvalidDataException("Invalid plan root");
            if (candidate.Root.Element("rooms") == null || candidate.Root.Element("walls") == null) throw new InvalidDataException("Missing rooms or walls");
            CabinetPlacementEngine.Resolve(candidate);
            plan = candidate;
            ReadCabinetVisibility();
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
        void TryImport(string path) { try { LoadPlan(File.ReadAllText(path)); RememberPlanSelection(path); Show("main"); status="Terv betöltve."; } catch(Exception ex) { status=ex.Message; Debug.LogException(ex); } }
        void Update()
        {
            if (pendingPath != null) { var p=pendingPath; pendingPath=null; TryImport(p); }
            if (pendingError != null) { status=pendingError; pendingError=null; }
            if (current != "viewer" || !worldCamera) return;
            var tr = worldCamera.transform;
            if(!worldCamera.orthographic)
                lastInteriorPosition=new Vector2(tr.position.x,tr.position.z);
            HandleFurnitureMouse();
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
            if (reset)
            {
                tr.position=cameraHome;tr.rotation=cameraHomeRotation;
                SyncCameraAngles();
            }
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
            if (Mathf.Abs(wheel) > .001f)
            {
                if(worldCamera.orthographic)
                {
                    worldCamera.orthographicSize=Mathf.Clamp(worldCamera.orthographicSize*Mathf.Pow(.85f,wheel),.8f,80f);
                    if(worldCamera.orthographicSize<2.4f)EnterRoomAt(tr.position);
                }
                else tr.position+=tr.forward*wheel*.45f;
            }
            if(worldCamera.orthographic&&tr.position.y<5f)EnterRoomAt(tr.position);
        }
        void SyncCameraAngles()
        {
            if (!worldCamera) return;
            var angles = worldCamera.transform.eulerAngles;
            cameraYaw = angles.y;
            cameraPitch = Mathf.DeltaAngle(0f, angles.x);
        }
        void HandleFurnitureMouse()
        {
            if(worldCamera==null||plan==null)return;
            bool pressed=false,held=false,released=false;
            Vector3 pointer=Vector3.zero;
#if ENABLE_INPUT_SYSTEM
            var mouse=Mouse.current;
            if(mouse==null)return;
            pressed=mouse.leftButton.wasPressedThisFrame;
            held=mouse.leftButton.isPressed;
            released=mouse.leftButton.wasReleasedThisFrame;
            pointer=mouse.position.ReadValue();
#elif ENABLE_LEGACY_INPUT_MANAGER
            pressed=Input.GetMouseButtonDown(0);
            held=Input.GetMouseButton(0);
            released=Input.GetMouseButtonUp(0);
            pointer=Input.mousePosition;
#else
            return;
#endif
            int handle;
            if(pressed&&HitGizmo(pointer,out handle))
            {
                activeGizmoHandle=handle;
                gizmoStartPointer=new Vector2(pointer.x,pointer.y);
                gizmoStartValue=N(selectedFurniture,GizmoKey(handle));
                furnitureDragging=false;
                return;
            }
            if(activeGizmoHandle>=0)
            {
                if(released){SaveLaminates();activeGizmoHandle=-1;}
                else if(held)DragGizmo(pointer);
                return;
            }
            // The object is picked through its rendered mesh colliders, not an invisible proxy.
            if(pressed&&pointer.y<Screen.height*.90f&&pointer.x>330f)
            {
                var ray=worldCamera.ScreenPointToRay(pointer);
                RaycastHit[] hits=Physics.RaycastAll(ray,200f);
                bool picked=false;
                foreach(var hit in hits.OrderBy(h=>h.distance))
                {
                    Transform node=hit.collider.transform;
                    XElement found=null;
                    while(node!=null&&found==null)
                    {
                        string name=node.name;
                        foreach(var item in FurniturePlacement.Objects(plan))
                        {
                            string id=S(item,"id");
                            if(!string.IsNullOrEmpty(id)&&
                                (name.EndsWith(" "+id,StringComparison.OrdinalIgnoreCase)||
                                 name.Equals(id,StringComparison.OrdinalIgnoreCase)))
                            {found=item;break;}
                        }
                        node=node.parent;
                    }
                    if(found==null)continue;
                    selectedFurniture=found;
                    picked=true;
                    furnitureEditError="";
                    furnitureDragging=!furnitureResizeMode;
                    var plane=new Plane(Vector3.up,new Vector3(0,N(found,"z")/1000f,0));
                    float distance;
                    if(plane.Raycast(ray,out distance))
                    {
                        Vector3 hitPoint=ray.GetPoint(distance);
                        furnitureDragOffset=FurniturePlacement.Center(found)-
                            new Vector2(hitPoint.x,hitPoint.z);
                    }
                    else furnitureDragOffset=Vector2.zero;
                    break;
                }
                if(!picked){selectedFurniture=null;furnitureDragging=false;}
            }
            if(released)furnitureDragging=false;
            if(!furnitureDragging||selectedFurniture==null||!held)return;
            var mouseRay=worldCamera.ScreenPointToRay(pointer);
            var dragPlane=new Plane(Vector3.up,new Vector3(0,N(selectedFurniture,"z")/1000f,0));
            float t;
            if(!dragPlane.Raycast(mouseRay,out t))return;
            Vector3 hitPosition=mouseRay.GetPoint(t);
            Vector2 candidate=new Vector2(hitPosition.x,hitPosition.z)+furnitureDragOffset;
            string reason;
            if(FurniturePlacement.TryMove(plan,selectedFurniture,
                Mathf.Round(candidate.x*100f)*10f,Mathf.Round(candidate.y*100f)*10f,out reason))
            {furnitureEditError="";SaveLaminates();RefreshViewerGeometry();}
            else furnitureEditError=reason;
        }
        void DrawSelectedFurniture(float barHeight)
        {
            if(selectedFurniture==null)return;
            var rect=new Rect(12,barHeight+12,330,Mathf.Min(Screen.height-barHeight-20,740));
            GUI.Box(rect,"Kijelölt elem: "+S(selectedFurniture,"id"));
            GUILayout.BeginArea(new Rect(rect.x+10,rect.y+28,rect.width-20,rect.height-35));
            furnitureInspectorScroll=GUILayout.BeginScrollView(furnitureInspectorScroll);
            GUILayout.Label("Bal egérgomb: kijelölés / húzás");
            GUILayout.Label(furnitureResizeMode?"MÉRETEZÉS (10 mm)":"MOZGATÁS (10 mm)");
            GUILayout.BeginHorizontal();
            if(GUILayout.Button("←"))ChangeFurniture(furnitureResizeMode?"width":"x",N(selectedFurniture,furnitureResizeMode?"width":"x")-10);
            if(GUILayout.Button("→"))ChangeFurniture(furnitureResizeMode?"width":"x",N(selectedFurniture,furnitureResizeMode?"width":"x")+10);
            if(GUILayout.Button("↑"))ChangeFurniture(furnitureResizeMode?selectedFurniture.Attribute("length")!=null?"length":"depth":"y",N(selectedFurniture,furnitureResizeMode?selectedFurniture.Attribute("length")!=null?"length":"depth":"y")+10);
            if(GUILayout.Button("↓"))ChangeFurniture(furnitureResizeMode?selectedFurniture.Attribute("length")!=null?"length":"depth":"y",N(selectedFurniture,furnitureResizeMode?selectedFurniture.Attribute("length")!=null?"length":"depth":"y")-10);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if(GUILayout.Button("↶ Forgatás -5°"))ChangeFurniture("rotation",N(selectedFurniture,"rotation")-5);
            if(GUILayout.Button("Forgatás +5° ↷"))ChangeFurniture("rotation",N(selectedFurniture,"rotation")+5);
            GUILayout.EndHorizontal();
            foreach(string key in new[]{"x","y","z","rotation","width","depth","length","height"})
            {
                if(selectedFurniture.Attribute(key)==null&&key!="z")continue;
                GUILayout.BeginHorizontal();
                GUILayout.Label(key,GUILayout.Width(70));
                float value=N(selectedFurniture,key);
                float step=key=="rotation"?5f:10f;
                if(GUILayout.Button("−",GUILayout.Width(32)))ChangeFurniture(key,value-step);
                GUILayout.Label(SofaNumber(value),GUILayout.Width(78));
                if(GUILayout.Button("+",GUILayout.Width(32)))ChangeFurniture(key,value+step);
                GUILayout.EndHorizontal();
            }
            GUILayout.Space(8);
            GUILayout.Label("Megjelenés és altípus",GUI.skin.box);
            if(selectedFurniture.Name.LocalName=="wardrobe")
            {
                string[] types={"Tolóajtós tükrös","Tolóajtós bútorlap","Vegyes tükrös","Nyitott gardrób"};
                int type=Mathf.Clamp((int)N(selectedFurniture,"wardrobeType",0),0,3);
                int newType=GUILayout.SelectionGrid(type,types,2);
                if(newType!=type)
                {
                    selectedFurniture.SetAttributeValue("wardrobeType",newType);
                    selectedFurniture.SetAttributeValue("leftMirror",newType==0||newType==2?1:0);
                    selectedFurniture.SetAttributeValue("rightMirror",newType==0?1:0);
                    SaveLaminates();RefreshViewerGeometry();
                }
                string[] finishes=MirroredWardrobeRenderer.Finishes;
                int finish=Mathf.Clamp((int)N(selectedFurniture,"finish",0),0,finishes.Length-1);
                int nextFinish=GUILayout.SelectionGrid(finish,finishes,2);
                if(nextFinish!=finish)
                {
                    selectedFurniture.SetAttributeValue("finish",nextFinish);
                    selectedFurniture.SetAttributeValue("color",MirroredWardrobeRenderer.Colors[nextFinish]);
                    SaveLaminates();RefreshViewerGeometry();
                }
                GUILayout.Label("Ajtó felülete");
                int door=Mathf.Clamp((int)N(selectedFurniture,"doorFinish",0),0,finishes.Length-1);
                int nextDoor=GUILayout.SelectionGrid(door,finishes,2);
                if(nextDoor!=door)
                {
                    selectedFurniture.SetAttributeValue("doorFinish",nextDoor);
                    selectedFurniture.SetAttributeValue("doorColor",MirroredWardrobeRenderer.Colors[nextDoor]);
                    SaveLaminates();RefreshViewerGeometry();
                }
            }
            else if(selectedFurniture.Name.LocalName=="plant")
            {
                string[] variants={"Szobanövény","Pálma","Anyósnyelv","Páfrány","Fikusz"};
                int variant=Mathf.Clamp((int)N(selectedFurniture,"variant",0),0,4);
                int next=GUILayout.SelectionGrid(variant,variants,2);
                if(next!=variant)
                {selectedFurniture.SetAttributeValue("variant",next);SaveLaminates();RefreshViewerGeometry();}
            }
            string[] colors={"#F4F4F1","#97704E","#74503B","#292A2C","#525359","#D9699B","#367B40","#2E638C","#D2C29A"};
            GUILayout.Label("Szín");
            int colorIndex=Array.IndexOf(colors,S(selectedFurniture,"color"));
            int nextColor=GUILayout.SelectionGrid(colorIndex<0?0:colorIndex,
                new[]{"Fehér","Tölgy","Dió","Fekete","Szürke","Rózsaszín","Zöld","Kék","Bézs"},3);
            if(nextColor>=0&&nextColor<colors.Length&&nextColor!=colorIndex)
            {
                selectedFurniture.SetAttributeValue("color",colors[nextColor]);
                SaveLaminates();RefreshViewerGeometry();
            }
            GUILayout.Label("Textúra (Resources/Textures)");
            string textureName=S(selectedFurniture,"texture","");
            string editedTexture=GUILayout.TextField(textureName);
            if(editedTexture!=textureName)
            {
                selectedFurniture.SetAttributeValue("texture",editedTexture);
                SaveLaminates();RefreshViewerGeometry();
            }
            if(GUILayout.Button("Duplikálás (+20 cm eltolás)"))DuplicateSelectedFurniture();
            if(!string.IsNullOrEmpty(furnitureEditError))GUILayout.Label(furnitureEditError);
            if(GUILayout.Button("Kijelölés megszüntetése")){selectedFurniture=null;furnitureDragging=false;}
            if(GUILayout.Button("DELETE – Elem törlése"))
            {
                selectedFurniture.Remove();selectedFurniture=null;furnitureDragging=false;
                SaveLaminates();RefreshViewerGeometry();
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }
        Vector2 furnitureInspectorScroll;
        void DuplicateSelectedFurniture()
        {
            if(selectedFurniture==null||plan==null)return;
            var original=selectedFurniture;
            var copy=new XElement(original);
            string baseId=S(original,"id","OBJ");
            int suffix=2;
            while(FurniturePlacement.Objects(plan).Any(e=>S(e,"id")==baseId+"_"+suffix))suffix++;
            copy.SetAttributeValue("id",baseId+"_"+suffix);
            original.AddAfterSelf(copy);
            string reason;
            Vector2 origin=FurniturePlacement.Center(original);
            bool placed=false;
            foreach(Vector2 offset in new[]{new Vector2(.2f,.2f),new Vector2(.2f,0),new Vector2(0,.2f),new Vector2(-.2f,0)})
            {
                if(FurniturePlacement.PlaceNew(plan,copy,origin+offset,out reason))
                {placed=true;break;}
            }
            if(!placed)
            {
                copy.Remove();furnitureEditError="Nincs ütközésmentes hely a másolatnak.";return;
            }
            selectedFurniture=copy;
            furnitureInspectorScroll=Vector2.zero;
            SaveLaminates();RefreshViewerGeometry();
        }
        void ChangeFurniture(string key,float value)
        {
            if(selectedFurniture==null)return;
            if((key=="width"||key=="depth"||key=="length"||key=="height")&&value<100)
            {furnitureEditError="A méret legalább 100 mm.";return;}
            if(key=="z"&&value<0){furnitureEditError="A magasság nem lehet negatív.";return;}
            string reason;
            if(FurniturePlacement.TryChange(plan,selectedFurniture,key,value,out reason))
            {furnitureEditError="";SaveLaminates();RefreshViewerGeometry();}
            else furnitureEditError=reason;
        }
        Vector2 ActiveFurniturePosition()
        {
            if(worldCamera!=null&&!worldCamera.orthographic)
                return new Vector2(worldCamera.transform.position.x,worldCamera.transform.position.z);
            return lastInteriorPosition;
        }
        bool AddFurnitureToActiveRoom(XElement item)
        {
            string reason;
            if(FurniturePlacement.PlaceNew(plan,item,ActiveFurniturePosition(),out reason))
            {selectedFurniture=item;furnitureEditError="";return true;}
            item.Remove();
            furnitureEditError=reason;
            Debug.LogWarning(reason);
            return false;
        }
        void EnterRoomAt(Vector3 from)
        {
            if(worldCamera==null)return;
            var rooms=plan?.Root?.Element("rooms");
            if(rooms==null)return;
            var candidates=rooms.Elements("room").Select(room=>
            {
                var pts=room.Elements("point").Select(p=>new Vector2(N(p,"x")/1000f,N(p,"y")/1000f)).ToList();
                Vector2 center=pts.Count==0?Vector2.zero:new Vector2(pts.Average(p=>p.x),pts.Average(p=>p.y));
                return new {room,center};
            }).ToList();
            if(candidates.Count==0)return;
            var nearest=candidates.OrderBy(x=>(x.center-new Vector2(from.x,from.z)).sqrMagnitude).First();
            EnterRoom(nearest.room);
        }
        void EnterRoom(XElement room)
        {
            if(worldCamera==null||room==null)return;
            var pts=room.Elements("point").Select(p=>new Vector2(N(p,"x")/1000f,N(p,"y")/1000f)).ToList();
            if(pts.Count<3)return;
            Vector2 center=new Vector2(pts.Average(p=>p.x),pts.Average(p=>p.y));
            apartmentOverview=false;
            lastInteriorPosition=center;
            worldCamera.orthographic=false;
            worldCamera.fieldOfView=65f;
            worldCamera.nearClipPlane=.025f;
            worldCamera.farClipPlane=200f;
            worldCamera.transform.position=new Vector3(center.x,1.6f,center.y);
            worldCamera.transform.rotation=Quaternion.Euler(0,0,0);
            cameraHome=worldCamera.transform.position;
            cameraHomeRotation=worldCamera.transform.rotation;
            SyncCameraAngles();
            roomNavigationOpen=false;
        }
        void SetApartmentOverview(bool overview)
        {
            apartmentOverview=overview;
            if(!worldCamera)return;
            if(overview)ApplyApartmentOverview(worldCamera);
            else
            {
                ApplyViewerStart(worldCamera.transform);
                worldCamera.orthographic=false;
                worldCamera.fieldOfView=60f;
            }
            cameraHome=worldCamera.transform.position;
            cameraHomeRotation=worldCamera.transform.rotation;
            SyncCameraAngles();
        }
        void ApplyApartmentOverview(Camera camera)
        {
            if(plan?.Root==null||camera==null)return;
            var rooms=plan.Root.Element("rooms");
            if(rooms==null)return;
            var points=rooms.Elements("room").SelectMany(room=>room.Elements("point"))
                .Select(p=>new Vector2(N(p,"x")/1000f,N(p,"y")/1000f)).ToList();
            if(points.Count<3)return;
            float minX=points.Min(p=>p.x),maxX=points.Max(p=>p.x);
            float minY=points.Min(p=>p.y),maxY=points.Max(p=>p.y);
            float cx=(minX+maxX)*.5f,cy=(minY+maxY)*.5f;
            float spanX=maxX-minX,spanY=maxY-minY;
            camera.orthographic=true;
            camera.nearClipPlane=.025f;
            camera.orthographicSize=Mathf.Max(spanY*.58f,spanX*.58f/Mathf.Max(.5f,camera.aspect));
            camera.transform.position=new Vector3(cx,Mathf.Max(14f,spanY*1.8f),cy);
            camera.transform.rotation=Quaternion.Euler(90,0,0);
            camera.nearClipPlane=.01f;
            camera.farClipPlane=200f;
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
            if(apartmentOverview&&plan?.Root?.Element("rooms")?.Elements("room").Skip(1).Any()==true)ApplyApartmentOverview(worldCamera);
            else ApplyViewerStart(go.transform);
            if(!rebuildingViewer)RestoreViewerCamera();
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
                Cube("Floor",new Vector3((min.x+max.x)/2,-.05f,(min.y+max.y)/2),new Vector3(max.x-min.x,.1f,max.y-min.y),new Color(.55f,.55f,.55f));
            }
            var renderPlan=VisibleCabinetPlan();
            var tiledPlan=new XDocument(renderPlan);
            var floorFinishes=tiledPlan.Root.Element("floorFinishes");
            if(floorFinishes!=null)
                foreach(var finish in floorFinishes.Elements("finish").ToList())
                    if(LaminateRenderer.HasFloor(renderPlan,S(finish,"roomId")))finish.Remove();
            FloorTileRenderer.Build(tiledPlan, element => geometry.Add(element));
            LaminateRenderer.Build(renderPlan, element=>geometry.Add(element));
            WallTileMeshRenderer.Build(renderPlan, element => geometry.Add(element));
            ApplianceTextureRenderer.Build(renderPlan, element => geometry.Add(element));
            ApplianceTextureRenderer.BuildCabinetFronts(renderPlan, element => geometry.Add(element));
            CountertopRenderer.Build(renderPlan, element => geometry.Add(element), (name, position, size, color, rotation) =>
            {
                var element = Cube(name, position, size, color);
                element.transform.rotation = Quaternion.Euler(0f, rotation, 0f);
            });
            SofaRenderer.Build(renderPlan, element=>geometry.Add(element));
            StandingDeskRenderer.Build(renderPlan, element=>geometry.Add(element));
            DoubleBedRenderer.Build(renderPlan, element=>geometry.Add(element));
            TvStandRenderer.Build(renderPlan, element=>geometry.Add(element));
            LedTvRenderer.Build(renderPlan, element=>geometry.Add(element));
            RugRenderer.Build(renderPlan, element=>geometry.Add(element));
            DividerShelfRenderer.Build(renderPlan, element=>geometry.Add(element));
            TableRenderer.Build(renderPlan, element=>geometry.Add(element));
            MirroredWardrobeRenderer.Build(renderPlan, element=>geometry.Add(element));
            DecorationRenderer.Build(renderPlan, element=>geometry.Add(element));
            PlanGeometryRenderer.Build(renderPlan, (name, position, size, color, rotation) =>
            {
                var element = Cube(name, position, size, color);
                element.transform.rotation = Quaternion.Euler(0f, rotation, 0f);
            }, element => geometry.Add(element));
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
            if(plan!=null)
            {
                var issues=CabinetManufacturing.Validate(plan);
                var cutPath=Path.Combine(Application.persistentDataPath,"szabasjegyzek.csv");
                File.WriteAllText(cutPath,CabinetManufacturing.Csv(plan),System.Text.Encoding.UTF8);
                status="Export: "+path+" és "+cutPath+" | Ellenőrzés: "+issues.Count+" hiba";
                foreach(var issue in issues)Debug.LogWarning("Cabinet validation: "+issue);
            }
            else status="Export: "+path;
            Debug.Log(status);
        }
        string GizmoKey(int i)
        {
            if(furnitureEditMode==2)return "rotation";
            if(i==3)return "rotation";
            if(i==4)return furnitureResizeMode?"height":"rotation";
            if(!furnitureResizeMode)return i==0?"x":i==1?"y":"z";
            return i==0?"width":i==1?(selectedFurniture.Attribute("length")!=null?"length":"depth"):"height";
        }
        Vector3 GizmoPoint(int i)
        {
            Vector2 center=FurniturePlacement.Center(selectedFurniture);
            Vector2 size=FurniturePlacement.Size(selectedFurniture);
            float y=N(selectedFurniture,"z")/1000f;
            float top=y+FurniturePlacement.Height(selectedFurniture);
            if(i==0)return new Vector3(center.x+size.x*.5f+.2f,y+.15f,center.y);
            if(i==1)return new Vector3(center.x,y+.15f,center.y+size.y*.5f+.2f);
            if(i==2)return new Vector3(center.x,top+.25f,center.y);
            if(i==3)return new Vector3(center.x-size.x*.5f-.25f,top,center.y);
            return new Vector3(center.x+size.x*.5f+.2f,top,center.y+size.y*.5f+.2f);
        }
        void DrawFurnitureGizmo()
        {
            if(selectedFurniture==null||worldCamera==null||addMenuOpen)return;
            string[] labels=furnitureEditMode==2?new[]{"↻X","↻Y","↻Z","↻","↻"}:
                furnitureResizeMode?new[]{"X","Y","Z","↻","⇲"}:new[]{"X→","Y→","Z↑","↻","⇲"};
            for(int i=0;i<5;i++)
            {
                Vector3 p=worldCamera.WorldToScreenPoint(GizmoPoint(i));
                if(p.z<=0)continue;
                var old=GUI.color;
                GUI.color=i==0?Color.red:i==1?Color.green:i==2?Color.cyan:Color.yellow;
                GUI.Box(new Rect(p.x-20,Screen.height-p.y-20,40,40),labels[i]);
                GUI.color=old;
            }
        }
        bool HitGizmo(Vector3 pointer,out int handle)
        {
            handle=-1;
            if(selectedFurniture==null||worldCamera==null)return false;
            for(int i=0;i<5;i++)
            {
                Vector3 p=worldCamera.WorldToScreenPoint(GizmoPoint(i));
                if(p.z>0&&Vector2.Distance(new Vector2(p.x,p.y),new Vector2(pointer.x,pointer.y))<23f)
                {handle=i;return true;}
            }
            return false;
        }
        void DragGizmo(Vector3 pointer)
        {
            if(activeGizmoHandle<0||selectedFurniture==null)return;
            Vector2 delta=new Vector2(pointer.x,pointer.y)-gizmoStartPointer;
            string key=GizmoKey(activeGizmoHandle);
            float value;
            if(key=="rotation")
                value=gizmoStartValue+Mathf.Round(delta.x/5f)*2f;
            else
            {
                // Project mouse displacement onto the selected 3D axis, like RuntimeCabinetDesigner.
                Vector3 start=GizmoPoint(activeGizmoHandle);
                Vector3 axis=activeGizmoHandle==0?Vector3.right:
                    activeGizmoHandle==1?Vector3.forward:Vector3.up;
                if(activeGizmoHandle==4)axis=Vector3.up;
                Vector3 p0=worldCamera.WorldToScreenPoint(start);
                Vector3 p1=worldCamera.WorldToScreenPoint(start+axis*.5f);
                Vector2 screenAxis=new Vector2(p1.x-p0.x,p1.y-p0.y);
                float projected=screenAxis.sqrMagnitude<.001f?delta.y:
                    Vector2.Dot(delta,screenAxis.normalized);
                float pixelsPerMetre=Mathf.Max(12f,screenAxis.magnitude*2f);
                float mm=Mathf.Round(projected/pixelsPerMetre*1000f/10f)*10f;
                value=gizmoStartValue+mm;
            }
            if((key=="width"||key=="depth"||key=="length"||key=="height")&&value<100)return;
            if(key=="z"&&value<0)return;
            if(Mathf.Abs(N(selectedFurniture,key)-value)<.001f)return;
            string reason;
            if(FurniturePlacement.TryChange(plan,selectedFurniture,key,value,out reason))
            {furnitureEditError="";RefreshViewerGeometry();}
            else furnitureEditError=reason;
        }
        void DrawFurnitureSelectionOutline()
        {
            if(selectedFurniture==null||worldCamera==null||addMenuOpen)return;
            Vector2[] corners=FurniturePlacement.Corners(selectedFurniture);
            float bottom=N(selectedFurniture,"z")/1000f;
            float top=bottom+FurniturePlacement.Height(selectedFurniture);
            var points=new Vector3[8];
            for(int i=0;i<4;i++)
            {
                points[i]=worldCamera.WorldToScreenPoint(new Vector3(corners[i].x,bottom,corners[i].y));
                points[i+4]=worldCamera.WorldToScreenPoint(new Vector3(corners[i].x,top,corners[i].y));
            }
            if(Event.current.type!=EventType.Repaint)return;
            int[,] edges={{0,1},{1,2},{2,3},{3,0},{4,5},{5,6},{6,7},{7,4},{0,4},{1,5},{2,6},{3,7}};
            for(int i=0;i<12;i++)
            {
                Vector3 a=points[edges[i,0]],b=points[edges[i,1]];
                if(a.z<=0||b.z<=0)continue;
                DrawViewerLine(new Vector2(a.x,Screen.height-a.y),new Vector2(b.x,Screen.height-b.y),
                    new Color(.2f,1f,.45f),2.5f);
            }
        }
        void DrawViewerLine(Vector2 a,Vector2 b,Color color,float width)
        {
            Vector2 d=b-a;
            if(d.sqrMagnitude<.001f)return;
            Matrix4x4 oldMatrix=GUI.matrix;
            Color oldColor=GUI.color;
            GUI.color=color;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(d.y,d.x)*Mathf.Rad2Deg,a);
            GUI.DrawTexture(new Rect(a.x,a.y-width*.5f,d.magnitude,width),Texture2D.whiteTexture);
            GUI.matrix=oldMatrix;
            GUI.color=oldColor;
        }
        void OnGUI()
        {
            if(current=="viewer")
            {
                DrawFurnitureSelectionOutline();
                DrawFurnitureGizmo();
                DrawCabinetVisibilityMenu();
            }
            if(current=="editor")xmlEditor.Draw();
            if(!string.IsNullOrEmpty(status))GUI.Label(new Rect(12,Screen.height-32,Screen.width-24,25),status);
        }
    }
}
