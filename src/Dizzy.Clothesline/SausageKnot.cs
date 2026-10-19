using UnityEngine;

namespace Dizzy.Clothesline
{
    // A hanging sausage bunch has a thin middle string that stops at the pin.
    // On a line, the clothesline-end knot sits in the gap above that string.
    // The bunch's own string is left as Bundle Up drew it.
    internal sealed class SausageKnot : MonoBehaviour
    {
        private const string StringName = "FirewoodBundleString";
        private const string SausageName = "sausage";

        private Mesh _mesh;
        private Vector3 _open;
        private float _drop;
        private float _ropeRadius;

        internal static void Sync(LineAnchor anchor)
        {
            ShipItem ship = anchor != null ? anchor.Ship : null;
            if (!Is(ship))
            {
                Clear(anchor);
                return;
            }

            if (anchor.Sausage == null)
            {
                // modcheck-ignore: SW605 the knot is new each time a bunch is hung
                var go = new GameObject("clothesline sausage knot");
                go.transform.SetParent(ship.transform, false);
                anchor.Sausage = go.AddComponent<SausageKnot>();
            }

            anchor.Sausage.Follow(anchor);
        }

        internal static void Clear(LineAnchor anchor)
        {
            if (anchor == null || anchor.Sausage == null)
                return;
            Destroy(anchor.Sausage.gameObject);
            anchor.Sausage = null;
        }

        private static bool Is(ShipItem ship)
        {
            return ship != null
                && ship.name == SausageName
                && ship.transform.Find(StringName) != null;
        }

        private void Follow(LineAnchor anchor)
        {
            ClotheslineLine line = anchor.Line;
            ShipItem ship = anchor.Ship;
            if (line == null || ship == null || anchor.Record == null)
                return;

            float drop = ClotheslineConfig.HangDrop.Value;
            float ropeRadius = ClotheslineConfig.Thickness.Value * 0.5f;
            Vector3 along = ship.transform.InverseTransformDirection(line.RopeDirectionWorld(anchor.Record.S));
            along.y = 0f;
            if (along.sqrMagnitude < 1e-8f)
                along = Vector3.right;
            along.Normalize();

            if (_mesh != null
                && (along - _open).sqrMagnitude < 1e-8f
                && Mathf.Abs(drop - _drop) < 1e-5f
                && Mathf.Abs(ropeRadius - _ropeRadius) < 1e-5f)
                return;

            if (_mesh == null)
            {
                _mesh = new Mesh { name = "Clothesline Sausage Knot" };
                // modcheck-ignore: SW605 fresh knot object, there is nothing to reuse
                MeshFilter filter = gameObject.AddComponent<MeshFilter>();
                filter.sharedMesh = _mesh;
                // modcheck-ignore: SW605 fresh knot object, there is nothing to reuse
                MeshRenderer renderer = gameObject.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = RopeMesh.Material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            if (!LineEye.Build(_mesh, along, drop, ropeRadius, out _))
                return;
            _open = along;
            _drop = drop;
            _ropeRadius = ropeRadius;
        }
    }
}
