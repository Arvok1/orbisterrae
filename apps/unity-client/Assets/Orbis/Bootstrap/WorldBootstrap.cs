using System;
using Orbis.Core;
using Orbis.Network;
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
        CityGameplayController gameplayController;
        GameNetworkClient networkClient;
        PanelSettings runtimePanelSettings;
        VolumeProfile runtimeVolumeProfile;

        GameStore store;
        ClientRuntimeConfig config;
        bool hintFromCommandLineLogin;
        bool playerModeHintShown;

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
            this.config = config;
            var locale = PlayerPrefs.GetString("orbis.locale", config.Locale);
            var style = PlayerPrefs.GetString("orbis.visual-style", "relief") == "manuscript"
                ? MapVisualStyle.Manuscript
                : MapVisualStyle.Relief;
            var initialFocus = new GeoCoordinate(-22.9068, -43.1729);
            var state = new ClientViewState(locale, style, initialFocus, InitialAltitudeMeters, string.Empty);
            store = new GameStore(state);

            networkClient = gameObject.AddComponent<GameNetworkClient>();
            if(!VisualAcceptanceRunner.Requested)networkClient.Initialize(config, store);

            var worldCamera = CreateCamera();
            CreateLighting(worldCamera);
            var globe = CreateGlobe(config, style);
            var cameraController = worldCamera.gameObject.AddComponent<GeoCameraController>();
            cameraController.Initialize(worldCamera, store, globe, initialFocus, InitialAltitudeMeters);
            if(VisualAcceptanceRunner.LegacyCityRequested)gameObject.AddComponent<LegacyCityReplayRenderer>().Initialize(store,globe);
            else gameObject.AddComponent<CityWorldRenderer>().Initialize(store, globe);
            var cartography=gameObject.AddComponent<WorldCartography>();
            cartography.Initialize(store, globe);
            gameObject.AddComponent<GeographicOverlay>().Initialize(store, globe);
            var hubMarkers=gameObject.AddComponent<HubMarkerController>();
            hubMarkers.Initialize(store, networkClient.Economy, globe);
            gameObject.AddComponent<CityInteractionController>().Initialize(store,globe,cartography,networkClient.EditCell,networkClient.SelectCity,hubMarkers.Pick,networkClient.SelectHub);
            CreateHud(store, globe, networkClient);
            if (!VisualAcceptanceRunner.Requested) StageAutomaticLogin(networkClient);
            if(VisualAcceptanceRunner.Requested)gameObject.AddComponent<VisualAcceptanceRunner>().Initialize(store,globe);
        }

        /// <summary>Logs an admin test account in automatically so the live world needs no typing.</summary>
        void StageAutomaticLogin(GameNetworkClient network)
        {
            hudController.PrepareLogin(config.TestUsername);
            var remembered = PlayerPrefs.GetString("orbis.auth.username", string.Empty);
            var rememberedPassword = PlayerPrefs.GetString("orbis.auth.password", string.Empty);
            var rememberEnabled = PlayerPrefs.GetInt("orbis.auth.remember", 0) == 1;
            var staged = AutoLoginPlan.Choose(config, store.State.Session != null, remembered, rememberedPassword, rememberEnabled);
            if (!staged.HasCredentials) return;
            hintFromCommandLineLogin = staged.FromCommandLine;
            network.Login(staged.Username, staged.Password);
        }

        void Update()
        {
            if (store == null || !hintFromCommandLineLogin) return;
            var plain = store.State.Session != null && !store.State.Session.IsAdmin;
            if (plain == playerModeHintShown) return;
            playerModeHintShown = plain;
            if (plain) store.Update(store.State.With(status: "player_mode"));
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

        GlobeRenderer CreateGlobe(ClientRuntimeConfig config, MapVisualStyle style)
        {
            var globeObject = new GameObject("Earth");
            globeObject.transform.SetParent(transform, false);
            var globe = globeObject.AddComponent<GlobeRenderer>();
            globe.Initialize(config);
            globe.SetVisualStyle(style);
            return globe;
        }

        void CreateHud(GameStore store, GlobeRenderer globe, GameNetworkClient network)
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
            hudController = new WorldHudController(document.rootVisualElement, store, globe, network);
            gameplayController = new CityGameplayController(document.rootVisualElement, store, network);
        }

        void OnDestroy()
        {
            hudController?.Dispose();
            gameplayController?.Dispose();
            if (runtimePanelSettings != null) Destroy(runtimePanelSettings);
            if (runtimeVolumeProfile != null) Destroy(runtimeVolumeProfile);
            if (instance == this) instance = null;
        }
    }
}
