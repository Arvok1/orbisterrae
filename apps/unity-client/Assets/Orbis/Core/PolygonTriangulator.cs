using System.Collections.Generic;
using UnityEngine;

namespace Orbis.Core
{
    public static class PolygonTriangulator
    {
        public static int[] Triangulate(IReadOnlyList<Vector2> points)
        {
            var order=new List<int>();
            var area=0f;
            for(var index=0;index<points.Count;index++)
            {
                var next=points[(index+1)%points.Count];
                area+=points[index].x*next.y-next.x*points[index].y;
                order.Add(index);
            }
            if(area<0)order.Reverse();
            var result=new List<int>();
            var attempts=points.Count*points.Count;
            while(order.Count>2&&attempts-->0)
            {
                var found=false;
                for(var cursor=0;cursor<order.Count;cursor++)
                {
                    var before=order[(cursor+order.Count-1)%order.Count];
                    var current=order[cursor];var after=order[(cursor+1)%order.Count];
                    if(Cross(points[current]-points[before],points[after]-points[current])<=0)continue;
                    var occupied=false;
                    foreach(var candidate in order)
                    {
                        if(candidate==before||candidate==current||candidate==after)continue;
                        var point=points[candidate];
                        if(Cross(points[current]-points[before],point-points[before])>=0
                            &&Cross(points[after]-points[current],point-points[current])>=0
                            &&Cross(points[before]-points[after],point-points[after])>=0) { occupied=true;break; }
                    }
                    if(occupied)continue;
                    result.Add(before);result.Add(current);result.Add(after);order.RemoveAt(cursor);found=true;break;
                }
                if(!found) return System.Array.Empty<int>();
            }
            return result.ToArray();
        }
        static float Cross(Vector2 from,Vector2 to)=>from.x*to.y-from.y*to.x;
    }
}
