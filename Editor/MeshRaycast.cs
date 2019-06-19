using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System;
using System.Linq;
using System.Reflection;

namespace uTools
{
    public static class MeshRaycast
    {
        static Type type_HandleUtility;
        static MethodInfo meth_IntersectRayMesh;
        static bool init;

        static MeshRaycast()
        {
            if (init)
            {
                return;
            }
            var editorTypes = typeof(UnityEditor.Editor).Assembly.GetTypes();

            type_HandleUtility = editorTypes.FirstOrDefault(t => t.Name == "HandleUtility");
            meth_IntersectRayMesh = type_HandleUtility.GetMethod("IntersectRayMesh",
                                                                  BindingFlags.Static | BindingFlags.NonPublic);
            init = true;
        }

        //get a point from interected with any meshes in scene, based on mouse position.
        //WE DON'T NOT NEED to have to have colliders ;)
        //usually used in conjunction with  PickGameObject()
        public static bool IntersectRayMesh(Ray ray, MeshFilter meshFilter, out RaycastHit hit)
        {
            return IntersectRayMesh(ray, meshFilter.sharedMesh, meshFilter.transform.localToWorldMatrix, out hit);
        }

        //get a point from interected with any meshes in scene, based on mouse position.
        //WE DON'T NOT NEED to have to have colliders ;)
        //usually used in conjunction with  PickGameObject()
        public static bool IntersectRayMesh(Ray ray, Mesh mesh, Matrix4x4 matrix, out RaycastHit hit)
        {
            var parameters = new object[] { ray, mesh, matrix, null };
            bool result = (bool)meth_IntersectRayMesh.Invoke(null, parameters);
            hit = (RaycastHit)parameters[3];
            return result;
        }
    }
}