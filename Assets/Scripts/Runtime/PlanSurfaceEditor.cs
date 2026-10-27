using System;
using System.Linq;
using System.Xml.Linq;
using UnityEngine;

namespace BuildYourFurniture
{
    // Surface editor operates on the same plan XML as the 3D renderer.
    public sealed class PlanSurfaceEditor
    {
        public bool Visible;
        int tab,selected;
        Vector2 scroll;
        Action saveTextureChange;
        XElement copied;
        string copiedKind;
        static string S(XElement e,string key,string fallback="")=>(string)e?.Attribute(key)??fallback;
        static float N(XElement e,string key,float fallback)
        {
            float n;return float.TryParse(S(e,key),System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,out n)?n:fallback;
        }
        static void Set(XElement e,string key,float value)=>
            e.SetAttributeValue(key,value.ToString("0.###",System.Globalization.CultureInfo.InvariantCulture));
        static string[] TextureNames()=>Resources.LoadAll<Texture2D>("Textures")
            .Where(t=>t!=null).Select(t=>t.name).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s=>s,StringComparer.OrdinalIgnoreCase).ToArray();
        static bool Choice(XElement target,string key,string[] names)
        {
            string old=S(target,key);
            var labels=new[]{"Nincs textúra"}.Concat(names).ToArray();
            int index=Array.FindIndex(names,n=>string.Equals("Textures/"+n,old,StringComparison.OrdinalIgnoreCase)||string.Equals("procedural:"+n,old,StringComparison.OrdinalIgnoreCase)||string.Equals(n,old,StringComparison.OrdinalIgnoreCase));
            int next=GUILayout.SelectionGrid(index+1,labels,2);
            if(next==index+1)return false;
            target.SetAttributeValue(key,next==0?"":(Array.IndexOf(SurfaceTextureLibrary.Names,names[next-1])>=0?"procedural:":"Textures/")+names[next-1]);
            return true;
        }
        static bool Number(XElement e,string key,string label,float step,float min,float max)
        {
            float v=N(e,key,min);
            GUILayout.BeginHorizontal();
            GUILayout.Label(label,GUILayout.Width(160));
            bool changed=false;
            if(GUILayout.Button("−",GUILayout.Width(30))){Set(e,key,Mathf.Clamp(v-step,min,max));changed=true;}
            GUILayout.Label(v.ToString("0.###"),GUILayout.Width(65));
            if(GUILayout.Button("+",GUILayout.Width(30))){Set(e,key,Mathf.Clamp(v+step,min,max));changed=true;}
            GUILayout.EndHorizontal();
            return changed;
        }
        public void Draw(XDocument plan,Action saveAndRefresh)
        {
            if(!Visible||plan?.Root==null)return;
            saveTextureChange=saveAndRefresh;
            float width=Mathf.Min(430,Screen.width*.43f);
            var rect=new Rect(Screen.width-width-8,56,width,Mathf.Max(120,Screen.height-65));
            FurnitureApp.DrawOpaquePanel(rect);
            GUI.Box(rect,"Felületek: falak / csempék / padlók");
            GUILayout.BeginArea(new Rect(rect.x+10,rect.y+26,rect.width-20,rect.height-36));
            scroll=GUILayout.BeginScrollView(scroll);
            int nextTab=GUILayout.SelectionGrid(tab,new[]{"Fal","Csempe","Padló"},3);
            if(nextTab!=tab){tab=nextTab;selected=0;}
            string group=tab==0?"walls":tab==1?"wallFinishes":"floorFinishes";
            string element=tab==0?"wall":"finish";
            var entries=plan.Root.Element(group)?.Elements(element).ToArray()??new XElement[0];
            if(entries.Length==0){GUILayout.Label("Nincs ilyen felület a tervben.");GUILayout.EndScrollView();GUILayout.EndArea();return;}
            selected=Mathf.Clamp(selected,0,entries.Length-1);
            GUILayout.Label("Válassz felületet:");
            for(int i=0;i<entries.Length;i++)
            {
                string label=S(entries[i],"id")+" "+S(entries[i],"name");
                if(GUILayout.Toggle(selected==i,label,"Button"))selected=i;
            }
            var item=entries[selected];
            bool changed=false;
            GUILayout.Label("Kijelölt: "+S(item,"id"),GUI.skin.box);
            if(GUILayout.Button("Felület másolása"))
            {copied=new XElement(tab==0?(item.Element("surface")??new XElement("surface")):item);copiedKind=group;}
            if(copied!=null&&copiedKind==group&&GUILayout.Button("Másolt felület beillesztése ide"))
            {
                var destination=tab==0?(item.Element("surface")??new XElement("surface")):item;
                if(tab==0&&destination.Parent==null)item.Add(destination);
                foreach(var attr in copied.Attributes())
                {
                    if(attr.Name.LocalName=="id"||attr.Name.LocalName=="name"||
                       attr.Name.LocalName=="x"||attr.Name.LocalName=="y"||
                       attr.Name.LocalName=="x1"||attr.Name.LocalName=="y1"||
                       attr.Name.LocalName=="roomId"||attr.Name.LocalName=="wallIds")continue;
                    destination.SetAttributeValue(attr.Name,attr.Value);
                }
                changed=true;
            }
            if(tab==0)
            {
                GUILayout.Label("Falfelület (mindkét oldal)");
                var finish=item.Element("surface");
                if(finish==null){finish=new XElement("surface");item.Add(finish);}
                changed|=EditAppearance(finish,true);
            }
            else
            {
                GUILayout.Label(tab==1?"Csempézés":"Padlóburkolat");
                changed|=EditAppearance(item,false);
                changed|=Number(item,"tileWidth","Lap szélesség (mm)",50,50,3000);
                changed|=Number(item,"tileHeight","Lap magasság (mm)",50,50,3000);
                changed|=Number(item,"groutWidth","Fuga (mm)",.5f,0,20);
                changed|=Number(item,"stagger","Eltolás (0–1)",.1f,0,.9f);
                if(tab==1)
                {
                    changed|=Number(item,"startHeight","Kezdőmagasság (mm)",100,0,3000);
                    changed|=Number(item,"rows","Sorok",1,1,50);
                    GUILayout.Label("Csempézendő falak (több is választható):");
                    GUILayout.Label("Kijelölt: "+S(item,"wallIds"));
                    foreach(var wall in plan.Root.Element("walls")?.Elements("wall")??Enumerable.Empty<XElement>())
                    {
                        string id=S(wall,"id");
                        var ids=S(item,"wallIds").Split(new[]{','},StringSplitOptions.RemoveEmptyEntries).ToList();
                        bool enabled=ids.Contains(id);
                        float dx=N(wall,"x1",0)-N(wall,"x",0);
                        float dy=N(wall,"y1",0)-N(wall,"y",0);
                        int length=Mathf.RoundToInt(Mathf.Sqrt(dx*dx+dy*dy));
                        bool toggled=GUILayout.Toggle(enabled,id+"  ("+length+" mm)");
                        if(toggled!=enabled)
                        {
                            if(toggled)ids.Add(id);else ids.Remove(id);
                            item.SetAttributeValue("wallIds",string.Join(",",ids));
                            changed=true;
                        }
                    }
                }
                else
                {
                    string direction=S(item,"direction","x");
                    if(GUILayout.Button("Fektetés iránya: "+direction))
                    {item.SetAttributeValue("direction",direction=="x"?"y":"x");changed=true;}
                }
                if(tab==1)
                {
                    GUILayout.Label("Vízszintes fuga – Keracolor FF");
                    changed|=KeracolorChoice(item,"groutColor");
                    GUILayout.Label("Függőleges fuga – Keracolor FF");
                    changed|=KeracolorChoice(item,"verticalGroutColor","groutColor");
                }
                else
                {
                    GUILayout.Label("Fuga színe – Keracolor FF");
                    changed|=KeracolorChoice(item,"groutColor");
                }
            }
            if(changed)saveAndRefresh();
            GUILayout.EndScrollView();GUILayout.EndArea();
        }
        // Keracolor FF catalogue swatches, approximate sRGB values for on-screen preview.
        // The numeric manufacturer codes are stored alongside the HEX colors in the plan.
        static readonly string[] KeracolorCodes={
            "100","103","110","111","112","113","114","120","130","131","132",
            "160","133","134","135","142","141","140","145","143","136","144",
            "149","174","170","181"
        };
        static readonly string[] KeracolorNames={
            "Fehér","Holdfény","Manhattan 2000","Ezüstszürke","Középszürke",
            "Cementszürke","Antracit","Fekete","Jázmin","Vanília","Bézs 2000",
            "Magnólia","Homok","Selyem","Aranypor","Gesztenye","Karamell",
            "Korall","Siennai föld","Terrakotta","Tőzeg","Csokoládé",
            "Vulkáni homok","Tornádó","Krókusz","Jadezöld"
        };
        static readonly string[] KeracolorHex={
            "#F5F5F3","#E8EAE6","#E1E1E1","#EBEBEB","#9F9F9F",
            "#888982","#4C5051","#373431","#F6E9D4","#FFF1D4","#E9D2B5",
            "#F0D0C0","#B5A08C","#A4917E","#AD8968","#B48665","#C59B7C",
            "#D09B8B","#C57D64","#925F52","#765F55","#664D43",
            "#544B46","#626A67","#D4DFEF","#A7C8AA"
        };
        static bool KeracolorChoice(XElement item,string key,string fallbackKey=null)
        {
            string current=S(item,key,fallbackKey==null?"":S(item,fallbackKey));
            string code=S(item,key+"Code");
            int chosen=Array.IndexOf(KeracolorCodes,code);
            if(chosen<0)chosen=Array.FindIndex(KeracolorHex,h=>string.Equals(h,current,StringComparison.OrdinalIgnoreCase));
            GUILayout.Label(chosen<0?"Egyedi szín: "+current:
                "Kiválasztva: "+KeracolorCodes[chosen]+" "+KeracolorNames[chosen]);
            bool changed=false;
            for(int i=0;i<KeracolorCodes.Length;i++)
            {
                if(i%2==0)GUILayout.BeginHorizontal();
                Color preview;
                ColorUtility.TryParseHtmlString(KeracolorHex[i],out preview);
                Color previous=GUI.backgroundColor;
                GUI.backgroundColor=Color.Lerp(Color.white,preview,.65f);
                if(GUILayout.Button(KeracolorCodes[i]+" "+KeracolorNames[i],
                    GUILayout.MinHeight(28)))
                {
                    item.SetAttributeValue(key,KeracolorHex[i]);
                    item.SetAttributeValue(key+"Code",KeracolorCodes[i]);
                    changed=true;
                }
                GUI.backgroundColor=previous;
                if(i%2==1||i==KeracolorCodes.Length-1)GUILayout.EndHorizontal();
            }
            return changed;
        }
        static bool ColorChoice(XElement item,string key)
        {
            string[] colors={"#FFFFFF","#E8E4DC","#C5C5C5","#A68B71","#575757","#242424","#B5C8D3","#D1B28B"};
            int old=Array.IndexOf(colors,S(item,key));
            int next=GUILayout.SelectionGrid(old<0?0:old,
                new[]{"Fehér","Törtfehér","Szürke","Bézs","Sötétszürke","Fekete","Kékesszürke","Homok"},4);
            if(next==old)return false;
            item.SetAttributeValue(key,colors[next]);return true;
        }
        bool EditAppearance(XElement item,bool wall)
        {
            bool changed=false;
            GUILayout.Label("Textúra a projektből");
            TexturePickerPopup.Button("Textúra választása",S(item,"texture"),id=>
            {
                item.SetAttributeValue("texture",id);
                saveTextureChange?.Invoke();
            });
            GUILayout.Label("Felület színe");
            changed|=ColorChoice(item,"color");
            float smooth=N(item,"smoothness",wall?.15f:.4f);
            GUILayout.Label("Fényesség: "+smooth.ToString("0.00"));
            float next=GUILayout.HorizontalSlider(smooth,0,1);
            if(Mathf.Abs(next-smooth)>.03f){Set(item,"smoothness",next);changed=true;}
            string mode=S(item,"textureMode","tile");
            int m=GUILayout.SelectionGrid(mode=="stretch"?1:0,new[]{"Ismétlés / mozaik","Nyújtás"},2);
            if((m==1)!=(mode=="stretch")){item.SetAttributeValue("textureMode",m==1?"stretch":"tile");changed=true;}
            if(m==0)changed|=Number(item,"textureSize","Mintaméret (mm)",100,50,3000);
            int rotation=(int)N(item,"textureRotation",0);
            int r=GUILayout.SelectionGrid(rotation>=45?1:0,new[]{"0°","90°"},2);
            if(r!=(rotation>=45?1:0)){Set(item,"textureRotation",r*90);changed=true;}
            return changed;
        }
    }
}