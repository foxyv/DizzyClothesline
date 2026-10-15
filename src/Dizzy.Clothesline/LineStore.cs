using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Dizzy.Clothesline
{
    // One line between two lamp hooks, saved by the hooks' instance ids.
    internal sealed class LineRecord
    {
        internal int HookA;
        internal int HookB;
        internal readonly List<HungRecord> Items = new List<HungRecord>();

        // The line in the world, when both hooks are spawned. Not saved.
        internal ClotheslineLine Live;
    }

    // One item hanging on a line. S runs from 0 at HookA to 1 at HookB.
    // Side is which way the item faces across the line, +1 or -1.
    internal sealed class HungRecord
    {
        internal int ItemId;
        internal float S;
        internal int Side;
    }

    // Lines and what hangs on them, kept in GameState.modData.
    //
    // Boats spawn their items only when the player is close, and destroy them again
    // when the player leaves. A record stays here the whole time; the world objects
    // come and go. So a record is only removed when the player unties a line or takes
    // an item off it, or when a save finds its hooks or item gone for good.
    internal static class LineStore
    {
        internal const string ModDataKey = "Dizzy.Clothesline";
        private const string Version = "1";

        internal static readonly List<LineRecord> All = new List<LineRecord>();
        private static readonly Dictionary<int, LineRecord> LineOfItem = new Dictionary<int, LineRecord>();
        private static readonly HashSet<int> Hooks = new HashSet<int>();

        internal static void Clear()
        {
            All.Clear();
            LineOfItem.Clear();
            Hooks.Clear();
        }

        internal static LineRecord Find(int hookA, int hookB)
        {
            for (int i = 0; i < All.Count; i++)
            {
                LineRecord line = All[i];
                if ((line.HookA == hookA && line.HookB == hookB) || (line.HookA == hookB && line.HookB == hookA))
                    return line;
            }
            return null;
        }

        internal static void LinesOnHook(int hookId, List<LineRecord> into)
        {
            into.Clear();
            if (!Hooks.Contains(hookId))
                return;
            for (int i = 0; i < All.Count; i++)
            {
                if (All[i].HookA == hookId || All[i].HookB == hookId)
                    into.Add(All[i]);
            }
        }

        internal static bool IsHook(int instanceId)
        {
            return Hooks.Contains(instanceId);
        }

        internal static bool IsHungItem(int instanceId)
        {
            return LineOfItem.ContainsKey(instanceId);
        }

        internal static bool TryGetItem(int instanceId, out LineRecord line, out HungRecord item)
        {
            item = null;
            if (!LineOfItem.TryGetValue(instanceId, out line))
                return false;
            for (int i = 0; i < line.Items.Count; i++)
            {
                if (line.Items[i].ItemId == instanceId)
                {
                    item = line.Items[i];
                    return true;
                }
            }
            return false;
        }

        internal static void Add(LineRecord line)
        {
            All.Add(line);
            Hooks.Add(line.HookA);
            Hooks.Add(line.HookB);
            for (int i = 0; i < line.Items.Count; i++)
                LineOfItem[line.Items[i].ItemId] = line;
        }

        internal static void Remove(LineRecord line)
        {
            All.Remove(line);
            for (int i = 0; i < line.Items.Count; i++)
                LineOfItem.Remove(line.Items[i].ItemId);
            RebuildHooks();
        }

        internal static void AddItem(LineRecord line, HungRecord item)
        {
            RemoveItem(item.ItemId);
            line.Items.Add(item);
            LineOfItem[item.ItemId] = line;
        }

        internal static void RemoveItem(int itemId)
        {
            LineRecord line;
            if (!LineOfItem.TryGetValue(itemId, out line))
                return;
            LineOfItem.Remove(itemId);
            for (int i = line.Items.Count - 1; i >= 0; i--)
            {
                if (line.Items[i].ItemId == itemId)
                    line.Items.RemoveAt(i);
            }
        }

        private static void RebuildHooks()
        {
            Hooks.Clear();
            for (int i = 0; i < All.Count; i++)
            {
                Hooks.Add(All[i].HookA);
                Hooks.Add(All[i].HookB);
            }
        }

        // Drops lines and hung items that no longer exist anywhere in the save,
        // spawned or waiting on a distant boat.
        internal static void Prune(HashSet<int> known)
        {
            for (int i = All.Count - 1; i >= 0; i--)
            {
                LineRecord line = All[i];
                if (known.Contains(line.HookA) && known.Contains(line.HookB))
                    continue;
                Plugin.Log.LogInfo("Dropping a saved line whose hooks are gone (" + line.HookA + ", " + line.HookB + ").");
                Remove(line);
            }

            for (int i = 0; i < All.Count; i++)
            {
                List<HungRecord> items = All[i].Items;
                for (int j = items.Count - 1; j >= 0; j--)
                {
                    if (!known.Contains(items[j].ItemId))
                    {
                        LineOfItem.Remove(items[j].ItemId);
                        items.RemoveAt(j);
                    }
                }
            }
        }

        internal static void Read()
        {
            Clear();
            if (GameState.modData == null)
                return;

            string raw;
            if (!GameState.modData.TryGetValue(ModDataKey, out raw) || string.IsNullOrEmpty(raw))
                return;

            LineRecord current = null;
            string[] rows = raw.Split(new[] { '\n' }, System.StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < rows.Length; i++)
            {
                string[] parts = rows[i].Trim().Split('\t');
                if (parts[0] == "L" && parts.Length >= 3)
                {
                    int a;
                    int b;
                    if (!TryInt(parts[1], out a) || !TryInt(parts[2], out b) || a == 0 || b == 0 || a == b)
                    {
                        current = null;
                        continue;
                    }
                    current = new LineRecord { HookA = a, HookB = b };
                    All.Add(current);
                }
                else if (parts[0] == "I" && parts.Length >= 4 && current != null)
                {
                    int id;
                    float s;
                    int side;
                    if (!TryInt(parts[1], out id) || id == 0)
                        continue;
                    if (!float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out s))
                        continue;
                    if (!TryInt(parts[3], out side))
                        continue;
                    current.Items.Add(new HungRecord { ItemId = id, S = s, Side = side < 0 ? -1 : 1 });
                }
            }

            RebuildHooks();
            for (int i = 0; i < All.Count; i++)
            {
                for (int j = 0; j < All[i].Items.Count; j++)
                    LineOfItem[All[i].Items[j].ItemId] = All[i];
            }

            Plugin.Log.LogInfo("Loaded " + All.Count + " line(s), " + LineOfItem.Count + " hanging item(s).");
        }

        internal static void Write()
        {
            if (GameState.modData == null)
                GameState.modData = new Dictionary<string, string>();

            if (All.Count == 0)
            {
                GameState.modData.Remove(ModDataKey);
                return;
            }

            var sb = new StringBuilder();
            sb.Append("V\t").Append(Version);
            for (int i = 0; i < All.Count; i++)
            {
                LineRecord line = All[i];
                sb.Append("\nL\t").Append(Int(line.HookA)).Append('\t').Append(Int(line.HookB));
                for (int j = 0; j < line.Items.Count; j++)
                {
                    HungRecord item = line.Items[j];
                    sb.Append("\nI\t").Append(Int(item.ItemId))
                        .Append('\t').Append(item.S.ToString("R", CultureInfo.InvariantCulture))
                        .Append('\t').Append(Int(item.Side));
                }
            }
            // modcheck-ignore: SW604 records outlive spawned objects; Prune drops only ids missing from both spawned and boat-cached items
            GameState.modData[ModDataKey] = sb.ToString();
        }

        private static bool TryInt(string text, out int value)
        {
            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        private static string Int(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }
    }
}
