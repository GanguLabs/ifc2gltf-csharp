﻿using SharpGLTF.Geometry;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Xbim.Common.Geometry;
using Xbim.Common.XbimExtensions;
using Xbim.Ifc;
using Xbim.Ifc4.Interfaces;
using Xbim.ModelGeometry.Scene;
using AlphaMode = SharpGLTF.Materials.AlphaMode;
using VERTEX = SharpGLTF.Geometry.VertexTypes.VertexPosition;

namespace ifc2gltf
{
    public static class GltfConverter
    {
        private static XbimShapeTriangulation GetMeshes (Xbim3DModelContext context, IIfcProduct product)
        {
            XbimShapeTriangulation ifcMesh = null;;
            
            var productShape =
                context.ShapeInstancesOf(product)
                    .Where(p => p.RepresentationType != XbimGeometryRepresentationType.OpeningsAndAdditionsExcluded)
                .Distinct();

            if (productShape.Any())
            {
                var shapeInstance = productShape.FirstOrDefault();
                var shapeGeometry = context.ShapeGeometry(shapeInstance.ShapeGeometryLabel);

                byte[] data = ((IXbimShapeGeometryData)shapeGeometry).ShapeData;

                //If you want to get all the faces and triangulation use this
                using (var stream = new MemoryStream(data))
                {
                    using (var reader = new BinaryReader(stream))
                    {
                        ifcMesh = reader.ReadShapeTriangulation();
                    }
                }
            }

            return ifcMesh;
        }

        public static SceneBuilder ToGltf(IfcStore model)
        {
            var scene = new SceneBuilder();
            var context = new Xbim3DModelContext(model);
            context.CreateContext();
              

            // Reference: https://stackoverflow.com/a/57042462/6908282

            List<XbimShapeGeometry> geometrys = context.ShapeGeometries().ToList();
            List<XbimShapeInstance> instances = context.ShapeInstances().ToList();

            List<XbimShapeTriangulation> allMeshesList = new List<XbimShapeTriangulation>();
            Dictionary<string, XbimShapeTriangulation> allMeshes = new Dictionary<string, XbimShapeTriangulation>();
            //Check all the instances
            foreach (var instance in instances)
            {
                var material = GetMaterial(model, instance);
                // var material = model.Instances.FirstOrDefault<IIfcElement>(d => d.GlobalId == instance._expressTypeId);
                // var materials = instance.HasAssociations.OfType<IIfcRelAssociatesMaterial>();
                XbimShapeGeometry geometry = context.ShapeGeometry(instance);   //Instance's geometry
                XbimRect3D box = geometry.BoundingBox; //bounding box you need
                XbimMatrix3D transformation = instance.Transformation;
                

                byte[] data = ((IXbimShapeGeometryData)geometry).ShapeData;

                //If you want to get all the faces and trinagulation use this
                using (var stream = new MemoryStream(data))
                {
                    using (var reader = new BinaryReader(stream))
                    {
                        var mesh = reader.ReadShapeTriangulation();
                        XbimShapeTriangulation transformedMesh = mesh.Transform(transformation);
                        List<XbimFaceTriangulation> faces = transformedMesh.Faces as List<XbimFaceTriangulation>;
                        List<XbimPoint3D> vertices = transformedMesh.Vertices as List<XbimPoint3D>;

                        allMeshes[instance.IfcTypeId.ToString()] = transformedMesh;
                        allMeshesList.Add(transformedMesh);

                        var glbMesh = GenerateMesh(transformedMesh, material);

                        scene.AddRigidMesh(glbMesh, Matrix4x4.Identity);
                    }
                }
            }

            return scene;
        }

        private static MaterialBuilder GetMaterial(IfcStore model, XbimShapeInstance instance)
        {
            //reference: https://stackoverflow.com/a/42852848/6908282
            var material = new MaterialBuilder()
               .WithDoubleSide(true)
               .WithMetallicRoughnessShader();
            var transform = instance.Transformation; //Transformation matrix (location point inside)
            var sStyle = model.Instances[instance.StyleLabel] as IIfcSurfaceStyle;
            if (sStyle != null)
            {
                var styleData = sStyle?.Styles.FirstOrDefault() as IIfcSurfaceStyleRendering;
                float transparency = (float)styleData.Transparency == 0 ? 1 : (float)styleData.Transparency;
                var color = new Vector4(
                    (float)styleData.SurfaceColour.Red,
                    (float)styleData.SurfaceColour.Green,
                    (float)styleData.SurfaceColour.Blue,
                    transparency
                    );

                material.WithBaseColor(color);
                if (transparency < 1 && transparency != 0) material.WithAlpha(AlphaMode.BLEND);
            }
            else
            {
                // material1.WithChannelParam("BaseColor", new Vector4(1, 0, 0, 1));
            }

            return material;
        }

        public static IMeshBuilder<MaterialBuilder> GenerateMesh(XbimShapeTriangulation meshBim, MaterialBuilder material1)
        {
            var mesh = new MeshBuilder<VERTEX>("mesh");
            var faces = (List<XbimFaceTriangulation>)meshBim.Faces;
            var vertices = (List<XbimPoint3D>)meshBim.Vertices;

            foreach (var face in meshBim.Faces)
            {
                var indeces = face.Indices;

                for (var triangle = 0; triangle < face.TriangleCount; triangle++)
                {
                    var start = triangle * 3;
                    var p0 = meshBim.Vertices[indeces[start]];
                    var p1 = meshBim.Vertices[indeces[start + 1]];
                    var p2 = meshBim.Vertices[indeces[start + 2]];

                    var prim = mesh.UsePrimitive(material1);
                    prim.AddTriangle(
                        new VERTEX((float)p0.X, (float)p0.Z, (float)p0.Y),
                        new VERTEX((float)p1.X, (float)p1.Z, (float)p1.Y),
                        new VERTEX((float)p2.X, (float)p2.Z, (float)p2.Y));
                }
            }

            return mesh;
        }


        public static List<XbimShapeTriangulation> AllMeshes(Xbim3DModelContext context)
        {
            // Reference: https://stackoverflow.com/a/57042462/6908282

            List<XbimShapeGeometry> geometrys = context.ShapeGeometries().ToList();
            List<XbimShapeInstance> instances = context.ShapeInstances().ToList();

            List<XbimShapeTriangulation> allMeshesList = new List<XbimShapeTriangulation>();
            Dictionary<string, XbimShapeTriangulation> allMeshes = new Dictionary<string, XbimShapeTriangulation>();
            //Check all the instances
            foreach (var instance in instances)
            {
                var transfor = instance.Transformation; //Transformation matrix (location point inside)

                XbimShapeGeometry geometry = context.ShapeGeometry(instance);   //Instance's geometry
                XbimRect3D box = geometry.BoundingBox; //bounding box you need
                XbimMatrix3D transformation = instance.Transformation;

                byte[] data = ((IXbimShapeGeometryData)geometry).ShapeData;

                //If you want to get all the faces and trinagulation use this
                using (var stream = new MemoryStream(data))
                {
                    using (var reader = new BinaryReader(stream))
                    {
                        var mesh = reader.ReadShapeTriangulation();

                        List<XbimFaceTriangulation> faces = mesh.Faces as List<XbimFaceTriangulation>;
                        List<XbimPoint3D> vertices = mesh.Vertices as List<XbimPoint3D>;

                        allMeshes[instance.IfcTypeId.ToString()] = mesh;
                        allMeshesList.Add(mesh);
                    }
                }
            }

            return allMeshesList;
        }


        public static SceneBuilder ToGltfOld(IfcStore model)
        {
            var context = new Xbim3DModelContext(model);
            context.CreateContext();

            List<XbimShapeTriangulation> getAllMeshes = AllMeshes(context);
            var ifcProject = model.Instances.OfType<IIfcProject>().FirstOrDefault();
            var ifcSite = model.Instances.OfType<IIfcSite>().FirstOrDefault();
            var ifcBuilding = ifcSite.Buildings.FirstOrDefault();
            var ifcStoreys = ifcBuilding.BuildingStoreys; // 3 in case of office

            var spaceMeshes = new List<XbimShapeTriangulation>();
            foreach (var storey in ifcStoreys)
            {
                var spaces = storey.Spaces;
                foreach (var space in spaces)
                {
                    var ifcmesh = GetMeshes(context, space);
                    spaceMeshes.Add(ifcmesh);
                }
            }

            var material1 = new MaterialBuilder()
               .WithDoubleSide(true)
               .WithMetallicRoughnessShader()
               .WithChannelParam("BaseColor", new Vector4(1, 0, 0, 1));

            var scene = new SceneBuilder();
            foreach (XbimShapeTriangulation meshBim in getAllMeshes)
            {
                var mesh = new MeshBuilder<VERTEX>("mesh");
                var faces = (List<XbimFaceTriangulation>)meshBim.Faces;
                var vertices = (List<XbimPoint3D>)meshBim.Vertices;

                foreach (var face in meshBim.Faces)
                {
                    var indeces = face.Indices;

                    for (var triangle = 0; triangle < face.TriangleCount; triangle++)
                    {
                        var start = triangle * 3;
                        var p0 = meshBim.Vertices[indeces[start]];
                        var p1 = meshBim.Vertices[indeces[start + 1]];
                        var p2 = meshBim.Vertices[indeces[start + 2]];

                        var prim = mesh.UsePrimitive(material1);
                        prim.AddTriangle(
                            new VERTEX((float)p0.X, (float)p0.Z, (float)p0.Y),
                            new VERTEX((float)p1.X, (float)p1.Z, (float)p1.Y),
                            new VERTEX((float)p2.X, (float)p2.Z, (float)p2.Y));
                    }
                }

                scene.AddRigidMesh(mesh, Matrix4x4.Identity);

            }

            return scene;
        }
    }
}
