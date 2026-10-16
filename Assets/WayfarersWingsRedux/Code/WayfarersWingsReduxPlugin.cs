using System;
using System.IO;
using KSP.Messages;
using Redux.ExtraModTypes;
using KSP.Game;
using KSP.UI.Binding;
using Redux.UI.Settings;
using ReduxLib.Configuration;
using SpaceWarp2.UI.API.Appbar;
using UnityEngine;
using WayfarersWings.Models.Session;
using WayfarersWingsRedux.UI;
using WayfarersWings.Managers;
using WayfarersWings.Managers.Messages;
using WayfarersWings.Managers.Observer;
using WayfarersWings.Utility;

namespace WayfarersWingsRedux
{
    /// <summary>Static access to the mod's logger for classes that aren't the plugin.</summary>
    internal static class SWLog
    {
        internal static ReduxLib.Logging.ILogger Logger;
        internal static void Info(string text) => Logger?.LogInfo(text);
        internal static void Warning(string text) => Logger?.LogWarning(text);
        internal static void Error(string text) => Logger?.LogError(text);
    }

    /// <summary>
    /// Wayfarer's Wings Redux: kerbals earn ribbons for what they do, and the first to do it gets a special one.
    /// A port of Wayfarer's Wings by leonardfactory (https://github.com/Kerbalight/WayfarersWings, MIT). The award
    /// logic lives in the ported <c>WayfarersWings.*</c> namespaces.
    /// </summary>
    public class WayfarersWingsReduxPlugin : KerbalMod
    {
        public const string ModGuid = "WayfarersWingsRedux";
        internal const string AppbarButtonId = "BTN-WayfarersWingsRedux";
        internal const string OabAppbarButtonId = "BTN-WayfarersWingsReduxOAB";

        /// <summary>Lets the ported code's debug logging through; off by default because it is chatty.</summary>
        internal static bool DebugLogging;

        internal static WayfarersWingsReduxPlugin Instance { get; private set; }

        private const float LayoutCheckInterval = 0.5f;

        private Texture2D _icon;
        private WingsWindow _window;
        private MissionSummaryWindow _summary;
        private bool _open;
        /// <summary>Ribbons or profiles changed; the open window is rebuilt on the next frame, once per batch.</summary>
        private bool _dirty;
        private UitkSettingsMenuManager _settingsMenu;
        private float _nextSettingsMenuLookup;
        private ConfigValue<float> _windowX, _windowY, _windowWidth, _windowHeight;
        private float _nextLayoutCheck;

        public override void OnPreInitialized()
        {
            SWLog.Logger = SWLogger;
            Instance = this;
            _icon = LoadIcon();
        }

        public override void OnInitialized()
        {
            // Redux snapshots the loading-action list before any OnPreInitialized runs, so loading actions added
            // there never run. The mod's addressables catalog is loaded by now, so load the definitions directly.
            LoadDefinitions(Constants.WingsPlanetsAddressableLabel, Core.Instance.ImportPlanetsConfig);
            LoadDefinitions(Constants.WingsDataAddressableLabel, Core.Instance.ImportWingsConfig);

            Settings.SetupConfig(SWConfiguration);
            DebugLogging = new ConfigValue<bool>(SWConfiguration.Bind("Debug", "Detailed logging", false,
                "Log every wing check", null)).Value;

            MessageListener.Instance.SubscribeToMessages();
            SaveManager.Instance.Register();

            var observers = new GameObject("WayfarersWingsRedux_Observers");
            observers.transform.parent = transform;
            observers.AddComponent<VesselsStateObserver>();

            MessageListener.SessionLoaded += WingsApi.RaiseLoaded;
            Game.Messages.PersistentSubscribe<WingAwardedMessage>(message =>
            {
                if (message is WingAwardedMessage awarded) WingsApi.RaiseAwarded(awarded);
            });
            Game.Messages.PersistentSubscribe<WingRevokedMessage>(message =>
            {
                if (message is WingRevokedMessage revoked) WingsApi.RaiseRevoked(revoked);
            });

            _windowX = new ConfigValue<float>(SWConfiguration.Bind("Window", "X", -1f, "Window position; -1 centers it", null));
            _windowY = new ConfigValue<float>(SWConfiguration.Bind("Window", "Y", -1f, "Window position; -1 centers it", null));
            _windowWidth = new ConfigValue<float>(SWConfiguration.Bind("Window", "Width", -1f, "Window size; -1 uses the default", null));
            _windowHeight = new ConfigValue<float>(SWConfiguration.Bind("Window", "Height", -1f, "Window size; -1 uses the default", null));

            _window = new WingsWindow(_icon, new Rect(_windowX.Value, _windowY.Value, _windowWidth.Value, _windowHeight.Value));
            _window.CloseClicked += () => SetOpen(false);
            _window.LayoutChanged += OnWindowLayoutChanged;
            _window.Visible = false;
            _summary = new MissionSummaryWindow(_icon);
            _summary.KerbalClicked += id =>
            {
                SetOpen(true);
                _window.SelectKerbal(id);
            };

            string name = Loc.T("UI/Achievements");
            Appbar.RegisterAppButton(name, AppbarButtonId, _icon, SetOpen);
            Appbar.RegisterOABAppButton(name, OabAppbarButtonId, _icon, SetOpen);
            Appbar.RegisterKSCAppButton(name, AppbarButtonId + "KSC", _icon, () => SetOpen(!_open));

            MessageListener.SessionLoaded += () =>
            {
                _window.Reset();
                _summary.Visible = false;
                _dirty = true;
            };
            Settings.Changed += () => _dirty = true;
            Game.Messages.PersistentSubscribe<WingAwardedMessage>(_ => _dirty = true);
            Game.Messages.PersistentSubscribe<WingRevokedMessage>(_ => _dirty = true);
            Game.Messages.PersistentSubscribe<WingKerbalProfileUpdatedMessage>(_ => _dirty = true);
            Game.Messages.PersistentSubscribe<WingMissionCompletedMessage>(OnMissionCompleted);
        }

        private void Update()
        {
            if (_window == null) return;
            bool inCampaign = InCampaign();
            // Mod windows draw over the game's own dialogs (the quit confirmation, for one), so step aside while the
            // pause or settings menu is up, as the game's own windows do.
            bool menusOpen = inCampaign && GameMenusOpen();
            _window.Visible = _open && inCampaign;
            _window.Suspended = menusOpen;
            if (!inCampaign) _summary.Visible = false;
            _summary.Suspended = menusOpen;

            if (_dirty && _window.Visible)
            {
                _dirty = false;
                _window.Refresh();
            }
            if (_window.Visible && Time.unscaledTime >= _nextLayoutCheck)
            {
                _nextLayoutCheck = Time.unscaledTime + LayoutCheckInterval;
                _window.CheckLayout();
            }
        }

        /// <summary>
        /// A crewed vessel was recovered and its ribbons are settled. (The recovery message itself comes before the
        /// last ribbons are awarded.)
        /// </summary>
        private void OnMissionCompleted(MessageCenterMessage message)
        {
            if (!Settings.ShowFlightReportSummary.Value) return;
            var vessel = (message as WingMissionCompletedMessage)?.Vessel;
            if (vessel == null || vessel.GlobalId != Core.ActiveVessel?.GlobalId) return;
            var crew = WingsSessionManager.Roster?.GetAllKerbalsInVessel(vessel.GlobalId);
            if (crew != null && crew.Count > 0) _summary.Show(crew);
        }

        /// <summary>Opens the window on a kerbal's record.</summary>
        internal bool ShowKerbal(KSP.Sim.impl.IGGuid kerbalId)
        {
            if (_window == null) return false;
            SetOpen(true);
            _window.SelectKerbal(kerbalId);
            return true;
        }

        private void SetOpen(bool open)
        {
            _open = open;
            if (open) _dirty = true;
            // Keep the app bar buttons in step when the window is closed from its own X button.
            foreach (var id in new[] { AppbarButtonId, OabAppbarButtonId })
                GameObject.Find(id)?.GetComponent<UIValue_WriteBool_Toggle>()?.SetValue(open);
        }

        private void OnWindowLayoutChanged(Rect layout)
        {
            _windowX.Value = layout.x;
            _windowY.Value = layout.y;
            _windowWidth.Value = layout.width;
            _windowHeight.Value = layout.height;
            SWConfiguration.Save();
        }

        private bool InCampaign()
        {
            if (Game?.SessionManager == null || Game.GlobalGameState == null) return false;
            var state = Game.GlobalGameState.GetGameState().GameState;
            return state != GameState.Invalid && state != GameState.WarmUpLoading && state != GameState.MainMenu
                && state != GameState.Loading && state != GameState.Flag;
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

        internal Texture2D Icon => _icon;

        private void LoadDefinitions(string label, Action<TextAsset> import)
        {
            int count = 0;
            GameManager.Instance.Assets.LoadByLabel<TextAsset>(label, asset =>
            {
                count++;
                try { import(asset); }
                catch (Exception e) { SWLogger.LogError($"Couldn't import {asset?.name}: {e}"); }
            }, _ =>
            {
                if (count == 0) SWLogger.LogError($"No ribbon definitions found under the addressables label {label}");
            }, true);
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
