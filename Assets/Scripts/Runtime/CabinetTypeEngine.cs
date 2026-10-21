using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using UnityEngine;

namespace BuildYourFurniture
{
    // Reads type XML from Resources/CabinetTypes and evaluates simple arithmetic formulas.
    // Geometry is built from finished (edge-banded) dimensions; raw cut sizes are computed separately.
    public static class CabinetTypeEngine
    {
        static string S(XElement e,string k,string fallback="")=>e==null?fallback:(string)e.Attribute(k)??fallback;
        static float Num(string value,float fallback=0){float n;return float.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out n)?n:fallback;}
        sealed class Expression
        {
            readonly string source;readonly Dictionary<string,float> vars;int i;
            public Expression(string s,Dictionary<string,float> v){source=s??"0";vars=v;}
            void Space(){while(i<source.Length&&char.IsWhiteSpace(source[i]))i++;}
            float Primary()
            {
                Space();if(i>=source.Length)throw new FormatException("Incomplete expression: "+source);
                if(source[i]=='+'||source[i]=='-'){char sign=source[i++];float v=Primary();return sign=='-'?-v:v;}
                if(source[i]=='('){i++;float n=Sum();Space();if(i>=source.Length||source[i++]!=')')throw new FormatException(source);return n;}
                if(source[i]=='$'){i++;int start=i;while(i<source.Length&&(char.IsLetterOrDigit(source[i])||source[i]=='_'))i++;string key=source.Substring(start,i-start);float value;if(!vars.TryGetValue(key,out value))throw new FormatException("Unknown variable "+key);return value;}
                int begin=i;while(i<source.Length&&(char.IsDigit(source[i])||source[i]=='.'))i++;
                if(begin==i)throw new FormatException("Unexpected expression "+source.Substring(i));
                return Num(source.Substring(begin,i-begin));
            }
            float Product(){float n=Primary();while(true){Space();if(i>=source.Length||source[i]!='*'&&source[i]!='/')return n;char op=source[i++];float v=Primary();n=op=='*'?n*v:n/v;}}
            float Sum(){float n=Product();while(true){Space();if(i>=source.Length||source[i]!='+'&&source[i]!='-')return n;char op=source[i++];float v=Product();n=op=='+'?n+v:n-v;}}
            public float Eval(){float n=Sum();Space();if(i!=source.Length)throw new FormatException(source);return n;}
        }
        public static float Evaluate(string formula,Dictionary<string,float> variables)=>new Expression(formula,variables).Eval();
        public static XDocument Load(string id)
        {
            if(string.IsNullOrEmpty(id)||id.IndexOfAny(new[]{'/','\\','.'})>=0)throw new ArgumentException("Invalid cabinet type id");
            var asset=Resources.Load<TextAsset>("CabinetTypes/"+id);
            if(!asset)throw new InvalidOperationException("Missing cabinet type XML: "+id);
            return XDocument.Parse(asset.text);
        }
        public static Dictionary<string,float> Variables(XElement type,XElement cabinet)
        {
            var values=new Dictionary<string,float>(StringComparer.Ordinal);
            foreach(var parameter in type.Element("parameters")?.Elements("param")??Enumerable.Empty<XElement>())
                values[S(parameter,"id")]=Num(S(parameter,"default"));
            foreach(var attribute in cabinet.Attributes())
                if(values.ContainsKey(attribute.Name.LocalName))
                    values[attribute.Name.LocalName]=Num(attribute.Value,values[attribute.Name.LocalName]);
            return values;
        }
        public static void Build(XElement cabinet,XElement defaults,Action<string,Vector3,Vector3,Color,float> cube)
        {
            string typeId=S(cabinet,"typeId",S(cabinet,"type","normal"));
            var type=Load(typeId).Root;
            var vars=Variables(type,cabinet);
            foreach(var a in defaults.Attributes())
                if(!vars.ContainsKey(a.Name.LocalName))vars[a.Name.LocalName]=Num(a.Value);
            float angle=Num(S(cabinet,"rotation"));
            float rad=angle*Mathf.Deg2Rad;
            float ox=Num(S(cabinet,"x"))*.001f,oz=Num(S(cabinet,"y"))*.001f;
            var materials=type.Element("materials")?.Elements("material").ToDictionary(m=>S(m,"id"))??new Dictionary<string,XElement>();
            var overrides=cabinet.Element("elements")?.Elements("element").ToDictionary(m=>S(m,"id"))??new Dictionary<string,XElement>();
            foreach(var part in type.Element("elements")?.Elements("element")??Enumerable.Empty<XElement>())
            {
                string id=S(part,"id");
                XElement change;overrides.TryGetValue(id,out change);
                if(S(change,"enabled",S(part,"enabled","true"))=="false")continue;
                string condition=S(part,"enabledWhen");
                if(condition=="plinth>0"&&vars.ContainsKey("plinth")&&vars["plinth"]<=0)continue;
                string materialId=S(change,"materialId",S(part,"materialId"));
                XElement material;materials.TryGetValue(materialId,out material);
                Color color;
                string hex=S(change,"color",S(cabinet,"bodyColor",S(material,"color","#20385B")));
                if(!ColorUtility.TryParseHtmlString(hex,out color))color=Color.gray;
                Func<string,float> v=k=>Evaluate(S(change,k,S(part,k,"0")),vars)*.001f;
                float x=v("x"),y=v("y"),z=v("z");
                Vector3 position=new Vector3(ox+Mathf.Cos(rad)*x+Mathf.Sin(rad)*z,y,oz-Mathf.Sin(rad)*x+Mathf.Cos(rad)*z);
                Vector3 size=new Vector3(v("width"),v("height"),v("depth"));
                if(size.x<=0||size.y<=0||size.z<=0)throw new InvalidOperationException("Invalid element "+id+" in "+S(cabinet,"id"));
                cube(S(cabinet,"id")+"_"+id,position,size,color,angle);
            }
        }
        public static Vector3 CutSize(XElement element,Dictionary<string,float> variables)
        {
            float w=Evaluate(S(element,"width"),variables);
            float h=Evaluate(S(element,"height"),variables);
            float d=Evaluate(S(element,"depth"),variables);
            // Front/back edge banding reduces depth; left/right reduces width; top/bottom reduces height.
            w-=Num(S(element,"edgeLeft"))+Num(S(element,"edgeRight"));
            h-=Num(S(element,"edgeTop"))+Num(S(element,"edgeBottom"));
            d-=Num(S(element,"edgeFront"))+Num(S(element,"edgeBack"));
            return new Vector3(w,h,d);
        }
    }
}
