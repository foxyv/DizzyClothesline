using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using HarmonyLib;

namespace Dizzy.Clothesline
{
    // Shares lines with the other players through Sailwind Coop.
    //
    // Coop has no message channel for other mods. Its packet type is a byte and it
    // uses less than 230 of them, so lines get a handler on a byte Coop leaves free.
    // Coop is found by reflection: without it loaded, none of this runs.
    //
    // The host owns the lines, as it owns the save. A joining guest ignores the lines
    // in its own stand-in save and takes the host's, sent with the world snapshot and
    // again once the join is done. Changes go to the host, which passes them on.
    //
    // Hanging is also announced through Coop's own ItemHung, naming one of the line's
    // hooks, so Coop on every machine stops treating the item as held. Our Hang
    // message goes out first, so the other side knows the item belongs on the line
    // and keeps it off the lamp hook.
    internal static class CoopLink
    {
        internal const string CoopGuid = "com.sailwindcoop.mod";

        private const byte Protocol = 1;
        // CoopSave.PhantomSlot.
        private const int PhantomSlot = 99;
        private static readonly byte[] TypeCandidates = { 241, 242, 243, 244, 245, 246, 247, 248 };

        private enum Kind : byte
        {
            Tie = 1,
            Untie = 2,
            Hang = 3,
            Unhang = 4,
            State = 5,
        }

        private sealed class Message
        {
            internal Kind Kind;
            internal int HookA;
            internal int HookB;
            internal int Item;
            internal float S;
            internal int Side;
            internal List<LineRecord> Lines;
        }

        private static bool _present;
        private static bool _applying;
        private static bool _registering;
        private static bool _warnedProtocol;
        private static bool _warnedTaken;
        private static object _registeredOn;

        private static PropertyInfo _isMultiplayer;
        private static PropertyInfo _isHost;
        private static PropertyInfo _network;
        private static FieldInfo _handlers;
        private static MethodInfo _register;
        private static MethodInfo _sendToAll;
        private static MethodInfo _sendTo;
        private static MethodInfo _sendToAllExcept;
        private static PropertyInfo _itemSync;
        private static MethodInfo _onLocalItemHung;
        private static PropertyInfo _remoteApplying;
        private static MethodInfo _setRemoteApplying;
        private static PropertyInfo _joinInProgress;
        private static PropertyInfo _guestContext;
        private static object _packetType;
        private static Delegate _handler;

        internal static void Apply(Harmony harmony)
        {
            PluginInfo info;
            if (!Chainloader.PluginInfos.TryGetValue(CoopGuid, out info) || info.Instance == null)
                return;

            try
            {
                Type plugin = info.Instance.GetType();
                Assembly coop = plugin.Assembly;
                Type network = coop.GetType("SailwindCoop.Networking.P2PNetworkManager");
                Type packetType = coop.GetType("SailwindCoop.Networking.Packets.PacketType");
                Type itemSync = coop.GetType("SailwindCoop.Sync.ItemSyncManager");
                Type boatSync = coop.GetType("SailwindCoop.Sync.BoatSyncManager");
                Type coopSave = coop.GetType("SailwindCoop.CoopSave");
                if (network == null || packetType == null || itemSync == null || boatSync == null || coopSave == null)
                {
                    Plugin.Log.LogWarning("Sailwind Coop is loaded, but its networking was not found. Lines will not be shared with other players.");
                    return;
                }

                const BindingFlags PublicStatic = BindingFlags.Public | BindingFlags.Static;
                _isMultiplayer = plugin.GetProperty("IsMultiplayer", PublicStatic);
                _isHost = plugin.GetProperty("IsHost", PublicStatic);
                _network = plugin.GetProperty("NetworkManager", PublicStatic);
                _handlers = network.GetField("_handlers", BindingFlags.NonPublic | BindingFlags.Instance);
                _register = network.GetMethod("RegisterHandler");
                Type handlerType = _register != null ? _register.GetParameters()[1].ParameterType : null;
                Type steamId = handlerType != null && handlerType.IsGenericType ? handlerType.GetGenericArguments()[0] : null;
                if (steamId == null)
                {
                    Plugin.Log.LogWarning("This version of Sailwind Coop has changed its networking. Lines will not be shared with other players.");
                    return;
                }

                _sendToAll = network.GetMethod("SendToAllReliable", new[] { packetType, typeof(Action<BinaryWriter>) });
                _sendTo = network.GetMethod("SendReliable", new[] { steamId, packetType, typeof(Action<BinaryWriter>) });
                _sendToAllExcept = network.GetMethod("SendToAllExcept", new[] { steamId, packetType, typeof(Action<BinaryWriter>), typeof(bool) });
                _itemSync = itemSync.GetProperty("Instance", PublicStatic);
                _onLocalItemHung = itemSync.GetMethod("OnLocalItemHung", new[] { typeof(ShipItem), typeof(ShipItem) });
                _remoteApplying = itemSync.GetProperty("IsApplyingRemoteState", BindingFlags.Public | BindingFlags.Instance);
                _setRemoteApplying = _remoteApplying != null ? _remoteApplying.GetSetMethod(true) : null;
                _joinInProgress = boatSync.GetProperty("IsJoinInProgress", PublicStatic);
                _guestContext = coopSave.GetProperty("ContextActive", PublicStatic);
                MethodInfo worldTo = boatSync.GetMethod("SendBoatWorldStateTo", new[] { steamId });
                MethodInfo worldAll = boatSync.GetMethod("SendBoatWorldState", Type.EmptyTypes);
                MethodInfo hungTo = itemSync.GetMethod("ResyncHungStateTo", new[] { steamId });

                if (_isMultiplayer == null || _isHost == null || _network == null || _sendToAll == null || _sendTo == null
                    || _sendToAllExcept == null || _itemSync == null || _onLocalItemHung == null || _remoteApplying == null
                    || _setRemoteApplying == null || _joinInProgress == null || _guestContext == null
                    || worldTo == null || worldAll == null || hungTo == null)
                {
                    Plugin.Log.LogWarning("This version of Sailwind Coop has changed its networking. Lines will not be shared with other players.");
                    return;
                }

                _packetType = PickPacketType(packetType);
                if (_packetType == null)
                {
                    Plugin.Log.LogWarning("Sailwind Coop has no free packet type left. Lines will not be shared with other players.");
                    return;
                }

                MethodInfo receive = typeof(CoopLink).GetMethod(nameof(Receive), BindingFlags.NonPublic | BindingFlags.Static);
                _handler = Delegate.CreateDelegate(handlerType, receive.MakeGenericMethod(steamId));

                // Coop registers its handlers each time a session starts, and clears them
                // when it ends. Register ours alongside.
                harmony.Patch(_register, postfix: new HarmonyMethod(typeof(CoopLink), nameof(AfterRegisterHandler)));
                harmony.Patch(worldTo, postfix: new HarmonyMethod(typeof(CoopLink), nameof(AfterSendTo)));
                harmony.Patch(hungTo, postfix: new HarmonyMethod(typeof(CoopLink), nameof(AfterSendTo)));
                harmony.Patch(worldAll, postfix: new HarmonyMethod(typeof(CoopLink), nameof(AfterSendToAll)));

                _present = true;
                EnsureHandler(_network.GetValue(null, null));
                Plugin.Log.LogInfo("Sailwind Coop found. Lines are shared with other players (packet type " + Convert.ToByte(_packetType) + ").");
            }
            catch (Exception ex)
            {
                _present = false;
                Plugin.Log.LogWarning("Could not link to Sailwind Coop. Lines will not be shared with other players: " + ex);
            }
        }

        private static object PickPacketType(Type enumType)
        {
            for (int i = 0; i < TypeCandidates.Length; i++)
            {
                if (!Enum.IsDefined(enumType, TypeCandidates[i]))
                    return Enum.ToObject(enumType, TypeCandidates[i]);
            }
            return null;
        }

        private static void AfterRegisterHandler(object __instance)
        {
            if (!_registering)
                EnsureHandler(__instance);
        }

        private static void EnsureHandler(object network)
        {
            if (!_present || network == null || _registering)
                return;

            try
            {
                IDictionary handlers = _handlers != null ? _handlers.GetValue(network) as IDictionary : null;
                if (handlers != null)
                {
                    if (handlers.Contains(_packetType))
                    {
                        if (!Equals(handlers[_packetType], _handler) && !_warnedTaken)
                        {
                            _warnedTaken = true;
                            Plugin.Log.LogWarning("Another mod uses Coop packet type " + Convert.ToByte(_packetType) + ". Lines will not be shared with other players.");
                        }
                        return;
                    }
                }
                else if (ReferenceEquals(_registeredOn, network))
                {
                    return;
                }

                _registering = true;
                _register.Invoke(network, new object[] { _packetType, _handler });
                _registeredOn = network;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("Could not listen for line changes from other players: " + ex);
            }
            finally
            {
                _registering = false;
            }
        }

        // ---- State ----

        internal static bool IsMultiplayer
        {
            get { return _present && Get(_isMultiplayer); }
        }

        private static bool IsHost
        {
            get { return _present && Get(_isHost); }
        }

        // A guest loads a stand-in save into slot 99 before joining. Its lines are not
        // the host's. Coop leaves ContextActive on after the guest leaves, so the slot
        // is what tells a later solo load apart.
        internal static bool GuestContext
        {
            get { return _present && SaveSlots.currentSlot == PhantomSlot && Get(_guestContext); }
        }

        // True while this machine applies a change another player made, ours or Coop's.
        // Nothing is sent back out then, and an item destroyed then is not taken off
        // its line in the save: the host decides that.
        internal static bool Applying
        {
            get
            {
                if (_applying)
                    return true;
                if (!IsMultiplayer)
                    return false;
                if (Get(_joinInProgress))
                    return true;
                object sync = ItemSync();
                return sync != null && Get(_remoteApplying, sync);
            }
        }

        private static object ItemSync()
        {
            try
            {
                return _itemSync.GetValue(null, null);
            }
            catch
            {
                return null;
            }
        }

        private static bool Get(PropertyInfo property, object target = null)
        {
            try
            {
                object value = property.GetValue(target, null);
                return value is bool flag && flag;
            }
            catch
            {
                return false;
            }
        }

        // ---- Sending ----

        internal static void SendTie(LineRecord line)
        {
            Send(new Message { Kind = Kind.Tie, HookA = line.HookA, HookB = line.HookB });
        }

        internal static void SendUntie(LineRecord line)
        {
            Send(new Message { Kind = Kind.Untie, HookA = line.HookA, HookB = line.HookB });
        }

        internal static void SendHang(LineRecord line, HungRecord item)
        {
            Send(new Message { Kind = Kind.Hang, HookA = line.HookA, HookB = line.HookB, Item = item.ItemId, S = item.S, Side = item.Side });
        }

        internal static void SendUnhang(int itemId)
        {
            Send(new Message { Kind = Kind.Unhang, Item = itemId });
        }

        // Coop's own bookkeeping for a hung item: it is no longer held by anyone.
        internal static void NotifyHung(ShipItem item, ShipItem hook)
        {
            if (!IsMultiplayer || _applying || item == null || hook == null)
                return;
            try
            {
                object sync = ItemSync();
                if (sync != null)
                    _onLocalItemHung.Invoke(sync, new object[] { item, hook });
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("Could not tell Coop an item was hung: " + ex);
            }
        }

        private static void Send(Message message)
        {
            if (!IsMultiplayer || _applying)
                return;
            try
            {
                object network = _network.GetValue(null, null);
                if (network != null)
                    _sendToAll.Invoke(network, new object[] { _packetType, Writer(message) });
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("Could not send a line change to the other players: " + ex);
            }
        }

        // After Coop sends a joining guest the world, and again when the join is done.
        private static void AfterSendTo(object[] __args)
        {
            if (!IsHost || __args == null || __args.Length == 0 || __args[0] == null)
                return;
            try
            {
                object network = _network.GetValue(null, null);
                if (network != null)
                    _sendTo.Invoke(network, new object[] { __args[0], _packetType, Writer(StateMessage()) });
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("Could not send the lines to a joining player: " + ex);
            }
        }

        // After a recovery Coop sends every guest the world again.
        private static void AfterSendToAll()
        {
            if (IsHost)
                Send(StateMessage());
        }

        private static Message StateMessage()
        {
            return new Message { Kind = Kind.State, Lines = LineStore.All };
        }

        private static Action<BinaryWriter> Writer(Message message)
        {
            return writer =>
            {
                writer.Write(Protocol);
                writer.Write((byte)message.Kind);
                switch (message.Kind)
                {
                    case Kind.Tie:
                    case Kind.Untie:
                        writer.Write(message.HookA);
                        writer.Write(message.HookB);
                        break;
                    case Kind.Hang:
                        writer.Write(message.HookA);
                        writer.Write(message.HookB);
                        writer.Write(message.Item);
                        writer.Write(message.S);
                        writer.Write((sbyte)message.Side);
                        break;
                    case Kind.Unhang:
                        writer.Write(message.Item);
                        break;
                    case Kind.State:
                        writer.Write(message.Lines.Count);
                        for (int i = 0; i < message.Lines.Count; i++)
                        {
                            LineRecord line = message.Lines[i];
                            writer.Write(line.HookA);
                            writer.Write(line.HookB);
                            writer.Write(line.Items.Count);
                            for (int j = 0; j < line.Items.Count; j++)
                            {
                                writer.Write(line.Items[j].ItemId);
                                writer.Write(line.Items[j].S);
                                writer.Write((sbyte)line.Items[j].Side);
                            }
                        }
                        break;
                }
            };
        }

        // ---- Receiving ----

        // Closed over Coop's SteamId type when the handler is made.
        private static void Receive<TSender>(TSender sender, BinaryReader reader)
        {
            try
            {
                byte protocol = reader.ReadByte();
                if (protocol != Protocol)
                {
                    if (!_warnedProtocol)
                    {
                        _warnedProtocol = true;
                        Plugin.Log.LogWarning("Another player has a different version of " + Plugin.PluginName + ". Their line changes are ignored.");
                    }
                    return;
                }

                Message message = Read(reader);
                if (message == null)
                    return;

                if (message.Kind == Kind.State)
                {
                    // Only the host sends the whole set.
                    if (IsHost)
                        return;
                }
                else if (IsHost)
                {
                    object network = _network.GetValue(null, null);
                    if (network != null)
                        _sendToAllExcept.Invoke(network, new object[] { sender, _packetType, Writer(message), true });
                }

                ApplyRemote(message);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("Could not apply a line change from another player: " + ex);
            }
        }

        private static Message Read(BinaryReader reader)
        {
            var message = new Message { Kind = (Kind)reader.ReadByte() };
            switch (message.Kind)
            {
                case Kind.Tie:
                case Kind.Untie:
                    message.HookA = reader.ReadInt32();
                    message.HookB = reader.ReadInt32();
                    return message;
                case Kind.Hang:
                    message.HookA = reader.ReadInt32();
                    message.HookB = reader.ReadInt32();
                    message.Item = reader.ReadInt32();
                    message.S = reader.ReadSingle();
                    message.Side = reader.ReadSByte() < 0 ? -1 : 1;
                    return message;
                case Kind.Unhang:
                    message.Item = reader.ReadInt32();
                    return message;
                case Kind.State:
                    int lines = reader.ReadInt32();
                    message.Lines = new List<LineRecord>(lines);
                    for (int i = 0; i < lines; i++)
                    {
                        var line = new LineRecord { HookA = reader.ReadInt32(), HookB = reader.ReadInt32() };
                        int items = reader.ReadInt32();
                        for (int j = 0; j < items; j++)
                        {
                            line.Items.Add(new HungRecord
                            {
                                ItemId = reader.ReadInt32(),
                                S = reader.ReadSingle(),
                                Side = reader.ReadSByte() < 0 ? -1 : 1,
                            });
                        }
                        message.Lines.Add(line);
                    }
                    return message;
                default:
                    return null;
            }
        }

        private static void ApplyRemote(Message message)
        {
            object sync = ItemSync();
            bool coopWasApplying = sync != null && Get(_remoteApplying, sync);
            _applying = true;
            try
            {
                // Keeps Coop from sending on what this change does to its items.
                if (sync != null && !coopWasApplying)
                    _setRemoteApplying.Invoke(sync, new object[] { true });

                switch (message.Kind)
                {
                    case Kind.Tie:
                        Lines.RemoteTie(message.HookA, message.HookB);
                        break;
                    case Kind.Untie:
                        Lines.RemoteUntie(message.HookA, message.HookB);
                        break;
                    case Kind.Hang:
                        Lines.RemoteHang(message.HookA, message.HookB, message.Item, message.S, message.Side);
                        break;
                    case Kind.Unhang:
                        Lines.RemoteUnhang(message.Item);
                        break;
                    case Kind.State:
                        Lines.ReplaceAll(message.Lines);
                        break;
                }
            }
            finally
            {
                if (sync != null && !coopWasApplying)
                {
                    try
                    {
                        _setRemoteApplying.Invoke(sync, new object[] { false });
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.LogWarning("Could not reset Coop's remote state flag: " + ex);
                    }
                }
                _applying = false;
            }
        }
    }
}
