using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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
            string editable=Path.Combine(Application.persistentDataPath,"XML","CabinetTypes",id+".xml");
            if(File.Exists(editable))return XDocument.Load(editable);
            var asset=Resources.Load<TextAsset>("CabinetTypes/"+id);
            if(!asset)throw new InvalidOperationException("Missing cabinet type XML: "+id);
            return XDocument.Parse(asset.text);
        }
        public static XElement EffectiveType(string id)
        {
            return EffectiveType(id,new HashSet<string>(StringComparer.Ordinal));
        }
        static XElement EffectiveType(string id,HashSet<string> visiting)
        {
            if(!visiting.Add(id))throw new InvalidOperationException("Cyclic cabinet type inheritance: "+id);
            XElement child=Load(id).Root;
            var inheritance=child.Element("inherits");
            if(inheritance==null){visiting.Remove(id);return child;}
            var parent=EffectiveType(S(inheritance,"typeId"),visiting);
            foreach(string group in new[]{"parameters","materials","elements","slots"})
            {
                var target=parent.Element(group);
                if(target==null){target=new XElement(group);parent.Add(target);}
                var source=child.Element(group);
                if(source!=null)foreach(var element in source.Elements())
                {
                    var existing=target.Elements().FirstOrDefault(e=>e.Name==element.Name&&S(e,"id")==S(element,"id"));
                    if(existing==null)target.Add(new XElement(element));
                    else foreach(var attribute in element.Attributes())existing.SetAttributeValue(attribute.Name,attribute.Value);
                }
            }
            var overrides=child.Element("overrides");
            if(overrides!=null)foreach(var change in overrides.Elements("element"))
            {
                var target=parent.Element("elements").Elements("element").FirstOrDefault(e=>S(e,"id")==S(change,"id"));
                if(target==null)throw new InvalidOperationException("Unknown inherited element "+S(change,"id"));
                foreach(var attribute in change.Attributes())target.SetAttributeValue(attribute.Name,attribute.Value);
            }
            parent.SetAttributeValue("id",id);
            visiting.Remove(id);return parent;
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
        public static void Build(XElement cabinet,XElement defaults,Action<string,Vector3,Vector3,Color,float> cube, Action<GameObject> register=null)
        {
            string typeId=S(cabinet,"typeId",S(cabinet,"typeRef",S(cabinet,"type","normal")));
            var type=EffectiveType(typeId);
            var vars=Variables(type,cabinet);
            if(defaults!=null)foreach(var a in defaults.Attributes())
                if(!vars.ContainsKey(a.Name.LocalName))vars[a.Name.LocalName]=Num(a.Value);
            var centers=CabinetAssembly.Resolve(type,cabinet,vars);
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
                var resolved=centers[id];
                float x=resolved.x*.001f,y=resolved.y*.001f,z=resolved.z*.001f;
                Vector3 position=new Vector3(ox+Mathf.Cos(rad)*x+Mathf.Sin(rad)*z,y,oz-Mathf.Sin(rad)*x+Mathf.Cos(rad)*z);
                Vector3 size=new Vector3(v("width"),v("height"),v("depth"));
                if(size.x<=0||size.y<=0||size.z<=0)throw new InvalidOperationException("Invalid element "+id+" in "+S(cabinet,"id"));
                if(S(part,"kind")=="lShapePanel")
                {
                    if(register==null)throw new InvalidOperationException("L-shaped panel needs mesh registration");
                    var polygon=(change?.Element("outline")??part.Element("outline"));
                    if(polygon==null)throw new InvalidOperationException("Missing outline for "+id);
                    var outline=new List<Vector2>();
                    foreach(var vertex in polygon.Elements("point"))
                        outline.Add(new Vector2(
                            Evaluate(S(vertex,"x"),vars)*.001f,
                            Evaluate(S(vertex,"z"),vars)*.001f));
                    // Mesh coordinates are local to the part's XML origin.
                    // Its y coordinate denotes the underside, not the centre.
                    var mesh=LShapePanelMesh.Create(S(cabinet,"id")+"_"+id,outline,size.y,
                        position,angle,color);
                    register(mesh);
                }
                else cube(S(cabinet,"id")+"_"+id,position,size,color,angle);
            }
            if(typeId=="corner")BuildCornerDrawer(cabinet,cube);
        }
        // Optional drawer front belonging to the corner instance, using the right arm.
        // The full drawer box/runner assembly will be described in its own XML type.
        public static void BuildCornerDrawer(XElement cabinet,Action<string,Vector3,Vector3,Color,float> cube)
        {
            var drawer=cabinet.Element("drawer");
            if(drawer==null)return;
            float x=Num(S(cabinet,"x"))*.001f,z=Num(S(cabinet,"y"))*.001f;
            float angle=Num(S(cabinet,"rotation")),rad=angle*Mathf.Deg2Rad;
            float leftDepth=Num(S(cabinet,"leftDepth","560"))*.001f;
            float rightDepth=Num(S(cabinet,"rightDepth","590"))*.001f;
            float plinth=Num(S(cabinet,"plinth","80"))*.001f;
            float board=Num(S(cabinet,"boardThickness","18"))*.001f;
            float width=Num(S(drawer,"width","390"))*.001f;
            float height=Num(S(drawer,"height","780"))*.001f;
            float offset=Num(S(drawer,"offset"))*.001f;
            float lx=leftDepth-.014f,lz=rightDepth+offset+width*.5f;
            var position=new Vector3(x+Mathf.Cos(rad)*lx+Mathf.Sin(rad)*lz,
                plinth+board+height*.5f,z-Mathf.Sin(rad)*lx+Mathf.Cos(rad)*lz);
            Color color;
            if(!ColorUtility.TryParseHtmlString(S(drawer,"color",S(cabinet,"frontColor","#20385B")),out color))color=Color.blue;
            cube(S(cabinet,"id")+"_drawerFront",position,new Vector3(.018f,height-.002f,width-.002f),color,angle);
        }
        public static IEnumerable<XElement> Parts(XElement cabinet)
        {
            var type=EffectiveType(S(cabinet,"typeId",S(cabinet,"typeRef",S(cabinet,"type"))));
            var variables=Variables(type,cabinet);
            var centers=CabinetAssembly.Resolve(type,cabinet,variables);
            foreach(var element in type.Element("elements").Elements("element"))
            {
                var change=cabinet.Element("elements")?.Elements("element").FirstOrDefault(e=>S(e,"id")==S(element,"id"));
                if(S(change,"enabled",S(element,"enabled","true"))=="false")continue;
                if(S(element,"enabledWhen")=="plinth>0"&&variables["plinth"]<=0)continue;
                var copy=new XElement(element);
                if(change!=null)foreach(var a in change.Attributes())copy.SetAttributeValue(a.Name,a.Value);
                foreach(var key in new[]{"width","height","depth","x","y","z"})
                    copy.SetAttributeValue(key,Evaluate(S(copy,key,"0"),variables).ToString("0.###",CultureInfo.InvariantCulture));
                Vector3 center=centers[S(copy,"id")];
                copy.SetAttributeValue("x",center.x.ToString("0.###",CultureInfo.InvariantCulture));
                copy.SetAttributeValue("y",center.y.ToString("0.###",CultureInfo.InvariantCulture));
                copy.SetAttributeValue("z",center.z.ToString("0.###",CultureInfo.InvariantCulture));
                yield return copy;
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
