using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Dizzy.Clothesline
{
    // A thin cord that hangs a hammer head up. One turn sits on the handle just
    // under the head, and two strands run up the sides of the head and meet on
    // top of the clothesline.
    //
    // The handle lies along local Z. The head is the wide end at the high Z.
    // A hammer is not hangable in the game. HangableItem is added only while it
    // is on a line, and taken off again when it comes down.
    internal sealed class HammerTie : MonoBehaviour
    {
        // Handle axis and the head, measured from the mesh. The head begins at
        // about 0.33 and the handle below it is a 3.2 cm radius shaft at x = 0.10.
        private const float AxisX = 0.10f;
        private const float AxisY = -0.002f;

        // From the mesh origin to the handle shaft. The game pins the origin, so the
        // pose subtracts this or the hammer hangs beside the rope.
        internal static Vector3 ShaftShift
        {
            get { return new Vector3(AxisX, AxisY, 0f); }
        }
        private const float HandleRadius = 0.032f;
        private const float WrapZ = 0.292f;
        private const float HeadTop = 0.430f;
        private const float HeadHalf = 0.040f;

        private static AccessTools.FieldRef<HangableItem, bool> _disallow;
        private static Material _cord;

        private Mesh _mesh;
        private Vector3 _rope;
        private Vector3 _axis;
        private float _ropeRadius;

        static HammerTie()
        {
            try
            {
                _disallow = AccessTools.FieldRefAccess<HangableItem, bool>("disallowHangingOnTrigger");
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

        // How far below the usual pin the origin sits so the top of the head, not
        // the origin, is the part just under the rope.
        internal static float Drop(ShipItem ship)
        {
            if (!Is(ship))
                return 0f;
            return HeadTop - ClotheslineConfig.HangDrop.Value + ClotheslineConfig.Thickness.Value * 0.5f + 0.012f;
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

            anchor.Hammer.Follow(anchor);
        }

        internal static void Clear(LineAnchor anchor)
        {
            if (anchor == null || anchor.Hammer == null)
                return;
            Destroy(anchor.Hammer.gameObject);
            anchor.Hammer = null;
        }

        private void Follow(LineAnchor anchor)
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

            if (_mesh != null
                && (rope - _rope).sqrMagnitude < 1e-8f
                && (axis - _axis).sqrMagnitude < 1e-8f
                && Mathf.Abs(ropeRadius - _ropeRadius) < 1e-5f)
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

            Build(_mesh, rope, axis, ropeRadius, ropeRadius / 3f);
            _rope = rope;
            _axis = axis;
            _ropeRadius = ropeRadius;
        }

        // One turn on the handle, then two strands up the narrow sides of the head.
        // They stop where they meet, on top of the clothesline.
        private static void Build(Mesh mesh, Vector3 rope, Vector3 ropeAxis, float lineRadius, float cordRadius)
        {
            Vector3 center = new Vector3(AxisX, AxisY, WrapZ);
            float loopR = HandleRadius + cordRadius * 0.35f;
            Vector3 side = Vector3.right;
            Vector3 around = Vector3.up;

            const int loopSegs = 24;
            var loop = new Vector3[loopSegs + 1];
            for (int i = 0; i <= loopSegs; i++)
            {
                float ang = i * Mathf.PI * 2f / loopSegs;
                loop[i] = center + (side * Mathf.Cos(ang) + around * Mathf.Sin(ang)) * loopR;
            }

            Vector3 leftLoop = center + side * loopR;
            Vector3 rightLoop = center - side * loopR;
            float flare = HeadHalf + cordRadius;
            Vector3 leftHead = new Vector3(AxisX + flare, AxisY, HeadTop - 0.02f);
            Vector3 rightHead = new Vector3(AxisX - flare, AxisY, HeadTop - 0.02f);

            Vector3 station = rope + ropeAxis * Vector3.Dot(center - rope, ropeAxis);
            Vector3 down = center - station;
            down -= ropeAxis * Vector3.Dot(down, ropeAxis);
            if (down.sqrMagnitude < 1e-8f)
                down = -Vector3.forward;
            down.Normalize();
            Vector3 meet = station - down * (lineRadius + cordRadius * 0.5f);
            float split = cordRadius * 0.4f;

            var paths = new List<Vector3[]>
            {
                loop,
                Strand(leftLoop, leftHead, meet - ropeAxis * split),
                Strand(rightLoop, rightHead, meet + ropeAxis * split),
            };
            RopeMesh.Build(mesh, paths, cordRadius);
        }

        private static Vector3[] Strand(Vector3 fromLoop, Vector3 besideHead, Vector3 meet)
        {
            var pts = new Vector3[8];
            for (int i = 0; i < 4; i++)
                pts[i] = Vector3.Lerp(fromLoop, besideHead, i / 3f);
            for (int i = 1; i <= 4; i++)
                pts[3 + i] = Vector3.Lerp(besideHead, meet, i / 4f);
            return pts;
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
