using HarmonyLib;

namespace Dizzy.Clothesline.Patches
{
    // Tells an item put away with its boat apart from one that is gone for good.
    [HarmonyPatch(typeof(ShipItem), nameof(ShipItem.DestroyItem))]
    // modcheck-ignore: SW801 never skips DestroyItem; the postfix checks __runOriginal for Dizzy.Fixes' map prefix
    internal static class DestroyItemPatch
    {
        private static void Prefix(ShipItem __instance)
        {
            Lines.BeginDestroy(__instance);
        }

        // Dizzy.Fixes skips DestroyItem for a kept Mirage Mountain map.
        private static void Postfix(ShipItem __instance, bool __runOriginal)
        {
            Lines.EndDestroy(__instance, __runOriginal);
        }
    }

    // Puts lines back as their hooks and items spawn, at load and when a boat comes
    // back into range.
    [HarmonyPatch(typeof(SaveablePrefab), nameof(SaveablePrefab.Load))]
    internal static class PrefabLoadPatch
    {
        private static void Postfix(SaveablePrefab __instance)
        {
            Lines.OnPrefabLoaded(__instance);
        }
    }
}
