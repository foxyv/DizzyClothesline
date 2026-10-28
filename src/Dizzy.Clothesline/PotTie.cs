using System.Collections.Generic;
using UnityEngine;

namespace Dizzy.Clothesline
{
    // A thin cord that hangs a soup pot level. One turn sits around each handle
    // where it leaves the wall, then the strand runs up that side and the two
    // meet on the clothesline. Pots already hang. This only draws the cord and
    // says how far to drop them. The pose that levels them lives in Lines.
    internal sealed class PotTie : MonoBehaviour
    {
        private const int Big = 157;

        // Both pots share a height. The handle is a strap. Just outside the wall it
        // is about 2 cm wide, measured from the meshes. The small pot is the fallback.
        private const float RimY = 0.1762f;
        private const float JoinY = 0.1026f;
        private const float SmallWall = 0.1613f;
        private const float BigWall = 0.2097f;
        private const float SmallWrapX = 0.180f;
        private const float BigWrapX = 0.234f;
        private const float SmallWrapR = 0.012f;
        private const float BigWrapR = 0.015f;

        private static Material _cord;

        private Mesh _mesh;
        private Transform _left;
        private Transform _right;
        private Mesh _leftMesh;
        private Mesh _rightMesh;
        private Motion _motion;

        // Loops and the rise to the rim are built once. The two strands up to the
        // rope are unit tubes, scaled out to wherever the line sits this frame.
        internal struct Motion
        {
            public bool Built;
            public int Bad;
            public float RopeRadius;
            public float Wall;
            public Vector3 LeftPos;
            public Quaternion LeftRot;
            public Vector3 LeftScale;
            public Vector3 RightPos;
            public Quaternion RightRot;
            public Vector3 RightScale;
        }

        internal static bool Is(ShipItem ship)
        {
            return ship != null && ship.GetComponent<ShipItemSoup>() != null;
        }

        // How far below the usual pin the origin sits, so the rim, not the origin,
        // is the part just under the rope. Both pots are the same height.
        internal static float Drop(ShipItem ship)
        {
            if (!Is(ship))
                return 0f;
            return RimY - ClotheslineConfig.HangDrop.Value + ClotheslineConfig.Thickness.Value * 0.5f + 0.012f;
        }

        internal static void Sync(LineAnchor anchor)
        {
            ShipItem ship = anchor != null ? anchor.Ship : null;
            if (!Is(ship))
            {
                Clear(anchor);
                return;
            }

            if (anchor.Pot == null)
            {
                // modcheck-ignore: SW605 the tie is new each time a pot is hung
                var go = new GameObject("clothesline pot tie");
                go.transform.SetParent(ship.transform, false);
                anchor.Pot = go.AddComponent<PotTie>();
            }

            anchor.Pot.Follow(anchor, Wall(ship));
        }

        internal static void Clear(LineAnchor anchor)
        {
            if (anchor == null || anchor.Pot == null)
                return;
            Destroy(anchor.Pot.gameObject);
            anchor.Pot = null;
        }

        private static float Wall(ShipItem ship)
        {
            SaveablePrefab save = ship.Getsaveable();
            int index = save != null ? save.prefabIndex : 0;
            return index == Big ? BigWall : SmallWall;
        }

        private void Follow(LineAnchor anchor, float wall)
        {
            long stamp = Profile.Stamp();
            try
            {
            ClotheslineLine line = anchor.Line;
            ShipItem ship = anchor.Ship;
            if (line == null || ship == null || anchor.Record == null)
                return;

            float ropeRadius = ClotheslineConfig.Thickness.Value * 0.5f;
            Vector3 rope = ship.transform.InverseTransformPoint(line.RopeWorld(anchor.Record.S));
            Vector3 axis = ship.transform.InverseTransformDirection(line.RopeDirectionWorld(anchor.Record.S));
            if (axis.sqrMagnitude < 1e-8f)
                return;
            axis.Normalize();

            if (Wants(_motion, ropeRadius, wall))
            {
                if (_mesh == null)
                {
                    _mesh = new Mesh { name = "Clothesline Pot Tie" };
                    // modcheck-ignore: SW605 fresh tie object, there is nothing to reuse
                    MeshFilter filter = gameObject.AddComponent<MeshFilter>();
                    filter.sharedMesh = _mesh;
                    // modcheck-ignore: SW605 fresh tie object, there is nothing to reuse
                    MeshRenderer renderer = gameObject.AddComponent<MeshRenderer>();
                    renderer.sharedMaterial = Cord();
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                }

                Ensure(ref _left, ref _leftMesh, "clothesline pot left");
                Ensure(ref _right, ref _rightMesh, "clothesline pot right");
            }

            Step(ref _motion, rope, axis, ropeRadius, wall, _mesh, _leftMesh, _rightMesh);
            if (!_motion.Built || _left == null)
                return;

            _left.localPosition = _motion.LeftPos;
            _left.localRotation = _motion.LeftRot;
            _left.localScale = _motion.LeftScale;
            _right.localPosition = _motion.RightPos;
            _right.localRotation = _motion.RightRot;
            _right.localScale = _motion.RightScale;
            }
            finally
            {
                Profile.Add(Profile.Kind.Pot, stamp);
            }
        }

        internal static bool Wants(in Motion motion, float ropeRadius, float wall)
        {
            return !motion.Built
                || Mathf.Abs(ropeRadius - motion.RopeRadius) > 1e-5f
                || Mathf.Abs(wall - motion.Wall) > 1e-5f;
        }

        internal static void Step(
            ref Motion motion, Vector3 rope, Vector3 axis, float ropeRadius, float wall,
            Mesh body, Mesh left, Mesh right)
        {
            if (!CordGuard.Usable(ref motion.Bad, rope, axis))
                return;
            float cord = ropeRadius / 3f;
            if (Wants(motion, ropeRadius, wall))
            {
                BuildBody(body, cord, wall);
                BuildSpan(left, cord);
                BuildSpan(right, cord);
                motion.Built = true;
                motion.RopeRadius = ropeRadius;
                motion.Wall = wall;
            }

            Vector3 leftFrom;
            Vector3 rightFrom;
            Vector3 leftTo;
            Vector3 rightTo;
            Ends(rope, axis, ropeRadius, cord, wall, out leftFrom, out rightFrom, out leftTo, out rightTo);
            Aim(ref motion.LeftPos, ref motion.LeftRot, ref motion.LeftScale, leftFrom, leftTo);
            Aim(ref motion.RightPos, ref motion.RightRot, ref motion.RightScale, rightFrom, rightTo);
        }

        // Turns around the handles, and the straight rise from each turn up to the rim.
        // Neither of those moves when the boat heels.
        private static void BuildBody(Mesh mesh, float cordRadius, float wall)
        {
            bool big = wall > 0.18f;
            float wrapX = big ? BigWrapX : SmallWrapX;
            float loopR = (big ? BigWrapR : SmallWrapR) + cordRadius * 0.35f;
            Vector3 leftCenter = new Vector3(wrapX, JoinY, 0f);
            Vector3 rightCenter = new Vector3(-wrapX, JoinY, 0f);
            Vector3 left = leftCenter + Vector3.up * loopR;
            Vector3 right = rightCenter + Vector3.up * loopR;
            float rise = RimY + 0.006f;
            var paths = new List<Vector3[]>
            {
                Turn(leftCenter, loopR),
                Turn(rightCenter, loopR),
                Rise(left, new Vector3(wall, rise, 0f)),
                Rise(right, new Vector3(-wall, rise, 0f)),
            };
            RopeMesh.Build(mesh, paths, cordRadius, Profile.Kind.Pot);
        }

        private static void BuildSpan(Mesh mesh, float cordRadius)
        {
            RopeMesh.Build(mesh, new[] { Vector3.zero, Vector3.forward }, cordRadius, Profile.Kind.Pot);
        }

        // Where each strand leaves the rim, and where it meets the rope.
        private static void Ends(
            Vector3 rope, Vector3 ropeAxis, float lineRadius, float cordRadius, float wall,
            out Vector3 leftFrom, out Vector3 rightFrom, out Vector3 leftTo, out Vector3 rightTo)
        {
            float rise = RimY + 0.006f;
            leftFrom = new Vector3(wall, rise, 0f);
            rightFrom = new Vector3(-wall, rise, 0f);

            Vector3 center = new Vector3(0f, JoinY, 0f);
            Vector3 station = rope + ropeAxis * Vector3.Dot(center - rope, ropeAxis);
            Vector3 down = center - station;
            down -= ropeAxis * Vector3.Dot(down, ropeAxis);
            if (down.sqrMagnitude < 1e-8f)
                down = -Vector3.up;
            down.Normalize();
            Vector3 meet = station - down * (lineRadius + cordRadius * 0.5f);
            float split = cordRadius * 0.4f;
            leftTo = meet - ropeAxis * split;
            rightTo = meet + ropeAxis * split;
        }

        private static void Aim(ref Vector3 pos, ref Quaternion rot, ref Vector3 scale, Vector3 from, Vector3 to)
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

        // A closed turn in the plane of the wall, around the handle strap.
        private static Vector3[] Turn(Vector3 center, float radius)
        {
            const int segs = 24;
            var loop = new Vector3[segs + 1];
            for (int i = 0; i <= segs; i++)
            {
                float ang = i * Mathf.PI * 2f / segs;
                loop[i] = center + new Vector3(0f, Mathf.Cos(ang), Mathf.Sin(ang)) * radius;
            }
            return loop;
        }

        private static Vector3[] Rise(Vector3 fromLoop, Vector3 aboveRim)
        {
            var pts = new Vector3[4];
            for (int i = 0; i < 4; i++)
                pts[i] = Vector3.Lerp(fromLoop, aboveRim, i / 3f);
            return pts;
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
            renderer.sharedMaterial = Cord();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            child = go.transform;
        }

        private static Material Cord()
        {
            if (_cord != null)
                return _cord;
            Material shared = RopeMesh.Material;
            if (shared == null)
                return null;
            _cord = new Material(shared);
            _cord.name = "Dizzy Clothesline Pot Cord";
            _cord.color = new Color(0.28f, 0.14f, 0.06f);
            return _cord;
        }

        private void OnDestroy()
        {
            if (_mesh != null)
                Destroy(_mesh);
            if (_leftMesh != null)
                Destroy(_leftMesh);
            if (_rightMesh != null)
                Destroy(_rightMesh);
        }
    }
}
