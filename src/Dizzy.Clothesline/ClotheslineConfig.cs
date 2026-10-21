using BepInEx.Configuration;
using UnityEngine;

namespace Dizzy.Clothesline
{
    internal static class ClotheslineConfig
    {
        internal static ConfigEntry<KeyCode> TieKey;
        internal static ConfigEntry<float> MaxLength;
        internal static ConfigEntry<float> Sag;
        internal static ConfigEntry<float> Thickness;
        internal static ConfigEntry<Color> RopeColor;
        internal static ConfigEntry<float> ItemSpacing;
        internal static ConfigEntry<float> HangDrop;
        internal static ConfigEntry<bool> Hangers;
        internal static ConfigEntry<bool> Metrics;

        internal static void Bind(ConfigFile config)
        {
            TieKey = config.Bind("Controls", "Tie Key", KeyCode.L,
                "Look at a lamp hook and press this to start a line, then look at a second hook and press it again to tie the line. "
                + "Press it while looking at a line to untie it, or, if the line has items on it, to space them out evenly (one item is centered).");

            MaxLength = config.Bind("Line", "Max Length", 6f, new ConfigDescription(
                "Longest line you can tie between two hooks, in metres.",
                new AcceptableValueRange<float>(1f, 20f)));
            Sag = config.Bind("Line", "Sag", 0.04f, new ConfigDescription(
                "How far the middle of a line hangs below its ends, as a share of the line's length.",
                new AcceptableValueRange<float>(0f, 0.25f)));
            Thickness = config.Bind("Line", "Thickness", 0.02f, new ConfigDescription(
                "Thickness of the rope, in metres.",
                new AcceptableValueRange<float>(0.005f, 0.08f)));
            RopeColor = config.Bind("Line", "Color", new Color(0.66f, 0.56f, 0.40f, 1f), "Color of the rope.");

            ItemSpacing = config.Bind("Hanging", "Item Spacing", 0.2f, new ConfigDescription(
                "Closest two items can hang to each other on a line, in metres.",
                new AcceptableValueRange<float>(0.05f, 2f)));
            HangDrop = config.Bind("Hanging", "Hang Drop", 0.02f, new ConfigDescription(
                "How far below the rope an item hangs, in metres. Raise it if items sink into the rope.",
                new AcceptableValueRange<float>(-0.2f, 0.3f)));
            Hangers = config.Bind("Hanging", "Hangers", true,
                "Draw the small cords, rings, and knots that join a hung item to the rope. Turn this off and the items still hang, with nothing extra between them and the line.");

            Metrics = config.Bind("Metrics", "Enabled", false,
                "Record hanger and frame timings. Once a minute, write the mean, median, p90, and p99 to the log and the screen. Leave this off to skip recording and hide that readout.");

            Sag.SettingChanged += (s, e) => Lines.RebuildAll();
            Thickness.SettingChanged += (s, e) => Lines.RebuildAll();
            HangDrop.SettingChanged += (s, e) => Lines.RebuildAll();
            RopeColor.SettingChanged += (s, e) => RopeMesh.ApplyColor();
        }

        internal static bool DrawHangers
        {
            get { return Hangers == null || Hangers.Value; }
        }

        internal static string TieKeyLabel
        {
            get
            {
                KeyCode key = TieKey != null ? TieKey.Value : KeyCode.L;
                string name = key.ToString();
                return name.StartsWith("Alpha") ? name.Substring(5) : name;
            }
        }
    }
}
