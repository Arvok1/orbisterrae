using System;
using UnityEngine.InputSystem;

namespace Orbis.World
{
    public sealed class OrbisInputActions : IDisposable
    {
        public InputAction Point { get; }
        public InputAction PrimaryDrag { get; }
        public InputAction OrbitDrag { get; }
        public InputAction Scroll { get; }
        public InputAction CameraMove { get; }
        public InputAction Rotate { get; }
        public InputAction Follow { get; }

        readonly InputActionMap map;

        public OrbisInputActions()
        {
            map = new InputActionMap("World");
            Point = map.AddAction("Point", InputActionType.PassThrough, "<Pointer>/position");
            PrimaryDrag = map.AddAction("PrimaryDrag", InputActionType.Button, "<Pointer>/press");
            OrbitDrag = map.AddAction("OrbitDrag", InputActionType.Button, "<Mouse>/rightButton");
            Scroll = map.AddAction("Zoom", InputActionType.PassThrough, "<Mouse>/scroll");

            CameraMove = map.AddAction("CameraMove", InputActionType.Value);
            CameraMove.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w")
                .With("Up", "<Keyboard>/upArrow")
                .With("Down", "<Keyboard>/s")
                .With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/a")
                .With("Left", "<Keyboard>/leftArrow")
                .With("Right", "<Keyboard>/d")
                .With("Right", "<Keyboard>/rightArrow");

            Rotate = map.AddAction("Rotate", InputActionType.Value);
            Rotate.AddCompositeBinding("1DAxis")
                .With("Negative", "<Keyboard>/q")
                .With("Positive", "<Keyboard>/e");
            Follow = map.AddAction("FollowRuler", InputActionType.Button, "<Keyboard>/f");
        }

        public void Enable() => map.Enable();
        public void Disable() => map.Disable();
        public void Dispose() => map.Dispose();
    }
}
