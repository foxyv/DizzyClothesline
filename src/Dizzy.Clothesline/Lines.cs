using System.Collections.Generic;
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
            return hang != null && !hang.IsHanging();
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
            if (!HangNow(line, record, hang, ship, true))
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

        // Puts an item on its place on a live line. face: turn it to face across the
        // line as a player hanging it would; a loaded item keeps its saved turn.
        private static bool HangNow(ClotheslineLine line, HungRecord record, HangableItem hang, ShipItem ship, bool face)
        {
            LineAnchor anchor = line.CreateAnchor(record);
            PlaceOnAnchor(ship, anchor, face);
            hang.ConnectJoint(anchor.Col);
            if (anchor.Item == hang)
                return true;

            line.RemoveAnchor(anchor);
            Object.Destroy(anchor.gameObject);
            return false;
        }

        // Where HangableItem will pin the item, so the twin is put there too.
        private static void PlaceOnAnchor(ShipItem ship, LineAnchor anchor, bool face)
        {
            if (face)
            {
                // Away from the player, the way an item on a wall hook ends up.
                Vector3 facing = anchor.transform.forward;
                facing.y = 0f;
                if (facing.sqrMagnitude > 1e-6f)
                    ship.transform.rotation = Quaternion.LookRotation(facing, Vector3.up);
            }
            ship.transform.position = anchor.transform.position - anchor.transform.forward * HookOffset;
        }

        // HooksHangMore turns an item as it hangs, except while the game loads. Turn a
        // hang that is not a load to face the line first, so it is turned only once.
        private static bool Loading
        {
            get { return GameState.currentlyLoading || GameState.loadingBoatLocalItems; }
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
                PlaceOnAnchor(ship, current, !Loading);
                Connect(hang, current);
            }
            else if (line.Live != null && ship != null)
            {
                HangNow(line.Live, record, hang, ship, !Loading);
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
                    HangableItem hang = prefab.GetComponent<HangableItem>();
                    if (hang == null)
                    {
                        waiting = true;
                        continue;
                    }
                    if (Hung.ContainsKey(hang.GetInstanceID()))
                        continue;

                    ShipItem ship = prefab.GetComponent<ShipItem>();
                    if (ship == null || hang.IsHanging() || IsHeldLocally(ship) || ship.GetCurrentInventorySlot() != -1)
                    {
                        // It ended up somewhere else.
                        Unfreeze(item.ItemId);
                        LineStore.RemoveItem(item.ItemId);
                        continue;
                    }

                    if (!HangNow(record.Live, item, hang, ship, !Loading))
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
            if (line.Live != null && hang != null && HangNow(line.Live, record, hang, ship, true))
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
