using System;
using System.Linq;
using UnityEngine;

namespace BuildYourFurniture
{
    // IMGUI modal texture browser: intercepts input before scene selection.
    public static class TexturePickerPopup
    {
        static Action<string> onChoose;
        static string title;
        static Vector2 scroll;
        public static bool IsOpen=>onChoose!=null;
        public static void Open(string caption,Action<string> choose)
        {
            title=caption;onChoose=choose;scroll=Vector2.zero;
        }
        public static bool Button(string caption,string selected,Action<string> choose)
        {
            if(!GUILayout.Button(caption+": "+(string.IsNullOrEmpty(selected)?"Alapértelmezett":selected)))return false;
            Open(caption,choose);return true;
        }
        public static void Draw()
        {
            if(!IsOpen)return;
            // A full-screen blocker prevents click-through to underlying IMGUI panels.
            GUI.Box(new Rect(0,0,Screen.width,Screen.height),"");
            float w=Mathf.Min(690,Screen.width-30),h=Mathf.Min(560,Screen.height-30);
            Rect rect=new Rect((Screen.width-w)/2,(Screen.height-h)/2,w,h);
            GUI.Box(rect,title+" — textúra választása");
            GUILayout.BeginArea(new Rect(rect.x+12,rect.y+30,w-24,h-42));
            if(GUILayout.Button("Bezárás",GUILayout.Height(30))){onChoose=null;GUILayout.EndArea();return;}
            scroll=GUILayout.BeginScrollView(scroll);
            if(GUILayout.Button("Alapértelmezett / nincs textúra",GUILayout.Height(35)))Select("");
            var textures=Resources.LoadAll<Texture2D>("Textures")
                .Where(t=>t!=null).GroupBy(t=>t.name,StringComparer.OrdinalIgnoreCase)
                .Select(g=>g.First()).OrderBy(t=>t.name,StringComparer.OrdinalIgnoreCase).ToArray();
            for(int i=0;i<SurfaceTextureLibrary.Names.Length;i++)
            {
                string name=SurfaceTextureLibrary.Names[i];
                if(GUILayout.Button("Minta: "+name,GUILayout.Height(30)))Select("procedural:"+name);
            }
            foreach(var t in textures)
            {
                if(GUILayout.Button("Fájl: "+t.name,GUILayout.Height(30)))Select("Textures/"+t.name);
            }
            GUILayout.EndScrollView();GUILayout.EndArea();
            if(Event.current.type==EventType.MouseDown||Event.current.type==EventType.ScrollWheel)
                Event.current.Use();
        }
        static void Select(string id)
        {
            var callback=onChoose;onChoose=null;callback?.Invoke(id);
        }
    }
}