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
        private Vector3 _rope;
        private Vector3 _axis;
        private float _ropeRadius;
        private float _body;

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
                && Mathf.Abs(ropeRadius - _ropeRadius) < 1e-5f
                && Mathf.Abs(wall - _body) < 1e-5f)
                return;

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

            Build(_mesh, rope, axis, ropeRadius, ropeRadius / 3f, wall);
            _rope = rope;
            _axis = axis;
            _ropeRadius = ropeRadius;
            _body = wall;
        }

        // One turn around each handle, then up the wall, meeting on top of the line.
        private static void Build(Mesh mesh, Vector3 rope, Vector3 ropeAxis, float lineRadius, float cordRadius, float wall)
        {
            bool big = wall > 0.18f;
            float wrapX = big ? BigWrapX : SmallWrapX;
            float loopR = (big ? BigWrapR : SmallWrapR) + cordRadius * 0.35f;
            Vector3 leftCenter = new Vector3(wrapX, JoinY, 0f);
            Vector3 rightCenter = new Vector3(-wrapX, JoinY, 0f);
            Vector3[] leftLoop = Turn(leftCenter, loopR);
            Vector3[] rightLoop = Turn(rightCenter, loopR);

            float rise = RimY + 0.006f;
            Vector3 left = leftCenter + Vector3.up * loopR;
            Vector3 right = rightCenter + Vector3.up * loopR;
            Vector3 leftRise = new Vector3(wall, rise, 0f);
            Vector3 rightRise = new Vector3(-wall, rise, 0f);

            Vector3 center = new Vector3(0f, JoinY, 0f);
            Vector3 station = rope + ropeAxis * Vector3.Dot(center - rope, ropeAxis);
            Vector3 down = center - station;
            down -= ropeAxis * Vector3.Dot(down, ropeAxis);
            if (down.sqrMagnitude < 1e-8f)
                down = -Vector3.up;
            down.Normalize();
            Vector3 meet = station - down * (lineRadius + cordRadius * 0.5f);
            float split = cordRadius * 0.4f;

            var paths = new List<Vector3[]>
            {
                leftLoop,
                rightLoop,
                Strand(left, leftRise, meet - ropeAxis * split),
                Strand(right, rightRise, meet + ropeAxis * split),
            };
            RopeMesh.Build(mesh, paths, cordRadius);
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

        private static Vector3[] Strand(Vector3 fromLoop, Vector3 aboveRim, Vector3 meet)
        {
            var pts = new Vector3[8];
            for (int i = 0; i < 4; i++)
                pts[i] = Vector3.Lerp(fromLoop, aboveRim, i / 3f);
            for (int i = 1; i <= 4; i++)
                pts[3 + i] = Vector3.Lerp(aboveRim, meet, i / 4f);
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
            _cord.name = "Dizzy Clothesline Pot Cord";
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
