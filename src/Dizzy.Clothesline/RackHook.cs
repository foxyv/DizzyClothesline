using UnityEngine;

namespace Dizzy.Clothesline
{
    // The date rack's iron wire stops at the pin, which a lamp hook covers. On a
    // line that wire continues up and over the rope, so the rack hangs from it.
    internal sealed class RackHook : MonoBehaviour
    {
        // The rack's own wire, in metres. Kept the same so the hook reads as that wire.
        private const float WireRadius = 0.003f;
        private const int DatePrefab = 42;
        private const string StickName = "FirewoodBundleStick";

        private static Material _iron;

        private Transform _arc;
        private Transform _rise;
        private Mesh _arcMesh;
        private Mesh _riseMesh;
        private Motion _motion;

        internal struct Motion
        {
            public bool Built;
            public int Bad;
            public float LineRadius;
            public Vector3 ArcStart;
            public Vector3 ArcPos;
            public Quaternion ArcRot;
            public Vector3 RisePos;
            public Quaternion RiseRot;
            public Vector3 RiseScale;
        }

        internal static bool IsRack(ShipItem ship)
        {
            if (!(ship is ShipItemFood) || ship.transform.Find(StickName) == null)
                return false;
            if (ship.name == "date" || ship.name == "dates")
                return true;
            SaveablePrefab save = ship.Getsaveable();
            return save != null && save.prefabIndex == DatePrefab;
        }

        internal static void Sync(LineAnchor anchor)
        {
            ShipItem ship = anchor != null ? anchor.Ship : null;
            if (!IsRack(ship))
            {
                Clear(anchor);
                return;
            }

            if (anchor.Rack == null)
            {
                // modcheck-ignore: SW605 the hook is new each time a rack is hung
                var go = new GameObject("clothesline rack hook");
                go.transform.SetParent(ship.transform, false);
                anchor.Rack = go.AddComponent<RackHook>();
            }

            anchor.Rack.Follow(anchor);
        }

        internal static void Clear(LineAnchor anchor)
        {
            if (anchor == null || anchor.Rack == null)
                return;
            Destroy(anchor.Rack.gameObject);
            anchor.Rack = null;
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

            float lineRadius = ClotheslineConfig.Thickness.Value * 0.5f;
            Vector3 rope = ship.transform.InverseTransformPoint(line.RopeWorld(anchor.Record.S));
            Vector3 axis = ship.transform.InverseTransformDirection(line.RopeDirectionWorld(anchor.Record.S));
            if (axis.sqrMagnitude < 1e-8f || !Finite(rope) || !Finite(axis))
                return;
            axis.Normalize();

            // The hook is built once around a canonical rope. Turning it lays the
            // arc over the real rope, and the rise is scaled out to meet the rack.
            if (Wants(_motion, lineRadius))
            {
                Ensure(ref _arc, ref _arcMesh, "clothesline rack arc");
                Ensure(ref _rise, ref _riseMesh, "clothesline rack rise");
            }

            Step(ref _motion, rope, axis, lineRadius, _arcMesh, _riseMesh);
            if (_arc == null || !_motion.Built)
                return;

            _arc.localPosition = _motion.ArcPos;
            _arc.localRotation = _motion.ArcRot;
            _arc.localScale = Vector3.one;
            _rise.localPosition = _motion.RisePos;
            _rise.localRotation = _motion.RiseRot;
            _rise.localScale = _motion.RiseScale;
            }
            finally
            {
                Profile.Add(Profile.Kind.Rack, stamp);
            }
        }

        internal static bool Wants(in Motion motion, float lineRadius)
        {
            return !motion.Built || Mathf.Abs(lineRadius - motion.LineRadius) > 1e-5f;
        }

        internal static void Step(ref Motion motion, Vector3 rope, Vector3 axis, float lineRadius, Mesh arcMesh, Mesh riseMesh)
        {
            if (!CordGuard.Usable(ref motion.Bad, rope, axis))
                return;
            if (Wants(motion, lineRadius))
            {
                motion.ArcStart = BuildArc(arcMesh, lineRadius);
                BuildRise(riseMesh);
                motion.LineRadius = lineRadius;
                motion.Built = true;
            }

            Vector3 toward = -rope;
            toward -= axis * Vector3.Dot(toward, axis);
            if (toward.sqrMagnitude < 1e-8f)
                toward = Vector3.down;
            toward.Normalize();
            Vector3 flank = Vector3.Cross(axis, toward);
            if (flank.sqrMagnitude < 1e-8f)
                return;

            motion.ArcPos = rope;
            motion.ArcRot = CordTurn.Look(flank.normalized, toward);
            Vector3 root = Vector3.down * 0.008f;
            Vector3 meet = motion.ArcPos + motion.ArcRot * motion.ArcStart;
            AimRise(ref motion.RisePos, ref motion.RiseRot, ref motion.RiseScale, root, meet);
        }

        // The arc in its own space. Local X is the rope, local Y faces the rack.
        private static Vector3 BuildArc(Mesh mesh, float lineRadius)
        {
            float arcR = lineRadius + WireRadius;
            Vector3 toward = Vector3.up;
            Vector3 flank = Vector3.Cross(Vector3.right, toward).normalized;
            const float turn = Mathf.PI + 0.65f + Mathf.PI * 0.5f;
            const int segs = 26;
            var arc = new Vector3[segs + 1];
            for (int i = 0; i <= segs; i++)
            {
                float ang = turn * i / segs;
                arc[i] = (toward * Mathf.Cos(ang) + flank * Mathf.Sin(ang)) * arcR;
            }

            RopeMesh.Build(mesh, arc, WireRadius, Profile.Kind.Rack);
            return arc[0];
        }

        private static void BuildRise(Mesh mesh)
        {
            RopeMesh.Build(mesh, new[] { Vector3.zero, Vector3.forward }, WireRadius, Profile.Kind.Rack);
        }

        private static void AimRise(ref Vector3 pos, ref Quaternion rot, ref Vector3 scale, Vector3 from, Vector3 to)
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
            // modcheck-ignore: SW605 fresh hook object, there is nothing to reuse
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            mesh = new Mesh { name = name };
            // modcheck-ignore: SW605 fresh hook object, there is nothing to reuse
            MeshFilter filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            // modcheck-ignore: SW605 fresh hook object, there is nothing to reuse
            MeshRenderer renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = Iron();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            child = go.transform;
        }

        private static bool Finite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsNaN(value.y) && !float.IsNaN(value.z)
                && !float.IsInfinity(value.x) && !float.IsInfinity(value.y) && !float.IsInfinity(value.z);
        }

        // The same flat black the rack's own wire uses.
        private static Material Iron()
        {
            if (_iron != null)
                return _iron;
            Shader shader = Shader.Find("Standard");
            if (shader == null)
                shader = Shader.Find("Legacy Shaders/Diffuse");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");
            if (shader == null)
                return null;

            _iron = new Material(shader);
            _iron.name = "Dizzy Clothesline Rack Wire";
            _iron.color = new Color(0.06f, 0.06f, 0.06f);
            if (shader.name == "Standard")
            {
                _iron.SetFloat("_Metallic", 0f);
                _iron.SetFloat("_Glossiness", 0.12f);
            }
            return _iron;
        }

        private void OnDestroy()
        {
            if (_arcMesh != null)
                Destroy(_arcMesh);
            if (_riseMesh != null)
                Destroy(_riseMesh);
        }
    }
}
