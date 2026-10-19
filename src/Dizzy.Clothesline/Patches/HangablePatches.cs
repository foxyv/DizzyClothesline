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

    // Runs after the game, and after other mods, have set the hung rotation.
    // A spyglass on a line is turned to point down. A hanging windicator is turned
    // a quarter turn so its ring faces the cord. A pot on a line is set level.
    // A Medi lantern is turned a quarter turn so its bail opens along the rope.
    // A wind chime keeps its hang pitch and is turned so its loop opens along the rope.
    // A wood mug is turned a quarter turn about its handle. Both metal mugs are only raised.
    // Everything else is left alone.
    [HarmonyPatch(typeof(HangableItem), "LateUpdate")]
    internal static class SpyglassPosePatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(HangableItem __instance)
        {
            Lines.PoseSpyglass(__instance);
            Lines.PoseWindicator(__instance);
            Lines.PoseHammer(__instance);
            Lines.PosePot(__instance);
            Lines.PoseLantern(__instance);
            Lines.PoseWindChime(__instance);
            Lines.PoseMug(__instance);
        }
    }

    // HooksHangMore refuses a bottle poured into any hanging pot. A pot on a line
    // is reported as not hanging for that one click, so the game's own pour runs.
    // A pot on a lamp hook still gets the refusal.
    [HarmonyPatch(typeof(ShipItemSoup), nameof(ShipItemSoup.OnItemClick))]
    internal static class LinePotFillPatch
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(ShipItemSoup __instance, PickupableItem heldItem)
        {
            Lines.PrepareLineFill(__instance, heldItem);
        }

        private static void Postfix()
        {
            Lines.EndLineFill();
        }
    }

    [HarmonyPatch(typeof(HangableItem), nameof(HangableItem.IsHanging))]
    // modcheck-ignore: SW801 skips the original only for one click, a water bottle poured into a pot on a line
    internal static class LinePotHangingPatch
    {
        private static bool Prefix(HangableItem __instance, ref bool __result)
        {
            if (!Lines.HideLinePotFromFillBlock(__instance))
                return true;
            __result = false;
            return false;
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
