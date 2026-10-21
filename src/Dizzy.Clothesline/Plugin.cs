using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace Dizzy.Clothesline
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency(CoopLink.CoopGuid, BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("com.raddude.hookshangmore", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInProcess("Sailwind.exe")]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.dizzy.sailwind.clothesline";
        public const string PluginName = "Dizzy Clothesline";
        public const string PluginVersion = "0.3.21";

        internal static ManualLogSource Log;
        internal static Plugin Instance;

        private Harmony _harmony;
        private bool _tickFailed;

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            ClotheslineConfig.Bind(Config);

            _harmony = new Harmony(PluginGuid);
            try
            {
                _harmony.PatchAll(typeof(Plugin).Assembly);
                CoopLink.Apply(_harmony);
            }
            catch (System.Exception ex)
            {
                Log.LogError(PluginName + " failed to patch Sailwind: " + ex);
            }

            Log.LogInfo(PluginName + " v" + PluginVersion + " loaded (Tie Key=" + ClotheslineConfig.TieKey.Value
                + ", Max Length=" + ClotheslineConfig.MaxLength.Value
                + ", Hangers=" + ClotheslineConfig.Hangers.Value
                + ", Metrics=" + ClotheslineConfig.Metrics.Value + ").");
        }

        private void Update()
        {
            Profile.Frame();
            try
            {
                Lines.Tick();
            }
            catch (System.Exception ex)
            {
                // Log once, not every frame.
                if (!_tickFailed)
                    Log.LogError("Clothesline update failed: " + ex);
                _tickFailed = true;
            }
        }

        private void OnGUI()
        {
            Profile.Draw();
        }

        private void OnDestroy()
        {
            Profile.Flush();
            _harmony?.UnpatchSelf();
            if (Instance == this)
                Instance = null;
        }
    }
}
