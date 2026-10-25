using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace BuildYourFurniture
{
    // RAL values are screen-display approximations, not calibrated physical paint references.
    public static class CabinetAppearanceLibrary
    {
        public struct Swatch
        {
            public readonly string Name,Hex;
            public Swatch(string name,string hex){Name=name;Hex=hex;}
        }
        public static readonly Swatch[] Presets={
            new Swatch("RAL 7016 Antracit","#383E42"),
            new Swatch("RAL 7021 Feketésszürke","#2F3234"),
            new Swatch("RAL 7035 Világosszürke","#CBD0CC"),
            new Swatch("RAL 9005 Fekete","#0E0E10"),
            new Swatch("RAL 9010 Fehér","#F1EDE1"),
            new Swatch("RAL 5002 Ultramarinkék","#20214F"),
            new Swatch("RAL 5010 Enciánkék","#0E477B"),
            new Swatch("RAL 5015 Égszínkék","#2271B3"),
            new Swatch("RAL 5024 Pasztellkék","#6A93B0"),
            new Swatch("RAL 6005 Mohazöld","#114232"),
            new Swatch("RAL 6018 Sárgászöld","#57A639"),
            new Swatch("RAL 3000 Tűzpiros","#AF2B1E"),
            new Swatch("RAL 3020 Közlekedési piros","#C1121C"),
            new Swatch("RAL 1003 Jelzéssárga","#F9A900"),
            new Swatch("RAL 8003 Agyagbarna","#7B5141"),
            new Swatch("RAL 9006 Fehéralumínium","#A5A5A5")
        };
        public static string ImportedFolder=>Path.Combine(Application.persistentDataPath,"XML","CabinetTextures");
        public static IEnumerable<string> ResourceTextures()
        {
            return Resources.LoadAll<Texture2D>("").Where(t=>t!=null).Select(t=>t.name)
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s=>s);
        }
        public static IEnumerable<string> ImportedTextures()
        {
            if(!Directory.Exists(ImportedFolder))return Enumerable.Empty<string>();
            return Directory.GetFiles(ImportedFolder).Where(IsImage)
                .Select(Path.GetFileName).OrderBy(s=>s).ToArray();
        }
        static bool IsImage(string path)
        {
            string ext=Path.GetExtension(path).ToLowerInvariant();
            return ext==".png"||ext==".jpg"||ext==".jpeg";
        }
        public static string Import(string source)
        {
            if(!File.Exists(source)||!IsImage(source))throw new InvalidDataException("PNG vagy JPG fájl szükséges.");
            if(new FileInfo(source).Length>32*1024*1024)throw new InvalidDataException("A textúra legfeljebb 32 MB lehet.");
            var bytes=File.ReadAllBytes(source);
            var test=new Texture2D(2,2);
            try{if(!ImageConversion.LoadImage(test,bytes))throw new InvalidDataException("Nem olvasható képfájl.");}
            finally{UnityEngine.Object.Destroy(test);}
            Directory.CreateDirectory(ImportedFolder);
            string filename=Guid.NewGuid().ToString("N").Substring(0,8)+"_"+Path.GetFileName(source);
            File.WriteAllBytes(Path.Combine(ImportedFolder,filename),bytes);
            return "imported:"+filename;
        }
        public static Texture2D Load(string id)
        {
            if(string.IsNullOrWhiteSpace(id))return null;
            if(id.StartsWith("imported:",StringComparison.Ordinal))
            {
                string name=id.Substring(9);
                if(Path.GetFileName(name)!=name)return null;
                string path=Path.Combine(ImportedFolder,name);
                if(!File.Exists(path))return null;
                var texture=new Texture2D(2,2);
                if(ImageConversion.LoadImage(texture,File.ReadAllBytes(path)))return texture;
                UnityEngine.Object.Destroy(texture);return null;
            }
            string resource=id.StartsWith("resource:",StringComparison.Ordinal)?id.Substring(9):id;
            return Resources.Load<Texture2D>(resource)??Resources.LoadAll<Texture2D>("").FirstOrDefault(t=>t.name==resource);
        }
    }
}