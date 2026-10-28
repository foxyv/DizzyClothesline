using UnityEngine;

namespace Dizzy.Clothesline
{
    // A physics or scale sample can put the rope metres from an item for a frame, or give a
    // rope point that is not a number. A cord that stretches to meet the rope then grows to
    // cover that gap, which is how the hammer ring once grew huge. A bad sample is ignored,
    // and the cord keeps the pose it had.
    internal static class CordGuard
    {
        // Farthest the rope may read from the item's own origin. Hanging cords are a few
        // centimetres to about a quarter of a metre long.
        internal const float MaxRopeDistance = 0.75f;

        // The widest a banana stalk may read. Dizzy Bundle Up makes it 0.006 m or a little more.
        internal const float MaxStalkRadius = 0.03f;

        // Frames in a row a bad sample is ignored. Past that the rope really is there, so the
        // cord follows it, as long as it is a number.
        internal const int Patience = 8;

        internal static bool Finite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsNaN(value.y) && !float.IsNaN(value.z)
                && !float.IsInfinity(value.x) && !float.IsInfinity(value.y) && !float.IsInfinity(value.z);
        }

        // Whether this frame's rope point and direction can be used. bad counts the bad frames
        // in a row, and resets on a good one.
        internal static bool Usable(ref int bad, Vector3 rope, Vector3 axis)
        {
            if (!Finite(rope) || !Finite(axis))
            {
                bad++;
                return false;
            }

            if (rope.magnitude > MaxRopeDistance)
            {
                bad++;
                return bad > Patience;
            }

            bad = 0;
            return true;
        }
    }
}
