using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Dizzy.Clothesline
{
    // A thin cord eye on the crown of a hammer. The eye does not reach the rope.
    // A D-ring, drawn by LoopTie, passes through the top of the eye and over the
    // clothesline.
    //
    // The handle lies along local Z. The head is the wide end at the high Z.
    // After the hang pose the rope lies along local Y, so the eye stands in that
    // plane and the ring's bar threads it along local X. A hammer is not hangable
    // in the game. HangableItem is added only while it is on a line, and taken off
    // again when it comes down.
    internal sealed class HammerTie : MonoBehaviour
    {
        // Handle axis and the head, measured from the mesh. The crown is the small
        // cap at the high Z, on the shaft at x = 0.10.
        private const float AxisX = 0.10f;
        private const float AxisY = -0.002f;
        private const float HeadTop = 0.430f;

        // Hole in the eye, in metres. Large enough to clear the crown and to leave
        // the top of the eye above the head.
        private const float EyeInside = 0.025f;

        // Inner top of the eye, on the shaft. The pose puts this point on the line's
        // down through the rope, and the D-ring bites it.
        internal static Vector3 HangPoint
        {
            get { return new Vector3(AxisX, AxisY, HeadTop + EyeInside); }
        }

        internal static float CordRadius
        {
            get { return ClotheslineConfig.Thickness.Value * 0.5f / 3f; }
        }

        private static AccessTools.FieldRef<HangableItem, bool> _disallow;
        private static AccessTools.FieldRef<HangableItem, bool> _lockX;
        private static AccessTools.FieldRef<HangableItem, bool> _lockZ;
        private static Material _cord;

        private Mesh _mesh;
        private float _cordRadius;

        static HammerTie()
        {
            try
            {
                _disallow = AccessTools.FieldRefAccess<HangableItem, bool>("disallowHangingOnTrigger");
                _lockX = AccessTools.FieldRefAccess<HangableItem, bool>("lockX");
                _lockZ = AccessTools.FieldRefAccess<HangableItem, bool>("lockZ");
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning("Could not keep a hung hammer off lamp hooks. " + ex.Message);
            }
        }

        internal static bool Is(ShipItem ship)
        {
            return ship != null && ship.GetComponent<ShipItemHammer>() != null;
        }

        // The pose puts the inner top of the eye on the pin. This is only the extra
        // gap under the rope, so the eye is not buried in the line.
        internal static float Drop(ShipItem ship)
        {
            if (!Is(ship))
                return 0f;
            return ClotheslineConfig.Thickness.Value * 0.5f + 0.012f - ClotheslineConfig.HangDrop.Value;
        }

        internal static HangableItem Prepare(ShipItem ship)
        {
            if (ship == null)
                return null;
            HangableItem hang = ship.GetComponent<HangableItem>();
            if (hang == null)
            {
                // modcheck-ignore: SW605 a hammer is not hangable in the game; this exists only while it is on a line
                hang = ship.gameObject.AddComponent<HangableItem>();
            }
            if (_disallow != null)
                _disallow(hang) = true;
            // The game's lock keeps X and Z at 0, so the hammer can only yaw around
            // world up through the origin. The hang turns about the eye.
            if (_lockX != null)
                _lockX(hang) = false;
            if (_lockZ != null)
                _lockZ(hang) = false;
            return hang;
        }

        internal static void Detach(ShipItem ship)
        {
            if (!Is(ship))
                return;
            HangableItem hang = ship.GetComponent<HangableItem>();
            if (hang != null)
                Destroy(hang);
        }

        internal static void Sync(LineAnchor anchor)
        {
            ShipItem ship = anchor != null ? anchor.Ship : null;
            if (!Is(ship))
            {
                Clear(anchor);
                return;
            }

            if (anchor.Hammer == null)
            {
                // modcheck-ignore: SW605 the tie is new each time a hammer is hung
                var go = new GameObject("clothesline hammer tie");
                go.transform.SetParent(ship.transform, false);
                anchor.Hammer = go.AddComponent<HammerTie>();
            }

            anchor.Hammer.Follow();
        }

        internal static void Clear(LineAnchor anchor)
        {
            if (anchor == null || anchor.Hammer == null)
                return;
            Destroy(anchor.Hammer.gameObject);
            anchor.Hammer = null;
        }

        // The eye is fixed on the hammer. A roll of the boat does not move it, so
        // it is built once. Only a change of cord thickness draws it again.
        private void Follow()
        {
            float cordRadius = CordRadius;
            if (cordRadius < 1e-5f)
                return;
            if (_mesh != null && Mathf.Abs(cordRadius - _cordRadius) < 1e-5f)
                return;

            if (_mesh == null)
            {
                _mesh = new Mesh { name = "Clothesline Hammer Tie" };
                // modcheck-ignore: SW605 fresh tie object, there is nothing to reuse
                MeshFilter filter = gameObject.AddComponent<MeshFilter>();
                filter.sharedMesh = _mesh;
                // modcheck-ignore: SW605 fresh tie object, there is nothing to reuse
                MeshRenderer renderer = gameObject.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = Cord();
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            Build(_mesh, cordRadius);
            _cordRadius = cordRadius;
        }

        // One closed loop in the YZ plane, centered on the crown. The hole is
        // EyeInside across, so the cord itself sits just outside the cap.
        private static void Build(Mesh mesh, float cordRadius)
        {
            Vector3 center = new Vector3(AxisX, AxisY, HeadTop);
            float loopR = EyeInside + cordRadius;
            const int loopSegs = 24;
            var loop = new Vector3[loopSegs + 1];
            for (int i = 0; i <= loopSegs; i++)
            {
                float ang = i * Mathf.PI * 2f / loopSegs;
                loop[i] = center + (Vector3.up * Mathf.Cos(ang) + Vector3.forward * Mathf.Sin(ang)) * loopR;
            }

            RopeMesh.Build(mesh, new List<Vector3[]> { loop }, cordRadius);
        }

        private static Material Cord()
        {
            if (_cord != null)
                return _cord;
            Material shared = RopeMesh.Material;
            if (shared == null)
                return null;
            _cord = new Material(shared);
            _cord.name = "Dizzy Clothesline Hammer Cord";
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
