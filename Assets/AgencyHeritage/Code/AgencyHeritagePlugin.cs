using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using KSP.Game;
using KSP.UI;
using KSP.Messages;
using KSP.Sim;
using KSP.UI.Binding;
using Redux.ExtraModTypes;
using ReduxLib.Configuration;
using SpaceWarp2.API.Backend.SaveGameManager;
using SpaceWarp2.API.SaveGameManager;
using SpaceWarp2.UI.API.Appbar;
using UnityEngine;

namespace AgencyHeritage
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
    /// Agency Heritage: rebrand your agency (name, flag, colors) as a recorded event, and look back over its
    /// brand eras and the blueprints saved in each.
    /// </summary>
    public class AgencyHeritagePlugin : KerbalMod
    {
        public const string ModGuid = "AgencyHeritage";
        public const string ModName = "Agency Heritage";
        internal const string AppbarButtonId = "BTN-AgencyHeritage";
        internal const string OabAppbarButtonId = "BTN-AgencyHeritageOAB";

        private const string LaunchEvent = "vesselLaunched";
        private const double SameLaunchSeconds = 5;

        private Texture2D _icon;
        private HeritageSaveData _data;
        private HeritageWindowUI _ui;
        private bool _open;
        private bool _importPending;
        private ConfigValue<bool> _paintNewPartsInAgencyColors;

        public override void OnPreInitialized()
        {
            SWLog.Logger = SWLogger;
            _icon = LoadIcon();
        }

        public override void OnInitialized()
        {
            _paintNewPartsInAgencyColors = new ConfigValue<bool>(SWConfiguration.Bind("VAB", "Start in agency colors", true,
                "Each time you enter the VAB, set the paint for new parts to the agency colors", null));

            _data = ModSaves.RegisterSaveLoadGameData<HeritageSaveData>(
                ModGuid,
                onLoad: loaded => { _data = loaded; _importPending = true; _ui?.Refresh(); },
                onNew: fresh =>
                {
                    // A save without history: the founding era is created once the agency is available.
                    fresh.Eras.Clear();
                    fresh.Flights.Clear();
                    fresh.Losses.Clear();
                    _importPending = true;
                    return fresh;
                },
                persistenceKind: PersistenceKind.PerSave);

            _ui = new HeritageWindowUI(_icon, this);
            _ui.CloseClicked += () => SetOpen(false);
            _ui.Visible = false;

            Appbar.RegisterAppButton(ModName, AppbarButtonId, _icon, SetOpen);
            Appbar.RegisterOABAppButton(ModName, OabAppbarButtonId, _icon, SetOpen);
            Appbar.RegisterKSCAppButton(ModName, AppbarButtonId + "KSC", _icon, () => SetOpen(!_open));

            Game.Messages.PersistentSubscribe<VesselLaunchedMessage>(OnVesselLaunched);
            Game.Messages.PersistentSubscribe<KerbalRemovingFromRoster>(OnKerbalRemoving);
            Game.Messages.PersistentSubscribe<GameStateChangedMessage>(OnGameStateChanged);
        }

        private void OnGameStateChanged(MessageCenterMessage message)
        {
            if (message is GameStateChangedMessage changed && changed.CurrentState == GameState.VehicleAssemblyBuilder
                && _paintNewPartsInAgencyColors.Value)
                PaintNewPartsInAgencyColors();
        }

        /// <summary>New parts copy the player's paint brush, not the agency colors, so point the brush at them.</summary>
        private void PaintNewPartsInAgencyColors()
        {
            var session = Session;
            if (session == null) return;
            session.SetMyPlayerPartColorBase(session.GetMyAgencyColorBase());
            session.SetMyPlayerPartColorAccent(session.GetMyAgencyColorAccent());
        }

        private void OnVesselLaunched(MessageCenterMessage message)
        {
            if (_data == null || !(message is VesselLaunchedMessage launched)) return;
            try
            {
                var vessel = launched.Vessel;
                var record = new FlightRecord { UT = UniverseTime, VesselName = vessel?.Name };
                var roster = Session?.KerbalRosterManager;
                if (vessel != null && roster != null)
                    foreach (var kerbal in roster.GetAllKerbalsInVessel(vessel.GlobalId))
                        record.Crew.Add(kerbal.NameKey);
                // This record has the names; replace any copy taken from the travel log a moment ago.
                _data.Flights.RemoveAll(f => Math.Abs(f.UT - record.UT) < SameLaunchSeconds);
                _data.Flights.Add(record);
            }
            catch (Exception e)
            {
                SWLogger.LogWarning($"Couldn't record a launch: {e.Message}");
            }
        }

        /// <summary>
        /// The game only removes a kerbal from the roster when they die (recovery moves the crew out first), and it
        /// keeps nothing of them afterwards. This is the last moment to record who they were, where, and their face.
        /// </summary>
        private void OnKerbalRemoving(MessageCenterMessage message)
        {
            if (_data == null || !(message is KerbalRemovingFromRoster removing) || removing.Kerbal == null) return;
            try
            {
                var kerbal = removing.Kerbal;
                var record = new LossRecord { Name = kerbal.NameKey, UT = UniverseTime };
                try { record.VesselName = Game?.UniverseModel?.FindVesselComponent(kerbal.Location.SimObjectId)?.Name; }
                catch (Exception) { /* the vessel may already be gone */ }
                record.PortraitFile = SavePortrait(kerbal, record.UT);
                _data.Losses.Add(record);
                SWLogger.LogInfo($"Recorded the loss of {record.Name}");
            }
            catch (Exception e)
            {
                SWLogger.LogWarning($"Couldn't record a lost kerbal: {e.Message}");
            }
        }

        internal string PortraitFolder =>
            CampaignFolder == null ? null : Path.Combine(CampaignFolder, "AgencyHeritage", "portraits");

        /// <summary>Copies the kerbal's portrait to a PNG; the game's copy goes with the kerbal.</summary>
        private string SavePortrait(KerbalInfo kerbal, double ut)
        {
            Sprite portrait;
            try { portrait = kerbal.Portrait; }
            catch (Exception) { return null; }
            if (portrait == null || portrait.texture == null || PortraitFolder == null) return null;

            // The portrait texture may not be CPU-readable, so read it back through a render texture.
            var rect = portrait.textureRect;
            var source = portrait.texture;
            var target = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active;
            var copy = new Texture2D((int)rect.width, (int)rect.height, TextureFormat.RGBA32, false);
            try
            {
                Graphics.Blit(source, target);
                RenderTexture.active = target;
                copy.ReadPixels(new Rect(rect.x, rect.y, rect.width, rect.height), 0, 0);
                copy.Apply();
                Directory.CreateDirectory(PortraitFolder);
                string file = $"{SafeFileName(kerbal.NameKey)}-{Math.Floor(ut):0}.png";
                File.WriteAllBytes(Path.Combine(PortraitFolder, file), copy.EncodeToPNG());
                return file;
            }
            catch (Exception e)
            {
                SWLogger.LogWarning($"Couldn't save the portrait of {kerbal.NameKey}: {e.Message}");
                return null;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
                Destroy(copy);
            }
        }

        private static string SafeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name;
        }

        /// <summary>
        /// Copies launches from the game's travel log into the flight records. The travel log drops a launch once its
        /// vessel is recovered or destroyed, so this keeps launches from before the mod was installed, or from before
        /// the window was opened, in the museum for good.
        /// </summary>
        internal void ImportTravelLogLaunches()
        {
            if (_data == null) return;
            var log = TravelLogSnapshot();
            if (log?.ObjectEvents == null) return;
            bool added = false;
            foreach (var launch in log.ObjectEvents)
            {
                if (launch == null || launch.EventKey != LaunchEvent) continue;
                // The same launch may already be recorded from VesselLaunchedMessage, a moment apart.
                if (_data.Flights.Exists(f => Math.Abs(f.UT - launch.UT) < SameLaunchSeconds)) continue;

                var record = new FlightRecord { UT = launch.UT };
                var roster = Session?.KerbalRosterManager;
                foreach (var id in launch.TravelObjectIds ?? new List<Guid>())
                {
                    var guid = default(KSP.Sim.impl.IGGuid);
                    guid.SetGuid(id);
                    var vessel = Game?.UniverseModel?.FindVesselComponent(guid);
                    if (vessel == null) continue;
                    record.VesselName ??= vessel.Name;
                    if (roster != null)
                        foreach (var kerbal in roster.GetAllKerbalsInVessel(guid))
                            if (!record.Crew.Contains(kerbal.NameKey)) record.Crew.Add(kerbal.NameKey);
                }
                _data.Flights.Add(record);
                added = true;
            }
            if (added) _data.Flights.Sort((a, b) => a.UT.CompareTo(b.UT));
        }

        internal string HomeWorldName
        {
            get
            {
                try { return Game?.UniverseModel?.HomeWorld?.Name ?? "Kerbin"; }
                catch (Exception) { return "Kerbin"; }
            }
        }

        /// <summary>A copy of the game's travel log: launches and firsts with their dates.</summary>
        internal SerializedTravelLog TravelLogSnapshot()
        {
            try
            {
                var log = new SerializedTravelLog();
                Game?.TravelLogManager?.Save(ref log);
                return log;
            }
            catch (Exception e)
            {
                SWLogger.LogWarning($"Couldn't read the travel log: {e.Message}");
                return null;
            }
        }

        internal List<KerbalInfo> Roster()
        {
            try { return Session?.KerbalRosterManager?.GetAllKerbals(); }
            catch (Exception) { return null; }
        }

        private void Update()
        {
            if (_ui == null) return;

            bool inCampaign = InCampaign();
            if (inCampaign)
            {
                EnsureFounded();
                if (_importPending)
                {
                    _importPending = false;
                    ImportTravelLogLaunches();
                }
            }
            _ui.Visible = _open && inCampaign;
        }

        internal HeritageSaveData Data => _data;

        internal string CampaignFolder => Game?.SaveLoadManager?.ActiveCampaignFolderPath;

        internal string WorkspaceFolder
        {
            get
            {
                try { return Game?.SaveLoadManager?.GetActiveCampaignWorkspaceFolderPath(); }
                catch (Exception) { return null; }
            }
        }

        internal double UniverseTime => Game?.UniverseModel?.UniverseTime ?? 0;

        internal SessionManager Session => Game?.SessionManager;

        private bool InCampaign()
        {
            if (Game?.SessionManager == null || Game.GlobalGameState == null) return false;
            var state = Game.GlobalGameState.GetGameState().GameState;
            return state != GameState.Invalid && state != GameState.WarmUpLoading && state != GameState.MainMenu
                && state != GameState.Loading;
        }

        /// <summary>A save with no history gets a founding era from the agency as it is now.</summary>
        private void EnsureFounded()
        {
            if (_data == null || _data.Eras.Count > 0) return;
            var session = Game.SessionManager;
            string name = session.GetMyAgencyName();
            if (string.IsNullOrEmpty(name)) return;

            _data.Eras.Add(new BrandEra
            {
                Name = name,
                Base = session.GetMyAgencyColorBase(),
                Accent = session.GetMyAgencyColorAccent(),
                FlagSpriteLocation = session.GetMyAgencyFlagSpriteLocation(),
                StartUT = 0,
                // Every blueprint saved before the mod was installed belongs to the founding era.
                StartRealTime = DateTime.MinValue,
            });
            SWLogger.LogInfo($"Recorded the founding era of {name}");
            _ui?.Refresh();
        }

        /// <summary>
        /// Applies a rebrand: archives the outgoing era's blueprint thumbnails, changes the agency, and records the
        /// new era. Returns an error message, or null on success.
        /// </summary>
        internal string Rebrand(string name, string flagSpriteLocation, Color colorBase, Color colorAccent, string note)
        {
            name = name?.Trim();
            if (string.IsNullOrEmpty(name)) return "The agency needs a name.";
            if (_data == null || Session == null) return "No campaign is loaded.";
            EnsureFounded();

            var outgoing = _data.CurrentEra();
            if (outgoing != null && !outgoing.ThumbnailsArchived)
            {
                try
                {
                    BlueprintArchive.ArchiveEra(_data, outgoing, CampaignFolder, WorkspaceFolder);
                }
                catch (Exception e)
                {
                    // Losing the archive shouldn't block the rebrand; the gallery falls back to live workspaces.
                    SWLogger.LogError($"Couldn't archive the blueprints of {outgoing.Name}: {e}");
                }
            }

            colorBase.a = colorAccent.a = 1f;
            Session.SetMyAgencyName(name);
            Session.SetMyAgencyColorBase(colorBase);
            Session.SetMyAgencyColorAccent(colorAccent);
            if (!string.IsNullOrEmpty(flagSpriteLocation) && flagSpriteLocation != Session.GetMyAgencyFlagSpriteLocation())
                Session.SetMyAgencyFlagSpriteLocation(flagSpriteLocation);
            // So the next part placed in the VAB is already in the new livery.
            PaintNewPartsInAgencyColors();

            _data.Eras.Add(new BrandEra
            {
                Name = name,
                Base = colorBase,
                Accent = colorAccent,
                FlagSpriteLocation = Session.GetMyAgencyFlagSpriteLocation(),
                StartUT = UniverseTime,
                StartRealTime = DateTime.Now,
                Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            });

            RefreshAgencyNameDisplays(name);
            SWLogger.LogInfo($"Rebranded {outgoing?.Name} as {name}");
            return null;
        }

        /// <summary>
        /// Nothing announces an agency change, and the KSC menu only reads the name when it starts, so update its
        /// bound property ourselves. The ESC menu re-reads the name each time it opens.
        /// </summary>
        private void RefreshAgencyNameDisplays(string name)
        {
            try
            {
                var ksc = FindAnyObjectByType<KSCMenuManager>();
                if (ksc == null) return;
                var field = typeof(KSCMenuManager).GetField("_agencyName", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                var property = field?.GetValue(ksc);
                property?.GetType().GetMethod("SetValue", new[] { typeof(string) })?.Invoke(property, new object[] { name });
            }
            catch (Exception e)
            {
                SWLogger.LogWarning($"Couldn't refresh the KSC agency name: {e.Message}");
            }
        }

        private void SetOpen(bool open)
        {
            _open = open;
            if (open) _ui.Refresh();
            // Keep the app bar buttons in step when the window is closed from its own X button.
            foreach (var id in new[] { AppbarButtonId, OabAppbarButtonId })
                GameObject.Find(id)?.GetComponent<UIValue_WriteBool_Toggle>()?.SetValue(open);
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
