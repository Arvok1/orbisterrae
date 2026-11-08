using System;
using System.Collections.Generic;
using System.Globalization;
using Orbis.Core;
using Orbis.Network;
using Orbis.V1;
using Orbis.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace Orbis.UI
{
    public sealed class WorldHudController : IDisposable
    {
        readonly GameStore store;
        readonly GlobeRenderer globe;
        readonly GameNetworkClient network;
        readonly VisualElement root;
        readonly Label title;
        readonly Label status;
        readonly Label coordinates;
        readonly Label altitude;
        readonly Label terrainStatus;
        readonly Label sessionLabel;
        readonly Button styleButton;
        readonly Button localeButton;
        readonly Button logoutButton;
        readonly VisualElement loginPanel;
        readonly TextField usernameInput;
        readonly TextField passwordInput;
        readonly Button loginButton;
        readonly Toggle autoLoginToggle;
        readonly VisualElement adminPanel;
        readonly Label adminSummary;
        readonly ScrollView cityList;
        readonly VisualElement cityInspector;
        readonly Label cityName;
        readonly Label cityDetails;
        readonly ScrollView buildingList;
        readonly VisualElement eventPanel;
        readonly ScrollView eventList;
        readonly TextField accountFilter;
        readonly TextField kingdomFilter;
        readonly TextField cityFilter;
        readonly TextField shardFilter;
        readonly TextField eventFilter;
        readonly Button applyFilters;
        readonly List<VisualElement> inputSurfaces = new();

        AdminSnapshot renderedAdmin;
        CitySnapshot renderedCity;
        string renderedLocale;

        const string RememberKey = "orbis.auth.remember";
        const string UsernameKey = "orbis.auth.username";
        const string PasswordKey = "orbis.auth.password";

        static readonly IReadOnlyDictionary<string, string> Portuguese = new Dictionary<string, string>
        {
            ["title"] = "ORBIS TERRAE",
            ["offline"] = "Servidor offline",
            ["relief"] = "Relevo 3D",
            ["manuscript"] = "Carta manuscrita",
            ["hint"] = "Arraste para mover · botão direito para orbitar · roda para aproximar",
        };

        static readonly IReadOnlyDictionary<string, string> English = new Dictionary<string, string>
        {
            ["title"] = "ORBIS TERRAE",
            ["offline"] = "Server offline",
            ["relief"] = "3D relief",
            ["manuscript"] = "Manuscript map",
            ["hint"] = "Drag to pan · right button to orbit · wheel to zoom",
        };

        public WorldHudController(VisualElement rootElement, GameStore gameStore, GlobeRenderer globeRenderer, GameNetworkClient networkClient)
        {
            root = rootElement ?? throw new ArgumentNullException(nameof(rootElement));
            store = gameStore ?? throw new ArgumentNullException(nameof(gameStore));
            globe = globeRenderer ?? throw new ArgumentNullException(nameof(globeRenderer));
            network = networkClient ?? throw new ArgumentNullException(nameof(networkClient));
            title = Require<Label>("world-title");
            status = Require<Label>("connection-status");
            coordinates = Require<Label>("coordinates");
            altitude = Require<Label>("altitude");
            terrainStatus = Require<Label>("terrain-status");
            sessionLabel = Require<Label>("session-label");
            styleButton = Require<Button>("style-toggle");
            localeButton = Require<Button>("locale-toggle");
            logoutButton = Require<Button>("logout-button");
            loginPanel = Require<VisualElement>("login-panel");
            usernameInput = Require<TextField>("username-input");
            passwordInput = Require<TextField>("password-input");
            loginButton = Require<Button>("login-button");
            autoLoginToggle = Require<Toggle>("auto-login-toggle");
            autoLoginToggle.SetValueWithoutNotify(PlayerPrefs.GetInt(RememberKey, 0) == 1);
            adminPanel = Require<VisualElement>("admin-panel");
            adminSummary = Require<Label>("admin-summary");
            cityList = Require<ScrollView>("city-list");
            cityInspector = Require<VisualElement>("city-inspector");
            cityName = Require<Label>("city-name");
            cityDetails = Require<Label>("city-details");
            buildingList = Require<ScrollView>("building-list");
            eventPanel = Require<VisualElement>("event-panel");
            eventList = Require<ScrollView>("event-list");
            accountFilter = Require<TextField>("account-filter");
            kingdomFilter = Require<TextField>("kingdom-filter");
            cityFilter = Require<TextField>("city-filter");
            shardFilter = Require<TextField>("shard-filter");
            eventFilter = Require<TextField>("event-filter");
            applyFilters = Require<Button>("apply-filters");

            root.Query<VisualElement>(className: "input-surface").ForEach(RegisterInputSurface);
            styleButton.clicked += ToggleStyle;
            localeButton.clicked += ToggleLocale;
            loginButton.clicked += Login;
            autoLoginToggle.RegisterValueChangedCallback(OnAutoLoginToggleChanged);
            logoutButton.clicked += network.Logout;
            applyFilters.clicked += ApplyFilters;
            passwordInput.RegisterCallback<KeyDownEvent>(OnPasswordKeyDown);
            root.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            store.Changed += Render;
            Render(store.State);
        }

        void OnGeometryChanged(GeometryChangedEvent geometry)
        {
            root.EnableInClassList("compact-layout", geometry.newRect.width <= 720f);
        }

        T Require<T>(string name) where T : VisualElement
        {
            return root.Q<T>(name) ?? throw new InvalidOperationException($"HUD element '{name}' was not found.");
        }

        void RegisterInputSurface(VisualElement surface)
        {
            inputSurfaces.Add(surface);
            surface.RegisterCallback<PointerEnterEvent>(_ => HudInputGate.Enter(surface));
            surface.RegisterCallback<PointerLeaveEvent>(_ => HudInputGate.Leave(surface));
            surface.RegisterCallback<FocusInEvent>(evt =>
            {
                var target=evt.target as VisualElement;
                HudInputGate.SetKeyboardFocus(target is TextField||target?.GetFirstAncestorOfType<TextField>()!=null);
            });
            surface.RegisterCallback<FocusOutEvent>(_ => HudInputGate.SetKeyboardFocus(false));
        }

        /// <summary>Stages the login panel from the command line and saved "remember me" preferences.</summary>
        public void PrepareLogin(string commandLineUsername)
        {
            usernameInput.value = string.IsNullOrWhiteSpace(commandLineUsername)
                ? PlayerPrefs.GetString(UsernameKey, string.Empty)
                : commandLineUsername;
            autoLoginToggle.SetValueWithoutNotify(PlayerPrefs.GetInt(RememberKey, 0) == 1);
        }

        void Login()
        {
            var username = usernameInput.value;
            var password = passwordInput.value;
            Remember(autoLoginToggle.value, username, password);
            network.Login(username, password);
            passwordInput.value = string.Empty;
        }

        void OnAutoLoginToggleChanged(ChangeEvent<bool> change)
        {
            Remember(change.newValue, usernameInput.value, passwordInput.value);
        }

        /// <summary>The auto-login toggle persists the account for the next launch.</summary>
        void Remember(bool enabled, string username, string password)
        {
            PlayerPrefs.SetInt(RememberKey, enabled ? 1 : 0);
            if (!enabled)
            {
                PlayerPrefs.DeleteKey(UsernameKey);
                PlayerPrefs.DeleteKey(PasswordKey);
                return;
            }

            PlayerPrefs.SetString(UsernameKey, (username ?? string.Empty).Trim());
            PlayerPrefs.SetString(PasswordKey, password ?? string.Empty);
        }

        void OnPasswordKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter) Login();
        }

        void ApplyFilters()
        {
            ulong? shard = ulong.TryParse(shardFilter.value, NumberStyles.None, CultureInfo.InvariantCulture, out var shardValue)
                ? shardValue
                : null;
            uint? eventKind = uint.TryParse(eventFilter.value, NumberStyles.None, CultureInfo.InvariantCulture, out var eventValue)
                ? eventValue
                : null;
            network.SetFilters(new AdminFilters(accountFilter.value, kingdomFilter.value, cityFilter.value, shard, eventKind));
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
            status.text = ClientText.Get(state.Locale,string.IsNullOrWhiteSpace(state.Status)?"offline":state.Status);
            coordinates.text = $"{state.Focus.LatitudeDegrees:+00.0000;-00.0000;00.0000}°, {state.Focus.LongitudeDegrees:+000.0000;-000.0000;000.0000}°";
            altitude.text = FormatAltitude(state.AltitudeMeters);
            terrainStatus.text = ClientText.Get(state.Locale,globe.PackageStatus);
            foreach(var pair in new[] { ("login-title","login"),("login-help","login_help"),("admin-title","observatory"),
                ("events-title","events"),("cities-title","cities"),("buildings-title","buildings") })
            { var label=root.Q<Label>(pair.Item1);if(label!=null)label.text=ClientText.Get(state.Locale,pair.Item2); }
            usernameInput.label=ClientText.Get(state.Locale,"username");passwordInput.label=ClientText.Get(state.Locale,"password");
            autoLoginToggle.label=ClientText.Get(state.Locale,"auto_login");
            loginButton.text=ClientText.Get(state.Locale,"sign_in");logoutButton.text=ClientText.Get(state.Locale,"sign_out");
            applyFilters.text=ClientText.Get(state.Locale,"apply_filters");accountFilter.label=ClientText.Get(state.Locale,"account");
            kingdomFilter.label=ClientText.Get(state.Locale,"kingdom");cityFilter.label=ClientText.Get(state.Locale,"city");
            eventFilter.label=ClientText.Get(state.Locale,"event");
            styleButton.text = state.VisualStyle == MapVisualStyle.Relief ? text["manuscript"] : text["relief"];
            localeButton.text = state.Locale == "en" ? "PT-BR" : "EN";
            root.Q<Label>("camera-hint").text = text["hint"];
            root.EnableInClassList("manuscript", state.VisualStyle == MapVisualStyle.Manuscript);

            var hasSession = state.Session != null;
            var isAdmin = state.Session?.IsAdmin == true;
            root.EnableInClassList("admin-active", isAdmin);
            loginPanel.style.display = hasSession ? DisplayStyle.None : DisplayStyle.Flex;
            logoutButton.style.display = hasSession ? DisplayStyle.Flex : DisplayStyle.None;
            sessionLabel.text = hasSession ? $"{state.Session.Username} · {state.Session.Role}" : string.Empty;
            adminPanel.style.display = isAdmin ? DisplayStyle.Flex : DisplayStyle.None;
            eventPanel.style.display = isAdmin ? DisplayStyle.Flex : DisplayStyle.None;
            cityInspector.style.display = !string.IsNullOrEmpty(state.SelectedCityId) ? DisplayStyle.Flex : DisplayStyle.None;

            var localeChanged=renderedLocale!=state.Locale;
            renderedLocale=state.Locale;
            if (!ReferenceEquals(renderedAdmin, state.Admin)||localeChanged)
            {
                renderedAdmin = state.Admin;
                RenderAdmin(state.Admin);
            }
            if (!ReferenceEquals(renderedCity, state.City)||localeChanged)
            {
                renderedCity = state.City;
                RenderCity(state.City);
            }
        }

        void RenderAdmin(AdminSnapshot snapshot)
        {
            cityList.Clear();
            eventList.Clear();
            if (snapshot == null)
            {
                adminSummary.text = ClientText.Get(store.State.Locale,"waiting_snapshot");
                return;
            }

            adminSummary.text = $"{snapshot.Cities.Count} {ClientText.Get(store.State.Locale,"cities")} · {snapshot.Entities.Count} entities · H3 {snapshot.Territory.Count}";
            foreach (var city in snapshot.Cities)
            {
                var captured = city;
                var button = new Button(() => SelectCity(captured))
                {
                    text = $"{city.Name}\nPop. {city.Population:N0} · Tier {city.Tier}",
                };
                button.AddToClassList("list-button");
                cityList.Add(button);
            }

            for (var index = snapshot.Events.Count - 1; index >= 0; index--)
            {
                var captured = snapshot.Events[index];
                var button = new Button(() => FocusEvent(captured))
                {
                    text = $"{FormatTime(captured.AtMs)} · {ClientText.Get(store.State.Locale,"event")} {captured.Kind}\n{(string.IsNullOrEmpty(captured.Region) ? $"Shard {captured.Shard}" : captured.Region)}",
                };
                button.AddToClassList("event-button");
                eventList.Add(button);
            }
        }

        void SelectCity(CityState city)
        {
            network.SelectCity(city.Id);
            if (city.Pos != null)
            {
                store.Focus(new GeoCoordinate(city.Pos.Lat, city.Pos.Lon), 12_000);
            }
        }

        void FocusEvent(AdminEvent adminEvent)
        {
            if (string.IsNullOrEmpty(adminEvent.CityId) || store.State.Admin == null) return;
            foreach (var city in store.State.Admin.Cities)
            {
                if (city.Id == adminEvent.CityId)
                {
                    SelectCity(city);
                    return;
                }
            }
        }

        void RenderCity(CitySnapshot city)
        {
            buildingList.Clear();
            if (city == null)
            {
                cityName.text = ClientText.Get(store.State.Locale,"city");
                cityDetails.text = ClientText.Get(store.State.Locale,"loading");
                return;
            }

            cityName.text = city.Name;
            string Text(string key)=>ClientText.Get(store.State.Locale,key);
            cityDetails.text = $"{city.Width}×{city.Height} · rev. {city.Revision}\n"
                + $"{Text("population")} {Population(city.Population):N0} · {Text("water")} {city.Metrics?.Water:P0}\n"
                + $"{Text("density")} {city.Metrics?.Density:P0} · {Text("defense")} {city.Metrics?.Defense:N1}\n"
                + $"{Text("food")} {city.Metrics?.FoodCapacity:N0} · {Text("housing")} {city.Metrics?.HousingCapacity:N0}\n"
                + $"{Text(city.Editable?"editable":"read_only")}";
            foreach (var building in city.Buildings)
            {
                buildingList.Add(new Label($"{Text(building.BuildingId)} · ({building.X}, {building.Y}) · {Text(building.Complete?"complete":"active")}"));
            }
        }

        static uint Population(PopulationState population)
        {
            return population == null ? 0 : population.Farmers + population.Artisans + population.Soldiers + population.Priests;
        }

        static string FormatAltitude(double meters)
        {
            return meters >= 1_000 ? $"Altitude: {meters / 1_000:N0} km" : $"Altitude: {meters:N0} m";
        }

        static string FormatTime(double milliseconds)
        {
            try { return DateTimeOffset.FromUnixTimeMilliseconds((long)milliseconds).ToLocalTime().ToString("HH:mm:ss"); }
            catch (ArgumentOutOfRangeException) { return "--:--:--"; }
        }

        public void Dispose()
        {
            styleButton.clicked -= ToggleStyle;
            localeButton.clicked -= ToggleLocale;
            loginButton.clicked -= Login;
            autoLoginToggle.UnregisterValueChangedCallback(OnAutoLoginToggleChanged);
            logoutButton.clicked -= network.Logout;
            applyFilters.clicked -= ApplyFilters;
            passwordInput.UnregisterCallback<KeyDownEvent>(OnPasswordKeyDown);
            root.UnregisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            store.Changed -= Render;
            HudInputGate.Reset();
        }
    }
}
