namespace Orbis.World
{
    public static class HudInputGate
    {
        static bool pointerBlocked;
        static readonly System.Collections.Generic.HashSet<object> surfaces=new();
        public static bool IsKeyboardBlocked { get; private set; }

        public static bool IsPointerBlocked => pointerBlocked||surfaces.Count>0;
        public static void Enter(object owner)=>surfaces.Add(owner);
        public static void Leave(object owner)=>surfaces.Remove(owner);
        public static void SetKeyboardFocus(bool focused)=>IsKeyboardBlocked=focused;

        public static void Enter() => pointerBlocked = true;
        public static void Leave() => pointerBlocked = false;
        public static void Reset() { pointerBlocked=false;surfaces.Clear();IsKeyboardBlocked=false; }
    }
}
