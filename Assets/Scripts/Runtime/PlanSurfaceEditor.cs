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
            int index=Array.FindIndex(names,n=>string.Equals("Textures/"+n,old,StringComparison.OrdinalIgnoreCase)||string.Equals(n,old,StringComparison.OrdinalIgnoreCase));
            int next=GUILayout.SelectionGrid(index+1,labels,2);
            if(next==index+1)return false;
            target.SetAttributeValue(key,next==0?"":"Textures/"+names[next-1]);
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
            float width=Mathf.Min(430,Screen.width*.43f);
            var rect=new Rect(Screen.width-width-8,56,width,Mathf.Max(120,Screen.height-65));
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
                    GUILayout.Label("Falazonosítók: "+S(item,"wallIds"));
                    foreach(var wall in plan.Root.Element("walls")?.Elements("wall")??Enumerable.Empty<XElement>())
                    {
                        string id=S(wall,"id");
                        var ids=S(item,"wallIds").Split(new[]{','},StringSplitOptions.RemoveEmptyEntries).ToList();
                        bool enabled=ids.Contains(id);
                        bool toggled=GUILayout.Toggle(enabled,id);
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
                GUILayout.Label("Fuga színe");
                changed|=ColorChoice(item,"groutColor");
            }
            if(changed)saveAndRefresh();
            GUILayout.EndScrollView();GUILayout.EndArea();
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
        static bool EditAppearance(XElement item,bool wall)
        {
            bool changed=false;
            GUILayout.Label("Textúra a projektből");
            changed|=Choice(item,"texture",TextureNames());
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