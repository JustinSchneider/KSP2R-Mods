using System;
using System.IO;
using System.Linq;
using FlightPlanRedux.Nodes;
using FlightPlanRedux.UI;
using KSP.Game;
using KSP.Sim.impl;
using KSP.UI;
using KSP.UI.Binding;
using Redux.ExtraModTypes;
using Redux.UI.Settings;
using SpaceWarp2.API.Configuration;
using SpaceWarp2.API.Input;
using SpaceWarp2.API.Backend.SaveGameManager;
using SpaceWarp2.API.SaveGameManager;
using SpaceWarp2.UI.API.Appbar;
using UnityEngine;

namespace FlightPlanRedux
{
    /// <summary>Static access to the mod's logger for classes that aren't the plugin.</summary>
    internal static class SWLog
    {
        internal static SpaceWarp2.API.Logging.ILogger Logger;
        internal static void Info(string text) => Logger?.LogInfo(text);
        internal static void Warning(string text) => Logger?.LogWarning(text);
        internal static void Error(string text) => Logger?.LogError(text);
    }

    /// <summary>
    /// Flight Plan Redux: plan your flight, fly your plan. A port of Flight Plan by schlosrat
    /// (https://github.com/schlosrat/FlightPlan, GPL-3.0), with Node Manager's helpers absorbed.
    /// </summary>
    public class FlightPlanReduxPlugin : KerbalMod, IFlightPlanHost
    {
        public const string ModGuid = "FlightPlanRedux";
        internal const string AppbarButtonId = "BTN-FlightPlanRedux";

        private const float RefreshInterval = 0.25f;
        private const float LayoutCheckInterval = 0.5f;
        private const int MaxSavedVessels = 64;

        /// <summary>Lets the ported MechJeb code's debug logging through; off by default because it is chatty.</summary>
        internal static bool DebugLogging;

        internal static FlightPlanReduxPlugin Instance { get; private set; }

        private Texture2D _icon;
        private FlightPlanWindow _window;
        private NodeService _nodes;
        private FlightPlanSaveData _data;
        private bool _open;
        private bool _ready;
        private float _nextRefresh, _nextLayoutCheck, _errorUntil;
        private UitkSettingsMenuManager _settingsMenu;
        private float _nextSettingsMenuLookup;

        private ConfigValue<KeyboardShortcut> _toggleKey;
        private ConfigValue<bool> _experimental;
        private ConfigValue<float> _smallError, _largeError;
        private ConfigValue<float> _windowX, _windowY, _windowWidth, _windowHeight;

        public override void OnPreInitialized()
        {
            Instance = this;
            SWLog.Logger = SWLogger;
            Icons.Folder = Path.Combine(SWMetadata.Folder.FullName, "assets", "images");
            _icon = LoadIcon();
        }

        public override void OnInitialized()
        {
            _toggleKey = new ConfigValue<KeyboardShortcut>(SWConfiguration.Bind("Keybinds", "Toggle window",
                new KeyboardShortcut(KeyCode.P, new[] { KeyCode.LeftAlt }), "Opens and closes the Flight Plan window", null));
            _experimental = new ConfigValue<bool>(SWConfiguration.Bind("Planning", "Experimental features", false,
                "Offers maneuvers that are known to be unreliable (intercept at a set time)", null));
            _smallError = new ConfigValue<float>(SWConfiguration.Bind("Planning", "Small error (%)", 1f,
                "A new node whose result differs from the plan by more than this gets a warning", null));
            _largeError = new ConfigValue<float>(SWConfiguration.Bind("Planning", "Large error (%)", 2f,
                "A new node whose result differs from the plan by more than this gets an error", null));
            DebugLogging = new ConfigValue<bool>(SWConfiguration.Bind("Debug", "Detailed logging", false,
                "Log the planning calculations", null)).Value;
            _windowX = new ConfigValue<float>(SWConfiguration.Bind("Window", "X", -1f, "Window position; -1 uses the default", null));
            _windowY = new ConfigValue<float>(SWConfiguration.Bind("Window", "Y", -1f, "Window position; -1 uses the default", null));
            _windowWidth = new ConfigValue<float>(SWConfiguration.Bind("Window", "Width", -1f, "Window size; -1 uses the default", null));
            _windowHeight = new ConfigValue<float>(SWConfiguration.Bind("Window", "Height", -1f, "Window size; -1 uses the default", null));

            _data = ModSaves.RegisterSaveLoadGameData<FlightPlanSaveData>(
                ModGuid,
                onLoad: loaded => _data = loaded,
                onNew: fresh =>
                {
                    fresh.Vessels.Clear();
                    return fresh;
                },
                persistenceKind: PersistenceKind.PerSave);

            _nodes = new NodeService(this);
            _nodes.NodesChanged += FlightPlanApi.RaiseNodesChanged;

            _window = new FlightPlanWindow(_icon, new Rect(_windowX.Value, _windowY.Value, _windowWidth.Value, _windowHeight.Value), this);
            _window.CloseClicked += () => SetOpen(false);
            _window.LayoutChanged += OnWindowLayoutChanged;
            _window.Visible = false;

            Appbar.RegisterAppButton(Loc.T("Name"), AppbarButtonId, _icon, SetOpen);

            FlightPlanApi.Plugin = this;
            _ready = true;
        }

        private void Update()
        {
            if (!_ready) return;
            bool inFlight = IsFlightScene();
            if (inFlight && _toggleKey.Value.Down && !_window.TypingInField) SetOpen(!_open);

            bool menusOpen = inFlight && GameMenusOpen();
            _window.Visible = _open && inFlight;
            _window.Suspended = menusOpen;
            if (!_window.Visible || menusOpen) return;

            if (Time.unscaledTime >= _nextRefresh && Time.unscaledTime >= _errorUntil)
            {
                _nextRefresh = Time.unscaledTime + RefreshInterval;
                try
                {
                    _window.Refresh();
                }
                catch (Exception e)
                {
                    // Don't flood the log every frame if something in the game state trips the refresh.
                    SWLogger.LogError($"Refresh failed: {e}");
                    _errorUntil = Time.unscaledTime + 5;
                }
            }
            if (Time.unscaledTime >= _nextLayoutCheck)
            {
                _nextLayoutCheck = Time.unscaledTime + LayoutCheckInterval;
                _window.CheckLayout();
            }
        }

        internal void SetOpen(bool open)
        {
            _open = open;
            _nextRefresh = 0;
            // Keep the app bar button in step when the window is closed from its own X button or the keybind.
            GameObject.Find(AppbarButtonId)?.GetComponent<UIValue_WriteBool_Toggle>()?.SetValue(open);
        }

        internal bool IsOpen => _open;

        /// <summary>Opens the window on a tab; for the API.</summary>
        internal void OpenTab(Tab tab)
        {
            SetOpen(true);
            _window.Show(tab);
        }

        private void OnWindowLayoutChanged(Rect layout)
        {
            _windowX.Value = layout.x;
            _windowY.Value = layout.y;
            _windowWidth.Value = layout.width;
            _windowHeight.Value = layout.height;
            SWConfiguration.Save();
        }

        // ---------------------------------------------------------------- IFlightPlanHost

        public VesselComponent Vessel => Game?.ViewController?.GetActiveSimVessel(true);
        public double UT => Game?.UniverseModel?.UniverseTime ?? 0;
        internal NodeService Nodes => _nodes;
        NodeService IFlightPlanHost.Nodes => _nodes;
        public bool Experimental => _experimental.Value;
        public double SmallErrorPercent => _smallError.Value;
        public double LargeErrorPercent => Math.Max(_smallError.Value, _largeError.Value);

        public VesselPlanState State
        {
            get
            {
                var vessel = Vessel;
                if (vessel == null || _data == null) return null;
                string id = vessel.GlobalId.ToString();
                var state = _data.For(id);
                // Keep the save small: forget the vessels used longest ago.
                if (_data.Vessels.Count > MaxSavedVessels)
                {
                    _data.Vessels.Remove(state);
                    _data.Vessels.RemoveRange(0, _data.Vessels.Count - MaxSavedVessels + 1);
                    _data.Vessels.Add(state);
                }
                return state;
            }
        }

        public void SetTarget(IGGuid id)
        {
            var vessel = Vessel;
            if (vessel == null || id == null) return;
            vessel.SetTargetByID(id);
            _nextRefresh = 0;
        }

        public void WarpTo(double ut)
        {
            var timeWarp = Game?.ViewController?.TimeWarp;
            if (timeWarp == null) return;
            if (timeWarp.IsWarping)
            {
                timeWarp.StopTimeWarp(false);
                return;
            }
            if (ut > UT + 1) timeWarp.WarpTo(ut);
        }

        /// <summary>Targets a celestial body by its internal name; for the API.</summary>
        internal bool TargetBody(string bodyName)
        {
            var body = Game?.UniverseModel?.FindCelestialBodyByName(bodyName);
            var vessel = Vessel;
            if (body == null || vessel == null) return false;
            vessel.SetTargetByID(body.GlobalId);
            _nextRefresh = 0;
            return true;
        }

        // ---------------------------------------------------------------- helpers

        /// <summary>In flight or map view, in a campaign that plays with the mod.</summary>
        private bool IsFlightScene()
        {
            if (!Enabled) return false;
            var state = Game?.GlobalGameState?.GetGameState();
            if (state == null) return false;
            return state.GameState == GameState.FlightView || state.GameState == GameState.Map3DView;
        }

        private bool GameMenusOpen()
        {
            try
            {
                if (Game?.UI != null && Game.UI.IsEscapeVisible()) return true;
                // Looked up at most once a second: this runs every frame, and the menu doesn't exist in every scene.
                if (_settingsMenu == null && Time.unscaledTime >= _nextSettingsMenuLookup)
                {
                    _nextSettingsMenuLookup = Time.unscaledTime + 1f;
                    _settingsMenu = FindAnyObjectByType<UitkSettingsMenuManager>();
                }
                return _settingsMenu != null && _settingsMenu.IsVisible;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private Texture2D LoadIcon()
        {
            var texture = new Texture2D(2, 2);
            try
            {
                string path = Path.Combine(SWMetadata.Folder.FullName, "assets", "images", "icon.png");
                if (File.Exists(path)) texture.LoadImage(File.ReadAllBytes(path));
            }
            catch (Exception e)
            {
                SWLogger.LogWarning($"Couldn't load the app bar icon: {e.Message}");
            }
            return texture;
        }
    }
}
