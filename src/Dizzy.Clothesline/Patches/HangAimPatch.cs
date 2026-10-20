using HarmonyLib;
using UnityEngine;

namespace Dizzy.Clothesline.Patches
{
    // After Dizzy Fixes and Bundle Up have chosen a look. While a hangable item
    // is held, a look that passes near a rope selects that rope. Nothing is
    // written when the rope is not the nearest target, so every other look is
    // left as those mods set it.
    [HarmonyPatch(typeof(GoPointer), "DoRaycast")]
    [HarmonyPriority(Priority.Last)]
    [HarmonyAfter("com.dizzy.sailwind.fixes", "com.dizzy.sailwind.firewoodbundle")]
    internal static class HangAimPatch
    {
        private static void Postfix(
            GoPointer __instance,
            PickupableItem ___heldItem,
            bool ___debugEditorPointer,
            Ray ___raycastRay,
            Transform ___pointer,
            ref GoPointerButton ___pointedAtButton,
            ref float ___currentLookDistance,
            ref RaycastHit ___hit)
        {
            if (!Playing(__instance))
                return;

            Ray ray = ___raycastRay;
            if (___debugEditorPointer && Camera.main != null)
                ray = Camera.main.ScreenPointToRay(Input.mousePosition);

            ClotheslineLine line;
            Vector3 point;
            float along;
            if (!Lines.TryAimHang(ray, ___heldItem, ___pointedAtButton, ___currentLookDistance, out line, out point, out along))
                return;

            if (___pointedAtButton != null && ___pointedAtButton != line)
                ___pointedAtButton.ForceUnlook();
            ___pointedAtButton = line;
            ___currentLookDistance = along;
            line.Look(__instance);

            RaycastHit hit = ___hit;
            hit.point = point;
            ___hit = hit;
            if (___pointer != null)
                ___pointer.position = point;
        }

        // The same cases where GoPointer.DoRaycast itself keeps or clears the look.
        private static bool Playing(GoPointer pointer)
        {
            if (pointer == null)
                return false;
            if (pointer.type == GoPointer.PointerType.crosshairMouse && GameState.inCursorMenu)
                return false;
            return !GameState.sleeping && !GameState.inBed && !BoatCamera.on;
        }
    }
}
