using UnityEngine;

namespace Dizzy.Clothesline
{
    // An apple, orange, or goat cheese bag already has a net gathered at the neck.
    // On a line it gets the same knot as a sausage bunch, in the bag's own net color.
    internal sealed class BagKnot : MonoBehaviour
    {
        private const string StringName = "FirewoodBundleString";
        private const string AppleName = "apple";
        private const string OrangeName = "orange";
        private const string GoatCheeseName = "goat cheese";

        private Mesh _mesh;
        private Material _material;
        private Vector3 _open;
        private float _drop;
        private float _ropeRadius;
        private Color _color;

        internal static void Sync(LineAnchor anchor)
        {
            ShipItem ship = anchor != null ? anchor.Ship : null;
            if (!Is(ship))
            {
                Clear(anchor);
                return;
            }

            if (anchor.Bag == null)
            {
                // modcheck-ignore: SW605 the knot is new each time a bag is hung
                var go = new GameObject("clothesline bag knot");
                go.transform.SetParent(ship.transform, false);
                anchor.Bag = go.AddComponent<BagKnot>();
            }

            anchor.Bag.Follow(anchor);
        }

        internal static void Clear(LineAnchor anchor)
        {
            if (anchor == null || anchor.Bag == null)
                return;
            Destroy(anchor.Bag.gameObject);
            anchor.Bag = null;
        }

        private static bool Is(ShipItem ship)
        {
            if (ship == null || ship.transform.Find(StringName) == null)
                return false;
            return ship.name == AppleName || ship.name == OrangeName || ship.name == GoatCheeseName;
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
            Color color = NetColor(ship);

            if (_mesh != null
                && (along - _open).sqrMagnitude < 1e-8f
                && Mathf.Abs(drop - _drop) < 1e-5f
                && Mathf.Abs(ropeRadius - _ropeRadius) < 1e-5f
                && color == _color)
                return;

            if (_mesh == null)
            {
                _mesh = new Mesh { name = "Clothesline Bag Knot" };
                // modcheck-ignore: SW605 fresh knot object, there is nothing to reuse
                MeshFilter filter = gameObject.AddComponent<MeshFilter>();
                filter.sharedMesh = _mesh;
                // modcheck-ignore: SW605 fresh knot object, there is nothing to reuse
                MeshRenderer renderer = gameObject.AddComponent<MeshRenderer>();
                _material = new Material(NetMaterial(ship));
                _material.name = "Clothesline Bag Knot";
                renderer.sharedMaterial = _material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
            else if (_material != null)
            {
                _material.color = color;
            }

            if (!LineEye.Build(_mesh, along, drop, ropeRadius, out _))
                return;
            _open = along;
            _drop = drop;
            _ropeRadius = ropeRadius;
            _color = color;
        }

        // The net strands are the bag's own cord. Copy that material so a dyed bag
        // keeps its color on the knot, without changing the strands themselves.
        private static Material NetMaterial(ShipItem ship)
        {
            foreach (Transform child in ship.transform)
            {
                if (child.name != StringName)
                    continue;
                MeshRenderer renderer = child.GetComponent<MeshRenderer>();
                if (renderer != null && renderer.sharedMaterial != null)
                    return renderer.sharedMaterial;
            }
            return RopeMesh.Material;
        }

        private static Color NetColor(ShipItem ship)
        {
            Material material = NetMaterial(ship);
            return material != null ? material.color : Color.white;
        }

        private void OnDestroy()
        {
            if (_mesh != null)
                Destroy(_mesh);
            if (_material != null)
                Destroy(_material);
        }
    }
}
