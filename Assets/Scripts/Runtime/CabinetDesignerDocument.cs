using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using UnityEngine;

namespace BuildYourFurniture
{
    // Independent editable cabinet format. Values are millimetres; positions are local to the cabinet.
    public sealed class CabinetDesignerDocument
    {
        public string Id=Guid.NewGuid().ToString("N");
        public string Name="Új szekrény";
        public readonly List<Dimension> Dimensions=new List<Dimension>();
        public readonly List<PaletteColor> Colors=new List<PaletteColor>();
        public readonly List<MaterialType> Materials=new List<MaterialType>();
        public readonly List<PanelElement> Elements=new List<PanelElement>();
        public sealed class Dimension { public string Id,Name; public float Value; }
        public sealed class PaletteColor { public string Id,Name,Hex; }
        public sealed class MaterialType { public string Id,Name,ColorId,Texture; }
        public sealed class Vertex { public string Id; public Vector2 Point; public string XFormula="",YFormula=""; }
        public sealed class PanelElement
        {
            public string Id=Guid.NewGuid().ToString("N"),Name="Bútorlap",Kind="panel",MaterialId="body";
            public Vector3 Position,Rotation;
            public float Thickness=18;
            public readonly List<Vertex> Outline=new List<Vertex>();
            public readonly Dictionary<string,float> Bands=new Dictionary<string,float>();
            public static PanelElement Rectangle(float width=600,float depth=400)
            {
                var p=new PanelElement();
                p.Outline.Add(new Vertex{Id="P1",Point=new Vector2(0,0)});
                p.Outline.Add(new Vertex{Id="P2",Point=new Vector2(width,0)});
                p.Outline.Add(new Vertex{Id="P3",Point=new Vector2(width,depth)});
                p.Outline.Add(new Vertex{Id="P4",Point=new Vector2(0,depth)});
                return p;
            }
            // Six-point L panel, clockwise around the finished outside contour.
            public static PanelElement LShape(float width=950,float depth=950,float armX=560,float armY=590)
            {
                if(width<=0||depth<=0||armX<=0||armY<=0||armX>=width||armY>=depth)
                    throw new ArgumentOutOfRangeException("Invalid L panel dimensions");
                var p=new PanelElement();
                p.Outline.Add(new Vertex{Id="P1",Point=new Vector2(0,0)});
                p.Outline.Add(new Vertex{Id="P2",Point=new Vector2(width,0)});
                p.Outline.Add(new Vertex{Id="P3",Point=new Vector2(width,armY)});
                p.Outline.Add(new Vertex{Id="P4",Point=new Vector2(armX,armY)});
                p.Outline.Add(new Vertex{Id="P5",Point=new Vector2(armX,depth)});
                p.Outline.Add(new Vertex{Id="P6",Point=new Vector2(0,depth)});
                return p;
            }
            public PanelElement Clone()
            {
                var p=new PanelElement{Id=Guid.NewGuid().ToString("N"),Name=Name+" másolat",Kind=Kind,
                    MaterialId=MaterialId,Position=Position+new Vector3(30,0,30),Rotation=Rotation,Thickness=Thickness};
                foreach(var v in Outline)p.Outline.Add(new Vertex{Id=v.Id,Point=v.Point,XFormula=v.XFormula,YFormula=v.YFormula});
                foreach(var b in Bands)p.Bands[b.Key]=b.Value;
                return p;
            }
        }
        static string F(float value)=>value.ToString("R",CultureInfo.InvariantCulture);
        static float N(XElement x,string name,float fallback=0)
        {
            float value;return float.TryParse((string)x?.Attribute(name),NumberStyles.Float,CultureInfo.InvariantCulture,out value)?value:fallback;
        }
        static string S(XElement x,string name,string fallback="")=>(string)x?.Attribute(name)??fallback;
        public static CabinetDesignerDocument New()
        {
            var d=new CabinetDesignerDocument();
            d.Dimensions.Add(new Dimension{Id="width",Name="Szélesség",Value=600});
            d.Dimensions.Add(new Dimension{Id="height",Name="Magasság",Value=860});
            d.Dimensions.Add(new Dimension{Id="depth",Name="Mélység",Value=400});
            d.Colors.Add(new PaletteColor{Id="bodyColor",Name="Korpusz",Hex="#BFA17A"});
            d.Materials.Add(new MaterialType{Id="body",Name="18 mm bútorlap",ColorId="bodyColor",Texture=""});
            return d;
        }
        public XDocument ToXml()
        {
            var root=new XElement("cabinetDesign",
                new XAttribute("version",1),
                new XAttribute("id",Id),
                new XAttribute("name",Name),
                new XAttribute("unit","mm"),
                new XAttribute("dimensionMode","finished"));
            var dimensions=new XElement("dimensions");
            foreach(var d in Dimensions)
                dimensions.Add(new XElement("dimension",new XAttribute("id",d.Id),
                    new XAttribute("name",d.Name),new XAttribute("value",F(d.Value))));
            root.Add(dimensions);
            var colors=new XElement("colors");
            foreach(var color in Colors)
                colors.Add(new XElement("color",new XAttribute("id",color.Id),
                    new XAttribute("name",color.Name),new XAttribute("hex",color.Hex)));
            root.Add(colors);
            var materials=new XElement("materials");
            foreach(var material in Materials)
                materials.Add(new XElement("material",new XAttribute("id",material.Id),
                    new XAttribute("name",material.Name),new XAttribute("colorId",material.ColorId),
                    new XAttribute("texture",material.Texture??"")));
            root.Add(materials);
            var elements=new XElement("elements");
            foreach(var panel in Elements)
            {
                var element=new XElement("element",new XAttribute("id",panel.Id),
                    new XAttribute("name",panel.Name),new XAttribute("kind",panel.Kind),
                    new XAttribute("materialId",panel.MaterialId),
                    new XAttribute("x",F(panel.Position.x)),new XAttribute("y",F(panel.Position.y)),
                    new XAttribute("z",F(panel.Position.z)),
                    new XAttribute("rx",F(panel.Rotation.x)),new XAttribute("ry",F(panel.Rotation.y)),
                    new XAttribute("rz",F(panel.Rotation.z)),
                    new XAttribute("thickness",F(panel.Thickness)));
                var outline=new XElement("outline");
                foreach(var vertex in panel.Outline)
                    outline.Add(new XElement("point",new XAttribute("id",vertex.Id),
                        new XAttribute("x",F(vertex.Point.x)),new XAttribute("y",F(vertex.Point.y)),
                        new XAttribute("xFormula",vertex.XFormula??""),
                        new XAttribute("yFormula",vertex.YFormula??"")));
                element.Add(outline);
                var edges=new XElement("edges");
                for(int i=0;i<panel.Outline.Count;i++)
                {
                    string from=panel.Outline[i].Id;
                    float band=panel.Bands.ContainsKey(from)?panel.Bands[from]:0;
                    edges.Add(new XElement("edge",new XAttribute("from",from),
                        new XAttribute("to",panel.Outline[(i+1)%panel.Outline.Count].Id),
                        new XAttribute("band",F(band))));
                }
                element.Add(edges);
                elements.Add(element);
            }
            root.Add(elements);
            return new XDocument(root);
        }
        public static CabinetDesignerDocument FromXml(XDocument xml)
        {
            var root=xml.Root;
            if(root==null||root.Name!="cabinetDesign")throw new InvalidDataException("Unknown cabinet designer XML");
            var d=new CabinetDesignerDocument{Id=S(root,"id"),Name=S(root,"name")};
            foreach(var x in root.Element("dimensions")?.Elements("dimension")??Enumerable.Empty<XElement>())
                d.Dimensions.Add(new Dimension{Id=S(x,"id"),Name=S(x,"name"),Value=N(x,"value")});
            foreach(var x in root.Element("colors")?.Elements("color")??Enumerable.Empty<XElement>())
                d.Colors.Add(new PaletteColor{Id=S(x,"id"),Name=S(x,"name"),Hex=S(x,"hex")});
            foreach(var x in root.Element("materials")?.Elements("material")??Enumerable.Empty<XElement>())
                d.Materials.Add(new MaterialType{Id=S(x,"id"),Name=S(x,"name"),ColorId=S(x,"colorId"),Texture=S(x,"texture")});
            foreach(var x in root.Element("elements")?.Elements("element")??Enumerable.Empty<XElement>())
            {
                var p=new PanelElement{Id=S(x,"id"),Name=S(x,"name"),Kind=S(x,"kind","panel"),
                    MaterialId=S(x,"materialId","body"),Thickness=N(x,"thickness",18),
                    Position=new Vector3(N(x,"x"),N(x,"y"),N(x,"z")),Rotation=new Vector3(N(x,"rx"),N(x,"ry"),N(x,"rz"))};
                foreach(var v in x.Element("outline")?.Elements("point")??Enumerable.Empty<XElement>())
                    p.Outline.Add(new Vertex{Id=S(v,"id"),Point=new Vector2(N(v,"x"),N(v,"y")),XFormula=S(v,"xFormula"),YFormula=S(v,"yFormula")});
                foreach(var e in x.Element("edges")?.Elements("edge")??Enumerable.Empty<XElement>())
                    p.Bands[S(e,"from")]=N(e,"band");
                d.Elements.Add(p);
            }
            return d;
        }
        public static string Folder=>Path.Combine(Application.persistentDataPath,"XML","CabinetDesigns");
        public string FilePath=>Path.Combine(Folder,Id+".xml");
        public void Save()
        {
            Directory.CreateDirectory(Folder);
            if(string.IsNullOrEmpty(Id)||Id.IndexOfAny(Path.GetInvalidFileNameChars())>=0)throw new InvalidDataException("Invalid cabinet ID");
            ToXml().Save(FilePath);

        }
        public static CabinetDesignerDocument Load(string path)=>FromXml(XDocument.Load(path));
        public static string[] Files()=>Directory.Exists(Folder)?Directory.GetFiles(Folder,"*.xml"):new string[0];
        public static void Delete(CabinetDesignerDocument d)
        {
            if(File.Exists(d.FilePath))File.Delete(d.FilePath);

        }
        // Resolve expressions against named cabinet dimensions before preview and cut calculations.
        // Formula syntax: $width, $depth, arithmetic and parentheses.
        public PanelElement Resolve(PanelElement source)
        {
            var result=source.Clone();
            result.Id=source.Id;result.Name=source.Name;result.Position=source.Position;
            var vars=Dimensions.ToDictionary(d=>d.Id,d=>d.Value,StringComparer.Ordinal);
            for(int i=0;i<result.Outline.Count;i++)
            {
                var vertex=result.Outline[i];
                if(!string.IsNullOrWhiteSpace(vertex.XFormula))
                    vertex.Point.x=CabinetTypeEngine.Evaluate(vertex.XFormula,vars);
                if(!string.IsNullOrWhiteSpace(vertex.YFormula))
                    vertex.Point.y=CabinetTypeEngine.Evaluate(vertex.YFormula,vars);
            }
            return result;
        }
        public static Vector2[] RawOutline(PanelElement p)
        {
            if(p.Outline.Count<3)throw new InvalidDataException("At least three points are required");
            var points=p.Outline.Select(v=>v.Point).ToArray();
            float area=0;
            for(int i=0;i<points.Length;i++)area+=points[i].x*points[(i+1)%points.Length].y-points[(i+1)%points.Length].x*points[i].y;
            if(Mathf.Abs(area)<.001f)throw new InvalidDataException("Invalid polygon area");
            var shifted=new Vector2[points.Length];
            var dirs=new Vector2[points.Length];
            for(int i=0;i<points.Length;i++)
            {
                var direction=(points[(i+1)%points.Length]-points[i]).normalized;
                if(direction.sqrMagnitude<.5f)throw new InvalidDataException("Duplicate polygon vertices");
                var inward=area>0?new Vector2(-direction.y,direction.x):new Vector2(direction.y,-direction.x);
                float band=p.Bands.ContainsKey(p.Outline[i].Id)?p.Bands[p.Outline[i].Id]:0;
                if(band<0)throw new InvalidDataException("Negative edge band");
                shifted[i]=points[i]+inward*band;
                dirs[i]=direction;
            }
            var raw=new Vector2[points.Length];
            for(int i=0;i<points.Length;i++)
            {
                int j=(i+points.Length-1)%points.Length;
                float det=dirs[j].x*dirs[i].y-dirs[j].y*dirs[i].x;
                if(Mathf.Abs(det)<.00001f)throw new InvalidDataException("Collinear adjacent polygon edges");
                Vector2 delta=shifted[i]-shifted[j];
                float t=(delta.x*dirs[i].y-delta.y*dirs[i].x)/det;
                raw[i]=shifted[j]+dirs[j]*t;
            }
            return raw;
        }
    }
}