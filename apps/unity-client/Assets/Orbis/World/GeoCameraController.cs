using Orbis.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Orbis.World
{
    public sealed class GeoCameraController : MonoBehaviour
    {
        const double MinimumAltitudeMeters = 20;
        const double MaximumAltitudeMeters = 50_000_000;
        const float DragThresholdPixels = 4f;

        OrbisInputActions input;
        GameStore store;
        Camera worldCamera;
        GeoCoordinate focus;
        double altitudeMeters;
        float headingDegrees;
        float pitchDegrees = 20f;
        Vector2 lastPointer;
        Vector2 pressPointer;
        bool primaryWasPressed;
        bool orbitWasPressed;

        public GeoCoordinate Focus => focus;
        public double AltitudeMeters => altitudeMeters;

        public void Initialize(Camera camera, GameStore gameStore, GeoCoordinate initialFocus, double initialAltitudeMeters)
        {
            worldCamera = camera;
            store = gameStore;
            focus = initialFocus;
            altitudeMeters = initialAltitudeMeters;
            input = new OrbisInputActions();
            input.Enable();
            ApplyTransform();
        }

        void Update()
        {
            if (input == null) return;

            var pointer = input.Point.ReadValue<Vector2>();
            var primaryPressed = input.PrimaryDrag.IsPressed();
            var orbitPressed = input.OrbitDrag.IsPressed();
            var pointerDelta = pointer - lastPointer;

            if (primaryPressed && !primaryWasPressed) pressPointer = pointer;
            if (orbitPressed && !orbitWasPressed) pressPointer = pointer;

            var passedThreshold = (pointer - pressPointer).sqrMagnitude >= DragThresholdPixels * DragThresholdPixels;
            if (!HudInputGate.IsPointerBlocked && passedThreshold)
            {
                if (primaryPressed) Pan(pointerDelta);
                if (orbitPressed) Orbit(pointerDelta);
            }

            if (!HudInputGate.IsPointerBlocked)
            {
                var scroll = input.Scroll.ReadValue<Vector2>().y;
                if (Mathf.Abs(scroll) > 0.01f) Zoom(scroll);

                var keyboard = input.CameraMove.ReadValue<Vector2>();
                if (keyboard.sqrMagnitude > 0.001f) Pan(keyboard * (220f * Time.unscaledDeltaTime));

                var rotate = input.Rotate.ReadValue<float>();
                if (Mathf.Abs(rotate) > 0.01f) headingDegrees += rotate * 55f * Time.unscaledDeltaTime;
            }

            primaryWasPressed = primaryPressed;
            orbitWasPressed = orbitPressed;
            lastPointer = pointer;
            ApplyTransform();
        }

        void Pan(Vector2 deltaPixels)
        {
            var metersPerPixel = Mathf.Max(0.1f, (float)(altitudeMeters / Mathf.Max(Screen.height, 1)));
            var latitudeMetersPerDegree = 111_320d;
            var longitudeScale = System.Math.Max(0.05, System.Math.Cos(focus.LatitudeDegrees * System.Math.PI / 180));
            var latitude = Mathf.Clamp((float)(focus.LatitudeDegrees + deltaPixels.y * metersPerPixel / latitudeMetersPerDegree), -89.5f, 89.5f);
            var longitude = focus.LongitudeDegrees - deltaPixels.x * metersPerPixel / (latitudeMetersPerDegree * longitudeScale);
            focus = new GeoCoordinate(latitude, longitude);
            PublishView();
        }

        void Orbit(Vector2 deltaPixels)
        {
            headingDegrees += deltaPixels.x * 0.18f;
            pitchDegrees = Mathf.Clamp(pitchDegrees - deltaPixels.y * 0.14f, 0f, 75f);
        }

        void Zoom(float scrollDelta)
        {
            var factor = Mathf.Exp(-scrollDelta * 0.0012f);
            altitudeMeters = System.Math.Clamp(altitudeMeters * factor, MinimumAltitudeMeters, MaximumAltitudeMeters);
            PublishView();
        }

        void ApplyTransform()
        {
            if (worldCamera == null) return;

            var surfaceNormal = GeoMath.SurfaceNormal(focus);
            var east = Vector3.Cross(Vector3.up, surfaceNormal);
            if (east.sqrMagnitude < 0.001f) east = Vector3.Cross(Vector3.forward, surfaceNormal);
            east.Normalize();
            var north = Vector3.Cross(surfaceNormal, east).normalized;
            var tangentDirection = Quaternion.AngleAxis(headingDegrees, surfaceNormal) * north;
            var tiltedNormal = Quaternion.AngleAxis(pitchDegrees, east) * surfaceNormal;
            var focusPoint = surfaceNormal * (float)GeoMath.EarthRadiusUnity;
            var cameraOffset = (tiltedNormal - tangentDirection * Mathf.Sin(pitchDegrees * Mathf.Deg2Rad) * 0.25f).normalized;
            worldCamera.transform.position = focusPoint + cameraOffset * (float)(altitudeMeters / GeoMath.MetersPerUnityUnit);
            worldCamera.transform.LookAt(focusPoint, north);
        }

        void PublishView()
        {
            if (store == null) return;
            store.Update(store.State.With(focus: focus, altitudeMeters: altitudeMeters));
        }

        void OnDisable()
        {
            input?.Disable();
        }

        void OnDestroy()
        {
            input?.Dispose();
        }
    }
}
