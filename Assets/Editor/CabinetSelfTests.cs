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
                var corner=cabinets.First(e=>(string)e.Attribute("id")=="F04");
                var cornerParts=CabinetTypeEngine.Parts(corner).ToDictionary(e=>(string)e.Attribute("id"));
                Func<XElement,string,float> dimension=(e,key)=>float.Parse((string)e.Attribute(key),
                    System.Globalization.CultureInfo.InvariantCulture);
                float innerX=dimension(corner,"leftLength")-dimension(corner,"leftDepth");
                float innerZ=dimension(corner,"rightLength")-dimension(corner,"rightDepth");
                check(cornerParts.ContainsKey("BASE")&&cornerParts["BASE"].Element("outline")!=null,"polygonal L base");
                check(Math.Abs(dimension(cornerParts["R1"],"x")-(innerX-50))<.01f,"R1 at inner corner");
                check(Math.Abs(dimension(cornerParts["R2"],"z")-(innerZ+50))<.01f,"R2 at inner corner");
                check(Math.Abs(dimension(cornerParts["LEFT"],"x")-
                    (dimension(corner,"leftLength")-dimension(corner,"boardThickness")/2))<.01f,"left end panel");
                check(Math.Abs(dimension(cornerParts["RIGHT"],"z")-
                    (dimension(corner,"rightLength")-dimension(corner,"boardThickness")/2))<.01f,"right end panel");
                var finished=PolygonCutGeometry.Finished(cornerParts["BASE"]);
                var cut=PolygonCutGeometry.Raw(cornerParts["BASE"]);
                check(finished.Count==6&&cut.Count==6,"six-sided banded L polygon");
                check(Math.Abs(finished.Max(p=>p.x)-finished.Min(p=>p.x)-950)<.01f,"950 mm finished left arm");
                check(Math.Abs(finished.Max(p=>p.y)-finished.Min(p=>p.y)-950)<.01f,"950 mm finished right arm");
                check(Math.Abs(dimension(cornerParts["LEFT"],"y")+
                    dimension(cornerParts["LEFT"],"height")/2-860)<.01f,"finished 860 mm cabinet height");
                check(Math.Abs(dimension(cornerParts["S2"],"depth")-372)<.01f,"S2 finished 372 mm");
                check(Math.Abs(dimension(cornerParts["RIGHT"],"x")-dimension(corner,"rightDepth")/2)<.01f,"right panel at outer wing end");
                check(Math.Abs(dimension(cornerParts["LEFT"],"z")-(dimension(corner,"rightLength")-dimension(corner,"leftDepth")/2))<.01f,"left panel at outer wing end");
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
