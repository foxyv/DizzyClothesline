using System.Collections.Generic;
using UnityEngine;

namespace Dizzy.Clothesline
{
    // Four thin cords hang a flower pot like a basket: a ring just under the rim, and four
    // strands from points on the ring up to one place under the rope. The ring is built
    // once. Each strand is a unit tube, scaled out to wherever the rope sits this frame.
    internal sealed class PlantTie : MonoBehaviour
    {
        private const string TieName = "clothesline plant tie";
        private const int RingSegments = 32;
        private const int Strands = 4;

        // A lamp hook has no rope, so the collar is sized as for a thin one.
        private const float MinLineRadius = 0.01f;

        // How far under the lip the ring sits.
        private const float RingBelowRim = 0.015f;

        private static Material _cord;

        private Mesh _mesh;
        private Transform _collar;
        private Mesh _collarMesh;
        private readonly Transform[] _strand = new Transform[Strands];
        private readonly Mesh[] _strandMesh = new Mesh[Strands];
        private Motion _motion;

        internal struct Strand
        {
            public Vector3 Pos;
            public Quaternion Rot;
            public Vector3 Scale;
        }

        internal struct Motion
        {
            public bool Built;
            public int Bad;
            public float RopeRadius;
            public float Radius;
            public Strand A;
            public Strand B;
            public Strand C;
            public Strand D;
            public Strand Collar;
        }

        // ---- Hung on a line ----

        internal static void Sync(LineAnchor anchor)
        {
            ShipItem ship = anchor != null ? anchor.Ship : null;
            PlantPots.Spec spec;
            if (!PlantPots.TryGet(ship, out spec))
            {
                Clear(anchor);
                return;
            }

            if (anchor.Plant == null)
                anchor.Plant = Ensure(ship);

            ClotheslineLine line = anchor.Line;
            if (line == null || anchor.Record == null)
                return;

            anchor.Plant.Follow(ship, spec, line.RopeWorld(anchor.Record.S),
                line.RopeDirectionWorld(anchor.Record.S), ClotheslineConfig.Thickness.Value * 0.5f);
        }

        internal static void Clear(LineAnchor anchor)
        {
            if (anchor == null || anchor.Plant == null)
                return;
            Destroy(anchor.Plant.gameObject);
            anchor.Plant = null;
        }

        // ---- Hung on a lamp hook ----

        internal static void FollowHook(ShipItem ship, PlantPots.Spec spec, Vector3 pin, Vector3 axis)
        {
            Ensure(ship).Follow(ship, spec, pin, axis, 0f);
        }

        internal static void ClearOn(ShipItem ship)
        {
            if (ship == null)
                return;
            Transform tie = ship.transform.Find(TieName);
            if (tie != null)
                Destroy(tie.gameObject);
        }

        private static PlantTie Ensure(ShipItem ship)
        {
            Transform existing = ship.transform.Find(TieName);
            if (existing != null)
            {
                PlantTie found = existing.GetComponent<PlantTie>();
                if (found != null)
                    return found;
            }

            // modcheck-ignore: SW605 the tie is new each time a pot is hung
            var go = new GameObject(TieName);
            go.transform.SetParent(ship.transform, false);
            return go.AddComponent<PlantTie>();
        }

        // ---- Each frame ----

        private void Follow(ShipItem ship, PlantPots.Spec spec, Vector3 ropeWorld, Vector3 axisWorld, float ropeRadius)
        {
            long stamp = Profile.Stamp();
            try
            {
                Vector3 rope = ship.transform.InverseTransformPoint(ropeWorld);
                Vector3 axis = ship.transform.InverseTransformDirection(axisWorld);
                if (axis.sqrMagnitude < 1e-8f)
                    return;
                axis.Normalize();

                if (Wants(_motion, ropeRadius, spec.Radius))
                {
                    if (_mesh == null)
                    {
                        _mesh = new Mesh { name = "Clothesline Plant Ring" };
                        // modcheck-ignore: SW605 fresh tie object, there is nothing to reuse
                        MeshFilter filter = gameObject.AddComponent<MeshFilter>();
                        filter.sharedMesh = _mesh;
                        // modcheck-ignore: SW605 fresh tie object, there is nothing to reuse
                        MeshRenderer renderer = gameObject.AddComponent<MeshRenderer>();
                        renderer.sharedMaterial = Cord();
                        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        renderer.receiveShadows = false;
                    }

                    for (int i = 0; i < Strands; i++)
                        EnsureStrand(i);
                    EnsureCollar();
                }

                Step(ref _motion, rope, axis, ropeRadius, spec, _mesh, _strandMesh, _collarMesh);
                if (!_motion.Built || _strand[0] == null || _collar == null)
                    return;

                Apply(_collar, _motion.Collar);
                Apply(_strand[0], _motion.A);
                Apply(_strand[1], _motion.B);
                Apply(_strand[2], _motion.C);
                Apply(_strand[3], _motion.D);
            }
            finally
            {
                Profile.Add(Profile.Kind.Plant, stamp);
            }
        }

        private static void Apply(Transform strand, Strand pose)
        {
            strand.localPosition = pose.Pos;
            strand.localRotation = pose.Rot;
            strand.localScale = pose.Scale;
        }

        internal static bool Wants(in Motion motion, float ropeRadius, float radius)
        {
            return !motion.Built
                || Mathf.Abs(ropeRadius - motion.RopeRadius) > 1e-5f
                || Mathf.Abs(radius - motion.Radius) > 1e-5f;
        }

        // The rope point and direction are in the pot's own space. A sample that is not a
        // number, or is far from the pot, is ignored and the cords keep their last pose.
        internal static void Step(
            ref Motion motion, Vector3 rope, Vector3 axis, float ropeRadius, PlantPots.Spec spec,
            Mesh ring, Mesh[] strands, Mesh collar)
        {
            float reach = spec.Rim + spec.Rise + CordGuard.MaxRopeDistance;
            if (!CordGuard.Usable(ref motion.Bad, rope, axis, reach))
                return;

            float lineRadius = Mathf.Max(ropeRadius, MinLineRadius);
            float cord = lineRadius / 3f;
            if (Wants(motion, ropeRadius, spec.Radius))
            {
                BuildRing(ring, cord, spec);
                BuildCollar(collar, cord, lineRadius);
                for (int i = 0; i < Strands; i++)
                    BuildSpan(strands != null ? strands[i] : null, cord);
                motion.Built = true;
                motion.RopeRadius = ropeRadius;
                motion.Radius = spec.Radius;
            }

            Vector3 station;
            Vector3 meet = Meet(rope, axis, CollarRadius(cord, lineRadius), spec, out station);
            motion.Collar.Pos = station;
            motion.Collar.Rot = CordTurn.Look(axis);
            motion.Collar.Scale = Vector3.one;
            float split = cord * 0.4f;
            Aim(ref motion.A, RingPoint(0, cord, spec), meet - axis * split);
            Aim(ref motion.B, RingPoint(1, cord, spec), meet + axis * split);
            Aim(ref motion.C, RingPoint(2, cord, spec), meet + axis * split);
            Aim(ref motion.D, RingPoint(3, cord, spec), meet - axis * split);
        }

        private static float RingRadius(float cord, PlantPots.Spec spec)
        {
            return spec.Radius + cord;
        }

        private static Vector3 RingPoint(int index, float cord, PlantPots.Spec spec)
        {
            float angle = index * Mathf.PI * 0.5f;
            float radius = RingRadius(cord, spec);
            return new Vector3(Mathf.Cos(angle) * radius, spec.Rim - RingBelowRim, Mathf.Sin(angle) * radius);
        }

        private static float CollarRadius(float cord, float lineRadius)
        {
            return lineRadius + cord * 1.2f;
        }

        // The bottom of the collar that loops over the rope, straight above the pot's middle.
        private static Vector3 Meet(Vector3 rope, Vector3 axis, float collarRadius, PlantPots.Spec spec, out Vector3 station)
        {
            Vector3 center = new Vector3(0f, spec.Rim, 0f);
            // The pot hangs level and the rope slopes, so the nearest point on the rope is off
            // to one side over a long drop. Use the point on the rope straight above the pot.
            Vector3 level = axis - Vector3.up * Vector3.Dot(axis, Vector3.up);
            float run = Vector3.Dot(axis, level);
            float along = run > 0.05f
                ? Vector3.Dot(center - rope, level) / run
                : Vector3.Dot(center - rope, axis);
            station = rope + axis * along;
            Vector3 down = center - station;
            down -= axis * Vector3.Dot(down, axis);
            if (down.sqrMagnitude < 1e-8f)
                down = -Vector3.up;
            down.Normalize();
            return station + down * collarRadius;
        }

        private static void Aim(ref Strand strand, Vector3 from, Vector3 to)
        {
            Vector3 delta = to - from;
            float length = delta.magnitude;
            if (length < 1e-5f)
            {
                strand.Scale = Vector3.zero;
                return;
            }

            strand.Pos = from;
            strand.Rot = CordTurn.Look(delta / length);
            strand.Scale = new Vector3(1f, 1f, length);
        }

        private static void BuildRing(Mesh mesh, float cordRadius, PlantPots.Spec spec)
        {
            float radius = RingRadius(cordRadius, spec);
            float y = spec.Rim - RingBelowRim;
            var loop = new Vector3[RingSegments + 1];
            for (int i = 0; i <= RingSegments; i++)
            {
                float angle = i * Mathf.PI * 2f / RingSegments;
                loop[i] = new Vector3(Mathf.Cos(angle) * radius, y, Mathf.Sin(angle) * radius);
            }

            RopeMesh.Build(mesh, new List<Vector3[]> { loop }, cordRadius, Profile.Kind.Plant);
        }

        // A closed turn around the rope, in the plane across it. The child is turned to the
        // rope each frame, so the loop is built once.
        private static void BuildCollar(Mesh mesh, float cordRadius, float lineRadius)
        {
            float radius = CollarRadius(cordRadius, lineRadius);
            var loop = new Vector3[RingSegments + 1];
            for (int i = 0; i <= RingSegments; i++)
            {
                float angle = i * Mathf.PI * 2f / RingSegments;
                loop[i] = new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f);
            }

            RopeMesh.Build(mesh, new List<Vector3[]> { loop }, cordRadius, Profile.Kind.Plant);
        }

        private static void BuildSpan(Mesh mesh, float cordRadius)
        {
            RopeMesh.Build(mesh, new[] { Vector3.zero, Vector3.forward }, cordRadius, Profile.Kind.Plant);
        }

        private void EnsureCollar()
        {
            if (_collar != null)
                return;
            // modcheck-ignore: SW605 fresh tie object, there is nothing to reuse
            var go = new GameObject("clothesline plant collar");
            go.transform.SetParent(transform, false);
            _collarMesh = new Mesh { name = "Clothesline Plant Collar" };
            // modcheck-ignore: SW605 fresh tie object, there is nothing to reuse
            MeshFilter filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = _collarMesh;
            // modcheck-ignore: SW605 fresh tie object, there is nothing to reuse
            MeshRenderer renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = Cord();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            _collar = go.transform;
        }

        private void EnsureStrand(int index)
        {
            if (_strand[index] != null)
                return;
            // modcheck-ignore: SW605 fresh tie object, there is nothing to reuse
            var go = new GameObject("clothesline plant strand " + index);
            go.transform.SetParent(transform, false);
            _strandMesh[index] = new Mesh { name = "Clothesline Plant Strand" };
            // modcheck-ignore: SW605 fresh tie object, there is nothing to reuse
            MeshFilter filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = _strandMesh[index];
            // modcheck-ignore: SW605 fresh tie object, there is nothing to reuse
            MeshRenderer renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = Cord();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            _strand[index] = go.transform;
        }

        private static Material Cord()
        {
            if (_cord != null)
                return _cord;
            Material shared = RopeMesh.Material;
            if (shared == null)
                return null;
            _cord = new Material(shared);
            _cord.name = "Dizzy Clothesline Plant Cord";
            _cord.color = new Color(0.28f, 0.14f, 0.06f);
            return _cord;
        }

        private void OnDestroy()
        {
            if (_mesh != null)
                Destroy(_mesh);
            if (_collarMesh != null)
                Destroy(_collarMesh);
            for (int i = 0; i < Strands; i++)
            {
                if (_strandMesh[i] != null)
                    Destroy(_strandMesh[i]);
            }
        }
    }
}
