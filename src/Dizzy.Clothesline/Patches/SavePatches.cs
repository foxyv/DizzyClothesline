using HarmonyLib;

namespace Dizzy.Clothesline.Patches
{
    [HarmonyPatch(typeof(SaveLoadManager), nameof(SaveLoadManager.SaveModData))]
    internal static class SaveModDataPatch
    {
        private static void Prefix()
        {
            try
            {
                Lines.OnSave();
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning("Could not save lines: " + ex);
            }
        }
    }

    [HarmonyPatch(typeof(SaveLoadManager), nameof(SaveLoadManager.LoadGame))]
    internal static class LoadGamePatch
    {
        private static void Prefix()
        {
            Lines.OnLoadStarted();
        }
    }

    [HarmonyPatch(typeof(SaveLoadManager), nameof(SaveLoadManager.LoadModData))]
    internal static class LoadModDataPatch
    {
        private static void Postfix()
        {
            try
            {
                Lines.OnModDataLoaded();
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning("Could not load lines: " + ex);
            }
        }
    }

    [HarmonyPatch(typeof(StartMenu), "StartNewGame")]
    internal static class StartNewGamePatch
    {
        private static void Prefix()
        {
            Lines.OnNewGame();
        }
    }
}
