using UnityEngine;

namespace Dizzy.Clothesline
{
    // Mugs hang through HooksHangMore. On a line, the wood mug and both metal
    // mugs sit one rope thickness higher. The gold mug is the plain metal mug
    // with another texture, so it gets the same lift. The wood mug is also
    // turned a quarter turn about its handle.
    internal static class MugHang
    {
        private const int Wood = 100;
        private const int Metal = 102;
        private const int MetalGold = 103;

        // The wood handle is a loop on local -X, from about x=-0.105 to the cup
        // near x=-0.066, and from the foot of the loop up to the rim. Its middle
        // is the pivot, so the turn leaves the loop where it was.
        internal static readonly Vector3 WoodHandle = new Vector3(-0.086f, 0.068f, 0f);

        internal static float Drop(ShipItem ship)
        {
            if (!Is(ship))
                return 0f;
            return -ClotheslineConfig.Thickness.Value;
        }

        internal static bool Turn(ShipItem ship)
        {
            return Index(ship) == Wood;
        }

        private static bool Is(ShipItem ship)
        {
            int index = Index(ship);
            return index == Wood || index == Metal || index == MetalGold;
        }

        private static int Index(ShipItem ship)
        {
            SaveablePrefab save = ship != null ? ship.Getsaveable() : null;
            return save != null ? save.prefabIndex : -1;
        }
    }
}
