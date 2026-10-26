using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace BuildYourFurniture
{
    // Resolves all panel coordinates through a dependency graph, including forward references.
    public static class CabinetFormulaResolver
    {
        static readonly Regex References=new Regex(@"\$?[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)?",RegexOptions.Compiled);
        public static float Evaluate(CabinetDesignerDocument document,string expression,string currentKey=null)
        {
            var cache=new Dictionary<string,float>(StringComparer.OrdinalIgnoreCase);
            var visiting=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            return EvaluateExpression(document,expression,cache,visiting);
        }
        static float EvaluateExpression(CabinetDesignerDocument document,string expression,
            Dictionary<string,float> cache,HashSet<string> visiting)
        {
            string expanded=References.Replace(expression??"",m=>
            {
                string key=m.Value.TrimStart('$');
                return ResolveKey(document,key,cache,visiting).ToString("R",CultureInfo.InvariantCulture);
            });
            return CabinetTypeEngine.Evaluate(expanded,new Dictionary<string,float>());
        }
        static float ResolveKey(CabinetDesignerDocument document,string key,
            Dictionary<string,float> cache,HashSet<string> visiting)
        {
            float cached;if(cache.TryGetValue(key,out cached))return cached;
            if(!visiting.Add(key))throw new InvalidOperationException("Körkörös hivatkozás: "+string.Join(" → ",visiting.ToArray())+" → "+key);
            try
            {
                float result;
                var dimension=document.Dimensions.FirstOrDefault(d=>string.Equals(d.Id,key,StringComparison.OrdinalIgnoreCase));
                if(dimension!=null)result=dimension.EffectiveValue;
                else
                {
                    var suffix=key.EndsWith(".delta",StringComparison.OrdinalIgnoreCase);
                    if(suffix)
                    {
                        var id=key.Substring(0,key.Length-6);
                        var dim=document.Dimensions.FirstOrDefault(d=>string.Equals(d.Id,id,StringComparison.OrdinalIgnoreCase));
                        if(dim==null)throw new FormatException("Ismeretlen méret: "+key);
                        result=dim.Delta;
                    }
                    else if(key.EndsWith(".original",StringComparison.OrdinalIgnoreCase))
                    {
                        var id=key.Substring(0,key.Length-9);
                        var dim=document.Dimensions.FirstOrDefault(d=>string.Equals(d.Id,id,StringComparison.OrdinalIgnoreCase));
                        if(dim==null)throw new FormatException("Ismeretlen méret: "+key);
                        result=dim.Value;
                    }
                    else
                    {
                        var match=Regex.Match(key,@"^([A-Za-z_][A-Za-z0-9_]*)\.([A-Za-z_][A-Za-z0-9_]*)([XY])$",RegexOptions.IgnoreCase);
                        if(!match.Success)throw new FormatException("Ismeretlen hivatkozás: "+key);
                        var panel=document.Elements.FirstOrDefault(p=>string.Equals(p.Id,match.Groups[1].Value,StringComparison.OrdinalIgnoreCase));
                        if(panel==null)throw new FormatException("Ismeretlen bútorlap: "+match.Groups[1].Value);
                        var vertex=panel.Outline.FirstOrDefault(v=>string.Equals(v.Id,match.Groups[2].Value,StringComparison.OrdinalIgnoreCase));
                        if(vertex==null)throw new FormatException("Ismeretlen pont: "+key);
                        bool x=string.Equals(match.Groups[3].Value,"X",StringComparison.OrdinalIgnoreCase);
                        string formula=x?vertex.XFormula:vertex.YFormula;
                        result=string.IsNullOrWhiteSpace(formula)?(x?vertex.Point.x:vertex.Point.y):
                            EvaluateExpression(document,formula,cache,visiting);
                    }
                }
                cache[key]=result;return result;
            }
            finally{visiting.Remove(key);}
        }
        public static void Resolve(CabinetDesignerDocument document,CabinetDesignerDocument.PanelElement source,
            CabinetDesignerDocument.PanelElement target)
        {
            var cache=new Dictionary<string,float>(StringComparer.OrdinalIgnoreCase);
            var visiting=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for(int i=0;i<source.Outline.Count;i++)
            {
                var v=source.Outline[i];
                string x=source.Id+"."+v.Id+"X",y=source.Id+"."+v.Id+"Y";
                target.Outline[i].Point=new UnityEngine.Vector2(
                    ResolveKey(document,x,cache,visiting),ResolveKey(document,y,cache,visiting));
            }
        }
    }
}