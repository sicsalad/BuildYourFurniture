using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using UnityEngine;

namespace BuildYourFurniture
{
    public static class CabinetManufacturing
    {
        static string S(XElement e,string key,string fallback="")=>e==null?fallback:(string)e.Attribute(key)??fallback;
        static float N(XElement e,string key,float fallback=0)
        {float n;return e!=null&&float.TryParse(S(e,key),NumberStyles.Float,CultureInfo.InvariantCulture,out n)?n:fallback;}
        static string F(float n)=>n.ToString("0.###",CultureInfo.InvariantCulture);
        static string Escape(string value)=>"\""+(value??"").Replace("\"","\"\"")+"\"";
        public static string Csv(XDocument plan)
        {
            var lines=new List<string>{"CabinetID;ElementID;MaterialID;Visibility;FinishedWidth_mm;FinishedHeight_mm;FinishedDepth_mm;CutWidth_mm;CutHeight_mm;CutDepth_mm;EdgeLeft_mm;EdgeRight_mm;EdgeTop_mm;EdgeBottom_mm;EdgeFront_mm;EdgeBack_mm;Quantity"};
            var cabinets=plan.Root.Element("cabinetry")?.Elements("cabinet")??Enumerable.Empty<XElement>();
            foreach(var cabinet in cabinets)
            {
                string type=S(cabinet,"typeId",S(cabinet,"typeRef",S(cabinet,"type")));
                foreach(var part in CabinetTypeEngine.Parts(cabinet))
                {
                    var raw=CabinetTypeEngine.CutSize(part,new Dictionary<string,float>());
                    var fields=new[]{S(cabinet,"id"),S(part,"id"),S(part,"materialId"),S(part,"visibility","internal"),
                        S(part,"width"),S(part,"height"),S(part,"depth"),F(raw.x),F(raw.y),F(raw.z),
                        S(part,"edgeLeft","0"),S(part,"edgeRight","0"),S(part,"edgeTop","0"),S(part,"edgeBottom","0"),S(part,"edgeFront","0"),S(part,"edgeBack","0"),"1"};
                    lines.Add(string.Join(";",fields.Select(Escape).ToArray()));
                }
            }
            return "\uFEFF"+string.Join("\r\n",lines.ToArray());
        }
        public static List<string> Validate(XDocument plan)
        {
            var errors=new List<string>();
            if(plan?.Root==null){errors.Add("Missing plan");return errors;}
            var cabinets=plan.Root.Element("cabinetry")?.Elements("cabinet").ToList()??new List<XElement>();
            try{CabinetPlacementEngine.Resolve(plan);}catch(Exception ex){errors.Add("Placement: "+ex.Message);}
            foreach(var cabinet in cabinets)
            {
                string id=S(cabinet,"id");
                try
                {
                    string type=S(cabinet,"typeId",S(cabinet,"typeRef",S(cabinet,"type")));
                    var definition=CabinetTypeEngine.EffectiveType(type);
                    var vars=CabinetTypeEngine.Variables(definition,cabinet);
                    float height=N(cabinet,"height",860),plinth=N(cabinet,"plinth",vars.ContainsKey("plinth")?vars["plinth"]:0);
                    float board=N(cabinet,"boardThickness",vars.ContainsKey("boardThickness")?vars["boardThickness"]:18);
                    if(height<=plinth+board)errors.Add(id+": height <= plinth + board");
                    var parts=CabinetTypeEngine.Parts(cabinet).ToList();
                    foreach(var part in parts)
                    {
                        float w=N(part,"width"),h=N(part,"height"),d=N(part,"depth");
                        if(w<=0||h<=0||d<=0)errors.Add(id+"/"+S(part,"id")+": non-positive finished dimension");
                        var raw=CabinetTypeEngine.CutSize(part,new Dictionary<string,float>());
                        if(raw.x<=0||raw.y<=0||raw.z<=0)errors.Add(id+"/"+S(part,"id")+": edge bands exceed finished dimension");
                    }
                    var basePart=parts.FirstOrDefault(e=>S(e,"id")=="BASE");
                    if(type=="normal"&&basePart!=null)
                    {
                        float baseTop=N(basePart,"y")+N(basePart,"height")/2;
                        foreach(string side in new[]{"LEFT","RIGHT"})
                        {
                            var panel=parts.FirstOrDefault(e=>S(e,"id")==side);
                            if(panel==null){errors.Add(id+": missing "+side);continue;}
                            float bottom=N(panel,"y")-N(panel,"height")/2;
                            if(Math.Abs(bottom-baseTop)>.01f)errors.Add(id+"/"+side+": does not rest on base; gap="+F(bottom-baseTop)+" mm");
                        }
                        if(Math.Abs(N(basePart,"width")-N(cabinet,"width",600))>.01f)errors.Add(id+": base width does not match cabinet width");
                    }
                    float maxY=parts.Max(e=>N(e,"y")+N(e,"height")/2);
                    if(maxY>height+.01f)errors.Add(id+": elements exceed total height by "+F(maxY-height)+" mm");
                    if(type=="corner")
                    {
                        float left=N(cabinet,"leftLength",950),right=N(cabinet,"rightLength",950);
                        float ld=N(cabinet,"leftDepth",560),rd=N(cabinet,"rightDepth",590);
                        if(ld>=left||rd>=right)errors.Add(id+": corner wing depth must be less than wing length");
                    }
                }
                catch(Exception ex){errors.Add(id+": "+ex.Message);}
            }
            return errors;
        }
    }
}
