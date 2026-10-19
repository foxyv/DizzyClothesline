using System.Collections.Generic;
using UnityEngine;

namespace Dizzy.Clothesline
{
    // A small loop of the spyglass cord. It passes through the handle, ring or the
    // top of a hook line and over the clothesline. Those items already hang; this
    // only draws the cord.
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

        private Mesh _mesh;
        private Vector3 _rope;
        private Vector3 _axis;
        private Vector3 _passage;
        private float _lineRadius;

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
            ClotheslineLine line = anchor.Line;
            ShipItem ship = anchor.Ship;
            if (line == null || ship == null || anchor.Record == null)
                return;

            float lineRadius = ClotheslineConfig.Thickness.Value * 0.5f;
            Vector3 rope = ship.transform.InverseTransformPoint(line.RopeWorld(anchor.Record.S));
            Vector3 axis = ship.transform.InverseTransformDirection(line.RopeDirectionWorld(anchor.Record.S));
            if (axis.sqrMagnitude < 1e-8f)
                return;
            axis.Normalize();

            if (_mesh != null
                && (rope - _rope).sqrMagnitude < 1e-8f
                && (axis - _axis).sqrMagnitude < 1e-8f
                && (passage - _passage).sqrMagnitude < 1e-8f
                && Mathf.Abs(lineRadius - _lineRadius) < 1e-5f)
                return;

            if (_mesh == null)
            {
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

            Build(_mesh, rope, axis, passage, lineRadius, lineRadius / 3f, metal, wire);
            _rope = rope;
            _axis = axis;
            _passage = passage;
            _lineRadius = lineRadius;
        }

        // A loop standing up over the rope. The bottom bites a little into the handle
        // and the crown bites the same fraction into the clothesline.
        private static void Build(
            Mesh mesh, Vector3 rope, Vector3 ropeAxis, Vector3 passage,
            float lineRadius, float cordRadius, float metalHalf, float wire)
        {
            Vector3 up = Vector3.up - ropeAxis * Vector3.Dot(Vector3.up, ropeAxis);
            if (up.sqrMagnitude < 1e-8f)
                up = Vector3.forward;
            up.Normalize();
            Vector3 flank = Vector3.Cross(ropeAxis, up).normalized;

            Vector3 rel = passage - rope;
            float passU = Vector3.Dot(rel, flank);
            float passV = Vector3.Dot(rel, up);
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

            RopeMesh.Build(mesh, new List<Vector3[]> { path.ToArray() }, cordRadius);
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

        private void OnDestroy()
        {
            if (_mesh != null)
                Destroy(_mesh);
        }
    }
}
