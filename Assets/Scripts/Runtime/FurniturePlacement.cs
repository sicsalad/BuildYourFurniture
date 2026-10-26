using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using UnityEngine;

namespace BuildYourFurniture
{
    // Plan coordinates and dimensions are millimetres; Unity coordinates are metres.
    public static class FurniturePlacement
    {
        public static readonly string[] Groups={
            "sofas","standingDesks","doubleBeds","tvStands","ledTvs",
            "rugs","dividerShelves","tables","mirroredWardrobes","decorations"
        };
        static float N(XElement e,string key,float fallback=0)
        {
            float v;return float.TryParse((string)e.Attribute(key),NumberStyles.Float,
                CultureInfo.InvariantCulture,out v)?v:fallback;
        }
        public static IEnumerable<XElement> Objects(XDocument plan)
        {
            if(plan?.Root==null)yield break;
            foreach(var name in Groups)
            {
                var group=plan.Root.Element(name);
                if(group==null)continue;
                foreach(var item in group.Elements().Where(e=>e.Attribute("id")!=null))yield return item;
            }
        }
        public static Vector2 Center(XElement item)=>new Vector2(N(item,"x")/1000f,N(item,"y")/1000f);
        public static Vector2 Size(XElement item)
        {
            float w=Mathf.Max(.05f,N(item,"width",1000)/1000f);
            float d=Mathf.Max(.05f,N(item,"depth",N(item,"length",600))/1000f);
            return new Vector2(w,d);
        }
        public static float Height(XElement item)
        {
            return Mathf.Max(.05f,N(item,"height",N(item,"headboardHeight",
                N(item,"frameHeight",N(item,"thickness",500))))/1000f);
        }
        public static Vector2[] Corners(XElement item)
        {
            Vector2 c=Center(item),s=Size(item)*.5f;
            float a=N(item,"rotation")*Mathf.Deg2Rad;
            float cos=Mathf.Cos(a),sin=Mathf.Sin(a);
            var local=new[]{new Vector2(-s.x,-s.y),new Vector2(s.x,-s.y),
                new Vector2(s.x,s.y),new Vector2(-s.x,s.y)};
            return local.Select(p=>c+new Vector2(cos*p.x+sin*p.y,-sin*p.x+cos*p.y)).ToArray();
        }
        public static bool Contains(Vector2 p,IList<Vector2> poly)
        {
            bool inside=false;
            for(int i=0,j=poly.Count-1;i<poly.Count;j=i++)
                if((poly[i].y>p.y)!=(poly[j].y>p.y)&&
                    p.x<(poly[j].x-poly[i].x)*(p.y-poly[i].y)/
                    (poly[j].y-poly[i].y)+poly[i].x)inside=!inside;
            return inside;
        }
        public static XElement RoomAt(XDocument plan,Vector2 p)
        {
            var rooms=plan?.Root?.Element("rooms");
            if(rooms==null)return null;
            return rooms.Elements("room").FirstOrDefault(room=>
            {
                var poly=room.Elements("point").Select(pt=>
                    new Vector2(N(pt,"x")/1000f,N(pt,"y")/1000f)).ToList();
                return poly.Count>=3&&Contains(p,poly);
            });
        }
        static bool Overlap(Vector2[] a,Vector2[] b)
        {
            foreach(var polygon in new[]{a,b})
                for(int i=0;i<polygon.Length;i++)
                {
                    Vector2 edge=polygon[(i+1)%polygon.Length]-polygon[i];
                    Vector2 axis=new Vector2(-edge.y,edge.x).normalized;
                    float amin=a.Min(p=>Vector2.Dot(p,axis)),amax=a.Max(p=>Vector2.Dot(p,axis));
                    float bmin=b.Min(p=>Vector2.Dot(p,axis)),bmax=b.Max(p=>Vector2.Dot(p,axis));
                    if(amax<=bmin+.001f||bmax<=amin+.001f)return false;
                }
            return true;
        }
        public static bool IsNonBlocking(XElement item)
        {
            if(item==null)return true;
            if(string.Equals((string)item.Attribute("collision"),"none",StringComparison.OrdinalIgnoreCase))return true;
            string type=item.Name.LocalName.ToLowerInvariant();
            return type=="rug"||type=="book"||type=="plant"||type=="flower"||
                type=="decoration"||type=="tv"||type=="tile"||type=="walltile";
        }
        static bool WithinRoom(XDocument plan,XElement item,out string reason)
        {
            reason="";
            var room=RoomAt(plan,Center(item));
            if(room==null){reason="A bútor középpontja nincs szobában.";return false;}
            var polygon=room.Elements("point").Select(pt=>
                new Vector2(N(pt,"x")/1000f,N(pt,"y")/1000f)).ToList();
            if(IsNonBlocking(item))return true;
            foreach(var corner in Corners(item))
                if(!Contains(corner,polygon))
                {reason="A bútor kilógna a szobából vagy falba ütközne.";return false;}
            return true;
        }
        public static bool HasCollision(XDocument plan,XElement item)
        {
            if(IsNonBlocking(item))return false;
            float bottom=N(item,"z")/1000f,top=bottom+Height(item);
            var corners=Corners(item);
            foreach(var other in Objects(plan))
            {
                if(ReferenceEquals(item,other)||IsNonBlocking(other))continue;
                float ob=N(other,"z")/1000f,ot=ob+Height(other);
                if(top<=ob+.001f||ot<=bottom+.001f)continue;
                if(Overlap(corners,Corners(other)))return true;
            }
            return false;
        }
        public static bool Valid(XDocument plan,XElement item,out string reason)
        {
            if(!WithinRoom(plan,item,out reason))return false;
            if(HasCollision(plan,item)){reason="A bútor egy másik elemmel ütközne.";return false;}
            reason="";return true;
        }
        public static bool TryChange(XDocument plan,XElement item,string key,float value,out string reason)
        {
            bool wasColliding=HasCollision(plan,item);
            string old=(string)item.Attribute(key);
            item.SetAttributeValue(key,value.ToString("0.###",CultureInfo.InvariantCulture));
            if(WithinRoom(plan,item,out reason)&&(!HasCollision(plan,item)||wasColliding))
            {reason="";return true;}
            if(string.IsNullOrEmpty(reason))reason="Új ütközés nem engedélyezett.";
            if(old==null)item.Attribute(key)?.Remove();else item.SetAttributeValue(key,old);
            return false;
        }
        public static bool TryMove(XDocument plan,XElement item,float x,float y,out string reason)
        {
            bool wasColliding=HasCollision(plan,item);
            string oldX=(string)item.Attribute("x"),oldY=(string)item.Attribute("y");
            item.SetAttributeValue("x",x.ToString("0.###",CultureInfo.InvariantCulture));
            item.SetAttributeValue("y",y.ToString("0.###",CultureInfo.InvariantCulture));
            if(WithinRoom(plan,item,out reason)&&(!HasCollision(plan,item)||wasColliding))
            {reason="";return true;}
            if(string.IsNullOrEmpty(reason))reason="Új ütközés nem engedélyezett.";
            item.SetAttributeValue("x",oldX);item.SetAttributeValue("y",oldY);
            return false;
        }
        public static bool PlaceNew(XDocument plan,XElement item,Vector2 preferred,out string reason)
        {
            var room=RoomAt(plan,preferred);
            if(room==null){reason="Válassz egy szobát.";return false;}
            var poly=room.Elements("point").Select(p=>
                new Vector2(N(p,"x")/1000f,N(p,"y")/1000f)).ToList();
            float minX=poly.Min(p=>p.x),maxX=poly.Max(p=>p.x);
            float minY=poly.Min(p=>p.y),maxY=poly.Max(p=>p.y);
            var candidates=new List<Vector2>{preferred};
            for(float y=minY+.2f;y<=maxY-.2f;y+=.15f)
                for(float x=minX+.2f;x<=maxX-.2f;x+=.15f)candidates.Add(new Vector2(x,y));
            foreach(var candidate in candidates.OrderBy(p=>(p-preferred).sqrMagnitude))
            {
                item.SetAttributeValue("x",Mathf.RoundToInt(candidate.x*1000));
                item.SetAttributeValue("y",Mathf.RoundToInt(candidate.y*1000));
                if(Valid(plan,item,out reason))
                {
                    item.SetAttributeValue("roomId",(string)room.Attribute("id"));
                    return true;
                }
            }
            reason="Nincs szabad hely ekkora bútornak ebben a szobában.";
            return false;
        }
    }
}