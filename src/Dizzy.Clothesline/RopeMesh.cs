using System.Collections.Generic;
using UnityEngine;

namespace Dizzy.Clothesline
{
    // Builds the rope as a thin tube along one or more centerlines. A knot is several
    // centerlines (the eye, and the turns around it) sharing this one mesh.
    internal static class RopeMesh
    {
        private const int Sides = 6;

        private static Material _material;

        internal static Material Material
        {
            get
            {
                if (_material != null)
                    return _material;

                Shader shader = Shader.Find("Standard");
                if (shader == null)
                    shader = Shader.Find("Legacy Shaders/Diffuse");
                if (shader == null)
                    shader = Shader.Find("Unlit/Color");
                if (shader == null)
                    return null;

                _material = new Material(shader);
                _material.name = "Dizzy Clothesline Rope";
                if (shader.name == "Standard")
                {
                    _material.SetFloat("_Metallic", 0f);
                    _material.SetFloat("_Glossiness", 0.1f);
                }
                ApplyColor();
                return _material;
            }
        }

        internal static void ApplyColor()
        {
            if (_material != null && ClotheslineConfig.RopeColor != null)
                _material.color = ClotheslineConfig.RopeColor.Value;
        }

        internal static void Build(Mesh mesh, Vector3[] points, float radius)
        {
            Build(mesh, new[] { points }, radius);
        }

        internal static void Build(Mesh mesh, IList<Vector3[]> paths, float radius)
        {
            int ringSize = Sides + 1;
            int verts = 0;
            int tris = 0;
            for (int p = 0; p < paths.Count; p++)
            {
                Vector3[] path = paths[p];
                if (path == null || path.Length < 2)
                    continue;
                verts += path.Length * ringSize;
                tris += (path.Length - 1) * Sides * 6;
            }

            mesh.Clear();
            if (verts == 0)
                return;

            var vertices = new Vector3[verts];
            var normals = new Vector3[verts];
            var uv = new Vector2[verts];
            var triangles = new int[tris];
            int vertexBase = 0;
            int triangleBase = 0;
            for (int p = 0; p < paths.Count; p++)
            {
                Vector3[] path = paths[p];
                if (path == null || path.Length < 2)
                    continue;
                WritePath(path, radius, vertices, normals, uv, triangles, ref vertexBase, ref triangleBase);
            }

            // Unity draws a triangle's front where Cross(v1 - v0, v2 - v0) points.
            // Every path is wound the same way, so one check covers the mesh.
            if (tris > 0)
            {
                Vector3 face = Vector3.Cross(
                    vertices[triangles[1]] - vertices[triangles[0]],
                    vertices[triangles[2]] - vertices[triangles[0]]);
                if (Vector3.Dot(face, normals[triangles[0]]) < 0f)
                {
                    for (int k = 0; k < triangles.Length; k += 3)
                    {
                        int swap = triangles[k + 1];
                        triangles[k + 1] = triangles[k + 2];
                        triangles[k + 2] = swap;
                    }
                }
            }

            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uv;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
        }

        // The frame is carried along the centerline. Rebuilding it from world up at
        // every ring flips the tube where a knot curls back on itself.
        private static void WritePath(
            Vector3[] points, float radius,
            Vector3[] vertices, Vector3[] normals, Vector2[] uv, int[] triangles,
            ref int vertexBase, ref int triangleBase)
        {
            int ringSize = Sides + 1;
            Vector3 side = Perpendicular(TangentAt(points, 0));
            float along = 0f;

            for (int i = 0; i < points.Length; i++)
            {
                Vector3 tangent = TangentAt(points, i);
                side -= tangent * Vector3.Dot(side, tangent);
                if (side.sqrMagnitude < 1e-8f)
                    side = Perpendicular(tangent);
                side.Normalize();
                Vector3 up = Vector3.Cross(side, tangent);
                if (i > 0)
                    along += Vector3.Distance(points[i - 1], points[i]);

                for (int j = 0; j < ringSize; j++)
                {
                    float angle = j * Mathf.PI * 2f / Sides;
                    Vector3 normal = side * Mathf.Cos(angle) + up * Mathf.Sin(angle);
                    int v = vertexBase + i * ringSize + j;
                    vertices[v] = points[i] + normal * radius;
                    normals[v] = normal;
                    uv[v] = new Vector2((float)j / Sides, along * 4f);
                }
            }

            int rings = points.Length;
            for (int i = 0; i < rings - 1; i++)
            {
                for (int j = 0; j < Sides; j++)
                {
                    int a = vertexBase + i * ringSize + j;
                    int b = a + ringSize;
                    triangles[triangleBase++] = a;
                    triangles[triangleBase++] = b;
                    triangles[triangleBase++] = a + 1;
                    triangles[triangleBase++] = a + 1;
                    triangles[triangleBase++] = b;
                    triangles[triangleBase++] = b + 1;
                }
            }

            vertexBase += rings * ringSize;
        }

        private static Vector3 TangentAt(Vector3[] points, int i)
        {
            Vector3 delta = points[Mathf.Min(i + 1, points.Length - 1)] - points[Mathf.Max(i - 1, 0)];
            if (delta.sqrMagnitude < 1e-12f)
                delta = points[Mathf.Min(i + 1, points.Length - 1)] - points[i];
            return delta.sqrMagnitude < 1e-12f ? Vector3.forward : delta.normalized;
        }

        private static Vector3 Perpendicular(Vector3 tangent)
        {
            Vector3 side = Vector3.Cross(tangent, Mathf.Abs(tangent.y) > 0.85f ? Vector3.right : Vector3.up);
            if (side.sqrMagnitude < 1e-8f)
                side = Vector3.Cross(tangent, Vector3.forward);
            return side.normalized;
        }
    }
}
