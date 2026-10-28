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
        // Items stay at least this far from the hooks, clear of the hooks' own colliders.
        // A knot longer than this pushes them out past the knot as well.
        private const float EndGap = 0.15f;
        private const float MoveTolerance = 0.001f;

        internal LineRecord Record;
        internal ShipItem HookA;
        internal ShipItem HookB;

        private readonly List<LineAnchor> _anchors = new List<LineAnchor>();
        private readonly List<CapsuleCollider> _segments = new List<CapsuleCollider>();
        private readonly List<Vector3[]> _paths = new List<Vector3[]>(5);
        private readonly Vector3[] _points = new Vector3[MeshSegments + 1];
        private float _knotReach;

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
            BuildMesh();
            RebuildColliders();

            for (int i = 0; i < _anchors.Count; i++)
                Place(_anchors[i]);
            _renderer.enabled = true;
        }

        private static Vector3 ToParent(Transform parent, Vector3 world)
        {
            return parent != null ? parent.InverseTransformPoint(world) : world;
        }

        // Hook up, in this object's space. Rebuild has already cleared our rotation,
        // so this is the direction the eye should open.
        private Vector3 HookUp(ShipItem hook)
        {
            return transform.InverseTransformDirection(hook.transform.up);
        }

        // Standing rope between the two knots, plus an eye and three turns at each hook.
        // On a line too short for that, the rope is left plain.
        private void BuildMesh()
        {
            float diameter = ClotheslineConfig.Thickness.Value;
            Compose(_paths, _points, _a, _b, _span, diameter, ClotheslineConfig.Sag.Value, HookUp(HookA), HookUp(HookB), out _knotReach);
            RopeMesh.Build(_mesh, _paths, diameter * 0.5f, Profile.Kind.Line);
        }

        // Hooks that stay put in boat space do not rebuild. A heel of the boat is
        // that case: the line turns with the boat, and its local ends do not move.
        internal static void StepMesh(
            ref bool built, ref Vector3 builtA, ref Vector3 builtB,
            Vector3 a, Vector3 b, float thickness, float sag,
            Vector3 hookUpA, Vector3 hookUpB,
            Vector3[] points, List<Vector3[]> paths, Mesh mesh)
        {
            // A hook position that is not a number would turn the whole rope to NaN.
            if (!CordGuard.Finite(a) || !CordGuard.Finite(b))
                return;
            if (built
                && (a - builtA).sqrMagnitude <= MoveTolerance * MoveTolerance
                && (b - builtB).sqrMagnitude <= MoveTolerance * MoveTolerance)
                return;

            float span = Vector3.Distance(a, b);
            Vector3 mid = (a + b) * 0.5f;
            float knotReach;
            Compose(paths, points, a - mid, b - mid, span, thickness, sag, hookUpA, hookUpB, out knotReach);
            RopeMesh.Build(mesh, paths, thickness * 0.5f, Profile.Kind.Line);
            built = true;
            builtA = a;
            builtB = b;
        }

        internal static void Compose(
            List<Vector3[]> paths, Vector3[] points,
            Vector3 a, Vector3 b, float span, float diameter, float sag,
            Vector3 hookUpA, Vector3 hookUpB, out float knotReach)
        {
            Vector3 Point(float s)
            {
                return SagPoint(a, b, span, sag, s);
            }

            float Arc()
            {
                int steps = MeshSegments * 4;
                float length = 0f;
                Vector3 prev = Point(0f);
                for (int i = 1; i <= steps; i++)
                {
                    Vector3 next = Point(i / (float)steps);
                    length += Vector3.Distance(prev, next);
                    prev = next;
                }
                return length;
            }

            Vector3 Along(float distance, bool fromEnd, out float s)
            {
                int steps = MeshSegments * 4;
                float walked = 0f;
                float sPrev = fromEnd ? 1f : 0f;
                Vector3 prev = Point(sPrev);
                for (int i = 1; i <= steps; i++)
                {
                    float sNext = fromEnd ? 1f - (float)i / steps : (float)i / steps;
                    Vector3 next = Point(sNext);
                    float step = Vector3.Distance(prev, next);
                    if (step > 1e-6f && walked + step >= distance)
                    {
                        float u = (distance - walked) / step;
                        s = Mathf.Lerp(sPrev, sNext, u);
                        return Vector3.Lerp(prev, next, u);
                    }

                    walked += step;
                    prev = next;
                    sPrev = sNext;
                }

                s = fromEnd ? 0f : 1f;
                return Point(s);
            }

            for (int i = 0; i <= MeshSegments; i++)
                points[i] = Point((float)i / MeshSegments);

            paths.Clear();
            float scale = RopeKnot.Scale(diameter, Arc());
            if (scale <= 0f)
            {
                knotReach = 0f;
                paths.Add(points);
                return;
            }

            knotReach = RopeKnot.Reach(diameter, scale);
            float trim = RopeKnot.Trim(diameter, scale);
            float fromA;
            float fromB;
            Vector3 joinA = Along(trim, false, out fromA);
            Vector3 joinB = Along(trim, true, out fromB);
            if (fromB - fromA < 0.02f)
            {
                knotReach = 0f;
                paths.Add(points);
                return;
            }

            var rope = new Vector3[MeshSegments + 1];
            for (int i = 0; i <= MeshSegments; i++)
                rope[i] = Point(Mathf.Lerp(fromA, fromB, i / (float)MeshSegments));
            rope[0] = joinA;
            rope[MeshSegments] = joinB;
            paths.Add(rope);
            RopeKnot.Add(paths, Point(0f), joinA, hookUpA, diameter, scale);
            RopeKnot.Add(paths, Point(1f), joinB, hookUpB, diameter, scale);
        }

        // Point on the rope, in this object's space. Sags along the boat's down, so it
        // heels with the boat like the hooks do.
        private Vector3 Point(float s)
        {
            return SagPoint(_a, _b, _span, ClotheslineConfig.Sag.Value, s);
        }

        internal static Vector3 SagPoint(Vector3 a, Vector3 b, float span, float sag, float s)
        {
            Vector3 p = Vector3.Lerp(a, b, s);
            p.y -= span * sag * 4f * s * (1f - s);
            return p;
        }

        internal Vector3 RopeWorld(float s)
        {
            return transform.TransformPoint(Point(Mathf.Clamp01(s)));
        }

        // The closest point on this rope to a look ray. Only points in front of the
        // ray, no farther than maxAlong, and within maxMiss of it, are considered.
        // The winner is the closest of those, so a click lands on the rope.
        internal bool NearestOnRay(Ray ray, float maxAlong, float maxMiss, out Vector3 point, out float along)
        {
            point = default(Vector3);
            along = 0f;
            float bestMiss = maxMiss * maxMiss;
            bool found = false;
            Vector3 origin = ray.origin;
            Vector3 dir = ray.direction;
            Vector3 previous = RopeWorld(0f);
            for (int i = 1; i <= MeshSegments; i++)
            {
                Vector3 next = RopeWorld(i / (float)MeshSegments);
                float segAlong;
                Vector3 segPoint;
                float miss;
                if (SegmentApproach(origin, dir, previous, next, maxAlong, out segAlong, out segPoint, out miss)
                    && miss <= bestMiss
                    && (!found || miss < bestMiss || (miss == bestMiss && segAlong < along)))
                {
                    bestMiss = miss;
                    point = segPoint;
                    along = segAlong;
                    found = true;
                }
                previous = next;
            }
            return found;
        }

        // Closest points of the ray and one rope sample. miss is squared.
        private static bool SegmentApproach(
            Vector3 origin, Vector3 dir, Vector3 a, Vector3 b, float maxAlong,
            out float along, out Vector3 point, out float miss)
        {
            along = 0f;
            point = a;
            miss = 0f;
            Vector3 ab = b - a;
            float abLen2 = ab.sqrMagnitude;
            if (abLen2 < 1e-10f || !Finite(a) || !Finite(b))
                return false;

            Vector3 ao = origin - a;
            float dAb = Vector3.Dot(dir, ab);
            float dAo = Vector3.Dot(dir, ao);
            float abAo = Vector3.Dot(ab, ao);
            float denom = abLen2 - dAb * dAb;
            float s;
            if (denom < 1e-8f)
            {
                float alongA = Vector3.Dot(a - origin, dir);
                float alongB = Vector3.Dot(b - origin, dir);
                if (alongA < 0f && alongB < 0f)
                    return false;
                if (alongA < 0f)
                    s = 1f;
                else if (alongB < 0f)
                    s = 0f;
                else
                    s = alongA <= alongB ? 0f : 1f;
            }
            else
            {
                s = (dAb * dAo - abAo) / denom;
                if (s < 0f)
                    s = 0f;
                else if (s > 1f)
                    s = 1f;
            }

            point = a + ab * s;
            if (!Finite(point))
                return false;
            along = Vector3.Dot(point - origin, dir);
            if (along < 0f || along > maxAlong)
                return false;
            miss = (point - (origin + dir * along)).sqrMagnitude;
            return true;
        }

        private static bool Finite(Vector3 v)
        {
            return !float.IsNaN(v.x) && !float.IsNaN(v.y) && !float.IsNaN(v.z)
                && !float.IsInfinity(v.x) && !float.IsInfinity(v.y) && !float.IsInfinity(v.z);
        }

        internal Vector3 RopeDirectionWorld(float s)
        {
            float a = Mathf.Clamp01(s - 0.01f);
            float b = Mathf.Clamp01(s + 0.01f);
            Vector3 along = Point(b) - Point(a);
            if (along.sqrMagnitude < 1e-10f)
                along = Vector3.right;
            return transform.TransformDirection(along.normalized);
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
            // The rope's own radius, plus a few millimetres. A fatter capsule hangs
            // in the air under the line, and growing it while an item is held moves
            // the spot the pointer has to find.
            float radius = ClotheslineConfig.Thickness.Value * 0.5f + 0.006f;
            for (int i = 0; i < MeshSegments; i++)
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

                Vector3 from = _points[i];
                Vector3 to = _points[i + 1];
                Vector3 along = to - from;
                float length = along.magnitude;
                col.transform.localPosition = (from + to) * 0.5f;
                col.transform.localRotation = length > 1e-4f ? Quaternion.LookRotation(along / length, Vector3.up) : Quaternion.identity;
                col.radius = radius;
                col.height = length + radius * 2f;
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

        internal void Place(LineAnchor anchor)
        {
            float s = anchor.Record.S;
            Vector3 across = Across(s) * anchor.Record.Side;
            anchor.transform.localRotation = Quaternion.LookRotation(across, Vector3.up);
            // Extra gap so an eyepiece, a hammer head, or a handle is not buried in
            // the rope. The pose itself puts that point on the pin.
            float drop = SpyglassTie.Drop(anchor.Ship) + HammerTie.Drop(anchor.Ship) + PotTie.Drop(anchor.Ship)
                + LanternHang.Drop(anchor.Ship) + WindChimeHang.Drop(anchor.Ship)
                + MugHang.Drop(anchor.Ship);
            anchor.transform.localPosition = Point(s) + across * Lines.HookOffset
                - Vector3.up * (ClotheslineConfig.HangDrop.Value + drop);
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
        // spacing from each other, and stay clear of the hooks and the knots on them.
        internal bool TryFindRoom(float wanted, out float s)
        {
            s = wanted;
            if (_span <= 0f)
                return false;

            float min = Mathf.Max(EndGap, _knotReach) / _span;
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
            long stamp = Profile.Stamp();
            try
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
                else
                {
                    SyncHangers(anchor);
                }
            }

            unclickable = Lines.HeldBlocksLines;
            lookText = Lines.LookTextFor(Record);
            }
            finally
            {
                Profile.Add(Profile.Kind.Late, stamp);
            }
        }

        private static void SyncHangers(LineAnchor anchor)
        {
            if (!ClotheslineConfig.DrawHangers)
            {
                StemTie.Clear(anchor);
                SpyglassTie.Clear(anchor);
                RackHook.Clear(anchor);
                LoopTie.Clear(anchor);
                HammerTie.Clear(anchor);
                PotTie.Clear(anchor);
                SausageKnot.Clear(anchor);
                BagKnot.Clear(anchor);
                return;
            }

            StemTie.Sync(anchor);
            SpyglassTie.Sync(anchor);
            RackHook.Sync(anchor);
            LoopTie.Sync(anchor);
            HammerTie.Sync(anchor);
            PotTie.Sync(anchor);
            SausageKnot.Sync(anchor);
            BagKnot.Sync(anchor);
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
