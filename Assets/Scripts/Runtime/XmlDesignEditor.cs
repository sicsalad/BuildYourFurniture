using System;
using System.IO;
using System.Xml.Linq;
using UnityEngine;

namespace BuildYourFurniture
{
    // Runtime IMGUI editor. In the Unity Editor it writes Resources source files;
    // in standalone builds it writes copies under persistentDataPath/XML.
    public sealed class XmlDesignEditor
    {
        static readonly string[] Names={"Plans/sample","CabinetTypes/normal","CabinetTypes/empty","CabinetTypes/corner","UI/main","UI/viewer","UI/materials","UI/editor"};
        int selected=-1;
        string content="",message="";
        Vector2 scroll;
        string PathFor(string key)
        {
#if UNITY_EDITOR
            return Path.Combine(Application.dataPath,"Resources",key+".xml");
#else
            return Path.Combine(Application.persistentDataPath,"XML",key+".xml");
#endif
        }
        void Open(int index)
        {
            selected=index;
            string key=Names[index],path=PathFor(key);
            if(File.Exists(path))content=File.ReadAllText(path);
            else
            {
                var asset=Resources.Load<TextAsset>(key);
                content=asset?asset.text:"";
            }
            message=content.Length==0?"Nem található: "+key:"Megnyitva: "+key;
        }
        void Save()
        {
            if(selected<0)return;
            try
            {
                var xml=XDocument.Parse(content,LoadOptions.PreserveWhitespace);
                string root=Names[selected].StartsWith("Plans/")?"plan":
                    Names[selected].StartsWith("CabinetTypes/")?"cabinetType":"screen";
                if(xml.Root==null||xml.Root.Name.LocalName!=root)
                    throw new InvalidDataException("Várt gyökérelem: "+root);
                if(root=="plan")
                {
                    CabinetPlacementEngine.Resolve(xml);
                    var issues=CabinetManufacturing.Validate(xml);
                    if(issues.Count>0)message="Geometriai figyelmeztetések: "+string.Join(" | ",issues.ToArray());
                }
                content=xml.ToString();
                string path=PathFor(Names[selected]);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path,content,System.Text.Encoding.UTF8);
#if UNITY_EDITOR
                UnityEditor.AssetDatabase.Refresh();
#endif
                message="Mentve: "+path;
            }
            catch(Exception ex){message="Hiba: "+ex.Message;}
        }
        int fontSize=20;
        GUIStyle editorStyle, listStyle, buttonStyle, labelStyle;
        void Styles()
        {
            if(editorStyle==null)
            {
                editorStyle=new GUIStyle(GUI.skin.textArea){wordWrap=false,richText=false};
                listStyle=new GUIStyle(GUI.skin.button){alignment=TextAnchor.MiddleLeft,wordWrap=true};
                buttonStyle=new GUIStyle(GUI.skin.button);
                labelStyle=new GUIStyle(GUI.skin.label){wordWrap=true};
            }
            editorStyle.fontSize=fontSize;
            listStyle.fontSize=Mathf.Max(14,fontSize-2);
            buttonStyle.fontSize=Mathf.Max(14,fontSize-3);
            labelStyle.fontSize=Mathf.Max(14,fontSize-3);
        }
        public void Draw()
        {
            var bounds=new Rect(16,120,Screen.width-32,Screen.height-155);
            var ev=Event.current;
            if(ev.type==EventType.ScrollWheel && (ev.control||ev.command) && bounds.Contains(ev.mousePosition))
            {
                fontSize=Mathf.Clamp(fontSize-(int)Mathf.Sign(ev.delta.y)*2,12,42);
                ev.Use();
            }
            Styles();
            float sidebar=bounds.width*.20f, gap=12f, editorWidth=bounds.width*.70f;
            float top=bounds.y+8f;
            GUI.Box(new Rect(bounds.x,top,sidebar,bounds.height-16),"XML fájlok");
            var listRect=new Rect(bounds.x+8,top+34,sidebar-16,bounds.height-50);
            GUILayout.BeginArea(listRect);
            for(int i=0;i<Names.Length;i++)
            {
                var style=new GUIStyle(listStyle);
                if(i==selected)style.normal.textColor=Color.cyan;
                if(GUILayout.Button(Names[i]+".xml",style,GUILayout.Height(42))&&i!=selected)Open(i);
            }
            GUILayout.EndArea();
            float editorX=bounds.x+sidebar+gap;
            GUI.Box(new Rect(editorX,top,editorWidth,bounds.height-16),"XML szerkesztő  |  Ctrl + görgő: betűméret ("+fontSize+" pt)");
            float toolbarY=top+34;
            GUILayout.BeginArea(new Rect(editorX+8,toolbarY,editorWidth-16,42));
            GUILayout.BeginHorizontal();
            if(GUILayout.Button("XML ellenőrzés",buttonStyle,GUILayout.Width(145)))
            {
                try{XDocument.Parse(content);message="Az XML szintaktikailag érvényes.";}
                catch(Exception ex){message=ex.Message;}
            }
            if(GUILayout.Button("Geometria ellenőrzés",buttonStyle,GUILayout.Width(200)))
            {
                try{var xml=XDocument.Parse(content);var issues=CabinetManufacturing.Validate(xml);message=issues.Count==0?"Geometria rendben.":string.Join(" | ",issues.ToArray());}
                catch(Exception ex){message="Hiba: "+ex.Message;}
            }
            if(GUILayout.Button("Mentés + pozíciószámítás",buttonStyle,GUILayout.Width(260)))Save();
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
            float textY=toolbarY+47;
            float messageHeight=56;
            GUI.Label(new Rect(editorX+10,bounds.y+bounds.height-messageHeight-8,editorWidth-20,messageHeight),message,labelStyle);
            var textRect=new Rect(editorX+8,textY,editorWidth-16,bounds.y+bounds.height-messageHeight-textY-12);
            // Scroll view is independent of the file list, with monospaced-style layout.
            scroll=GUI.BeginScrollView(textRect,scroll,new Rect(0,0,Mathf.Max(textRect.width-22,content.Length>0?textRect.width-22:1),Mathf.Max(textRect.height-22,(content.Split('\\n').Length+2)*(fontSize+7))));
            var contentRect=new Rect(0,0,Mathf.Max(textRect.width-22,100),Mathf.Max(textRect.height-22,(content.Split('\\n').Length+2)*(fontSize+7)));
            content=GUI.TextArea(contentRect,content,editorStyle);
            GUI.EndScrollView();
        }
    }
}
