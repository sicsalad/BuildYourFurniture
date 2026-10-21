#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using UnityEditor;
using UnityEngine;

namespace BuildYourFurniture
{
    public static class CabinetSelfTests
    {
        [MenuItem("BuildYourFurniture/Validate Cabinet System")]
        public static void Run()
        {
            int checks=0;
            Action<bool,string> check=(condition,message)=>{
                checks++;
                if(!condition)throw new Exception("Test failed: "+message);
            };
            try
            {
                var sample=Resources.Load<TextAsset>("Plans/sample");
                check(sample!=null,"sample XML exists");
                var plan=XDocument.Parse(sample.text);
                CabinetPlacementEngine.Resolve(plan);
                var cabinets=plan.Root.Element("cabinetry").Elements("cabinet").ToList();
                check(cabinets.Count>=6,"sample cabinets");
                foreach(string id in new[]{"normal","empty","corner"})
                {
                    var type=CabinetTypeEngine.EffectiveType(id);
                    check(type.Element("elements").Elements("element").Any(),"elements in "+id);
                }
                var empty=CabinetTypeEngine.Parts(cabinets.First(e=>(string)e.Attribute("id")=="F01")).ToList();
                check(empty.All(e=>(string)e.Attribute("id")!="BASE"),"empty cabinet has no base");
                var normal=CabinetTypeEngine.Parts(cabinets.First(e=>(string)e.Attribute("id")=="F02")).ToList();
                check(normal.Any(e=>(string)e.Attribute("id")=="BASE"),"normal cabinet has base");
                var basePart=normal.First(e=>(string)e.Attribute("id")=="BASE");
                var left=normal.First(e=>(string)e.Attribute("id")=="LEFT");
                float baseTop=float.Parse((string)basePart.Attribute("y"),System.Globalization.CultureInfo.InvariantCulture)+
                    float.Parse((string)basePart.Attribute("height"),System.Globalization.CultureInfo.InvariantCulture)/2;
                float sideBottom=float.Parse((string)left.Attribute("y"),System.Globalization.CultureInfo.InvariantCulture)-
                    float.Parse((string)left.Attribute("height"),System.Globalization.CultureInfo.InvariantCulture)/2;
                check(Math.Abs(baseTop-sideBottom)<.01f,"side rests on base");
                string csv=CabinetManufacturing.Csv(plan);
                check(csv.Contains("F02")&&csv.Contains("CutWidth_mm"),"cut-list content");
                var issues=CabinetManufacturing.Validate(plan);
                if(issues.Count>0)Debug.LogWarning("Cabinet validation issues:\n"+string.Join("\n",issues.ToArray()));
                Debug.Log("Cabinet XML self-tests passed: "+checks+" checks; geometric warnings: "+issues.Count);
            }
            catch(Exception ex){Debug.LogError("Cabinet self-tests failed: "+ex);throw;}
        }
    }
}
#endif
