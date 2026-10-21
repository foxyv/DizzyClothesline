using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace Dizzy.Clothesline
{
    // A small loop of the spyglass cord. It passes through the handle, ring, the
    // top of a hook line, or the eye on a hammer, and over the clothesline. Those
    // items already hang; this only draws the cord.
    internal sealed class LoopTie : MonoBehaviour
    {
        private const int KettleA = 382;
        private const int KettleE = 383;
        private const int KettleM = 384;
        private const int TelltaleE = 502;
        private const int TelltaleA = 510;
        private const int TelltaleM = 511;
        private const int Anemometer = 517;

        private static Material _cord;

        // A one-frame miss can stretch the ring. Recheck on this interval and, if the
        // handle and the rope are still too far apart, draw the ring at its real size.
        private const float CheckInterval = 1f;

        private Mesh _mesh;
        private HandleMotion _handle;
        private RingMotion _ring;

        // A handle ring, built once for a gap of this length. A roll of the boat
        // turns it onto the rope instead of rebuilding the mesh.
        internal struct HandleMotion
        {
            public bool Built;
            public float Length;
            public float LineRadius;
            public float Metal;
            public float Wire;
            public bool Place;
            public Vector3 Position;
            public Quaternion Rotation;
        }

        // The hammer's ring is a fixed cord. It is redrawn on the one-second check.
        internal struct RingMotion
        {
            public bool Built;
            public Vector3 Rope;
            public Vector3 Axis;
            public Vector3 Passage;
            public float LineRadius;
            public float NextCheck;
        }

        internal static void Sync(LineAnchor anchor)
        {
            ShipItem ship = anchor != null ? anchor.Ship : null;
            Vector3 passage;
            float metal;
            float wire;
            if (!TrySeat(ship, out passage, out metal, out wire))
            {
                Clear(anchor);
                return;
            }

            if (anchor.Loop == null)
            {
                // modcheck-ignore: SW605 the loop is new each time one of these is hung
                var go = new GameObject("clothesline loop tie");
                go.transform.SetParent(ship.transform, false);
                anchor.Loop = go.AddComponent<LoopTie>();
            }

            anchor.Loop.Follow(anchor, passage, metal, wire);
        }

        internal static void Clear(LineAnchor anchor)
        {
            if (anchor == null || anchor.Loop == null)
                return;
            Destroy(anchor.Loop.gameObject);
            anchor.Loop = null;
        }

        internal static bool IsWindicator(ShipItem ship)
        {
            SaveablePrefab save = ship != null ? ship.Getsaveable() : null;
            int id = save != null ? save.prefabIndex : 0;
            return id == TelltaleE || id == TelltaleA || id == TelltaleM || id == Anemometer;
        }

        // The underside of the handle, where the cord should bite in, and how thick
        // that wire is. metal is how far the wire reaches to either side. Measured
        // from the meshes.
        private static bool TrySeat(ShipItem ship, out Vector3 passage, out float metal, out float wire)
        {
            passage = Vector3.zero;
            metal = 0f;
            wire = 0f;
            if (ship == null)
                return false;

            if (ship.GetComponent<ShipItemKettle>() != null)
            {
                SaveablePrefab save = ship.Getsaveable();
                int index = save != null ? save.prefabIndex : 0;
                if (index == KettleE)
                {
                    passage = new Vector3(0f, 0.222f, 0f);
                    metal = 0.012f;
                    wire = 0.006f;
                }
                else if (index == KettleM)
                {
                    passage = new Vector3(0f, 0.268f, 0f);
                    metal = 0.011f;
                    wire = 0.020f;
                }
                else
                {
                    // Al'Ankh, and any kettle whose mesh we have not measured.
                    passage = new Vector3(0f, index == KettleA ? 0.294f : 0.28f, 0f);
                    metal = 0.014f;
                    wire = 0.013f;
                }
                return true;
            }

            if (ship.name == "bucket")
            {
                Transform handle = ship.transform.Find("bucket handle");
                if (handle == null)
                    return false;
                // The underside of the bail. The handle is upright while it hangs.
                Vector3 onHandle = handle.TransformPoint(new Vector3(0f, 0.197f, 0f));
                passage = ship.transform.InverseTransformPoint(onHandle);
                metal = 0.011f;
                wire = 0.014f;
                return true;
            }

            if (IsWindicator(ship))
            {
                // The underside of the top of the ring.
                passage = new Vector3(0f, -0.010f, 0f);
                metal = 0.014f;
                wire = 0.028f;
                return true;
            }

            // The inner top of the eye on the crown. The ring bites that cord, so
            // the metal and the wire are the cord itself, not a kettle handle.
            if (HammerTie.Is(ship))
            {
                float cord = HammerTie.CordRadius;
                passage = HammerTie.HangPoint;
                metal = cord;
                wire = cord * 2f;
                return true;
            }

            // A strung line of hooks or lures. The strings meet at the origin.
            if (ship.GetComponent<ShipItemFishingHook>() != null && ship.transform.Find("HookLineCord") != null)
            {
                passage = Vector3.zero;
                metal = 0.0014f;
                wire = 0.0028f;
                return true;
            }

            return false;
        }

        private void Follow(LineAnchor anchor, Vector3 passage, float metal, float wire)
        {
            long stamp = Profile.Stamp();
            try
            {
            ClotheslineLine line = anchor.Line;
            ShipItem ship = anchor.Ship;
            if (line == null || ship == null || anchor.Record == null)
                return;

            float lineRadius = ClotheslineConfig.Thickness.Value * 0.5f;
            // A bad sample must not leave the cord scaled up from an earlier frame.
            transform.localScale = Vector3.one;

            // A hammer's ring is a fixed cord on the eye, the way a bundle's string
            // is fixed on the bunch. It stands up the shaft. Measuring the live gap
            // rebuilt it at a new size whenever a load moved the rope.
            if (HammerTie.Is(ship))
            {
                float gap = ClotheslineConfig.HangDrop.Value + HammerTie.Drop(ship);
                if (_mesh == null)
                    EnsureMesh();
                StepHammer(ref _ring, passage, gap, lineRadius, metal, wire, Time.time, _mesh);
                return;
            }

            // The handle stays put on the item and the rope moves as the boat heels.
            // Rebuilding the cord for that shift was hundreds of meshes a second.
            // The mesh is the ring for this gap, stood on local Y. Turning the object
            // lays that Y along the handle-to-rope direction.
            Vector3 liveRope = ship.transform.InverseTransformPoint(line.RopeWorld(anchor.Record.S));
            Vector3 liveAxis = ship.transform.InverseTransformDirection(line.RopeDirectionWorld(anchor.Record.S));
            if (_mesh == null && !_handle.Built)
                EnsureMesh();
            StepHandle(ref _handle, passage, liveRope, liveAxis, lineRadius, metal, wire, _mesh);
            if (!_handle.Place)
                return;
            transform.localPosition = _handle.Position;
            transform.localRotation = _handle.Rotation;
            }
            finally
            {
                Profile.Add(Profile.Kind.Loop, stamp);
            }
        }

        internal static void StepHammer(
            ref RingMotion motion, Vector3 passage, float gap, float lineRadius,
            float metal, float wire, float time, Mesh mesh)
        {
            // Local Z is the shaft, up toward the head. The eye is in the YZ plane,
            // so the bar through it runs on local X and the rope, when the boat is
            // level, lies on local Y.
            Vector3 rope = passage + Vector3.forward * gap;
            Vector3 axis = Vector3.up;
            bool check = time >= motion.NextCheck;
            if (!check
                && motion.Built
                && (rope - motion.Rope).sqrMagnitude < 1e-8f
                && (axis - motion.Axis).sqrMagnitude < 1e-8f
                && (passage - motion.Passage).sqrMagnitude < 1e-8f
                && Mathf.Abs(lineRadius - motion.LineRadius) < 1e-5f)
                return;

            Build(mesh, rope, axis, passage, lineRadius, lineRadius / 3f, metal, wire, check, motion.Built, Vector3.forward);
            if (check)
                motion.NextCheck = time + CheckInterval;
            motion.Rope = rope;
            motion.Axis = axis;
            motion.Passage = passage;
            motion.LineRadius = lineRadius;
            motion.Built = true;
        }

        internal static void StepHandle(
            ref HandleMotion motion, Vector3 passage, Vector3 liveRope, Vector3 liveAxis,
            float lineRadius, float metal, float wire, Mesh mesh)
        {
            Vector3 rel = passage - liveRope;
            bool bad = liveAxis.sqrMagnitude < 1e-8f
                || !Finite(rel) || !Finite(liveRope) || !Finite(liveAxis) || !Finite(passage)
                || rel.sqrMagnitude > 0.2f * 0.2f;
            if (bad)
            {
                if (motion.Built)
                    return;
                rel = Vector3.up * 0.03f;
                liveAxis = Vector3.right;
            }

            float length = rel.magnitude;
            if (length < 1e-4f)
            {
                if (motion.Built)
                    return;
                rel = Vector3.up * 0.03f;
                length = 0.03f;
                liveAxis = Vector3.right;
            }

            if (!motion.Built
                || Mathf.Abs(length - motion.Length) > 0.01f
                || Mathf.Abs(lineRadius - motion.LineRadius) > 1e-4f
                || Mathf.Abs(metal - motion.Metal) > 1e-5f
                || Mathf.Abs(wire - motion.Wire) > 1e-5f)
            {
                Build(mesh, -Vector3.up * length, Vector3.right, Vector3.zero, lineRadius, lineRadius / 3f, metal, wire, true, motion.Built, Vector3.up);
                motion.Built = true;
                motion.Length = length;
                motion.LineRadius = lineRadius;
                motion.Metal = metal;
                motion.Wire = wire;
            }

            Vector3 relDir = rel / length;
            Vector3 axisDir = liveAxis.normalized;
            axisDir -= relDir * Vector3.Dot(axisDir, relDir);
            if (axisDir.sqrMagnitude < 1e-8f)
                axisDir = Vector3.Cross(relDir, Vector3.right);
            if (axisDir.sqrMagnitude < 1e-8f)
                axisDir = Vector3.Cross(relDir, Vector3.forward);
            axisDir.Normalize();
            Vector3 flank = Vector3.Cross(axisDir, relDir);
            if (flank.sqrMagnitude < 1e-8f)
                return;

            motion.Place = true;
            motion.Position = passage;
            motion.Rotation = CordTurn.Look(flank.normalized, relDir);
        }

        private void EnsureMesh()
        {
            if (_mesh != null)
                return;
            _mesh = new Mesh { name = "Clothesline Loop Tie" };
            // modcheck-ignore: SW605 fresh tie object, there is nothing to reuse
            MeshFilter filter = gameObject.AddComponent<MeshFilter>();
            filter.sharedMesh = _mesh;
            // modcheck-ignore: SW605 fresh tie object, there is nothing to reuse
            MeshRenderer renderer = gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = Cord();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        // A loop standing up over the rope. The bottom bites a little into the handle
        // and the crown bites the same fraction into the clothesline.
        private static void Build(
            Mesh mesh, Vector3 rope, Vector3 ropeAxis, Vector3 passage,
            float lineRadius, float cordRadius, float metalHalf, float wire, bool fix, bool drawn, Vector3 upHint)
        {
            Vector3 upSource = upHint.sqrMagnitude > 1e-8f ? upHint : Vector3.up;
            Vector3 up = upSource - ropeAxis * Vector3.Dot(upSource, ropeAxis);
            if (up.sqrMagnitude < 1e-8f)
                up = Vector3.forward;
            up.Normalize();
            Vector3 flank = Vector3.Cross(ropeAxis, up);
            if (flank.sqrMagnitude < 1e-8f)
                flank = Vector3.right;
            flank.Normalize();

            // The handle and the rope are a couple of centimetres apart when the item
            // is on the pin. A physics or scale sample can put them metres apart for a
            // frame, and the ring was growing to cover that whole gap. Ignore that
            // frame. On the timed check, draw the ring at its usual size instead.
            const float maxReach = 0.2f;
            Vector3 rel = passage - rope;
            if (!Finite(rel) || !Finite(rope) || rel.sqrMagnitude > maxReach * maxReach)
            {
                // drawn is the previous ring. vertexCount would ask the player.
                if (drawn && !fix)
                    return;
                rel = up * 0.03f;
                rope = passage - rel;
            }

            // A sideways miss of a centimetre still widens the ring onto the handle.
            // It does not get to set the diameter.
            float passU = Mathf.Clamp(Vector3.Dot(rel, flank), -0.05f, 0.05f);
            float passV = Mathf.Clamp(Vector3.Dot(rel, up), -0.12f, 0.12f);
            // Five percent of the wire, so the cord sits in the handle instead of above it.
            float seat = passV - cordRadius + wire * 0.05f;
            float arc = lineRadius + cordRadius;
            float side = Mathf.Max(metalHalf + cordRadius + 0.006f, arc, Mathf.Abs(passU) + arc);
            // Five percent of the clothesline, taken out of the clearance over its top.
            float peak = lineRadius + cordRadius - lineRadius * 2f * 0.05f;
            // The split sits a little above the bottom bar, with a short stub still on the bar.
            float span = Mathf.Abs(seat);
            float riseSign = seat < 0f ? 1f : -1f;
            float gap = Mathf.Min(cordRadius * 5f, span * 0.35f);
            float stub = Mathf.Min(cordRadius * 2.5f, Mathf.Max(0f, span - gap) * 0.45f);
            float foot = seat + riseSign * stub;
            float open = seat + riseSign * (stub + gap);
            float bow = cordRadius * 1.35f;
            float left = passU - side;
            float right = passU + side;

            var path = new List<Vector3>(48);
            const int footSteps = 3;
            for (int i = 0; i <= footSteps; i++)
            {
                float v = Mathf.Lerp(foot, seat, i / (float)footSteps);
                path.Add(rope + flank * left + up * v);
            }
            const int bottom = 8;
            for (int i = 1; i <= bottom; i++)
            {
                float t = i / (float)bottom;
                float u = Mathf.Lerp(left, right, t);
                float v = seat - bow * Mathf.Sin(Mathf.PI * t);
                path.Add(rope + flank * u + up * v);
            }
            const int rise = 4;
            for (int i = 1; i <= rise; i++)
            {
                float v = Mathf.Lerp(seat, 0f, i / (float)rise);
                path.Add(rope + flank * (passU + side) + up * v);
            }
            const int arcSteps = 12;
            for (int i = 1; i < arcSteps; i++)
            {
                float t = Mathf.PI * i / arcSteps;
                path.Add(rope + flank * (passU + side * Mathf.Cos(t)) + up * (peak * Mathf.Sin(t)));
            }
            for (int i = 0; i <= rise; i++)
            {
                float v = Mathf.Lerp(0f, open, i / (float)rise);
                path.Add(rope + flank * (passU - side) + up * v);
            }

            RopeMesh.Build(mesh, new List<Vector3[]> { path.ToArray() }, cordRadius, Profile.Kind.Loop);
        }

        // The same dark brown as the spyglass cord.
        private static Material Cord()
        {
            if (_cord != null)
                return _cord;
            Material shared = RopeMesh.Material;
            if (shared == null)
                return null;
            _cord = new Material(shared);
            _cord.name = "Dizzy Clothesline Loop";
            _cord.color = new Color(0.28f, 0.14f, 0.06f);
            return _cord;
        }

        private void LateUpdate()
        {
            if (transform.localScale != Vector3.one)
                transform.localScale = Vector3.one;
        }

        private static bool Finite(Vector3 v)
        {
            return !float.IsNaN(v.x) && !float.IsNaN(v.y) && !float.IsNaN(v.z)
                && !float.IsInfinity(v.x) && !float.IsInfinity(v.y) && !float.IsInfinity(v.z);
        }

        private void OnDestroy()
        {
            if (_mesh != null)
                Destroy(_mesh);
        }
    }
}
