using System.Collections.Generic;
using UnityEngine;

namespace Dizzy.Clothesline
{
    // A teardrop eye at a hook, with three turns of rope around the joint.
    //
    // The standing rope stops at the first turn. From there two strands run side by
    // side through the turns, then open into the eye. The round end of the eye sits
    // on the hook. Lengths scale with the rope thickness, and shrink on a short line
    // so both knots still leave rope between them. The strand spacing does not
    // shrink: the turns have to stay wrapped around this rope.
    internal static class RopeKnot
    {
        // Lengths, in rope diameters. Tuned so the eye is longer than the turns and
        // opens wider than them. The pitch is under one diameter so neighbouring
        // turns overlap and the knot has no slit between coils.
        private const float WrapPitch = 0.82f;
        // Pitches from where the standing rope ends to where the two strands open.
        // That spot is the middle of the third turn.
        private const float OpenAtTurns = 2.2f;
        private const float EyeLengths = 6.2f;
        private const float EyeHalfWidths = 2.55f;
        // How far the turns reach back onto the standing rope, in pitches. The tuck
        // that hides the start of the whipping is this long, so it finishes just as
        // the standing rope does.
        private const float HelixLead = 0.3f;

        // Not scaled. Two strands sit against each other, and the turns encircle both.
        private const float StrandGap = 0.52f;
        // How far a turn sinks into the strands, in diameters. The tube is a hexagon,
        // so this has to reach past the flats or the middle of the knot stays open.
        private const float WrapBite = 0.16f;

        private const float ReachLengths = HelixLead * WrapPitch + OpenAtTurns * WrapPitch + EyeLengths;
        private const float TrimLengths = OpenAtTurns * WrapPitch + EyeLengths;

        // Below this the eye would be shorter than the rope is thick, so the line is
        // drawn plain instead.
        private const float MinScale = 0.4f;

        // Share of the line the two knots together may use.
        private const float Fit = 0.28f;

        internal static float Scale(float diameter, float arc)
        {
            float natural = Reach(diameter, 1f);
            if (natural < 1e-4f || arc < 1e-4f)
                return 0f;
            float scale = arc * Fit / natural;
            if (scale < MinScale)
                return 0f;
            return scale > 1f ? 1f : scale;
        }

        // How far the knot reaches back from the hook, including the turn that sits
        // on the standing rope. Items hang clear of this.
        internal static float Reach(float diameter, float scale)
        {
            return ReachLengths * diameter * scale;
        }

        // How far from the hook the standing rope stops, at the first turn.
        internal static float Trim(float diameter, float scale)
        {
            return TrimLengths * diameter * scale;
        }

        // hookUp is the hook's up, in the same space as hook and ropeEnd. The eye opens
        // in that direction, so turning the hook turns the loop with it.
        internal static void Add(List<Vector3[]> paths, Vector3 hook, Vector3 ropeEnd, Vector3 hookUp, float diameter, float scale)
        {
            Vector3 axis = hook - ropeEnd;
            float trim = axis.magnitude;
            if (trim < 1e-4f || diameter < 1e-4f)
                return;

            Vector3 outward = axis / trim;
            Vector3 bend = FlatAgainst(hookUp, outward);
            if (bend.sqrMagnitude < 1e-6f)
                bend = FlatAgainst(Vector3.up, outward);
            if (bend.sqrMagnitude < 1e-6f)
                bend = FlatAgainst(Vector3.right, outward);
            bend.Normalize();
            Vector3 side = Vector3.Cross(bend, outward);

            float pitch = WrapPitch * diameter * scale;
            float openAt = OpenAtTurns * pitch;
            float eyeLen = trim - openAt;
            if (eyeLen < diameter * 0.5f)
                return;

            float halfSep = StrandGap * diameter;
            float halfW = Mathf.Max(EyeHalfWidths * diameter * scale, halfSep + diameter * 0.35f);
            paths.Add(Eye(ropeEnd, outward, bend, halfSep, halfW, pitch, openAt, eyeLen));
            paths.Add(Turns(ropeEnd, outward, bend, side, diameter, halfSep, pitch));
        }

        // Two strands through the turns, opening into the eye and meeting at the hook.
        private static Vector3[] Eye(
            Vector3 origin, Vector3 outward, Vector3 bend,
            float halfSep, float halfW, float pitch, float openAt, float eyeLen)
        {
            // Just past the standing rope, inside the first turn, which is already
            // at full size there.
            float legStart = 0.08f * pitch;
            var right = new List<Vector2>(28);
            const int straight = 5;
            for (int i = 0; i < straight; i++)
            {
                float z = Mathf.Lerp(legStart, openAt, i / (float)(straight - 1));
                right.Add(new Vector2(z, halfSep));
            }

            Vector2 belly = new Vector2(openAt + eyeLen * 0.62f, halfW);
            Sample(right, new Vector2(openAt, halfSep), new Vector2(openAt + eyeLen * 0.16f, halfSep),
                new Vector2(openAt + eyeLen * 0.42f, halfW), belly, 8);
            Sample(right, belly, new Vector2(openAt + eyeLen * 0.82f, halfW),
                new Vector2(openAt + eyeLen, halfW * 0.48f), new Vector2(openAt + eyeLen, 0f), 10);

            var pts = new Vector3[right.Count * 2 - 1];
            for (int i = 0; i < right.Count; i++)
                pts[i] = origin + outward * right[i].x + bend * right[i].y;
            for (int i = right.Count - 2; i >= 0; i--)
                pts[right.Count + (right.Count - 2 - i)] = origin + outward * right[i].x - bend * right[i].y;
            return pts;
        }

        // Three turns. The first is centered where the standing rope ends, the third
        // where the strands open. The start tucks into the standing rope so the cut
        // end of the turn does not show. The path is an oval around both strands:
        // a circle that clears them on the wide side stands off them on the narrow
        // side, and that opening is what shows through the knot.
        private static Vector3[] Turns(
            Vector3 origin, Vector3 outward, Vector3 bend, Vector3 side,
            float diameter, float halfSep, float pitch)
        {
            const int turns = 3;
            const int perTurn = 16;
            int segs = turns * perTurn;
            float bite = WrapBite * diameter;
            float narrow = diameter - bite;
            float wide = halfSep + diameter - bite;
            float startZ = -HelixLead * pitch;
            float endZ = startZ + turns * pitch;
            var pts = new Vector3[segs + 1];
            for (int i = 0; i <= segs; i++)
            {
                float u = i / (float)segs;
                float ang = u * turns * Mathf.PI * 2f;
                float z = Mathf.Lerp(startZ, endZ, u);
                float across = narrow;
                float along = wide;
                float turn = u * turns;
                if (turn < HelixLead)
                {
                    float k = Mathf.SmoothStep(0f, 1f, turn / HelixLead);
                    float tucked = diameter * 0.02f;
                    across = Mathf.Lerp(tucked, narrow, k);
                    along = Mathf.Lerp(tucked, wide, k);
                }

                pts[i] = origin + outward * z
                    + side * (Mathf.Cos(ang) * across)
                    + bend * (Mathf.Sin(ang) * along);
            }

            return pts;
        }

        // The part of direction that is perpendicular to the rope, so the eye can roll
        // around the rope without leaving its axis.
        private static Vector3 FlatAgainst(Vector3 direction, Vector3 axis)
        {
            return direction - axis * Vector3.Dot(direction, axis);
        }

        private static void Sample(List<Vector2> into, Vector2 a, Vector2 b, Vector2 c, Vector2 d, int steps)
        {
            for (int i = 1; i <= steps; i++)
            {
                float t = i / (float)steps;
                float u = 1f - t;
                into.Add(u * u * u * a + 3f * u * u * t * b + 3f * u * t * t * c + t * t * t * d);
            }
        }
    }
}
