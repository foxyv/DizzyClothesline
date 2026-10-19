using System.Collections.Generic;
using UnityEngine;

namespace Dizzy.Clothesline
{
    // A thinner string that hangs a banana bunch from the line. The bunch's stalk
    // stops at the pin, and a lamp hook's metal covers that gap. On a line the string
    // wraps the stalk, then loops over the rope.
    internal sealed class StemTie : MonoBehaviour
    {
        private const string StickName = "FirewoodBundleStick";
        private const string BananaName = "banana";
        private const int WrapTurns = 2;
        private const int PerTurn = 12;

        private Mesh _mesh;
        private Vector3 _rope;
        private Vector3 _axis;
        private float _ropeRadius;
        private float _stringRadius;

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
            ClotheslineLine line = anchor.Line;
            ShipItem ship = anchor.Ship;
            if (line == null || ship == null || anchor.Record == null)
                return;

            float ropeRadius = ClotheslineConfig.Thickness.Value * 0.5f;
            float stringRadius = ropeRadius * 0.5f;
            Vector3 rope = ship.transform.InverseTransformPoint(line.RopeWorld(anchor.Record.S));
            Vector3 axis = ship.transform.InverseTransformDirection(line.RopeDirectionWorld(anchor.Record.S));
            if (axis.sqrMagnitude < 1e-8f)
                return;
            axis.Normalize();

            if (_mesh != null
                && (rope - _rope).sqrMagnitude < 1e-8f
                && (axis - _axis).sqrMagnitude < 1e-8f
                && Mathf.Abs(ropeRadius - _ropeRadius) < 1e-5f
                && Mathf.Abs(stringRadius - _stringRadius) < 1e-5f)
                return;

            Vector3 top;
            Vector3 down;
            float stalkRadius;
            if (!TryStalk(ship, rope, out top, out down, out stalkRadius))
                return;

            if (_mesh == null)
            {
                _mesh = new Mesh { name = "Clothesline Stem Tie" };
                // modcheck-ignore: SW605 fresh tie object, there is nothing to reuse
                MeshFilter filter = gameObject.AddComponent<MeshFilter>();
                filter.sharedMesh = _mesh;
                // modcheck-ignore: SW605 fresh tie object, there is nothing to reuse
                MeshRenderer renderer = gameObject.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = RopeMesh.Material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            Build(_mesh, rope, axis, ropeRadius, stringRadius, top, down, stalkRadius);
            _rope = rope;
            _axis = axis;
            _ropeRadius = ropeRadius;
            _stringRadius = stringRadius;
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
            radius = cap.radius;
            return true;
        }

        private static void Build(
            Mesh mesh, Vector3 rope, Vector3 ropeAxis, float ropeRadius, float stringRadius,
            Vector3 stemTop, Vector3 down, float stalkRadius)
        {
            // On the full part of the stalk, just under the tip that meets the pin.
            Vector3 origin = stemTop + down * stalkRadius;
            float wrapRadius = stalkRadius + stringRadius * 0.35f;
            float pitch = stringRadius * 2f * 1.15f;
            Vector3 towardRope = rope - origin;
            Vector3 side = towardRope - down * Vector3.Dot(towardRope, down);
            if (side.sqrMagnitude < 1e-8f)
                side = Vector3.Cross(down, Vector3.right);
            if (side.sqrMagnitude < 1e-8f)
                side = Vector3.Cross(down, Vector3.forward);
            side.Normalize();
            Vector3 bend = Vector3.Cross(down, side);

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

                wraps[i] = origin + down * (u * pitch * WrapTurns)
                    + (side * Mathf.Cos(ang) + bend * Mathf.Sin(ang)) * radial;
            }

            // A turn around the rope. Angle 0 faces the stem, so the far side is over the line.
            float loopRadius = ropeRadius + stringRadius * 0.25f;
            Vector3 toStem = origin - rope;
            Vector3 towardStem = toStem - ropeAxis * Vector3.Dot(toStem, ropeAxis);
            if (towardStem.sqrMagnitude < 1e-8f)
                towardStem = Vector3.Cross(ropeAxis, Vector3.up);
            if (towardStem.sqrMagnitude < 1e-8f)
                towardStem = Vector3.Cross(ropeAxis, Vector3.right);
            towardStem.Normalize();
            Vector3 loopSide = Vector3.Cross(ropeAxis, towardStem).normalized;

            const int loopSegs = 20;
            var loop = new Vector3[loopSegs + 1];
            for (int i = 0; i <= loopSegs; i++)
            {
                float ang = i * Mathf.PI * 2f / loopSegs;
                loop[i] = rope + (towardStem * Mathf.Cos(ang) + loopSide * Mathf.Sin(ang)) * loopRadius;
            }

            Vector3 from = wraps[wraps.Length - 1];
            Vector3 to = loop[0];
            var bridge = new Vector3[4];
            for (int i = 0; i < 4; i++)
                bridge[i] = Vector3.Lerp(from, to, i / 3f);

            var paths = new List<Vector3[]> { wraps, bridge, loop };
            RopeMesh.Build(mesh, paths, stringRadius);
        }

        private void OnDestroy()
        {
            if (_mesh != null)
                Destroy(_mesh);
        }
    }
}
