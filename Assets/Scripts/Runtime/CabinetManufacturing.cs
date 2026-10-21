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
            var lines=new List<string>{"CabinetID;ElementID;MaterialID;Visibility;FinishedWidth_mm;FinishedHeight_mm;FinishedDepth_mm;CutWidth_mm;CutHeight_mm;CutDepth_mm;EdgeLeft_mm;EdgeRight_mm;EdgeTop_mm;EdgeBottom_mm;EdgeFront_mm;EdgeBack_mm;Quantity;RawPolygonXZ_mm"};
            var cabinets=plan.Root.Element("cabinetry")?.Elements("cabinet")??Enumerable.Empty<XElement>();
            foreach(var cabinet in cabinets)
            {
                string type=S(cabinet,"typeId",S(cabinet,"typeRef",S(cabinet,"type")));
                foreach(var part in CabinetTypeEngine.Parts(cabinet))
                {
                    var raw=CabinetTypeEngine.CutSize(part,new Dictionary<string,float>());
                    var fields=new[]{S(cabinet,"id"),S(part,"id"),S(part,"materialId"),S(part,"visibility","internal"),
                        S(part,"width"),S(part,"height"),S(part,"depth"),F(raw.x),F(raw.y),F(raw.z),
                        S(part,"edgeLeft","0"),S(part,"edgeRight","0"),S(part,"edgeTop","0"),S(part,"edgeBottom","0"),S(part,"edgeFront","0"),S(part,"edgeBack","0"),"1",S(part,"kind")=="polygonPanel"?PolygonCutGeometry.RawOutline(part):""};
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
                        var basePanel=parts.FirstOrDefault(e=>S(e,"id")=="BASE");
                        if(basePanel==null)errors.Add(id+": missing L base");
                        else
                        {
                            var outline=PolygonCutGeometry.Finished(basePanel);
                            var rawOutline=PolygonCutGeometry.Raw(basePanel);
                            if(outline.Count!=6||rawOutline.Count!=6)errors.Add(id+": L base must have six finished and raw vertices");
                            float minX=outline.Min(p=>p.x),maxX=outline.Max(p=>p.x);
                            float minZ=outline.Min(p=>p.y),maxZ=outline.Max(p=>p.y);
                            if(Math.Abs(maxX-minX-left)>.01f||Math.Abs(maxZ-minZ-right)>.01f)
                                errors.Add(id+": finished L footprint differs from requested outside dimensions");
                            if(Math.Abs(N(basePanel,"height")-board)>.01f)errors.Add(id+": L base thickness differs from board thickness");
                        }
                        foreach(string sideId in new[]{"LEFT","RIGHT"})
                        {
                            var side=parts.FirstOrDefault(e=>S(e,"id")==sideId);
                            if(side==null){errors.Add(id+": missing "+sideId);continue;}
                            float bottom=N(side,"y")-N(side,"height")/2;
                            if(Math.Abs(bottom-(plinth+board))>.01f)errors.Add(id+"/"+sideId+": bottom not seated on L base");
                            float top=N(side,"y")+N(side,"height")/2;
                            if(Math.Abs(top-height)>.01f)errors.Add(id+"/"+sideId+": top not flush with cabinet top");
                        }
                        var byId=parts.ToDictionary(e=>S(e,"id"));
                        foreach(var joint in new[]{new[]{"R1","R2"},new[]{"R1","K1"},new[]{"R2","K2"},
                            new[]{"LEFT","K1"},new[]{"RIGHT","K2"},new[]{"LEFT","S1"},
                            new[]{"RIGHT","S2"},new[]{"S1","S2"}})
                        {
                            XElement a,b;
                            if(!byId.TryGetValue(joint[0],out a)||!byId.TryGetValue(joint[1],out b))continue;
                            float[] ac={N(a,"x"),N(a,"y"),N(a,"z")};
                            float[] bc={N(b,"x"),N(b,"y"),N(b,"z")};
                            float[] ad={N(a,"width"),N(a,"height"),N(a,"depth")};
                            float[] bd={N(b,"width"),N(b,"height"),N(b,"depth")};
                            int touching=0;bool separated=false,penetrating=true;
                            for(int axis=0;axis<3;axis++)
                            {
                                float overlap=(ad[axis]+bd[axis])/2-Math.Abs(ac[axis]-bc[axis]);
                                if(overlap<-.05f)separated=true;
                                if(Math.Abs(overlap)<=.05f)touching++;
                                if(overlap<=.05f)penetrating=false;
                            }
                            if(separated||touching==0)
                                errors.Add(id+": joint "+joint[0]+"/"+joint[1]+
                                    (separated?" has a gap":" overlaps without a mating face"));
                        }
                        foreach(var part in parts.Where(e=>S(e,"id")!="BASE"))
                        {
                            float x=N(part,"x"),z=N(part,"z"),w=N(part,"width"),d=N(part,"depth");
                            if(x-w/2<-.01f||x+w/2>left+.01f||z-d/2<-.01f||z+d/2>right+.01f)
                                errors.Add(id+"/"+S(part,"id")+": extends outside finished cabinet envelope");
                        }
                    }
                }
                catch(Exception ex){errors.Add(id+": "+ex.Message);}
            }
            return errors;
        }
    }
}
