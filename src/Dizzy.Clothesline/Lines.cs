using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Dizzy.Clothesline
{
    // Tying and untying lines, hanging items on them, and putting both back after a
    // load or when a distant boat spawns its items again.
    internal static class Lines
    {
        // HangableItem.LateUpdate pins an item this far out from its hook.
        internal const float HookOffset = 0.128f;

        // GoPointer.DoRaycast's reach and layer mask.
        private const float Reach = 1.8f;
        private const int LookMask = -604165;
        // While a hangable item is held, a look this close to the rope counts as
        // a hit on the rope. The capsules stay thin; this is not a collider.
        private const float AimRadius = 0.12f;

        private const float MinLength = 0.3f;
        private const float RetryWindow = 10f;
        private const float RetryInterval = 0.25f;
        private const float FreezeTimeout = 10f;

        private static readonly AccessTools.FieldRef<HangableItem, Collider> CurrentHook;
        private static readonly AccessTools.FieldRef<HangableItem, Rigidbody> HangBody;
        private static readonly AccessTools.FieldRef<HangableItem, ItemRigidbody> HangTwin;
        private static readonly bool Ready;

        // Hung items by HangableItem instance id. Instance ids still work on destroyed objects.
        private static readonly Dictionary<int, LineAnchor> Hung = new Dictionary<int, LineAnchor>();

        // Items held still from the moment they load until they are hung again, by save id.
        private static readonly Dictionary<int, FrozenItem> Frozen = new Dictionary<int, FrozenItem>();
        private static readonly List<int> ExpiredFreezes = new List<int>();

        private static readonly RaycastHit[] Hits = new RaycastHit[16];

        private static ShipItem _pendingHook;
        private static LineRenderer _preview;
        private static GoPointer _pointer;
        private static float _nextPointerSearch;

        internal static bool HeldBlocksLines;

        private static bool _dirty;
        private static float _retryAt;
        private static float _retryUntil;

        private static ShipItem _destroying;
        private static bool _destroyingKeeps;

        private sealed class FrozenItem
        {
            internal ShipItem Ship;
            internal float Since;
        }

        static Lines()
        {
            try
            {
                CurrentHook = AccessTools.FieldRefAccess<HangableItem, Collider>("currentHook");
                HangBody = AccessTools.FieldRefAccess<HangableItem, Rigidbody>("itemRigidbody");
                HangTwin = AccessTools.FieldRefAccess<HangableItem, ItemRigidbody>("itemRigidbodyC");
                Ready = true;
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError("HangableItem has changed; lines are turned off. " + ex);
            }
        }

        internal static Vector3 TiePoint(ShipItem hook)
        {
            return hook.transform.position - hook.transform.forward * HookOffset;
        }

        // Held by anyone. Coop marks an item another player carries as held by our
        // own pointer.
        internal static bool HookTaken(ShipItem hook)
        {
            return hook.held != null || hook.GetCurrentInventorySlot() != -1;
        }

        // In this player's own hand.
        internal static bool IsHeldLocally(ShipItem item)
        {
            return item != null && item.held != null && item.held.GetHeldItem() == item;
        }

        private static int Id(ShipItem item)
        {
            SaveablePrefab save = item != null ? item.Getsaveable() : null;
            return save != null ? save.instanceId : 0;
        }

        private static ShipItem FindLive(int instanceId)
        {
            if (instanceId == 0 || SaveLoadManager.instance == null)
                return null;
            List<SaveablePrefab> prefabs = SaveLoadManager.instance.GetCurrentPrefabs();
            for (int i = 0; i < prefabs.Count; i++)
            {
                if (prefabs[i] != null && prefabs[i].instanceId == instanceId)
                    return prefabs[i].GetComponent<ShipItem>();
            }
            return null;
        }

        // ---- Every frame ----

        internal static void Tick()
        {
            if (!Ready)
                return;

            UpdateHeld();
            HandleKey();
            UpdatePreview();
            if (_dirty || (_retryAt > 0f && Time.time >= _retryAt))
                Resolve();
            if (Frozen.Count > 0)
                ReleaseExpiredFreezes();
        }

        private static GoPointer Pointer()
        {
            if (_pointer != null)
                return _pointer;
            if (Time.unscaledTime < _nextPointerSearch)
                return null;

            _nextPointerSearch = Time.unscaledTime + 1f;
            GoPointer[] pointers = Object.FindObjectsOfType<GoPointer>();
            for (int i = 0; i < pointers.Length; i++)
            {
                if (pointers[i] != null && pointers[i].type == GoPointer.PointerType.crosshairMouse)
                {
                    _pointer = pointers[i];
                    break;
                }
            }
            return _pointer;
        }

        private static void UpdateHeld()
        {
            GoPointer pointer = Pointer();
            PickupableItem held = pointer != null ? pointer.GetHeldItem() : null;
            bool canHang = CanHang(held);
            HeldBlocksLines = held != null && !canHang;

            _canHang = canHang;
        }

        private static bool _canHang;

        // The key does two things: it spaces out a line with items on it, and unties
        // an empty one, so a loaded line can't be untied by accident.
        internal static string LookTextFor(LineRecord record)
        {
            if (_canHang)
                return "hang here";
            string key = ClotheslineConfig.TieKeyLabel;
            return record != null && record.Items.Count > 0
                ? "clothesline\n" + key + ": space out"
                : "clothesline\n" + key + ": untie";
        }

        private static bool CanHang(PickupableItem item)
        {
            ShipItem ship = item as ShipItem;
            if (ship == null || !ship.sold)
                return false;
            HangableItem hang = ship.GetComponent<HangableItem>();
            if (SpyglassTie.Is(ship) || HammerTie.Is(ship))
                return hang == null || !hang.IsHanging();
            return hang != null && !hang.IsHanging();
        }

        // A look that passes near a rope while the held item can hang. The point is
        // on the rope. A target already closer along the ray, other than a line,
        // blocks it: that look belongs to whatever is in front.
        internal static bool TryAimHang(
            Ray ray,
            PickupableItem held,
            GoPointerButton current,
            float currentDistance,
            out ClotheslineLine line,
            out Vector3 point,
            out float along)
        {
            line = null;
            point = default(Vector3);
            along = 0f;
            if (!CanHang(held) || LineStore.All.Count == 0)
                return false;

            Vector3 dir = ray.direction;
            float scale = dir.magnitude;
            if (scale < 1e-6f)
                return false;
            Ray aimed = new Ray(ray.origin, dir / scale);

            // The line's own capsule is the rope, so it must not block the snap
            // onto the centerline. Anything else in front of the rope does.
            float limit = Reach;
            bool blocked = false;
            if (current != null && !(current is ClotheslineLine) && currentDistance < limit)
            {
                limit = currentDistance;
                blocked = true;
            }

            float bestAlong = float.MaxValue;
            for (int i = 0; i < LineStore.All.Count; i++)
            {
                ClotheslineLine live = LineStore.All[i].Live;
                if (live == null || live.unclickable)
                    continue;
                Vector3 segPoint;
                float segAlong;
                if (!live.NearestOnRay(aimed, limit, AimRadius, out segPoint, out segAlong))
                    continue;
                if (blocked ? segAlong >= limit : segAlong > limit)
                    continue;
                if (segAlong >= bestAlong)
                    continue;
                bestAlong = segAlong;
                line = live;
                point = segPoint;
                along = segAlong;
            }
            return line != null;
        }

        // ---- Tying and untying ----

        private static void HandleKey()
        {
            if (!Input.GetKeyDown(ClotheslineConfig.TieKey.Value))
                return;
            if (!GameState.playing || GameState.inCursorMenu || GameState.sleeping || BoatCamera.on)
                return;

            GoPointer pointer = Pointer();
            if (pointer == null)
                return;

            ShipItem hook;
            ClotheslineLine line;
            LookTarget(pointer, out hook, out line);

            if (line != null)
            {
                if (line.Record.Items.Count > 0)
                {
                    Spread(line.Record, true);
                    return;
                }
                Untie(line.Record, "Line untied.", true);
                return;
            }
            if (hook == null)
            {
                if (_pendingHook != null)
                    CancelPending("Line cancelled.");
                return;
            }
            if (_pendingHook == null)
            {
                StartLine(hook);
                return;
            }
            if (hook == _pendingHook)
            {
                CancelPending("Line cancelled.");
                return;
            }
            FinishLine(_pendingHook, hook);
        }

        // The game's pointer skips nailed items, and with a lantern in hand it only sees
        // free hooks, so look for ourselves. Stop at the first thing in the way.
        private static void LookTarget(GoPointer pointer, out ShipItem hook, out ClotheslineLine line)
        {
            hook = null;
            line = null;

            Transform from = pointer.transform;
            // modcheck-ignore: SW501 hits are sorted by distance and the loop returns at the first blocking hit
            int count = Physics.RaycastNonAlloc(new Ray(from.position, from.forward), Hits, Reach, LookMask, QueryTriggerInteraction.Collide);
            SortByDistance(count);
            for (int i = 0; i < count; i++)
            {
                Collider col = Hits[i].collider;
                if (col == null)
                    continue;

                ClotheslineLine hitLine = col.GetComponentInParent<ClotheslineLine>();
                if (hitLine != null)
                {
                    line = hitLine;
                    return;
                }

                ShipItem item = col.GetComponentInParent<ShipItem>();
                if (item != null)
                {
                    if (item.held != null)
                        continue;
                    if (item is ShipItemLampHook)
                    {
                        hook = item;
                        return;
                    }
                    // See past a lantern on a lamp hook, not past an item on a line:
                    // the line behind it is not what the player is looking at.
                    HangableItem hang = item.GetComponent<HangableItem>();
                    if (hang != null && hang.IsHanging() && !Hung.ContainsKey(hang.GetInstanceID()))
                        continue;
                    return;
                }

                if (col.isTrigger)
                    continue;
                return;
            }
        }

        private static void SortByDistance(int count)
        {
            for (int i = 1; i < count; i++)
            {
                RaycastHit hit = Hits[i];
                int j = i - 1;
                while (j >= 0 && Hits[j].distance > hit.distance)
                {
                    Hits[j + 1] = Hits[j];
                    j--;
                }
                Hits[j + 1] = hit;
            }
        }

        private static bool CheckHook(ShipItem hook)
        {
            if (hook == null || !hook.sold || HookTaken(hook) || Id(hook) == 0)
                return false;
            ItemRigidbody twin = hook.GetItemRigidbody();
            if (hook.wallAttachment && !hook.nailed && (twin == null || !twin.attached))
            {
                Notify("Put the hook up on a wall first.");
                return false;
            }
            return true;
        }

        private static void StartLine(ShipItem hook)
        {
            if (!CheckHook(hook))
                return;
            _pendingHook = hook;
            PlayKnotSound();
            Notify("Now look at another hook and press " + ClotheslineConfig.TieKeyLabel + " to tie the line.");
        }

        private static void FinishLine(ShipItem a, ShipItem b)
        {
            if (!CheckHook(a))
            {
                CancelPending(null);
                return;
            }
            if (!CheckHook(b))
                return;

            if (a.transform.parent != b.transform.parent)
            {
                Notify("Both hooks need to be on the same boat.");
                return;
            }

            float length = Vector3.Distance(TiePoint(a), TiePoint(b));
            float max = ClotheslineConfig.MaxLength.Value;
            if (length > max)
            {
                Notify("Too far for one line. The longest is " + max.ToString("0.#") + " m.");
                return;
            }
            if (length < MinLength)
            {
                Notify("Those hooks are too close together.");
                return;
            }

            int idA = Id(a);
            int idB = Id(b);
            if (LineStore.Find(idA, idB) != null)
            {
                CancelPending("Those hooks already have a line.");
                return;
            }

            var record = new LineRecord { HookA = idA, HookB = idB };
            LineStore.Add(record);
            Spawn(record, a, b);
            CoopLink.SendTie(record);
            _pendingHook = null;
            PlayKnotSound();
            Notify("Line tied.");
        }

        private static void CancelPending(string message)
        {
            _pendingHook = null;
            if (message != null)
                Notify(message);
        }

        private static void UpdatePreview()
        {
            if (_pendingHook != null && HookTaken(_pendingHook))
                _pendingHook = null;
            if (_pendingHook == null)
            {
                // Also clears a hook that was destroyed while pending.
                _pendingHook = null;
                if (_preview != null)
                    _preview.enabled = false;
                return;
            }

            GoPointer pointer = Pointer();
            if (pointer == null)
                return;

            if (_preview == null)
            {
                var go = new GameObject("Dizzy Clothesline Preview");
                _preview = go.AddComponent<LineRenderer>();
                _preview.useWorldSpace = true;
                _preview.positionCount = 2;
                _preview.numCapVertices = 2;
                _preview.sharedMaterial = RopeMesh.Material;
                _preview.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _preview.receiveShadows = false;
            }

            Vector3 end = pointer.pointer != null
                ? pointer.pointer.position
                : pointer.transform.position + pointer.transform.forward * 1.5f;
            float width = ClotheslineConfig.Thickness.Value;
            _preview.startWidth = width;
            _preview.endWidth = width;
            _preview.SetPosition(0, TiePoint(_pendingHook));
            _preview.SetPosition(1, end);
            _preview.enabled = true;
        }

        private static void Spawn(LineRecord record, ShipItem a, ShipItem b)
        {
            var go = new GameObject("Dizzy Clothesline");
            go.AddComponent<MeshFilter>();
            go.AddComponent<MeshRenderer>();
            ClotheslineLine line = go.AddComponent<ClotheslineLine>();
            line.Init(record, a, b);
            record.Live = line;
        }

        // Takes the line down for good. Whatever hangs on it falls. broadcast: this
        // player untied it, so the other players untie it too.
        internal static void Untie(LineRecord record, string message, bool broadcast)
        {
            ClotheslineLine line = record.Live;
            record.Live = null;
            if (line != null)
            {
                LineAnchor[] anchors = line.Anchors();
                for (int i = 0; i < anchors.Length; i++)
                {
                    LineAnchor anchor = anchors[i];
                    if (anchor.Item != null)
                        anchor.Item.DisconnectJoint();
                    if (anchor != null && Hung.ContainsKey(anchor.HangId))
                        Release(anchor, false);
                }
                // Destroy waits for the end of the frame; stop its LateUpdate now.
                line.Record = null;
                Object.Destroy(line.gameObject);
            }
            LineStore.Remove(record);

            if (broadcast)
                CoopLink.SendUntie(record);
            if (message != null)
            {
                PlayKnotSound();
                Notify(message);
            }
        }

        // Spaces the items on a line evenly, in their present order, with equal gaps
        // at both ends. One item ends up in the middle.
        internal static void Spread(LineRecord record, bool broadcast)
        {
            if (record == null || record.Items.Count == 0)
            {
                if (broadcast)
                    Notify("Nothing hangs on this line.");
                return;
            }

            List<HungRecord> items = record.Items;
            items.Sort((a, b) => a.S.CompareTo(b.S));
            for (int i = 0; i < items.Count; i++)
                items[i].S = (i + 0.5f) / items.Count;

            if (record.Live != null)
                record.Live.Rebuild();
            if (broadcast)
            {
                CoopLink.SendSpread(record);
                PlayKnotSound();
            }
        }

        // A hook was destroyed without DestroyItem.
        internal static void HookVanished(LineRecord record)
        {
            // In Coop the host decides what is gone. A guest only takes the line
            // out of its world.
            if (CoopLink.Applying)
                Despawn(record);
            else
                Untie(record, null, false);
        }

        // Takes the line out of the world but keeps it in the save, for when its boat
        // goes out of range and its items are put away.
        private static void Despawn(LineRecord record)
        {
            ClotheslineLine line = record.Live;
            record.Live = null;
            if (line == null)
                return;

            LineAnchor[] anchors = line.Anchors();
            for (int i = 0; i < anchors.Length; i++)
                Release(anchors[i], true);
            line.Record = null;
            Object.Destroy(line.gameObject);
        }

        internal static void RebuildAll()
        {
            for (int i = 0; i < LineStore.All.Count; i++)
            {
                if (LineStore.All[i].Live != null)
                    LineStore.All[i].Live.Rebuild();
            }
        }

        // ---- Hanging ----

        internal static bool HangByPlayer(ClotheslineLine line, PickupableItem held, Vector3 point)
        {
            if (!Ready || !CanHang(held))
                return false;

            var ship = (ShipItem)held;
            int id = Id(ship);
            if (id == 0)
                return false;

            float s;
            if (!line.TryFindRoom(line.ClosestS(point), out s))
            {
                Notify("No room there on the line.");
                return false;
            }

            Vector3 view = ship.held != null ? ship.held.transform.forward : line.transform.forward;
            var record = new HungRecord { ItemId = id, S = s, Side = line.SideFacing(s, view) };
            LineStore.AddItem(line.Record, record);

            HangableItem hang = ship.GetComponent<HangableItem>();
            if (!HangNow(line, record, hang, ship))
            {
                // Another mod stopped the hang. Keep the item in hand.
                LineStore.RemoveItem(id);
                return false;
            }

            // Ours first, so the others know the item belongs on this line when
            // Coop's own ItemHung for it arrives.
            CoopLink.SendHang(line.Record, record);
            CoopLink.NotifyHung(ship, line.HookA);
            return true;
        }

        // Puts an item on its place on a live line. The saved world rotation is not
        // used: the game stores it in world space, so a restart leaves the item
        // turned against the boat. Face it across the line, then apply the hang pose.
        private static bool HangNow(ClotheslineLine line, HungRecord record, HangableItem hang, ShipItem ship)
        {
            if (ship == null)
                return false;
            if (SpyglassTie.Is(ship))
                hang = SpyglassTie.Prepare(ship);
            else if (HammerTie.Is(ship))
                hang = HammerTie.Prepare(ship);
            if (hang == null)
                return false;

            LineAnchor anchor = line.CreateAnchor(record);
            PlaceOnAnchor(ship, anchor, true);
            hang.ConnectJoint(anchor.Col);
            if (anchor.Item != hang)
            {
                line.RemoveAnchor(anchor);
                Object.Destroy(anchor.gameObject);
                SpyglassTie.Detach(ship);
                HammerTie.Detach(ship);
                return false;
            }

            RestoreHangRotation(hang);
            return true;
        }

        // Where HangableItem will pin the item, so the twin is put there too.
        private static void PlaceOnAnchor(ShipItem ship, LineAnchor anchor, bool face)
        {
            // Place uses the ship to drop a spyglass until its eyepiece is under the rope.
            anchor.Ship = ship;
            if (anchor.Line != null)
                anchor.Line.Place(anchor);

            if (face && !SpyglassTie.Is(ship) && !HammerTie.Is(ship))
            {
                // Away from the player, the way an item on a wall hook ends up.
                Vector3 facing = anchor.transform.forward;
                facing.y = 0f;
                if (facing.sqrMagnitude > 1e-6f)
                    ship.transform.rotation = Quaternion.LookRotation(facing, Vector3.up);
            }
            ship.transform.position = anchor.transform.position - anchor.transform.forward * HookOffset;
        }

        // HooksHangMore adds its yaw once in ConnectJoint, and skips that during a load.
        private static bool Loading
        {
            get { return GameState.currentlyLoading || GameState.loadingBoatLocalItems; }
        }

        // The line facing is already on the item. During a load, add the yaw
        // HooksHangMore skipped, then the poses that level a pot or turn a lantern.
        private static void RestoreHangRotation(HangableItem hang)
        {
            if (hang == null)
                return;
            if (Loading)
                ApplyMissedHangYaw(hang);
            PoseSpyglass(hang);
            PoseWindicator(hang);
            PoseHammer(hang);
            PosePot(hang);
            PoseLantern(hang);
            PoseWindChime(hang);
            PoseMug(hang);
        }

        private static bool _yawLookup;
        private static FieldInfo _hangingItems;
        private static MethodInfo _tryHangOffset;
        private static PropertyInfo _offsetRotation;

        // Same yaw HooksHangMore.HangablePatches adds in ConnectJoint outside a load.
        private static void ApplyMissedHangYaw(HangableItem hang)
        {
            if (!_yawLookup)
            {
                _yawLookup = true;
                System.Type offsets = AccessTools.TypeByName("HooksHangMore.Offsets");
                System.Type extensions = AccessTools.TypeByName("HooksHangMore.OffsetsExtensions");
                if (offsets != null && extensions != null)
                {
                    _hangingItems = AccessTools.Field(offsets, "HangingItems");
                    _offsetRotation = AccessTools.Property(offsets, "Rotation");
                    List<MethodInfo> methods = AccessTools.GetDeclaredMethods(extensions);
                    for (int i = 0; i < methods.Count; i++)
                    {
                        if (methods[i].Name != "TryGetOffset")
                            continue;
                        ParameterInfo[] parameters = methods[i].GetParameters();
                        if (parameters.Length == 3 && parameters[1].ParameterType == typeof(HangableItem))
                        {
                            _tryHangOffset = methods[i];
                            break;
                        }
                    }
                }
            }

            if (_hangingItems == null || _tryHangOffset == null || _offsetRotation == null)
                return;
            object table = _hangingItems.GetValue(null);
            if (table == null)
                return;
            var args = new object[] { table, hang, null };
            object found = _tryHangOffset.Invoke(null, args);
            if (!(found is bool) || !(bool)found || args[2] == null)
                return;
            object rotation = _offsetRotation.GetValue(args[2], null);
            if (!(rotation is Vector3))
                return;
            float yaw = ((Vector3)rotation).y;
            if (Mathf.Abs(yaw) < 0.01f)
                return;
            Vector3 euler = hang.transform.eulerAngles;
            euler.y += yaw;
            hang.transform.eulerAngles = euler;
        }

        // HangableItem.ConnectJoint. A place on a line: does what the game does for a
        // lamp hook, without the joint: the twin goes still and the item is pinned in
        // HangableItem.LateUpdate.
        //
        // A lamp hook, for an item that belongs on a line: Coop announces a hang on a
        // line as a hang on the line's first hook. Keep the item on its line.
        internal static bool TryConnect(HangableItem hang, Collider hook)
        {
            if (!Ready || hang == null || hook == null)
                return false;
            LineAnchor anchor = hook.GetComponent<LineAnchor>();
            if (anchor != null)
            {
                Connect(hang, anchor);
                return true;
            }

            SaveablePrefab save = hang.GetComponent<SaveablePrefab>();
            int id = save != null ? save.instanceId : 0;
            LineRecord line;
            HungRecord record;
            if (id == 0 || !LineStore.TryGetItem(id, out line, out record))
                return false;

            ShipItem ship = hang.GetComponent<ShipItem>();
            LineAnchor current;
            if (Hung.TryGetValue(hang.GetInstanceID(), out current))
            {
                // Coop has just put the item's physics back. Hang it again.
                PlaceOnAnchor(ship, current, true);
                Connect(hang, current);
                RestoreHangRotation(hang);
            }
            else if (line.Live != null && ship != null)
            {
                HangNow(line.Live, record, hang, ship);
            }
            else
            {
                Freeze(ship, id);
                MarkDirty();
            }
            return true;
        }

        private static void Connect(HangableItem hang, LineAnchor anchor)
        {
            ShipItem ship = hang.GetComponent<ShipItem>();
            ItemRigidbody twin = ship != null ? ship.GetItemRigidbody() : null;
            CurrentHook(hang) = anchor.Col;
            if (twin != null)
            {
                HangTwin(hang) = twin;
                Rigidbody body = twin.GetBody();
                if (body != null)
                    HangBody(hang) = body;
                twin.attached = true;
                twin.disableCol = true;
                twin.inStove = true;
                twin.ForceRigidbodyToWalkCol();
            }
            if (ship != null)
                ship.ToggleDisallowDisembarking(true);

            anchor.Item = hang;
            anchor.Ship = ship;
            anchor.HangId = hang.GetInstanceID();
            Hung[anchor.HangId] = anchor;
            Frozen.Remove(anchor.Record.ItemId);
            StemTie.Sync(anchor);
            SpyglassTie.Sync(anchor);
            RackHook.Sync(anchor);
            LoopTie.Sync(anchor);
            HammerTie.Sync(anchor);
            PotTie.Sync(anchor);
            SausageKnot.Sync(anchor);
            BagKnot.Sync(anchor);
        }

        // HangableItem.DisconnectJoint, for an item on a line.
        internal static bool TryDisconnect(HangableItem hang)
        {
            if (!Ready || hang == null)
                return false;
            LineAnchor anchor;
            if (!Hung.TryGetValue(hang.GetInstanceID(), out anchor))
                return false;

            bool keep = _destroyingKeeps && _destroying != null && _destroying.gameObject == hang.gameObject;
            Release(anchor, keep);
            return true;
        }

        // keepRecord: the item is only being put away with its boat, so it stays hung
        // in the save and stays still until it is destroyed.
        // modcheck-ignore: SW603 an item put away with its boat (-2) or sunk (-3) keeps its record; only a real destroy removes it
        private static void Release(LineAnchor anchor, bool keepRecord)
        {
            StemTie.Clear(anchor);
            SpyglassTie.Clear(anchor);
            RackHook.Clear(anchor);
            LoopTie.Clear(anchor);
            HammerTie.Clear(anchor);
            PotTie.Clear(anchor);
            SausageKnot.Clear(anchor);
            BagKnot.Clear(anchor);
            Hung.Remove(anchor.HangId);
            if (anchor.Item != null)
                CurrentHook(anchor.Item) = null;

            if (!keepRecord)
            {
                if (anchor.Record != null)
                {
                    LineStore.RemoveItem(anchor.Record.ItemId);
                    // Coop sends a pickup on its own, but not to a player whose boat
                    // has put its items away. This reaches their saved line too.
                    if (IsHeldLocally(anchor.Ship))
                        CoopLink.SendUnhang(anchor.Record.ItemId);
                }

                ShipItem ship = anchor.Ship;
                if (ship != null)
                {
                    ItemRigidbody twin = ship.GetItemRigidbody();
                    if (twin != null)
                    {
                        twin.attached = false;
                        twin.disableCol = false;
                        twin.inStove = false;
                        // The twin stood still while the item hung. Bring it to the item,
                        // so the item falls from where it hung.
                        if (ship.held == null)
                        {
                            if (ship.currentActualBoat != null)
                                twin.ForceRigidbodyToWalkCol();
                            else if (twin.GetBody() != null)
                                ship.ResetRigidbody();
                        }
                    }
                    ship.ToggleDisallowDisembarking(false);
                    SpyglassTie.Detach(ship);
                    HammerTie.Detach(ship);
                }
            }

            if (anchor.Line != null)
                anchor.Line.RemoveAnchor(anchor);
            Object.Destroy(anchor.gameObject);
        }

        // The item was destroyed without DestroyItem, so it is gone for good, unless
        // Coop is setting up this guest's world, which the host's lines then replace.
        internal static void ItemVanished(LineAnchor anchor)
        {
            Release(anchor, CoopLink.Applying);
        }

        // HangableItem.OnTriggerEnter hangs a freshly loaded item on any lamp hook it
        // touches. Items that belong on a line must wait for their line instead.
        internal static bool Claims(HangableItem hang)
        {
            if (!Ready || hang == null)
                return false;
            if (Hung.ContainsKey(hang.GetInstanceID()))
                return true;
            SaveablePrefab save = hang.GetComponent<SaveablePrefab>();
            return save != null && save.instanceId != 0 && LineStore.IsHungItem(save.instanceId);
        }

        // ---- Items coming and going ----

        internal static void BeginDestroy(ShipItem item)
        {
            _destroying = item;
            SaveablePrefab save = item != null ? item.Getsaveable() : null;
            int parent = save != null ? save.GetParentObject() : 0;
            // -2: its boat went out of range. -3: its boat sank and is waiting to be
            // recovered. Either way the item comes back later. In Coop, an item another
            // player destroyed: the host's save decides whether it is gone.
            _destroyingKeeps = parent == -2 || parent == -3 || CoopLink.Applying;
        }

        internal static void EndDestroy(ShipItem item, bool destroyed)
        {
            try
            {
                if (!Ready || !destroyed || item == null)
                    return;

                HangableItem hang = item.GetComponent<HangableItem>();
                LineAnchor anchor;
                if (hang != null && Hung.TryGetValue(hang.GetInstanceID(), out anchor))
                    Release(anchor, _destroyingKeeps);

                int id = Id(item);
                if (id == 0 || !LineStore.IsHook(id))
                    return;

                var lines = new List<LineRecord>();
                LineStore.LinesOnHook(id, lines);
                for (int i = 0; i < lines.Count; i++)
                {
                    if (_destroyingKeeps)
                        Despawn(lines[i]);
                    else
                        Untie(lines[i], null, false);
                }
            }
            finally
            {
                _destroying = null;
                _destroyingKeeps = false;
            }
        }

        internal static void OnPrefabLoaded(SaveablePrefab prefab)
        {
            if (!Ready || prefab == null || prefab.instanceId == 0)
                return;

            int id = prefab.instanceId;
            if (LineStore.IsHungItem(id))
            {
                Freeze(prefab.GetComponent<ShipItem>(), id);
                MarkDirty();
            }
            else if (LineStore.IsHook(id))
            {
                MarkDirty();
            }
        }

        // Holds a loaded item where it was saved until its line is back, so it does not
        // drop to the deck in between.
        private static void Freeze(ShipItem ship, int id)
        {
            ItemRigidbody twin = ship != null ? ship.GetItemRigidbody() : null;
            if (twin == null)
                return;
            twin.attached = true;
            twin.disableCol = true;
            twin.inStove = true;
            Frozen[id] = new FrozenItem { Ship = ship, Since = Time.time };
        }

        private static void Unfreeze(int id)
        {
            FrozenItem frozen;
            if (!Frozen.TryGetValue(id, out frozen))
                return;
            Frozen.Remove(id);

            ShipItem ship = frozen.Ship;
            if (ship == null)
                return;
            HangableItem hang = ship.GetComponent<HangableItem>();
            if (hang != null && Hung.ContainsKey(hang.GetInstanceID()))
                return;
            ItemRigidbody twin = ship.GetItemRigidbody();
            if (twin == null)
                return;
            twin.attached = false;
            twin.disableCol = false;
            twin.inStove = false;
        }

        private static void ReleaseExpiredFreezes()
        {
            ExpiredFreezes.Clear();
            foreach (KeyValuePair<int, FrozenItem> pair in Frozen)
            {
                if (pair.Value.Ship == null || Time.time - pair.Value.Since > FreezeTimeout)
                    ExpiredFreezes.Add(pair.Key);
            }
            for (int i = 0; i < ExpiredFreezes.Count; i++)
            {
                Plugin.Log.LogWarning("Item " + ExpiredFreezes[i] + " could not be put back on its line.");
                Unfreeze(ExpiredFreezes[i]);
            }
        }

        private static void MarkDirty()
        {
            _dirty = true;
            _retryUntil = Time.time + RetryWindow;
        }

        // Builds every line whose hooks are spawned, and hangs every spawned item that
        // belongs on one.
        private static void Resolve()
        {
            _dirty = false;
            _retryAt = 0f;
            if (SaveLoadManager.instance == null || LineStore.All.Count == 0)
                return;

            var live = new Dictionary<int, SaveablePrefab>();
            List<SaveablePrefab> prefabs = SaveLoadManager.instance.GetCurrentPrefabs();
            for (int i = 0; i < prefabs.Count; i++)
            {
                if (prefabs[i] != null && prefabs[i].instanceId != 0)
                    live[prefabs[i].instanceId] = prefabs[i];
            }

            bool waiting = false;
            LineRecord[] records = LineStore.All.ToArray();
            for (int i = 0; i < records.Length; i++)
            {
                LineRecord record = records[i];
                if (record.Live == null)
                {
                    SaveablePrefab pa;
                    SaveablePrefab pb;
                    if (!live.TryGetValue(record.HookA, out pa) || !live.TryGetValue(record.HookB, out pb))
                        continue;
                    ShipItem a = pa.GetComponent<ShipItem>();
                    ShipItem b = pb.GetComponent<ShipItem>();
                    if (a == null || b == null)
                        continue;
                    if (a.transform.parent != b.transform.parent)
                    {
                        waiting = true;
                        continue;
                    }
                    Spawn(record, a, b);
                }

                for (int j = record.Items.Count - 1; j >= 0; j--)
                {
                    HungRecord item = record.Items[j];
                    SaveablePrefab prefab;
                    if (!live.TryGetValue(item.ItemId, out prefab))
                        continue;

                    // Bundle mods may add HangableItem a little after the item loads.
                    // A spyglass grows its own only while it hangs here.
                    ShipItem ship = prefab.GetComponent<ShipItem>();
                    HangableItem hang = prefab.GetComponent<HangableItem>();
                    if (hang == null && !SpyglassTie.Is(ship) && !HammerTie.Is(ship))
                    {
                        waiting = true;
                        continue;
                    }
                    if (hang != null && Hung.ContainsKey(hang.GetInstanceID()))
                        continue;

                    if (ship == null || (hang != null && hang.IsHanging()) || IsHeldLocally(ship) || ship.GetCurrentInventorySlot() != -1)
                    {
                        // It ended up somewhere else.
                        Unfreeze(item.ItemId);
                        LineStore.RemoveItem(item.ItemId);
                        continue;
                    }

                    if (!HangNow(record.Live, item, hang, ship))
                        waiting = true;
                }
            }

            if (waiting && Time.time < _retryUntil)
                _retryAt = Time.time + RetryInterval;
        }

        // ---- Saving and loading ----

        internal static void OnSave()
        {
            HashSet<int> known = KnownIds();
            if (known != null)
                LineStore.Prune(known);
            LineStore.Write();
        }

        // Every item in the save: the spawned ones, and the ones a distant boat keeps.
        private static HashSet<int> KnownIds()
        {
            SaveLoadManager manager = SaveLoadManager.instance;
            if (manager == null)
                return null;

            var known = new HashSet<int>();
            List<SaveablePrefab> prefabs = manager.GetCurrentPrefabs();
            for (int i = 0; i < prefabs.Count; i++)
            {
                if (prefabs[i] != null)
                    known.Add(prefabs[i].instanceId);
            }

            SaveableObject[] objects = manager.GetCurrentObjects();
            if (objects == null)
                return known;
            for (int i = 0; i < objects.Length; i++)
            {
                SaveableObject obj = objects[i];
                if (obj == null || obj.localItems == null || !obj.localItems.HasLocalItems())
                    continue;
                List<SavePrefabData> cached = obj.localItems.GetCachedItems();
                for (int j = 0; j < cached.Count; j++)
                {
                    if (cached[j] != null)
                        known.Add(cached[j].instanceId);
                }
            }
            return known;
        }

        internal static void OnLoadStarted()
        {
            Reset();
            LineStore.Clear();
        }

        internal static void OnModDataLoaded()
        {
            // A Coop guest's stand-in save has its own old lines. The host's arrive
            // with the world.
            if (CoopLink.GuestContext)
            {
                Plugin.Log.LogInfo("Joining a Coop game: waiting for the host's lines.");
                return;
            }
            LineStore.Read();
            MarkDirty();
        }

        // ---- Changes from other players (Coop) ----

        internal static void RemoteTie(int hookA, int hookB)
        {
            if (LineStore.Find(hookA, hookB) != null)
                return;
            LineStore.Add(new LineRecord { HookA = hookA, HookB = hookB });
            MarkDirty();
        }

        internal static void RemoteSpread(int hookA, int hookB)
        {
            Spread(LineStore.Find(hookA, hookB), false);
        }

        internal static void RemoteUntie(int hookA, int hookB)
        {
            LineRecord record = LineStore.Find(hookA, hookB);
            if (record != null)
                Untie(record, null, false);
        }

        internal static void RemoteHang(int hookA, int hookB, int itemId, float s, int side)
        {
            LineRecord line = LineStore.Find(hookA, hookB);
            if (line == null)
            {
                line = new LineRecord { HookA = hookA, HookB = hookB };
                LineStore.Add(line);
            }

            ShipItem ship = FindLive(itemId);
            HangableItem hang = ship != null ? ship.GetComponent<HangableItem>() : null;
            if (hang != null)
            {
                // Off wherever it hangs here first.
                LineAnchor old;
                if (Hung.TryGetValue(hang.GetInstanceID(), out old))
                    Release(old, false);
                else if (hang.IsHanging())
                    hang.DisconnectJoint();
            }

            var record = new HungRecord { ItemId = itemId, S = s, Side = side };
            LineStore.AddItem(line, record);
            if (line.Live != null && ship != null && HangNow(line.Live, record, hang, ship))
                return;

            Freeze(ship, itemId);
            MarkDirty();
        }

        internal static void RemoteUnhang(int itemId)
        {
            ShipItem ship = FindLive(itemId);
            HangableItem hang = ship != null ? ship.GetComponent<HangableItem>() : null;
            LineAnchor anchor;
            if (hang != null && Hung.TryGetValue(hang.GetInstanceID(), out anchor))
            {
                Release(anchor, false);
                return;
            }
            LineStore.RemoveItem(itemId);
            Unfreeze(itemId);
        }

        // Point a spyglass down from its eyepiece, toward the earth, the way a
        // hanging bundle stays upright. Local +Z is the objective, so it hangs
        // below the eyepiece. The turn is about that point rather than the mesh
        // origin. The line's own up would lean the glass out over the deck when
        // the boat heels.
        internal static void PoseSpyglass(HangableItem hang)
        {
            if (hang == null)
                return;
            LineAnchor anchor;
            if (!Hung.TryGetValue(hang.GetInstanceID(), out anchor) || anchor == null || anchor.Ship == null)
                return;
            if (!SpyglassTie.Is(anchor.Ship))
                return;

            Transform hook = anchor.transform;
            Vector3 across = hook.forward;
            across.y = 0f;
            if (across.sqrMagnitude < 1e-6f)
                across = Vector3.forward;
            Quaternion rotation = Quaternion.LookRotation(Vector3.down, across.normalized);
            Vector3 pin = hook.position - hook.forward * HookOffset;
            PinHang(anchor.Ship, pin - rotation * SpyglassTie.EyePoint(anchor.Ship), rotation);
        }

        // Turn a hammer so its head, at the high end of local Z, is up and the
        // handle hangs toward the earth, the way a hanging bundle stays upright.
        // A quarter turn around that axis brings the wide face of the head out
        // toward the line. The turn is about the inner top of the eye, on the pin.
        // The mesh origin sits beside the shaft, so pivoting there swings the head
        // off the line. The line's own up would lean the handle out over the deck
        // when the boat heels.
        internal static void PoseHammer(HangableItem hang)
        {
            if (hang == null)
                return;
            LineAnchor anchor;
            if (!Hung.TryGetValue(hang.GetInstanceID(), out anchor) || anchor == null || anchor.Ship == null)
                return;
            if (!HammerTie.Is(anchor.Ship))
                return;

            Transform hook = anchor.transform;
            Vector3 across = hook.forward;
            across.y = 0f;
            if (across.sqrMagnitude < 1e-6f)
                across = Vector3.forward;
            Quaternion rotation = Quaternion.AngleAxis(90f, Vector3.up)
                * Quaternion.LookRotation(Vector3.up, across.normalized);
            Vector3 pin = hook.position - hook.forward * HookOffset;
            PinHang(anchor.Ship, pin - rotation * HammerTie.HangPoint, rotation);
        }

        // The visual body is kinematic, but a leftover spin turns the mesh around
        // its middle. Plant the attachment point and clear that spin.
        private static void PinHang(ShipItem ship, Vector3 position, Quaternion rotation)
        {
            ship.transform.SetPositionAndRotation(position, rotation);
            Rigidbody body = ship.GetComponent<Rigidbody>();
            if (body == null)
                return;
            if (!body.isKinematic)
            {
                body.velocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.isKinematic = true;
            }
            body.position = position;
            body.rotation = rotation;
        }

        // HooksHangMore hangs a pot tilted, with a handle toward the hook. On a line,
        // sit it level under the rope, turned so the handles point out from the line.
        // The cord is drawn by PotTie.
        internal static void PosePot(HangableItem hang)
        {
            if (hang == null)
                return;
            LineAnchor anchor;
            if (!Hung.TryGetValue(hang.GetInstanceID(), out anchor) || anchor == null || anchor.Ship == null)
                return;
            if (!PotTie.Is(anchor.Ship))
                return;

            Transform hook = anchor.transform;
            Vector3 across = hook.forward;
            across.y = 0f;
            if (across.sqrMagnitude < 1e-6f)
                across = Vector3.forward;
            anchor.Ship.transform.rotation = Quaternion.AngleAxis(90f, Vector3.up)
                * Quaternion.LookRotation(across.normalized, Vector3.up);
            anchor.Ship.transform.position = hook.position - hook.forward * HookOffset;

            // Hanging turns the collider off, so a bottle cannot find the pot. Leave it
            // on while the pot is on a line. A lamp hook keeps it off.
            ItemRigidbody twin = anchor.Ship.GetItemRigidbody();
            if (twin != null && twin.disableCol)
            {
                twin.disableCol = false;
                twin.ToggleCollider(true);
            }
        }

        // True while a water bottle is being clicked onto a pot that is on a line.
        // HooksHangMore refuses to fill any hanging pot. For that one click, IsHanging
        // reports false so the pour goes through. A pot on a lamp hook is left alone.
        private static int _fillLinePot;

        internal static void PrepareLineFill(ShipItemSoup soup, PickupableItem held)
        {
            _fillLinePot = 0;
            if (soup == null || held == null || !IsOnLine(soup))
                return;
            ShipItemBottle bottle = held.GetComponent<ShipItemBottle>();
            if (bottle == null || bottle.amount != 1f)
                return;
            HangableItem hang = soup.GetComponent<HangableItem>();
            if (hang != null)
                _fillLinePot = hang.GetInstanceID();
        }

        internal static bool HideLinePotFromFillBlock(HangableItem hang)
        {
            return _fillLinePot != 0 && hang != null && hang.GetInstanceID() == _fillLinePot;
        }

        internal static void EndLineFill()
        {
            _fillLinePot = 0;
        }

        internal static bool IsOnLine(ShipItem ship)
        {
            if (ship == null)
                return false;
            HangableItem hang = ship.GetComponent<HangableItem>();
            LineAnchor anchor;
            return hang != null && Hung.TryGetValue(hang.GetInstanceID(), out anchor) && anchor != null;
        }

        // A Medi lantern's bail is a flat loop, thin along local Z. Facing across the
        // line leaves that loop parallel to the rope. A quarter turn around up puts
        // the opening along the rope, so the lantern hangs from its own bail.
        internal static void PoseLantern(HangableItem hang)
        {
            if (hang == null)
                return;
            LineAnchor anchor;
            if (!Hung.TryGetValue(hang.GetInstanceID(), out anchor) || anchor == null || anchor.Ship == null)
                return;
            if (!LanternHang.Turn(anchor.Ship))
                return;

            Vector3 across = anchor.transform.forward;
            across.y = 0f;
            if (across.sqrMagnitude < 1e-6f)
                across = Vector3.forward;
            anchor.Ship.transform.rotation = Quaternion.LookRotation(across.normalized, Vector3.up)
                * Quaternion.Euler(0f, 90f, 0f);
        }

        // A wind chime is pitched so local +Z is up and the tubes fall. The loop at
        // that top is thin along local Y, so its opening faces across the line. A
        // quarter turn of yaw puts the opening along the rope. The pitch stays.
        internal static void PoseWindChime(HangableItem hang)
        {
            if (hang == null)
                return;
            LineAnchor anchor;
            if (!Hung.TryGetValue(hang.GetInstanceID(), out anchor) || anchor == null || anchor.Ship == null)
                return;
            if (!WindChimeHang.Is(anchor.Ship))
                return;

            Vector3 across = anchor.transform.forward;
            across.y = 0f;
            if (across.sqrMagnitude < 1e-6f)
                across = Vector3.forward;
            anchor.Ship.transform.rotation = Quaternion.LookRotation(across.normalized, Vector3.up)
                * Quaternion.Euler(-90f, 90f, 0f);
        }

        // HooksHangMore rolls a wood mug so its handle, on local -X, points up, and
        // shifts the mug until that handle meets the hook. The handle's opening
        // still faces across the line. A quarter turn around the handle puts the
        // opening along the rope and leaves the handle on the hook.
        internal static void PoseMug(HangableItem hang)
        {
            if (hang == null)
                return;
            LineAnchor anchor;
            if (!Hung.TryGetValue(hang.GetInstanceID(), out anchor) || anchor == null || anchor.Ship == null)
                return;
            if (!MugHang.Turn(anchor.Ship))
                return;

            Vector3 across = anchor.transform.forward;
            across.y = 0f;
            if (across.sqrMagnitude < 1e-6f)
                across = Vector3.forward;
            Quaternion facing = Quaternion.LookRotation(across.normalized, Vector3.up);
            Quaternion hung = facing * Quaternion.Euler(0f, 0f, 270f);
            Quaternion turned = facing * Quaternion.Euler(0f, 90f, 270f);
            Vector3 handle = anchor.Ship.transform.position + hung * MugHang.WoodHandle;
            anchor.Ship.transform.rotation = turned;
            anchor.Ship.transform.position = handle - turned * MugHang.WoodHandle;
        }

        // The hanging ring is edge-on when the item faces across the line. A quarter
        // turn brings the ring around so the cord passes through it.
        internal static void PoseWindicator(HangableItem hang)
        {
            if (hang == null)
                return;
            LineAnchor anchor;
            if (!Hung.TryGetValue(hang.GetInstanceID(), out anchor) || anchor == null || anchor.Ship == null)
                return;
            if (!LoopTie.IsWindicator(anchor.Ship))
                return;

            Vector3 across = anchor.transform.forward;
            across.y = 0f;
            if (across.sqrMagnitude < 1e-6f)
                across = Vector3.forward;
            anchor.Ship.transform.rotation = Quaternion.LookRotation(across.normalized, Vector3.up)
                * Quaternion.Euler(0f, 90f, 0f);
        }

        // The host's whole set of lines, for a joining guest. It comes twice, with the
        // world and after the join; the second is usually the same.
        internal static void ReplaceAll(List<LineRecord> lines)
        {
            if (SameAsStore(lines))
            {
                MarkDirty();
                return;
            }

            var released = new List<ShipItem>();
            LineRecord[] old = LineStore.All.ToArray();
            for (int i = 0; i < old.Length; i++)
            {
                if (old[i].Live == null)
                    continue;
                LineAnchor[] anchors = old[i].Live.Anchors();
                for (int j = 0; j < anchors.Length; j++)
                {
                    if (anchors[j].Ship != null)
                        released.Add(anchors[j].Ship);
                }
                Despawn(old[i]);
            }

            LineStore.Clear();
            for (int i = 0; i < lines.Count; i++)
                LineStore.Add(lines[i]);

            // Let go of anything that hung here but not on the host.
            for (int i = 0; i < released.Count; i++)
            {
                int id = Id(released[i]);
                if (!LineStore.IsHungItem(id))
                    LetFall(released[i]);
            }
            var frozen = new List<int>(Frozen.Keys);
            for (int i = 0; i < frozen.Count; i++)
            {
                if (!LineStore.IsHungItem(frozen[i]))
                    Unfreeze(frozen[i]);
            }

            Plugin.Log.LogInfo("Took " + LineStore.All.Count + " line(s) from the Coop host.");
            MarkDirty();
        }

        private static bool SameAsStore(List<LineRecord> lines)
        {
            if (lines.Count != LineStore.All.Count)
                return false;
            for (int i = 0; i < lines.Count; i++)
            {
                LineRecord mine = LineStore.All[i];
                LineRecord theirs = lines[i];
                if (mine.HookA != theirs.HookA || mine.HookB != theirs.HookB || mine.Items.Count != theirs.Items.Count)
                    return false;
                for (int j = 0; j < mine.Items.Count; j++)
                {
                    HungRecord a = mine.Items[j];
                    HungRecord b = theirs.Items[j];
                    if (a.ItemId != b.ItemId || a.S != b.S || a.Side != b.Side)
                        return false;
                }
            }
            return true;
        }

        private static void LetFall(ShipItem ship)
        {
            ItemRigidbody twin = ship != null ? ship.GetItemRigidbody() : null;
            if (twin == null)
                return;
            twin.attached = false;
            twin.disableCol = false;
            twin.inStove = false;
            ship.ToggleDisallowDisembarking(false);
        }

        internal static void OnNewGame()
        {
            Reset();
            LineStore.Clear();
            if (GameState.modData != null)
                GameState.modData.Remove(LineStore.ModDataKey);
        }

        private static void Reset()
        {
            for (int i = 0; i < LineStore.All.Count; i++)
            {
                if (LineStore.All[i].Live != null)
                    Object.Destroy(LineStore.All[i].Live.gameObject);
                LineStore.All[i].Live = null;
            }
            Hung.Clear();
            Frozen.Clear();
            _pendingHook = null;
            _dirty = false;
            _retryAt = 0f;
        }

        // ---- Feedback ----

        private static void Notify(string message)
        {
            if (NotificationUi.instance != null)
                NotificationUi.instance.ShowNotification(message, 2.5f);
        }

        private static void PlayKnotSound()
        {
            if (UISoundPlayer.instance != null)
                UISoundPlayer.instance.PlaySmallItemDropSound();
        }
    }
}
