namespace Dizzy.Clothesline
{
    // Lanterns that already have a loop. On a line, most of them sit one rope
    // thickness higher so the loop rests on the rope. The two Medi lanterns are
    // turned as well: their bail is a flat loop in the local X plane, thin along
    // local Z, and the usual facing leaves that loop parallel to the rope.
    internal static class LanternHang
    {
        private const int AlAnkh = 110;
        private const int EmeraldYellow = 111;
        private const int EmeraldRed = 112;
        private const int EmeraldGreen = 113;
        private const int Medi = 114;
        private const int MediBig = 133;
        private const int EmeraldBlue = 134;

        // Positive lowers the pin. The big Medi lantern sits half a thickness
        // below the usual pin. The others sit one thickness above it.
        internal static float Drop(ShipItem ship)
        {
            int index = Index(ship);
            if (index < 0)
                return 0f;
            float thickness = ClotheslineConfig.Thickness.Value;
            if (index == MediBig)
                return thickness * 0.5f;
            return -thickness;
        }

        internal static bool Turn(ShipItem ship)
        {
            int index = Index(ship);
            return index == Medi || index == MediBig;
        }

        private static int Index(ShipItem ship)
        {
            SaveablePrefab save = ship != null ? ship.Getsaveable() : null;
            if (save == null)
                return -1;
            switch (save.prefabIndex)
            {
                case AlAnkh:
                case EmeraldYellow:
                case EmeraldRed:
                case EmeraldGreen:
                case Medi:
                case MediBig:
                case EmeraldBlue:
                    return save.prefabIndex;
                default:
                    return -1;
            }
        }
    }
}
