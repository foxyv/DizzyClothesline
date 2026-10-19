namespace Dizzy.Clothesline
{
    // Metal and bamboo wind chimes. The game pitches them so the tubes hang
    // down. On a line they are also turned a quarter turn, and raised one rope
    // thickness, so the loop at the top sits on the rope.
    internal static class WindChimeHang
    {
        private const int Metal = 135;
        private const int Bamboo = 136;

        internal static float Drop(ShipItem ship)
        {
            if (!Is(ship))
                return 0f;
            return -ClotheslineConfig.Thickness.Value;
        }

        internal static bool Is(ShipItem ship)
        {
            SaveablePrefab save = ship != null ? ship.Getsaveable() : null;
            if (save == null)
                return false;
            return save.prefabIndex == Metal || save.prefabIndex == Bamboo;
        }
    }
}
