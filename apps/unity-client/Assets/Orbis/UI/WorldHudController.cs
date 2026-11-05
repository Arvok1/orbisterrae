using System;
using System.Collections.Generic;
using Orbis.Core;
using Orbis.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace Orbis.UI
{
    public sealed class WorldHudController : IDisposable
    {
        readonly GameStore store;
        readonly GlobeRenderer globe;
        readonly VisualElement root;
        readonly Label title;
        readonly Label status;
        readonly Label coordinates;
        readonly Label altitude;
        readonly Button styleButton;
        readonly Button localeButton;
        readonly List<VisualElement> inputSurfaces = new();

        static readonly IReadOnlyDictionary<string, string> Portuguese = new Dictionary<string, string>
        {
            ["title"] = "ORBIS TERRAE",
            ["offline"] = "Fundação Unity · servidor ainda não conectado",
            ["relief"] = "Relevo 3D",
            ["manuscript"] = "Carta manuscrita",
            ["hint"] = "Arraste para mover · botão direito para orbitar · roda para aproximar",
        };

        static readonly IReadOnlyDictionary<string, string> English = new Dictionary<string, string>
        {
            ["title"] = "ORBIS TERRAE",
            ["offline"] = "Unity foundation · server not connected yet",
            ["relief"] = "3D relief",
            ["manuscript"] = "Manuscript map",
            ["hint"] = "Drag to pan · right button to orbit · wheel to zoom",
        };

        public WorldHudController(VisualElement rootElement, GameStore gameStore, GlobeRenderer globeRenderer)
        {
            root = rootElement ?? throw new ArgumentNullException(nameof(rootElement));
            store = gameStore ?? throw new ArgumentNullException(nameof(gameStore));
            globe = globeRenderer ?? throw new ArgumentNullException(nameof(globeRenderer));
            title = Require<Label>("world-title");
            status = Require<Label>("connection-status");
            coordinates = Require<Label>("coordinates");
            altitude = Require<Label>("altitude");
            styleButton = Require<Button>("style-toggle");
            localeButton = Require<Button>("locale-toggle");

            root.Query<VisualElement>(className: "input-surface").ForEach(RegisterInputSurface);
            styleButton.clicked += ToggleStyle;
            localeButton.clicked += ToggleLocale;
            root.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            store.Changed += Render;
            Render(store.State);
        }

        void OnGeometryChanged(GeometryChangedEvent geometry)
        {
            root.EnableInClassList("compact-layout", geometry.newRect.width <= 520f);
        }

        T Require<T>(string name) where T : VisualElement
        {
            return root.Q<T>(name) ?? throw new InvalidOperationException($"HUD element '{name}' was not found.");
        }

        void RegisterInputSurface(VisualElement surface)
        {
            inputSurfaces.Add(surface);
            surface.RegisterCallback<PointerEnterEvent>(_ => HudInputGate.Enter());
            surface.RegisterCallback<PointerLeaveEvent>(_ => HudInputGate.Leave());
        }

        void ToggleStyle()
        {
            var next = store.State.VisualStyle == MapVisualStyle.Relief ? MapVisualStyle.Manuscript : MapVisualStyle.Relief;
            globe.SetVisualStyle(next);
            PlayerPrefs.SetString("orbis.visual-style", next == MapVisualStyle.Relief ? "relief" : "manuscript");
            store.Update(store.State.With(visualStyle: next));
        }

        void ToggleLocale()
        {
            var next = store.State.Locale == "pt-BR" ? "en" : "pt-BR";
            PlayerPrefs.SetString("orbis.locale", next);
            store.Update(store.State.With(locale: next));
        }

        void Render(ClientViewState state)
        {
            var text = state.Locale == "en" ? English : Portuguese;
            title.text = text["title"];
            status.text = string.IsNullOrWhiteSpace(state.Status) ? text["offline"] : state.Status;
            coordinates.text = $"{state.Focus.LatitudeDegrees:+00.0000;-00.0000;00.0000}°, {state.Focus.LongitudeDegrees:+000.0000;-000.0000;000.0000}°";
            altitude.text = FormatAltitude(state.AltitudeMeters, state.Locale);
            styleButton.text = state.VisualStyle == MapVisualStyle.Relief ? text["manuscript"] : text["relief"];
            localeButton.text = state.Locale == "en" ? "PT-BR" : "EN";
            var hint = root.Q<Label>("camera-hint");
            if (hint != null) hint.text = text["hint"];
            root.EnableInClassList("manuscript", state.VisualStyle == MapVisualStyle.Manuscript);
        }

        static string FormatAltitude(double meters, string locale)
        {
            var label = locale == "en" ? "Altitude" : "Altitude";
            return meters >= 1_000
                ? $"{label}: {meters / 1_000:N0} km"
                : $"{label}: {meters:N0} m";
        }

        public void Dispose()
        {
            styleButton.clicked -= ToggleStyle;
            localeButton.clicked -= ToggleLocale;
            root.UnregisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            store.Changed -= Render;
            HudInputGate.Reset();
        }
    }
}
