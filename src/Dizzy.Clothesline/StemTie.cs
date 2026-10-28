using System.Collections.Generic;
using UnityEngine;

namespace Dizzy.Clothesline
{
    // A thinner string that hangs a banana bunch from the line. The bunch's stalk
    // stops at the pin, and a lamp hook's metal covers that gap. On a line the string
    // wraps the stalk, then loops over the rope.
    //
    // The wrap, the loop, and the strand between them are built once. A roll of the
    // boat turns the wrap on the stalk and the loop onto the rope. Rebuilding the
    // whole string for that shift was the hitch.
    internal sealed class StemTie : MonoBehaviour
    {
        private const string StickName = "FirewoodBundleStick";
        private const string BananaName = "banana";
        private const int WrapTurns = 2;
        private const int PerTurn = 12;

        private Transform _wrap;
        private Transform _loop;
        private Transform _span;
        private Mesh _wrapMesh;
        private Mesh _loopMesh;
        private Mesh _spanMesh;
        private Motion _motion;

        // The three pieces, aimed. Built is false until the tubes exist.
        internal struct Motion
        {
            public bool Built;
            public int Bad;
            public float RopeRadius;
            public float StringRadius;
            public float StalkRadius;
            public Vector3 WrapEnd;
            public Vector3 LoopStart;
            public Vector3 WrapPos;
            public Quaternion WrapRot;
            public Vector3 LoopPos;
            public Quaternion LoopRot;
            public Vector3 SpanPos;
            public Quaternion SpanRot;
            public Vector3 SpanScale;
        }

        internal static void Sync(LineAnchor anchor)
        {
            ShipItem ship = anchor != null ? anchor.Ship : null;
            if (!IsBananaBunch(ship))
            {
                Clear(anchor);
                return;
            }

            if (anchor.Tie == null)
            {
                // modcheck-ignore: SW605 the tie is new each time a bunch is hung
                var go = new GameObject("clothesline stem tie");
                go.transform.SetParent(ship.transform, false);
                anchor.Tie = go.AddComponent<StemTie>();
            }

            anchor.Tie.Follow(anchor);
        }

        internal static void Clear(LineAnchor anchor)
        {
            if (anchor == null || anchor.Tie == null)
                return;
            Destroy(anchor.Tie.gameObject);
            anchor.Tie = null;
        }

        private static bool IsBananaBunch(ShipItem ship)
        {
            return ship != null
                && ship.name == BananaName
                && ship.transform.Find(StickName) != null;
        }

        private void Follow(LineAnchor anchor)
        {
            long stamp = Profile.Stamp();
            try
            {
                ClotheslineLine line = anchor.Line;
                ShipItem ship = anchor.Ship;
                if (line == null || ship == null || anchor.Record == null)
                    return;

                transform.localPosition = Vector3.zero;
                transform.localRotation = Quaternion.identity;
                transform.localScale = Vector3.one;

                float ropeRadius = ClotheslineConfig.Thickness.Value * 0.5f;
                float stringRadius = ropeRadius * 0.5f;
                Vector3 rope = ship.transform.InverseTransformPoint(line.RopeWorld(anchor.Record.S));
                Vector3 axis = ship.transform.InverseTransformDirection(line.RopeDirectionWorld(anchor.Record.S));
                if (axis.sqrMagnitude < 1e-8f || !Finite(rope) || !Finite(axis))
                    return;
                axis.Normalize();

                Vector3 top;
                Vector3 down;
                float stalkRadius;
                if (!TryStalk(ship, rope, out top, out down, out stalkRadius))
                    return;

                if (Wants(_motion, ropeRadius, stringRadius, stalkRadius))
                {
                    Ensure(ref _wrap, ref _wrapMesh, "clothesline stem wrap");
                    Ensure(ref _loop, ref _loopMesh, "clothesline stem loop");
                    Ensure(ref _span, ref _spanMesh, "clothesline stem span");
                }

                Step(ref _motion, rope, axis, top, down, stalkRadius, ropeRadius, _wrapMesh, _loopMesh, _spanMesh);
                if (_wrap == null || !_motion.Built)
                    return;

                _wrap.localPosition = _motion.WrapPos;
                _wrap.localRotation = _motion.WrapRot;
                _wrap.localScale = Vector3.one;
                _loop.localPosition = _motion.LoopPos;
                _loop.localRotation = _motion.LoopRot;
                _loop.localScale = Vector3.one;
                _span.localPosition = _motion.SpanPos;
                _span.localRotation = _motion.SpanRot;
                _span.localScale = _motion.SpanScale;
            }
            finally
            {
                Profile.Add(Profile.Kind.Stem, stamp);
            }
        }

        internal static bool Wants(in Motion motion, float ropeRadius, float stringRadius, float stalkRadius)
        {
            return !motion.Built
                || Mathf.Abs(ropeRadius - motion.RopeRadius) > 1e-5f
                || Mathf.Abs(stringRadius - motion.StringRadius) > 1e-5f
                || Mathf.Abs(stalkRadius - motion.StalkRadius) > 1e-5f;
        }

        // rope and axis are already in the bunch's space. A heel changes them.
        // The tubes are rebuilt only when a radius changes.
        internal static void Step(
            ref Motion motion, Vector3 rope, Vector3 axis, Vector3 top, Vector3 down,
            float stalkRadius, float ropeRadius, Mesh wrap, Mesh loop, Mesh span)
        {
            if (!CordGuard.Usable(ref motion.Bad, rope, axis))
                return;
            stalkRadius = Mathf.Min(stalkRadius, CordGuard.MaxStalkRadius);
            float stringRadius = ropeRadius * 0.5f;
            Vector3 origin = top + down * stalkRadius;
            Vector3 towardRope = rope - origin;
            Vector3 side = towardRope - down * Vector3.Dot(towardRope, down);
            if (side.sqrMagnitude < 1e-8f)
                side = Vector3.Cross(down, Vector3.right);
            if (side.sqrMagnitude < 1e-8f)
                side = Vector3.Cross(down, Vector3.forward);
            side.Normalize();

            if (Wants(motion, ropeRadius, stringRadius, stalkRadius))
            {
                motion.WrapEnd = BuildWrap(wrap, stringRadius, stalkRadius);
                motion.LoopStart = BuildLoop(loop, ropeRadius, stringRadius);
                BuildSpan(span, stringRadius);
                motion.RopeRadius = ropeRadius;
                motion.StringRadius = stringRadius;
                motion.StalkRadius = stalkRadius;
                motion.Built = true;
            }

            motion.WrapPos = origin;
            motion.WrapRot = CordTurn.Look(side, down);

            Vector3 toStem = origin - rope;
            Vector3 towardStem = toStem - axis * Vector3.Dot(toStem, axis);
            if (towardStem.sqrMagnitude < 1e-8f)
                towardStem = Vector3.Cross(axis, Vector3.up);
            if (towardStem.sqrMagnitude < 1e-8f)
                towardStem = Vector3.Cross(axis, Vector3.right);
            towardStem.Normalize();
            Vector3 flank = Vector3.Cross(axis, towardStem);
            if (flank.sqrMagnitude < 1e-8f)
                return;

            motion.LoopPos = rope;
            motion.LoopRot = CordTurn.Look(flank.normalized, towardStem);
            Vector3 from = motion.WrapPos + motion.WrapRot * motion.WrapEnd;
            Vector3 to = motion.LoopPos + motion.LoopRot * motion.LoopStart;
            AimSpan(ref motion.SpanPos, ref motion.SpanRot, ref motion.SpanScale, from, to);
        }

        // The bunch's own capsule is the stalk. Its end nearer the rope is the stem.
        private static bool TryStalk(ShipItem ship, Vector3 rope, out Vector3 top, out Vector3 down, out float radius)
        {
            top = Vector3.zero;
            down = Vector3.down;
            radius = 0f;
            CapsuleCollider cap = ship.GetComponent<CapsuleCollider>();
            if (cap == null || cap.radius < 1e-4f)
                return false;

            Vector3 axis = cap.direction == 0 ? Vector3.right : cap.direction == 1 ? Vector3.up : Vector3.forward;
            float half = cap.height * 0.5f;
            Vector3 a = cap.center + axis * half;
            Vector3 b = cap.center - axis * half;
            top = (a - rope).sqrMagnitude <= (b - rope).sqrMagnitude ? a : b;
            down = (a + b) * 0.5f - top;
            if (down.sqrMagnitude < 1e-8f)
                down = -axis;
            down.Normalize();
            radius = Mathf.Min(cap.radius, CordGuard.MaxStalkRadius);
            return true;
        }

        // Spiral in wrap-local space. Local Y runs down the stalk, local Z faces the rope.
        private static Vector3 BuildWrap(Mesh mesh, float stringRadius, float stalkRadius)
        {
            Vector3 down = Vector3.up;
            Vector3 side = Vector3.forward;
            Vector3 bend = Vector3.Cross(down, side);
            float wrapRadius = stalkRadius + stringRadius * 0.35f;
            float pitch = stringRadius * 2f * 1.15f;

            int segs = WrapTurns * PerTurn;
            var wraps = new Vector3[segs + 1];
            for (int i = 0; i <= segs; i++)
            {
                float u = i / (float)segs;
                float ang = u * WrapTurns * Mathf.PI * 2f;
                float radial = wrapRadius;
                float turn = u * WrapTurns;
                if (turn < 0.35f)
                {
                    float k = Mathf.SmoothStep(0f, 1f, turn / 0.35f);
                    radial = Mathf.Lerp(stalkRadius * 0.15f, wrapRadius, k);
                }

                wraps[i] = down * (u * pitch * WrapTurns)
                    + (side * Mathf.Cos(ang) + bend * Mathf.Sin(ang)) * radial;
            }

            RopeMesh.Build(mesh, wraps, stringRadius, Profile.Kind.Stem);
            return wraps[wraps.Length - 1];
        }

        // A ring in loop-local space. Local X is the rope, local Y faces the stalk.
        private static Vector3 BuildLoop(Mesh mesh, float ropeRadius, float stringRadius)
        {
            float loopRadius = ropeRadius + stringRadius * 0.25f;
            Vector3 towardStem = Vector3.up;
            Vector3 ropeAxis = Vector3.right;
            Vector3 loopSide = Vector3.Cross(ropeAxis, towardStem).normalized;

            const int loopSegs = 20;
            var loop = new Vector3[loopSegs + 1];
            for (int i = 0; i <= loopSegs; i++)
            {
                float ang = i * Mathf.PI * 2f / loopSegs;
                loop[i] = (towardStem * Mathf.Cos(ang) + loopSide * Mathf.Sin(ang)) * loopRadius;
            }

            RopeMesh.Build(mesh, loop, stringRadius, Profile.Kind.Stem);
            return loop[0];
        }

        private static void BuildSpan(Mesh mesh, float stringRadius)
        {
            RopeMesh.Build(mesh, new[] { Vector3.zero, Vector3.forward }, stringRadius, Profile.Kind.Stem);
        }

        // The span mesh is one unit long on local Z. Scaling that axis meets the loop.
        private static void AimSpan(ref Vector3 pos, ref Quaternion rot, ref Vector3 scale, Vector3 from, Vector3 to)
        {
            Vector3 delta = to - from;
            float length = delta.magnitude;
            if (length < 1e-5f)
            {
                scale = Vector3.zero;
                return;
            }
            pos = from;
            rot = CordTurn.Look(delta / length);
            scale = new Vector3(1f, 1f, length);
        }

        private void Ensure(ref Transform child, ref Mesh mesh, string name)
        {
            if (child != null)
                return;
            // modcheck-ignore: SW605 fresh tie object, there is nothing to reuse
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            mesh = new Mesh { name = name };
            // modcheck-ignore: SW605 fresh tie object, there is nothing to reuse
            MeshFilter filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            // modcheck-ignore: SW605 fresh tie object, there is nothing to reuse
            MeshRenderer renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = RopeMesh.Material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            child = go.transform;
        }

        private static bool Finite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsNaN(value.y) && !float.IsNaN(value.z)
                && !float.IsInfinity(value.x) && !float.IsInfinity(value.y) && !float.IsInfinity(value.z);
        }

        private void OnDestroy()
        {
            if (_wrapMesh != null)
                Destroy(_wrapMesh);
            if (_loopMesh != null)
                Destroy(_loopMesh);
            if (_spanMesh != null)
                Destroy(_spanMesh);
        }
    }
}
