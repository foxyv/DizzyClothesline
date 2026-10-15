using UnityEngine;

namespace Dizzy.Clothesline
{
    // Builds the rope as a thin tube along a list of points.
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
            int rings = points.Length;
            int ringSize = Sides + 1;
            var vertices = new Vector3[rings * ringSize];
            var normals = new Vector3[vertices.Length];
            var uv = new Vector2[vertices.Length];
            var triangles = new int[(rings - 1) * Sides * 6];

            float along = 0f;
            for (int i = 0; i < rings; i++)
            {
                Vector3 prev = points[Mathf.Max(i - 1, 0)];
                Vector3 next = points[Mathf.Min(i + 1, rings - 1)];
                Vector3 tangent = (next - prev).normalized;
                Vector3 side = Vector3.Cross(tangent, Mathf.Abs(tangent.y) > 0.99f ? Vector3.right : Vector3.up).normalized;
                Vector3 up = Vector3.Cross(side, tangent);
                if (i > 0)
                    along += Vector3.Distance(points[i - 1], points[i]);

                for (int j = 0; j < ringSize; j++)
                {
                    float angle = j * Mathf.PI * 2f / Sides;
                    Vector3 normal = side * Mathf.Cos(angle) + up * Mathf.Sin(angle);
                    int v = i * ringSize + j;
                    vertices[v] = points[i] + normal * radius;
                    normals[v] = normal;
                    uv[v] = new Vector2((float)j / Sides, along * 4f);
                }
            }

            int t = 0;
            for (int i = 0; i < rings - 1; i++)
            {
                for (int j = 0; j < Sides; j++)
                {
                    int a = i * ringSize + j;
                    int b = a + ringSize;
                    triangles[t++] = a;
                    triangles[t++] = b;
                    triangles[t++] = a + 1;
                    triangles[t++] = a + 1;
                    triangles[t++] = b;
                    triangles[t++] = b + 1;
                }
            }

            // Unity draws a triangle's front where Cross(v1 - v0, v2 - v0) points.
            // Turn the winding round if the first one faces into the tube.
            Vector3 face = Vector3.Cross(vertices[triangles[1]] - vertices[triangles[0]], vertices[triangles[2]] - vertices[triangles[0]]);
            if (Vector3.Dot(face, normals[triangles[0]]) < 0f)
            {
                for (int k = 0; k < triangles.Length; k += 3)
                {
                    int swap = triangles[k + 1];
                    triangles[k + 1] = triangles[k + 2];
                    triangles[k + 2] = swap;
                }
            }

            mesh.Clear();
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uv;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
        }
    }
}
