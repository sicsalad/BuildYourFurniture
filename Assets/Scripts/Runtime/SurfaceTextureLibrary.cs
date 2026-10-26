using System;
using System.Collections.Generic;
using UnityEngine;

namespace BuildYourFurniture
{
    // Offline procedural materials. No web requests and no external image files required.
    public static class SurfaceTextureLibrary
    {
        public static readonly string[] Names={
            "Feher marvany","Szurke marvany","Fekete marvany","Travertin","Homokko",
            "Beton vilagos","Beton sotet","Terrazzo","Granit","Palako",
            "Tolgy natur","Tolgy fustolt","Dio","Bukk","Fenyofa",
            "Feher csempe","Kek csempe","Zold csempe","Terrakotta","Szurke gres"
        };
        static readonly Color[] Colors={
            new Color(.91f,.91f,.88f),new Color(.63f,.65f,.67f),new Color(.18f,.19f,.21f),
            new Color(.76f,.67f,.52f),new Color(.76f,.69f,.55f),new Color(.76f,.76f,.74f),
            new Color(.32f,.33f,.34f),new Color(.82f,.79f,.72f),new Color(.49f,.49f,.48f),
            new Color(.35f,.38f,.4f),new Color(.65f,.46f,.27f),new Color(.35f,.27f,.22f),
            new Color(.39f,.23f,.15f),new Color(.77f,.65f,.46f),new Color(.76f,.57f,.37f),
            new Color(.92f,.92f,.9f),new Color(.24f,.43f,.62f),new Color(.3f,.53f,.4f),
            new Color(.67f,.32f,.22f),new Color(.58f,.6f,.61f)
        };
        static readonly Dictionary<string,Texture2D> Cache=new Dictionary<string,Texture2D>();
        public static Texture2D Load(string name)
        {
            if(string.IsNullOrEmpty(name))return null;
            string key=name.StartsWith("procedural:",StringComparison.OrdinalIgnoreCase)?name.Substring(11):name;
            int index=Array.FindIndex(Names,n=>string.Equals(n,key,StringComparison.OrdinalIgnoreCase));
            if(index<0)return null;
            Texture2D result;
            if(Cache.TryGetValue(key,out result)&&result!=null)return result;
            const int size=256;
            result=new Texture2D(size,size,TextureFormat.RGB24,true);
            result.name=key;result.wrapMode=TextureWrapMode.Repeat;
            bool wood=index>=10&&index<=14;
            bool marble=index<=2;
            bool stone=index>=3&&index<=9;
            for(int y=0;y<size;y++)
            for(int x=0;x<size;x++)
            {
                float a=Mathf.PerlinNoise((x+index*37)*.027f,(y+index*53)*.027f);
                float b=Mathf.PerlinNoise((x+index*91)*.11f,(y+index*23)*.11f);
                float fine=Mathf.PerlinNoise(x*.48f+index,y*.48f+index);
                float value;
                if(wood)
                {
                    float grain=Mathf.Sin(x*.095f+Mathf.PerlinNoise(x*.012f,y*.025f)*8f);
                    value=.85f+.11f*grain+.09f*(a-.5f)+.05f*(fine-.5f);
                }
                else if(marble)
                {
                    float vein=Mathf.Abs(Mathf.Sin((x+y*.32f)*.052f+a*7f));
                    value=.96f-.25f*Mathf.Pow(1f-vein,14f)+.06f*(b-.5f);
                }
                else if(stone)
                    value=.88f+.22f*(a-.5f)+.11f*(b-.5f)+.06f*(fine-.5f);
                else
                    value=.97f+.08f*(a-.5f)+.04f*(fine-.5f);
                if(index==7&&fine>.72f)value*=.52f;
                result.SetPixel(x,y,Colors[index]*Mathf.Clamp(value,.25f,1.2f));
            }
            result.Apply(true,false);Cache[key]=result;return result;
        }
    }
}