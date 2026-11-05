namespace Orbis.World
{
    public static class HudInputGate
    {
        static bool pointerBlocked;

        public static bool IsPointerBlocked => pointerBlocked;

        public static void Enter() => pointerBlocked = true;
        public static void Leave() => pointerBlocked = false;
        public static void Reset() => pointerBlocked = false;
    }
}
