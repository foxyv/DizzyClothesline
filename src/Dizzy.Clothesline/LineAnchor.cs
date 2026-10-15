using UnityEngine;

namespace Dizzy.Clothesline
{
    // One hanging place on a line. HangableItem treats its collider as the hook, and
    // pins the item at position - forward * Lines.HookOffset, the same as a lamp hook.
    // The collider stays disabled, so nothing can bump into it or hang on it by itself.
    internal sealed class LineAnchor : MonoBehaviour
    {
        internal ClotheslineLine Line;
        internal HungRecord Record;
        internal Collider Col;

        // Set once the item is hung.
        internal HangableItem Item;
        internal ShipItem Ship;
        internal int HangId;
    }
}
