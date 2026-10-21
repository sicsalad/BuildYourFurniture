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
        public void Draw()
        {
            GUILayout.BeginArea(new Rect(24,135,Screen.width-48,Screen.height-195));
            GUILayout.Label("Válassz XML-t:");
            int next=GUILayout.SelectionGrid(selected,Names,4,GUILayout.Height(65));
            if(next>=0&&next!=selected)Open(next);
            GUILayout.BeginHorizontal();
            if(GUILayout.Button("XML ellenőrzés",GUILayout.Width(145)))
            {
                try{XDocument.Parse(content);message="Az XML szintaktikailag érvényes.";}
                catch(Exception ex){message=ex.Message;}
            }
            if(GUILayout.Button("Geometria ellenőrzés",GUILayout.Width(180)))
            {
                try{var xml=XDocument.Parse(content);var issues=CabinetManufacturing.Validate(xml);message=issues.Count==0?"Geometria rendben.":string.Join(" | ",issues.ToArray());}
                catch(Exception ex){message="Hiba: "+ex.Message;}
            }
            if(GUILayout.Button("Mentés + pozíciószámítás",GUILayout.Width(230)))Save();
            GUILayout.Label(message);
            GUILayout.EndHorizontal();
            scroll=GUILayout.BeginScrollView(scroll);
            content=GUILayout.TextArea(content,GUILayout.ExpandHeight(true));
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }
    }
}
