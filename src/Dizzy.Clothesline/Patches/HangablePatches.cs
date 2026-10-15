using HarmonyLib;
using UnityEngine;

namespace Dizzy.Clothesline.Patches
{
    // Items on a line go through the game's own HangableItem, so HooksHangMore's
    // offsets, hang-drying and IsHanging() checks in other mods work on lines too.
    // These prefixes step in only when the "hook" is a place on a line. They run
    // first so other mods' prefixes see a normal hook every other time.

    [HarmonyPatch(typeof(HangableItem), nameof(HangableItem.ConnectJoint))]
    // modcheck-ignore: SW801 runs first and skips the original only for a line's hang point; Bundle Up's postfix checks __runOriginal
    internal static class ConnectJointPatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(HangableItem __instance, Collider hook)
        {
            // The game would look for a ShipItemLampHook on the hook and fail.
            return !Lines.TryConnect(__instance, hook);
        }
    }

    [HarmonyPatch(typeof(HangableItem), nameof(HangableItem.DisconnectJoint))]
    // modcheck-ignore: SW801 skips the original only for items on a line; Bundle Up's prefix there only frees lamp hooks
    internal static class DisconnectJointPatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(HangableItem __instance)
        {
            return !Lines.TryDisconnect(__instance);
        }
    }

    [HarmonyPatch(typeof(HangableItem), nameof(HangableItem.OnTriggerEnter))]
    internal static class HangOnTriggerPatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(HangableItem __instance)
        {
            return !Lines.Claims(__instance);
        }
    }
}
