using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Dizzy.Clothesline
{
    // A thin cord that hangs a spyglass. The glass points down. One loop sits a
    // little below the eyepiece, and two strands run up to the top and cross on
    // the clothesline.
    //
    // The barrel lies along local Z and the player looks out along +Z, so the
    // eyepiece is the wide end at the most negative Z. A spyglass is not hangable
    // in the game. HangableItem is added only while it is on a line, and taken off
    // again when it comes down, so a lamp hook still will not take one.
    internal sealed class SpyglassTie : MonoBehaviour
    {
        // How far below the eyepiece the loop sits, in metres. The collar there is
        // still about as wide as the eye cup.
        private const float LoopDrop = 0.06f;
        // The little spyglass. Its draw-tube is wider than the eye cup.
        private const int SmallestPrefab = 162;

        private static AccessTools.FieldRef<HangableItem, bool> _disallow;

        private static Material _cord;

        private Mesh _mesh;
        private Vector3 _rope;
        private Vector3 _axis;
        private float _ropeRadius;
        private float _cupRadius;
        private float _eyeZ;

        static SpyglassTie()
        {
            try
            {
                _disallow = AccessTools.FieldRefAccess<HangableItem, bool>("disallowHangingOnTrigger");
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning("Could not keep a hung spyglass off lamp hooks. " + ex.Message);
            }
        }

        internal static bool Is(ShipItem ship)
        {
            return ship != null && ship.GetComponent<ShipItemSpyglass>() != null;
        }

        // How far below the usual pin the item origin has to sit so the eyepiece,
        // not the origin, meets the line. One rope thickness extra, so the knot
        // around the eyepiece is not buried in the clothesline.
        internal static float Drop(ShipItem ship)
        {
            if (!Is(ship))
                return 0f;
            MeshFilter filter = ship.GetComponent<MeshFilter>();
            Mesh mesh = filter != null ? filter.sharedMesh : null;
            if (mesh == null)
                return 0.45f;
            return -mesh.bounds.min.z + ClotheslineConfig.Thickness.Value;
        }

        // The component the line's hang path already understands. Added fresh, so
        // the game's own trigger cannot also hang it on a lamp hook.
        internal static HangableItem Prepare(ShipItem ship)
        {
            if (ship == null)
                return null;
            HangableItem hang = ship.GetComponent<HangableItem>();
            if (hang == null)
            {
                // modcheck-ignore: SW605 a spyglass is not hangable in the game; this exists only while it is on a line
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

            if (anchor.Lash == null)
            {
                // modcheck-ignore: SW605 the tie is new each time a spyglass is hung
                var go = new GameObject("clothesline spyglass tie");
                go.transform.SetParent(ship.transform, false);
                anchor.Lash = go.AddComponent<SpyglassTie>();
            }

            anchor.Lash.Follow(anchor);
        }

        internal static void Clear(LineAnchor anchor)
        {
            if (anchor == null || anchor.Lash == null)
                return;
            Destroy(anchor.Lash.gameObject);
            anchor.Lash = null;
        }

        private void Follow(LineAnchor anchor)
        {
            ClotheslineLine line = anchor.Line;
            ShipItem ship = anchor.Ship;
            if (line == null || ship == null || anchor.Record == null)
                return;

            MeshFilter filter = ship.GetComponent<MeshFilter>();
            Mesh barrel = filter != null ? filter.sharedMesh : null;
            if (barrel == null)
                return;

            float ropeRadius = ClotheslineConfig.Thickness.Value * 0.5f;
            float cupRadius = Mathf.Max(barrel.bounds.extents.x, barrel.bounds.extents.y);
            float eyeZ = barrel.bounds.min.z;
            Vector3 rope = ship.transform.InverseTransformPoint(line.RopeWorld(anchor.Record.S));
            Vector3 axis = ship.transform.InverseTransformDirection(line.RopeDirectionWorld(anchor.Record.S));
            if (axis.sqrMagnitude < 1e-8f)
                return;
            axis.Normalize();

            if (_mesh != null
                && (rope - _rope).sqrMagnitude < 1e-8f
                && (axis - _axis).sqrMagnitude < 1e-8f
                && Mathf.Abs(ropeRadius - _ropeRadius) < 1e-5f
                && Mathf.Abs(cupRadius - _cupRadius) < 1e-5f
                && Mathf.Abs(eyeZ - _eyeZ) < 1e-5f)
                return;

            if (_mesh == null)
            {
                _mesh = new Mesh { name = "Clothesline Spyglass Tie" };
                // modcheck-ignore: SW605 fresh tie object, there is nothing to reuse
                MeshFilter added = gameObject.AddComponent<MeshFilter>();
                added.sharedMesh = _mesh;
                // modcheck-ignore: SW605 fresh tie object, there is nothing to reuse
                MeshRenderer renderer = gameObject.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = Cord();
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            // A third of the clothesline's radius: half was still half again too thick.
            // The little glass's tube sticks out past the eye cup, so its loop is
            // lifted by one clothesline thickness or the cord sits inside the tube.
            float cordRadius = ropeRadius / 3f;
            float lift = IsSmallest(ship) ? ropeRadius * 2f : 0f;
            Build(_mesh, rope, axis, ropeRadius, cordRadius, cupRadius, eyeZ, lift);
            _rope = rope;
            _axis = axis;
            _ropeRadius = ropeRadius;
            _cupRadius = cupRadius;
            _eyeZ = eyeZ;
        }

        // One closed loop around the barrel, then two strands up the sides. They
        // cross on top of the clothesline: each continues a short way past the
        // other, which is the knot. lift pushes the loop out past a wider tube.
        private static void Build(
            Mesh mesh, Vector3 rope, Vector3 ropeAxis, float lineRadius, float cordRadius,
            float cupRadius, float eyeZ, float lift)
        {
            Vector3 barrel = Vector3.forward;
            Vector3 side = Vector3.Cross(barrel, ropeAxis);
            if (side.sqrMagnitude < 1e-8f)
                side = Vector3.Cross(barrel, Vector3.up);
            if (side.sqrMagnitude < 1e-8f)
                side = Vector3.Cross(barrel, Vector3.right);
            side.Normalize();
            Vector3 around = Vector3.Cross(barrel, side);

            float loopR = cupRadius + cordRadius * 0.35f + lift;
            Vector3 loopCenter = new Vector3(0f, 0f, eyeZ + LoopDrop);
            const int loopSegs = 24;
            var loop = new Vector3[loopSegs + 1];
            for (int i = 0; i <= loopSegs; i++)
            {
                float ang = i * Mathf.PI * 2f / loopSegs;
                loop[i] = loopCenter + (side * Mathf.Cos(ang) + around * Mathf.Sin(ang)) * loopR;
            }

            Vector3 leftLoop = loopCenter + side * loopR;
            Vector3 rightLoop = loopCenter - side * loopR;
            float topR = cupRadius + cordRadius * 0.35f;
            float topZ = eyeZ + cordRadius;
            Vector3 leftTop = new Vector3(0f, 0f, topZ) + side * topR;
            Vector3 rightTop = new Vector3(0f, 0f, topZ) - side * topR;

            Vector3 towardGlass = loopCenter - rope;
            towardGlass -= ropeAxis * Vector3.Dot(towardGlass, ropeAxis);
            if (towardGlass.sqrMagnitude < 1e-8f)
                towardGlass = barrel;
            towardGlass.Normalize();
            Vector3 knot = rope - towardGlass * (lineRadius + cordRadius * 0.5f);
            float split = cordRadius * 0.4f;
            float tail = lineRadius * 2f;
            Vector3 leftCross = knot - ropeAxis * split;
            Vector3 rightCross = knot + ropeAxis * split;

            var paths = new List<Vector3[]>
            {
                loop,
                Strand(leftLoop, leftTop, leftCross, leftCross - side * tail),
                Strand(rightLoop, rightTop, rightCross, rightCross + side * tail),
            };
            RopeMesh.Build(mesh, paths, cordRadius);
        }

        private static bool IsSmallest(ShipItem ship)
        {
            SaveablePrefab save = ship.Getsaveable();
            return save != null && save.prefabIndex == SmallestPrefab;
        }

        // Dark brown, kept off the clothesline's own colour.
        private static Material Cord()
        {
            if (_cord != null)
                return _cord;
            Material shared = RopeMesh.Material;
            if (shared == null)
                return null;
            _cord = new Material(shared);
            _cord.name = "Dizzy Clothesline Spyglass Cord";
            _cord.color = new Color(0.28f, 0.14f, 0.06f);
            return _cord;
        }

        private static Vector3[] Strand(Vector3 fromLoop, Vector3 atTop, Vector3 cross, Vector3 tail)
        {
            var pts = new Vector3[8];
            for (int i = 0; i < 4; i++)
                pts[i] = Vector3.Lerp(fromLoop, atTop, i / 3f);
            for (int i = 1; i <= 2; i++)
                pts[3 + i] = Vector3.Lerp(atTop, cross, i / 2f);
            pts[6] = Vector3.Lerp(cross, tail, 0.5f);
            pts[7] = tail;
            return pts;
        }

        private void OnDestroy()
        {
            if (_mesh != null)
                Destroy(_mesh);
        }
    }
}
