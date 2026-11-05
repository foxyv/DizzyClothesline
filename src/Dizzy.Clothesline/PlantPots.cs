using HarmonyLib;
using UnityEngine;

namespace Dizzy.Clothesline
{
    // The seven flower pots (prefab 190 to 196) are not hangable in the game. Each gets a
    // HangableItem when it loads, so a pot can go on a line or a lamp hook the way a lantern
    // does. The pot hangs level, with the rim a set distance under the rope or hook, and
    // PlantTie draws four thin cords from the rim up to the rope.
    internal static class PlantPots
    {
        private const int First = 190;
        private const int Last = 196;

        // How far under a lamp hook's pin the rim sits, on top of the rise of the cords.
        private const float HookGap = 0.01f;

        // The shortest the cords can be made with the Plant Drop setting.
        private const float MinRise = 0.1f;

        // Measured from the pot meshes, in the pot's own space. The rim is the top of the lip.
        // The foliage spreads wider than the pot, so the cords simply pass through it.
        internal struct Spec
        {
            public float Rim;
            public float Radius;
            public float Rise;
        }

        internal static bool TryGet(int prefabIndex, out Spec spec)
        {
            spec = default(Spec);
            if (prefabIndex < First || prefabIndex > Last)
                return false;

            if (prefabIndex == 196)
            {
                spec.Rim = 0.145f;
                spec.Radius = 0.356f;
            }
            else if (prefabIndex == 194 || prefabIndex == 195)
            {
                spec.Rim = 0.037f;
                spec.Radius = 0.127f;
            }
            else
            {
                spec.Rim = 0.09f;
                spec.Radius = 0.226f;
            }

            // A basket's cords meet well above its rim, so they form a cone.
            // The Plant Drop setting lengthens or shortens that, but never below a short stub.
            spec.Rise = Mathf.Max(MinRise, Mathf.Clamp(spec.Radius * 2.2f, 0.3f, 0.6f) + ClotheslineConfig.PlantDropValue);
            return true;
        }

        internal static bool TryGet(ShipItem ship, out Spec spec)
        {
            SaveablePrefab save = ship != null ? ship.Getsaveable() : null;
            if (save == null)
            {
                spec = default(Spec);
                return false;
            }

            return TryGet(save.prefabIndex, out spec);
        }

        internal static bool Is(ShipItem ship)
        {
            Spec spec;
            return TryGet(ship, out spec);
        }

        // How far below the usual pin the origin sits, so the cords' meeting point is the
        // part just under the rope.
        internal static float Drop(ShipItem ship)
        {
            Spec spec;
            if (!TryGet(ship, out spec))
                return 0f;
            return spec.Rim + spec.Rise - ClotheslineConfig.HangDrop.Value
                + ClotheslineConfig.Thickness.Value * 0.5f + 0.012f;
        }

        internal static void MakeHangable(SaveablePrefab save)
        {
            if (save == null || !ClotheslineConfig.PlantsHang)
                return;
            Spec spec;
            if (!TryGet(save.prefabIndex, out spec) || save.GetComponent<HangableItem>() != null)
                return;

            // modcheck-ignore: SW605 a flower pot is not hangable in the game; this adds it once when the pot loads
            save.gameObject.AddComponent<HangableItem>();
        }

        // After HangableItem has pinned a pot at a lamp hook, drop it so the rim hangs
        // under the hook, and draw the cords up to it. A pot on a line is posed by the line.
        internal static void PoseOnHook(HangableItem hang)
        {
            if (hang == null)
                return;
            ShipItem ship = hang.GetComponent<ShipItem>();
            Spec spec;
            if (!TryGet(ship, out spec))
                return;
            if (Lines.OnLine(hang))
                return;

            Collider hook = Lines.HookOf(hang);
            if (hook == null || !hang.IsHanging())
            {
                PlantTie.ClearOn(ship);
                return;
            }

            // HangableItem puts an item's origin 0.128 behind the hook, because a lantern's
            // ring sits off its origin. A pot is centred on its origin, so hang it from the
            // hook point itself, or it sits to one side of the hook and the cords.
            Vector3 pin = hook.transform.position;
            ship.transform.position = pin - Vector3.up * (spec.Rim + spec.Rise + HookGap);
            if (ClotheslineConfig.DrawHangers)
                PlantTie.FollowHook(ship, spec, pin, hook.transform.right);
            else
                PlantTie.ClearOn(ship);
        }
    }

    [HarmonyPatch(typeof(SaveablePrefab), nameof(SaveablePrefab.Start))]
    internal static class PlantHangablePatch
    {
        private static void Postfix(SaveablePrefab __instance)
        {
            PlantPots.MakeHangable(__instance);
        }
    }
}
