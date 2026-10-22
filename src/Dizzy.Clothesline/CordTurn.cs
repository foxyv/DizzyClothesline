using UnityEngine;

namespace Dizzy.Clothesline
{
    // Unity's Quaternion.LookRotation and FromToRotation are native calls. They
    // do not run in the hang-budget test, so the cord aims go through here. The
    // basis is the one LookRotation documents: local Z along forward, local X
    // along the cross of up and forward, local Y along the cross of those two.
    internal static class CordTurn
    {
        internal static Quaternion Look(Vector3 forward)
        {
            return Look(forward, Vector3.up);
        }

        internal static Quaternion Look(Vector3 forward, Vector3 upwards)
        {
            if (forward.sqrMagnitude < 1e-12f)
                return Identity();

            Vector3 z = forward.normalized;
            Vector3 x = Vector3.Cross(upwards, z);
            if (x.sqrMagnitude < 1e-12f)
            {
                Vector3 alt = Mathf.Abs(z.y) < 0.9f ? Vector3.up : Vector3.right;
                x = Vector3.Cross(alt, z);
            }
            x.Normalize();
            Vector3 y = Vector3.Cross(z, x);
            return FromAxes(x, y, z);
        }

        // The rotation that carries `from` onto `to`.
        internal static Quaternion FromTo(Vector3 from, Vector3 to)
        {
            if (from.sqrMagnitude < 1e-12f || to.sqrMagnitude < 1e-12f)
                return Identity();

            Vector3 f = from.normalized;
            Vector3 t = to.normalized;
            float dot = Vector3.Dot(f, t);
            if (dot > 0.999999f)
                return Identity();

            if (dot < -0.999999f)
            {
                // Any half turn about an axis square to f will do, but they are not alike.
                // A level vector turned about a level axis lands level and upside down, which
                // is a knot hung the wrong way up. Turn it about the vertical instead.
                Vector3 axis;
                if (Mathf.Abs(f.y) < 1e-3f)
                {
                    axis = Vector3.up;
                }
                else
                {
                    axis = Vector3.Cross(Vector3.up, f);
                    if (axis.sqrMagnitude < 1e-8f)
                        axis = Vector3.right;
                }
                axis.Normalize();
                return AxisAngle(axis, 180f);
            }

            Vector3 cross = Vector3.Cross(f, t);
            Quaternion q;
            q.x = cross.x;
            q.y = cross.y;
            q.z = cross.z;
            q.w = 1f + dot;
            return Normalized(q);
        }

        private static Quaternion FromAxes(Vector3 x, Vector3 y, Vector3 z)
        {
            float trace = x.x + y.y + z.z;
            Quaternion q;
            if (trace > 0f)
            {
                float s = Mathf.Sqrt(trace + 1f) * 2f;
                q.w = 0.25f * s;
                q.x = (y.z - z.y) / s;
                q.y = (z.x - x.z) / s;
                q.z = (x.y - y.x) / s;
            }
            else if (x.x > y.y && x.x > z.z)
            {
                float s = Mathf.Sqrt(1f + x.x - y.y - z.z) * 2f;
                q.w = (y.z - z.y) / s;
                q.x = 0.25f * s;
                q.y = (y.x + x.y) / s;
                q.z = (z.x + x.z) / s;
            }
            else if (y.y > z.z)
            {
                float s = Mathf.Sqrt(1f + y.y - x.x - z.z) * 2f;
                q.w = (z.x - x.z) / s;
                q.x = (y.x + x.y) / s;
                q.y = 0.25f * s;
                q.z = (z.y + y.z) / s;
            }
            else
            {
                float s = Mathf.Sqrt(1f + z.z - x.x - y.y) * 2f;
                q.w = (x.y - y.x) / s;
                q.x = (z.x + x.z) / s;
                q.y = (z.y + y.z) / s;
                q.z = 0.25f * s;
            }
            return q;
        }

        private static Quaternion AxisAngle(Vector3 axis, float degrees)
        {
            float half = degrees * Mathf.Deg2Rad * 0.5f;
            float s = Mathf.Sin(half);
            Quaternion q;
            q.x = axis.x * s;
            q.y = axis.y * s;
            q.z = axis.z * s;
            q.w = Mathf.Cos(half);
            return q;
        }

        private static Quaternion Identity()
        {
            Quaternion q;
            q.x = 0f;
            q.y = 0f;
            q.z = 0f;
            q.w = 1f;
            return q;
        }

        private static Quaternion Normalized(Quaternion q)
        {
            float mag = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            if (mag < 1e-12f)
                return Identity();
            q.x /= mag;
            q.y /= mag;
            q.z /= mag;
            q.w /= mag;
            return q;
        }
    }
}
