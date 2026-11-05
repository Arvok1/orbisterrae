using System;
using Orbis.Core;
using Orbis.UI;
using Orbis.World;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

namespace Orbis.Bootstrap
{
    public sealed class WorldBootstrap : MonoBehaviour
    {
        const double InitialAltitudeMeters = 18_000_000;
        static WorldBootstrap instance;

        WorldHudController hudController;
        PanelSettings runtimePanelSettings;
        VolumeProfile runtimeVolumeProfile;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void CreateRuntime()
        {
            if (instance != null || FindAnyObjectByType<WorldBootstrap>() != null) return;
            var host = new GameObject("Orbis Runtime");
            instance = host.AddComponent<WorldBootstrap>();
            DontDestroyOnLoad(host);
        }

        void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 1;

            var defaults = ClientRuntimeConfig.CreateDefaults(Application.persistentDataPath);
            var config = ClientRuntimeConfig.Parse(defaults, Environment.GetCommandLineArgs());
            var locale = PlayerPrefs.GetString("orbis.locale", config.Locale);
            var style = PlayerPrefs.GetString("orbis.visual-style", "relief") == "manuscript"
                ? MapVisualStyle.Manuscript
                : MapVisualStyle.Relief;
            var initialFocus = new GeoCoordinate(-22.9068, -43.1729);
            var store = new GameStore(new ClientViewState(locale, style, initialFocus, InitialAltitudeMeters, string.Empty));

            var worldCamera = CreateCamera();
            CreateLighting(worldCamera);
            var globe = CreateGlobe(style);
            var cameraController = worldCamera.gameObject.AddComponent<GeoCameraController>();
            cameraController.Initialize(worldCamera, store, initialFocus, InitialAltitudeMeters);
            CreateHud(store, globe);
        }

        static Camera CreateCamera()
        {
            var existing = Camera.main;
            var cameraObject = existing == null ? new GameObject("World Camera") : existing.gameObject;
            var camera = existing == null ? cameraObject.AddComponent<Camera>() : existing;
            cameraObject.tag = "MainCamera";
            camera.fieldOfView = 45f;
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 120_000f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.004f, 0.006f, 0.012f);
            var additionalData = cameraObject.GetComponent<UniversalAdditionalCameraData>() ?? cameraObject.AddComponent<UniversalAdditionalCameraData>();
            additionalData.renderPostProcessing = true;
            return camera;
        }

        void CreateLighting(Camera worldCamera)
        {
            var lightObject = new GameObject("Sun");
            lightObject.transform.rotation = Quaternion.Euler(38f, -28f, 0f);
            var sun = lightObject.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.35f;
            sun.color = new Color(1f, 0.88f, 0.69f);
            sun.shadows = LightShadows.Soft;

            runtimeVolumeProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            var tonemapping = runtimeVolumeProfile.Add<Tonemapping>();
            tonemapping.mode.Override(TonemappingMode.ACES);
            var bloom = runtimeVolumeProfile.Add<Bloom>();
            bloom.intensity.Override(0.22f);
            bloom.threshold.Override(1.05f);
            var vignette = runtimeVolumeProfile.Add<Vignette>();
            vignette.intensity.Override(0.2f);
            vignette.smoothness.Override(0.72f);

            var volumeObject = new GameObject("World Post Processing");
            var volume = volumeObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 10f;
            volume.sharedProfile = runtimeVolumeProfile;
            volumeObject.transform.SetParent(transform, false);
            lightObject.transform.SetParent(transform, false);
            worldCamera.transform.SetParent(transform, true);
        }

        GlobeRenderer CreateGlobe(MapVisualStyle style)
        {
            var globeObject = new GameObject("Earth");
            globeObject.transform.SetParent(transform, false);
            var globe = globeObject.AddComponent<GlobeRenderer>();
            globe.Initialize();
            globe.SetVisualStyle(style);
            return globe;
        }

        void CreateHud(GameStore store, GlobeRenderer globe)
        {
            var visualTree = Resources.Load<VisualTreeAsset>("Orbis/WorldHud");
            if (visualTree == null) throw new InvalidOperationException("WorldHud.uxml was not found in Resources/Orbis.");

            var panelSettingsAsset = Resources.Load<PanelSettings>("Orbis/WorldPanelSettings");
            if (panelSettingsAsset == null) throw new InvalidOperationException("WorldPanelSettings.asset was not found in Resources/Orbis.");
            runtimePanelSettings = Instantiate(panelSettingsAsset);
            runtimePanelSettings.name = "Orbis Runtime Panel Settings";

            var hudObject = new GameObject("World HUD");
            hudObject.transform.SetParent(transform, false);
            var document = hudObject.AddComponent<UIDocument>();
            document.panelSettings = runtimePanelSettings;
            document.sortingOrder = 100;
            document.visualTreeAsset = visualTree;
            hudController = new WorldHudController(document.rootVisualElement, store, globe);
        }

        void OnDestroy()
        {
            hudController?.Dispose();
            if (runtimePanelSettings != null) Destroy(runtimePanelSettings);
            if (runtimeVolumeProfile != null) Destroy(runtimeVolumeProfile);
            if (instance == this) instance = null;
        }
    }
}
