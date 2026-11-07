using System;
using System.Collections.Generic;
using UnityEngine;

namespace Orbis.World
{
    public static class UrbanKit
    {
        public static readonly string[] Roles = { "house", "field", "workshop", "forge", "warehouse", "market", "port",
            "shipyard", "temple", "barracks", "wall", "tower", "well", "granary", "smokehouse" };
        public static readonly Color[] Palette = { new(.48f,.41f,.29f), new(.21f,.12f,.075f), new(.3f,.18f,.12f), new(.3f,.39f,.19f) };

        public static Mesh Build(string role, int variant, int lod)
        {
            var shape = new Shape();
            var height = 4.5f + variant * .35f;
            if (role == "field")
            {
                shape.Box(new(0,.12f,0),new(9,.24f,9),3);
                for (var row = -3; row <= 3; row += lod == 0 ? 1 : 3) shape.Box(new(row,.38f,0),new(.25f,.5f,8),3);
            }
            else if (role == "well")
            {
                shape.Cylinder(new(0,.8f,0),2,1.6f,8,0);
                shape.Box(new(-2,2.4f,0),new(.3f,4,.3f),1);
                shape.Box(new(2,2.4f,0),new(.3f,4,.3f),1);
                shape.Roof(5,4,4.4f,1.6f,2);
            }
            else if (role == "wall")
            {
                shape.Box(new(0,3.5f,0),new(9,7,2.2f),0);
                for (var merlon = -4; merlon <= 4; merlon += 2) shape.Box(new(merlon,7.6f,0),new(1.2f,1.2f,2.4f),0);
            }
            else if (role == "tower")
            {
                shape.Cylinder(new(0,6,0),3.4f,12,8,0);
                shape.Cylinder(new(0,12,0),3.9f,.8f,8,0);
                for (var angle = 0; angle < 360; angle += 60)
                {
                    var direction = Quaternion.Euler(0,angle,0) * Vector3.forward;
                    shape.Box(direction * 3.4f + Vector3.up * 13,new(1.4f,1.4f,1.4f),0);
                }
            }
            else if (role == "port" || role == "shipyard")
            {
                shape.Box(new(0,.4f,0),new(9,.8f,9),1);
                for (var post = -4; post <= 4; post += 4) shape.Box(new(post,1,3),new(.6f,2,.6f),1);
                if (role == "shipyard")
                {
                    shape.Box(new(0,1.4f,0),new(3,1.4f,7),0);
                    shape.Box(new(-3,3,0),new(.5f,6,.5f),1);
                    shape.Box(new(3,3,0),new(.5f,6,.5f),1);
                    shape.Box(new(0,6,0),new(7,.5f,.5f),1);
                }
            }
            else
            {
                var stone = role == "temple" || role == "barracks" || role == "forge";
                shape.Box(new(0,height/2,0),new(8,height,8),0);
                shape.Roof(9,9,height,2.6f,2);
                shape.Box(new(0,1.6f,-4.04f),new(1.5f,3.2f,.12f),1);
                if (lod == 0)
                {
                    foreach (var side in new[] { -3.7f, 3.7f })
                    {
                        shape.Box(new(side,height/2,-4.12f),new(.3f,height,.3f),1);
                        shape.Box(new(side,height/2,4.12f),new(.3f,height,.3f),1);
                    }
                    shape.Box(new(0,height-.3f,-4.12f),new(8,.35f,.3f),1);
                    shape.Box(new(-2,2.7f,-4.15f),new(1.2f,1.4f,.16f),1);
                    shape.Box(new(2,2.7f,-4.15f),new(1.2f,1.4f,.16f),1);
                }
                if (role == "forge" || role == "smokehouse") shape.Box(new(2.5f,height+1,2),new(1.2f,5,1.2f),0);
                if (role == "workshop")
                {
                    shape.Box(new(0,1.6f,-5),new(5,.4f,1.5f),1);
                    shape.Box(new(-3,1.2f,-5),new(1.2f,2.4f,1.2f),1);
                }
                if (role == "market")
                {
                    shape.Roof(8,4,2.7f,1,3,0,-6);
                    for (var side = -3; side <= 3; side += 6) shape.Box(new(side,1.5f,-6),new(.25f,3,.25f),1);
                }
                if (role == "temple")
                {
                    shape.Box(new(0,6.5f,2),new(2.8f,8,2.8f),0);
                    shape.Roof(3.5f,3.5f,10.5f,2,2,0,2);
                    shape.Box(new(0,.25f,-5),new(5,.5f,2),0);
                }
                if (role == "barracks") shape.Box(new(3,8,-3),new(.2f,6,.2f),1);
                if (role == "warehouse" || role == "granary")
                    for (var crate = -3; crate <= 3; crate += 3) shape.Box(new(crate,.8f,-5),new(1.5f,1.6f,1.5f),1);
                if (stone && lod == 0)
                    for (var row = 1; row <= 3; row++) shape.Box(new(0,row*1.15f,-4.1f),new(7.8f,.08f,.08f),1);
            }
            if (lod == 2)
            {
                shape = new Shape();
                shape.Box(new(0,1.5f,0),new(7,3,7),role == "field" ? 3 : 0);
                if (role != "field") shape.Roof(8,8,3,2,2);
            }
            return shape.Mesh($"{role}-{variant}-lod{lod}");
        }

        public static Mesh Construction(int lod)
        {
            var shape = new Shape();
            shape.Box(new(0,.25f,0),new(9,.5f,9),0);
            for (var side = -4; side <= 4; side += 8)
            {
                shape.Box(new(side,2.5f,-4),new(.3f,5,.3f),1);
                shape.Box(new(side,2.5f,4),new(.3f,5,.3f),1);
                shape.Box(new(0,3.5f,side),new(8,.3f,.3f),1);
            }
            if (lod == 0) shape.Box(new(0,.8f,0),new(3,1.6f,3),1);
            return shape.Mesh("construction-lod"+lod);
        }

        sealed class Shape
        {
            readonly List<Vector3> vertices = new();
            readonly List<int>[] triangles = { new(),new(),new(),new() };
            public void Box(Vector3 center, Vector3 size, int material)
            {
                var half = size / 2;
                var start = vertices.Count;
                foreach (var point in new[] { new Vector3(-1,-1,-1),new Vector3(1,-1,-1),new Vector3(1,1,-1),new Vector3(-1,1,-1),
                    new Vector3(-1,-1,1),new Vector3(1,-1,1),new Vector3(1,1,1),new Vector3(-1,1,1) }) vertices.Add(center + Vector3.Scale(point,half));
                foreach (var index in new[] { 0,2,1,0,3,2,4,5,6,4,6,7,0,1,5,0,5,4,3,7,6,3,6,2,0,4,7,0,7,3,1,2,6,1,6,5 }) triangles[material].Add(start+index);
            }
            public void Roof(float width, float depth, float bottom, float height, int material, float offsetX=0, float offsetZ=0)
            {
                var start=vertices.Count;
                foreach (var point in new[] { new Vector3(-width/2,bottom,-depth/2),new Vector3(width/2,bottom,-depth/2),new Vector3(0,bottom+height,-depth/2),
                    new Vector3(-width/2,bottom,depth/2),new Vector3(width/2,bottom,depth/2),new Vector3(0,bottom+height,depth/2) }) vertices.Add(point+new Vector3(offsetX,0,offsetZ));
                foreach (var index in new[] { 0,2,1,3,4,5,0,3,5,0,5,2,1,2,5,1,5,4,0,1,4,0,4,3 }) triangles[material].Add(start+index);
            }
            public void Cylinder(Vector3 center,float radius,float height,int sides,int material)
            {
                var start=vertices.Count;
                for(var side=0;side<sides;side++)
                {
                    var angle=side*Mathf.PI*2/sides;
                    vertices.Add(center+new Vector3(Mathf.Cos(angle)*radius,-height/2,Mathf.Sin(angle)*radius));
                    vertices.Add(center+new Vector3(Mathf.Cos(angle)*radius,height/2,Mathf.Sin(angle)*radius));
                }
                for(var side=0;side<sides;side++)
                {
                    var current=side*2;var next=(side+1)%sides*2;
                    foreach(var index in new[] { current,current+1,next+1,current,next+1,next }) triangles[material].Add(start+index);
                    if(side>0&&side<sides-1) foreach(var index in new[] { 1,current+1,next+1,0,next,current }) triangles[material].Add(start+index);
                }
            }
            public Mesh Mesh(string label)
            {
                var mesh=new Mesh { name=label,subMeshCount=4 };
                mesh.SetVertices(vertices);
                for(var material=0;material<4;material++) mesh.SetTriangles(triangles[material],material);
                mesh.RecalculateNormals();mesh.RecalculateBounds();
                return mesh;
            }
        }
    }
}
