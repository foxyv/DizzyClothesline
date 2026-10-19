using System.Collections.Generic;
using UnityEngine;

namespace Dizzy.Clothesline
{
    // The teardrop and three turns used at a clothesline end, stood on a thin
    // cord. The eye opens across the rope and is pushed out so the line passes
    // through the loop.
    internal static class LineEye
    {
        internal const float CordRadius = 0.003f;
        private const float AroundGap = 0.002f;

        internal static bool Build(Mesh mesh, Vector3 along, float drop, float ropeRadius, out Vector3 open)
        {
            open = Vector3.Cross(Vector3.up, along);
            if (open.sqrMagnitude < 1e-8f)
                open = Vector3.forward;
            open.Normalize();

            float diameter = CordRadius * 2f;
            float natural = RopeKnot.Trim(diameter, 1f);
            if (mesh == null || drop < diameter || natural < 1e-4f)
                return false;
            float scale = Mathf.Min(1f, drop / natural);
            float trim = RopeKnot.Trim(diameter, scale);

            var paths = new List<Vector3[]>();
            RopeKnot.Add(paths, Vector3.up * trim, Vector3.zero, open, diameter, scale);
            if (paths.Count == 0)
                return false;
            ClearOfRope(paths[0], Vector3.up * drop, along, ropeRadius + CordRadius + AroundGap);
            RopeMesh.Build(mesh, paths, CordRadius);
            return true;
        }

        private static void ClearOfRope(Vector3[] eye, Vector3 center, Vector3 axis, float clear)
        {
            if (eye == null || clear < 1e-4f)
                return;
            for (int i = 0; i < eye.Length; i++)
            {
                Vector3 from = eye[i] - center;
                Vector3 radial = from - axis * Vector3.Dot(from, axis);
                float distance = radial.magnitude;
                if (distance >= clear)
                    continue;
                if (distance < 1e-5f)
                    radial = Vector3.up;
                else
                    radial /= distance;
                eye[i] += radial * (clear - distance);
            }
        }
    }
}
