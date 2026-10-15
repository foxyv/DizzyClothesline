using System.Collections.Generic;
using UnityEngine;

namespace Dizzy.Clothesline
{
    // A rope between two lamp hooks. It is a GoPointerButton, so the game's own pointer
    // outlines it, shows its look text, and hands it the held item on a click.
    //
    // It lives under the hooks' parent (the boat), not under a hook: colliders under an
    // item belong to that item. Its own kinematic Rigidbody keeps its colliders out of
    // the boat's compound collider.
    internal sealed class ClotheslineLine : GoPointerButton
    {
        private const int MeshSegments = 24;
        private const int ColliderSegments = 12;
        private const float ColliderRadius = 0.03f;
        // Items stay this far from the hooks, clear of the hooks' own colliders.
        private const float EndGap = 0.15f;
        private const float MoveTolerance = 0.001f;

        internal LineRecord Record;
        internal ShipItem HookA;
        internal ShipItem HookB;

        private readonly List<LineAnchor> _anchors = new List<LineAnchor>();
        private readonly List<CapsuleCollider> _segments = new List<CapsuleCollider>();
        private readonly Vector3[] _points = new Vector3[MeshSegments + 1];

        private Mesh _mesh;
        private MeshRenderer _renderer;
        private Vector3 _a;
        private Vector3 _b;
        private float _span;

        private Transform _builtParent;
        private Vector3 _builtPosA;
        private Vector3 _builtPosB;
        private Quaternion _builtRotA;
        private Quaternion _builtRotB;

        private Vector3 _hitPoint;
        private bool _hasHit;

        internal void Init(LineRecord record, ShipItem hookA, ShipItem hookB)
        {
            Record = record;
            HookA = hookA;
            HookB = hookB;
            lookText = "";

            // modcheck-ignore: SW605 fresh GameObject made in Lines.Spawn, there is nothing to reuse
            Rigidbody body = gameObject.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;

            // GoPointer sends a hit on a child tagged ItemSubcollider to its parent's
            // collider, and looks for the button there. This one only has to exist.
            // modcheck-ignore: SW605 fresh GameObject made in Lines.Spawn, there is nothing to reuse
            BoxCollider own = gameObject.AddComponent<BoxCollider>();
            own.isTrigger = true;
            own.size = Vector3.one * 0.01f;
            own.enabled = false;

            _renderer = GetComponent<MeshRenderer>();
            _renderer.sharedMaterial = RopeMesh.Material;
            _mesh = new Mesh { name = "Dizzy Clothesline Rope" };
            GetComponent<MeshFilter>().sharedMesh = _mesh;

            Rebuild();
        }

        internal void Rebuild()
        {
            if (HookA == null || HookB == null)
                return;

            Transform parent = HookA.transform.parent;
            if (transform.parent != parent)
                transform.SetParent(parent, false);

            Vector3 a = ToParent(parent, Lines.TiePoint(HookA));
            Vector3 b = ToParent(parent, Lines.TiePoint(HookB));
            Vector3 mid = (a + b) * 0.5f;
            transform.localPosition = mid;
            transform.localRotation = Quaternion.identity;
            transform.localScale = Vector3.one;
            _a = a - mid;
            _b = b - mid;
            _span = Vector3.Distance(a, b);

            _builtParent = parent;
            _builtPosA = HookA.transform.localPosition;
            _builtPosB = HookB.transform.localPosition;
            _builtRotA = HookA.transform.localRotation;
            _builtRotB = HookB.transform.localRotation;

            for (int i = 0; i <= MeshSegments; i++)
                _points[i] = Point((float)i / MeshSegments);
            RopeMesh.Build(_mesh, _points, ClotheslineConfig.Thickness.Value * 0.5f);
            RebuildColliders();

            for (int i = 0; i < _anchors.Count; i++)
                Place(_anchors[i]);
            _renderer.enabled = true;
        }

        private static Vector3 ToParent(Transform parent, Vector3 world)
        {
            return parent != null ? parent.InverseTransformPoint(world) : world;
        }

        // Point on the rope, in this object's space. Sags along the boat's down, so it
        // heels with the boat like the hooks do.
        private Vector3 Point(float s)
        {
            Vector3 p = Vector3.Lerp(_a, _b, s);
            p.y -= _span * ClotheslineConfig.Sag.Value * 4f * s * (1f - s);
            return p;
        }

        // Level direction across the rope at s.
        private Vector3 Across(float s)
        {
            Vector3 along = Point(Mathf.Min(s + 0.01f, 1f)) - Point(Mathf.Max(s - 0.01f, 0f));
            Vector3 across = Vector3.Cross(Vector3.up, along);
            across.y = 0f;
            return across.sqrMagnitude > 1e-8f ? across.normalized : Vector3.forward;
        }

        private void RebuildColliders()
        {
            int per = MeshSegments / ColliderSegments;
            for (int i = 0; i < ColliderSegments; i++)
            {
                CapsuleCollider col;
                if (i < _segments.Count)
                {
                    col = _segments[i];
                }
                else
                {
                    var go = new GameObject("rope segment");
                    go.tag = "ItemSubcollider";
                    go.transform.SetParent(transform, false);
                    col = go.AddComponent<CapsuleCollider>();
                    col.isTrigger = true;
                    col.direction = 2;
                    _segments.Add(col);
                }

                Vector3 from = _points[i * per];
                Vector3 to = _points[(i + 1) * per];
                Vector3 along = to - from;
                float length = along.magnitude;
                col.transform.localPosition = (from + to) * 0.5f;
                col.transform.localRotation = length > 1e-4f ? Quaternion.LookRotation(along / length, Vector3.up) : Quaternion.identity;
                col.radius = ColliderRadius;
                col.height = length + ColliderRadius * 2f;
            }
        }

        internal LineAnchor CreateAnchor(HungRecord record)
        {
            var go = new GameObject("hang point");
            go.transform.SetParent(transform, false);
            BoxCollider col = go.AddComponent<BoxCollider>();
            col.isTrigger = true;
            col.size = Vector3.one * 0.01f;
            col.enabled = false;

            LineAnchor anchor = go.AddComponent<LineAnchor>();
            anchor.Line = this;
            anchor.Record = record;
            anchor.Col = col;
            _anchors.Add(anchor);
            Place(anchor);
            return anchor;
        }

        internal void RemoveAnchor(LineAnchor anchor)
        {
            _anchors.Remove(anchor);
        }

        internal LineAnchor[] Anchors()
        {
            return _anchors.ToArray();
        }

        private void Place(LineAnchor anchor)
        {
            float s = anchor.Record.S;
            Vector3 across = Across(s) * anchor.Record.Side;
            anchor.transform.localRotation = Quaternion.LookRotation(across, Vector3.up);
            anchor.transform.localPosition = Point(s) + across * Lines.HookOffset - Vector3.up * ClotheslineConfig.HangDrop.Value;
        }

        // Nearest place on the rope to a world point, from 0 at HookA to 1 at HookB.
        internal float ClosestS(Vector3 world)
        {
            Vector3 p = transform.InverseTransformPoint(world);
            float best = 0.5f;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < MeshSegments; i++)
            {
                Vector3 a = _points[i];
                Vector3 ab = _points[i + 1] - a;
                float t = ab.sqrMagnitude > 1e-8f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
                float distance = (a + ab * t - p).sqrMagnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = (i + t) / MeshSegments;
                }
            }
            return best;
        }

        // A free place at or near where the player clicked. Items keep the configured
        // spacing from each other, and stay clear of the hooks.
        internal bool TryFindRoom(float wanted, out float s)
        {
            s = wanted;
            if (_span <= 0f)
                return false;

            float min = EndGap / _span;
            float max = 1f - min;
            if (min > max)
                return false;

            float gap = ClotheslineConfig.ItemSpacing.Value / _span;
            float clamped = Mathf.Clamp(wanted, min, max);
            if (IsFree(clamped, gap))
            {
                s = clamped;
                return true;
            }

            float best = -1f;
            float bestDistance = float.MaxValue;
            List<HungRecord> items = Record.Items;
            for (int i = 0; i < items.Count; i++)
            {
                for (int side = -1; side <= 1; side += 2)
                {
                    float candidate = items[i].S + gap * side;
                    if (candidate < min || candidate > max || !IsFree(candidate, gap * 0.999f))
                        continue;
                    float distance = Mathf.Abs(candidate - clamped);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = candidate;
                    }
                }
            }

            // Do not jump far from where the player aimed.
            float reach = Mathf.Max(0.5f, ClotheslineConfig.ItemSpacing.Value * 2f);
            if (best < 0f || bestDistance * _span > reach)
                return false;
            s = best;
            return true;
        }

        private bool IsFree(float s, float gap)
        {
            List<HungRecord> items = Record.Items;
            for (int i = 0; i < items.Count; i++)
            {
                if (Mathf.Abs(items[i].S - s) < gap)
                    return false;
            }
            return true;
        }

        // Which way across the rope faces away from someone looking along viewWorld.
        internal int SideFacing(float s, Vector3 viewWorld)
        {
            Vector3 view = transform.InverseTransformDirection(viewWorld);
            return Vector3.Dot(Across(s), view) >= 0f ? 1 : -1;
        }

        public override void OnActivateHit(RaycastHit activatingHit)
        {
            _hitPoint = activatingHit.point;
            _hasHit = true;
        }

        public override bool OnItemClick(PickupableItem heldItem)
        {
            Vector3 point = _hasHit ? _hitPoint : transform.position;
            _hasHit = false;
            if (Record == null)
                return false;
            return Lines.HangByPlayer(this, heldItem, point);
        }

        public override void ExtraLateUpdate()
        {
            if (Record == null)
                return;

            if (HookA == null || HookB == null)
            {
                Lines.HookVanished(Record);
                return;
            }

            if (Lines.HookTaken(HookA) || Lines.HookTaken(HookB))
            {
                // In Coop, whoever took the hook tells the others.
                bool mine = Lines.IsHeldLocally(HookA) || Lines.IsHeldLocally(HookB);
                Lines.Untie(Record, "The line came untied.", mine);
                return;
            }

            // Right after a load each hook boards its boat on its own physics step.
            // Wait until both are under the same parent.
            Transform parent = HookA.transform.parent;
            if (HookB.transform.parent != parent)
            {
                _renderer.enabled = false;
                return;
            }

            if (parent != _builtParent
                || Moved(HookA.transform, _builtPosA, _builtRotA)
                || Moved(HookB.transform, _builtPosB, _builtRotB))
            {
                Rebuild();
            }

            for (int i = _anchors.Count - 1; i >= 0; i--)
            {
                LineAnchor anchor = _anchors[i];
                if (anchor.Item == null)
                    Lines.ItemVanished(anchor);
                else if (Lines.IsHeldLocally(anchor.Ship))
                    anchor.Item.DisconnectJoint();
            }

            unclickable = Lines.HeldBlocksLines;
            lookText = Lines.LineLookText;
        }

        private static bool Moved(Transform hook, Vector3 position, Quaternion rotation)
        {
            return (hook.localPosition - position).sqrMagnitude > MoveTolerance * MoveTolerance
                || Quaternion.Angle(hook.localRotation, rotation) > 0.5f;
        }

        private void OnDestroy()
        {
            if (_mesh != null)
                Destroy(_mesh);
        }
    }
}
