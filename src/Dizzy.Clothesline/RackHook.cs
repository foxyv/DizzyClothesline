using System.Collections.Generic;
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

        private Mesh _mesh;
        private Vector3 _rope;
        private Vector3 _axis;
        private float _lineRadius;

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
                && Mathf.Abs(lineRadius - _lineRadius) < 1e-5f)
                return;

            if (_mesh == null)
            {
                _mesh = new Mesh { name = "Clothesline Rack Hook" };
                // modcheck-ignore: SW605 fresh hook object, there is nothing to reuse
                MeshFilter filter = gameObject.AddComponent<MeshFilter>();
                filter.sharedMesh = _mesh;
                // modcheck-ignore: SW605 fresh hook object, there is nothing to reuse
                MeshRenderer renderer = gameObject.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = Iron();
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            Build(_mesh, rope, axis, lineRadius);
            _rope = rope;
            _axis = axis;
            _lineRadius = lineRadius;
        }

        // From the top of the rack's wire, up to the rope, over the top, and another
        // quarter turn down the far side so the wire stays hooked.
        private static void Build(Mesh mesh, Vector3 rope, Vector3 ropeAxis, float lineRadius)
        {
            Vector3 towardRack = -rope;
            towardRack -= ropeAxis * Vector3.Dot(towardRack, ropeAxis);
            if (towardRack.sqrMagnitude < 1e-8f)
                towardRack = Vector3.down;
            towardRack.Normalize();
            Vector3 flank = Vector3.Cross(ropeAxis, towardRack).normalized;

            float arcR = lineRadius + WireRadius;
            Vector3 meet = rope + towardRack * arcR;
            var rise = new Vector3[5];
            rise[0] = Vector3.down * 0.008f;
            for (int i = 1; i < rise.Length; i++)
                rise[i] = Vector3.Lerp(Vector3.zero, meet, (i - 1) / (float)(rise.Length - 2));

            const float turn = Mathf.PI + 0.65f + Mathf.PI * 0.5f;
            const int segs = 26;
            var arc = new Vector3[segs + 1];
            for (int i = 0; i <= segs; i++)
            {
                float ang = turn * i / segs;
                arc[i] = rope + (towardRack * Mathf.Cos(ang) + flank * Mathf.Sin(ang)) * arcR;
            }

            var paths = new List<Vector3[]> { rise, arc };
            RopeMesh.Build(mesh, paths, WireRadius);
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
            if (_mesh != null)
                Destroy(_mesh);
        }
    }
}
