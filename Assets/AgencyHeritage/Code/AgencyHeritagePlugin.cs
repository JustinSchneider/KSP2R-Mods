using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using KSP.Game;
using KSP.UI;
using KSP.Messages;
using KSP.Sim;
using KSP.Sim.impl;
using KSP.UI.Binding;
using Redux.ExtraModTypes;
using Redux.UI.Settings;
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
        internal const string AppbarButtonId = "BTN-AgencyHeritage";
        internal const string OabAppbarButtonId = "BTN-AgencyHeritageOAB";

        private const string LaunchEvent = "vesselLaunched";
        private const double SameLaunchSeconds = 5;
        private const float LayoutCheckInterval = 0.5f;

        private Texture2D _icon;
        private HeritageSaveData _data;
        private HeritageWindowUI _ui;
        private bool _open;
        private bool _importPending;
        /// <summary>The history other mods were last told about through <see cref="HeritageApi.Loaded"/>.</summary>
        private HeritageSaveData _announced;
        /// <summary>
        /// False from the moment a save starts loading until the game says it has finished. Until then the travel log
        /// may still hold the previous campaign's launches.
        /// </summary>
        private bool _travelLogReady;
        /// <summary>A rebrand asked for from behind the pause menu, opened once the player is back in the game.</summary>
        private (string Flag, Color? Base, Color? Accent)? _pendingRebrand;
        private UitkSettingsMenuManager _settingsMenu;
        private float _nextSettingsMenuLookup;
        private ConfigValue<bool> _paintNewPartsInAgencyColors;
        private ConfigValue<bool> _redirectStockIdentity;
        private ConfigValue<float> _windowX, _windowY, _windowWidth, _windowHeight;
        private float _nextLayoutCheck;

        public override void OnPreInitialized()
        {
            SWLog.Logger = SWLogger;
            _icon = LoadIcon();
        }

        public override void OnInitialized()
        {
            _paintNewPartsInAgencyColors = new ConfigValue<bool>(SWConfiguration.Bind("VAB", "Start in agency colors", true,
                "New VAB parts use agency colors", null));
            _redirectStockIdentity = new ConfigValue<bool>(SWConfiguration.Bind("Compatibility", "Redirect stock flag and color changes", true,
                "The game's agency flag setting and the VAB's agency colors button open the Rebrand page. Turn off if another mod manages the agency's identity.", null));
            _windowX = new ConfigValue<float>(SWConfiguration.Bind("Window", "X", -1f, "Window position; -1 centers it", null));
            _windowY = new ConfigValue<float>(SWConfiguration.Bind("Window", "Y", -1f, "Window position; -1 centers it", null));
            _windowWidth = new ConfigValue<float>(SWConfiguration.Bind("Window", "Width", -1f, "Window size; -1 uses the default", null));
            _windowHeight = new ConfigValue<float>(SWConfiguration.Bind("Window", "Height", -1f, "Window size; -1 uses the default", null));

            _data = ModSaves.RegisterSaveLoadGameData<HeritageSaveData>(
                ModGuid,
                onLoad: loaded => { _data = loaded; _travelLogReady = false; _importPending = true; _ui?.Refresh(); },
                onNew: fresh =>
                {
                    // A save without history: the founding era is created once the agency is available.
                    fresh.Eras.Clear();
                    fresh.Flights.Clear();
                    fresh.Losses.Clear();
                    _travelLogReady = false;
                    _importPending = true;
                    return fresh;
                },
                persistenceKind: PersistenceKind.PerSave);

            _ui = new HeritageWindowUI(_icon, this, new Rect(_windowX.Value, _windowY.Value, _windowWidth.Value, _windowHeight.Value));
            _ui.CloseClicked += () => SetOpen(false);
            _ui.LayoutChanged += OnWindowLayoutChanged;
            _ui.Visible = false;

            string name = Loc.T("Name");
            Appbar.RegisterAppButton(name, AppbarButtonId, _icon, SetOpen);
            Appbar.RegisterOABAppButton(name, OabAppbarButtonId, _icon, SetOpen);
            Appbar.RegisterKSCAppButton(name, AppbarButtonId + "KSC", _icon, () => SetOpen(!_open));

            Game.Messages.PersistentSubscribe<VesselLaunchedMessage>(OnVesselLaunched);
            Game.Messages.PersistentSubscribe<VesselSituationChangedMessage>(OnSituationChanged);
            Game.Messages.PersistentSubscribe<SOIEnteredMessage>(OnSoiEntered);
            Game.Messages.PersistentSubscribe<VesselSplitMessage>(OnVesselSplit);
            Game.Messages.PersistentSubscribe<VesselUndockedMessage>(OnVesselUndocked);
            Game.Messages.PersistentSubscribe<VesselDockedMessage>(OnVesselDocked);
            Game.Messages.PersistentSubscribe<VesselRecoveredMessage>(OnVesselRecovered);
            Game.Messages.PersistentSubscribe<VesselDestroyedMessage>(OnVesselDestroyed);
            Game.Messages.PersistentSubscribe<KerbalRemovingFromRoster>(OnKerbalRemoving);
            Game.Messages.PersistentSubscribe<VesselSavedMessage>(OnBlueprintSaved);
            Game.Messages.PersistentSubscribe<GameStateChangedMessage>(OnGameStateChanged);
            Game.Messages.PersistentSubscribe<GameLoadFinishedMessage>(_ => _travelLogReady = true);

            BlueprintArchive.PartCrewCapacity = name =>
            {
                var parts = Game?.Parts;
                if (parts == null || !parts.TryGet(name, out KSP.Sim.Definitions.PartCore core) || core?.data == null) return null;
                return core.data.crewCapacity;
            };
            StockIdentityPatches.Plugin = this;
            HeritageApi.Plugin = this;
            try
            {
                CreateHarmonyAndPatchAll();
            }
            catch (Exception e)
            {
                // Without the patches the stock flag and color controls simply work as they always have.
                SWLogger.LogError($"Couldn't patch the stock agency controls: {e}");
            }
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
                if (!IsNewMission(vessel)) return;
                var record = new FlightRecord { UT = UniverseTime, VesselName = vessel?.Name };
                var roster = Session?.KerbalRosterManager;
                if (vessel != null && roster != null)
                    foreach (var kerbal in roster.GetAllKerbalsInVessel(vessel.GlobalId))
                        record.Crew.Add(kerbal.NameKey);
                if (vessel != null)
                {
                    Follow(record, vessel.GlobalId);
                    try
                    {
                        record.Parts = vessel.SimulationObject?.PartOwner?.PartCount ?? 0;
                        record.Mass = vessel.totalMass;
                    }
                    catch (Exception) { /* the patch falls back to a mid-sized rocket */ }
                }
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
        /// The game "launches" every vessel the first time it leaves pre-launch, including a stage that has just been
        /// cut loose. A mission is a vessel with a pod or probe core aboard that isn't already part of a flight (a
        /// separated lander carries its flight on).
        /// </summary>
        private bool IsNewMission(VesselComponent vessel) => vessel == null || (CarriesFlight(vessel) && FlightOf(vessel) == null);

        // ─── Blueprint performance ───

        /// <summary>
        /// A blueprint file keeps its parts but not how it performs, so when one is saved, note the VAB's own Δv, TWR
        /// and burn time for it. The newest workspace .meta is the one just written.
        /// </summary>
        private void OnBlueprintSaved(MessageCenterMessage message)
        {
            if (_data == null) return;
            try
            {
                var assembly = Game?.OAB?.Current?.Stats?.MainAssembly as KSP.OAB.ObjectAssembly;
                var deltaV = assembly?.VesselDeltaV;
                if (deltaV == null) return;
                // The first stage to fire is the highest-numbered one with engines in it.
                var first = deltaV.StageInfo?.Where(st => st.EnginesActiveInStage != null && st.EnginesActiveInStage.Count > 0)
                    .OrderByDescending(st => st.Stage).FirstOrDefault();
                _pendingBlueprint = new BlueprintStats
                {
                    DeltaVVac = deltaV.TotalDeltaVVac,
                    DeltaVAsl = deltaV.TotalDeltaVASL,
                    BurnTime = deltaV.TotalBurnTime,
                    LaunchTwr = first?.TWRASL ?? 0,
                };
                // The file may still be being written; match the figures to it a moment later.
                _pendingBlueprintAt = Time.unscaledTime + 1f;
            }
            catch (Exception e)
            {
                SWLogger.LogWarning($"Couldn't note the saved blueprint's performance: {e.Message}");
            }
        }

        private BlueprintStats _pendingBlueprint;
        private float _pendingBlueprintAt;

        /// <summary>Files the figures noted at a save under the workspace just written: the newest .meta.</summary>
        private void FilePendingBlueprint()
        {
            var stats = _pendingBlueprint;
            _pendingBlueprint = null;
            try
            {
                var folder = WorkspaceFolder;
                if (_data == null || folder == null || !Directory.Exists(folder)) return;
                var newest = new DirectoryInfo(folder).GetFiles("*.meta").OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault();
                if (newest == null || (DateTime.UtcNow - newest.LastWriteTimeUtc).TotalSeconds > 30) return;
                var meta = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(newest.FullName));
                stats.Workspace = meta["Name"]?.ToString() ?? Path.GetFileNameWithoutExtension(newest.Name);
                stats.SaveTime = meta["SaveTime"]?.ToObject<long>() ?? 0;
                // Only the latest save matches the file on disk (an ended era's archive keeps its own copy).
                _data.Blueprints.RemoveAll(b => b.Workspace == stats.Workspace);
                _data.Blueprints.Add(stats);
            }
            catch (Exception e)
            {
                SWLogger.LogWarning($"Couldn't note the saved blueprint's performance: {e.Message}");
            }
        }

        // ─── Following a flight after launch ───

        private static string Key(IGGuid id) => id.Guid.ToString("N");

        private static void Follow(FlightRecord flight, IGGuid id)
        {
            string key = Key(id);
            if (!flight.VesselIds.Contains(key)) flight.VesselIds.Add(key);
            if (!flight.ActiveIds.Contains(key)) flight.ActiveIds.Add(key);
        }

        /// <summary>The flight a vessel belongs to; the newest one if (oddly) more than one claims it.</summary>
        private FlightRecord FlightOf(IGGuid id)
        {
            if (_data == null) return null;
            string key = Key(id);
            for (int i = _data.Flights.Count - 1; i >= 0; i--)
                if (_data.Flights[i].VesselIds.Contains(key)) return _data.Flights[i];
            return null;
        }

        private FlightRecord FlightOf(VesselComponent vessel) => vessel == null ? null : FlightOf(vessel.GlobalId);

        private static string BodyOf(VesselComponent vessel)
        {
            try { return vessel?.mainBody?.bodyName; }
            catch (Exception) { return null; }
        }

        private void AddEvent(FlightRecord flight, FlightEventKind kind, string body)
        {
            if (flight == null) return;
            flight.Events.Add(new FlightEvent { UT = UniverseTime, Kind = kind, Body = body });
        }

        private static bool InFlight(VesselSituations situation) =>
            situation == VesselSituations.Flying || situation == VesselSituations.SubOrbital
            || situation == VesselSituations.Orbiting || situation == VesselSituations.Escaping;

        /// <summary>A vessel that bounces on touchdown lands again within seconds; that's still one landing.</summary>
        private const double BounceSeconds = 15;

        /// <summary>A touchdown at home this soon after launch is the craft settling, not coming back.</summary>
        private const double SettleSeconds = 30;

        private void OnSituationChanged(MessageCenterMessage message)
        {
            if (!(message is VesselSituationChangedMessage changed)) return;
            try
            {
                var vessel = changed.Vessel;
                var flight = FlightOf(vessel);
                if (flight == null || vessel.IsKerbalEVA) return;
                string body = BodyOf(vessel);
                var situation = changed.NewSituation;
                if ((situation == VesselSituations.Landed || situation == VesselSituations.Splashed) && InFlight(changed.OldSituation))
                {
                    var kind = situation == VesselSituations.Landed ? FlightEventKind.Landed : FlightEventKind.Splashed;
                    var last = flight.Events.FindLast(e => e.Kind == FlightEventKind.Landed || e.Kind == FlightEventKind.Splashed);
                    if (last != null && last.Body == body && UniverseTime - last.UT < BounceSeconds) return;
                    // Settling on the pad or runway just after liftoff isn't a landing.
                    if (body == HomeWorldName && UniverseTime - flight.UT < SettleSeconds) return;
                    AddEvent(flight, kind, body);
                }
                else if (situation == VesselSituations.Orbiting
                         && !flight.Events.Exists(e => e.Kind == FlightEventKind.Orbit && e.Body == body))
                {
                    AddEvent(flight, FlightEventKind.Orbit, body);
                }
            }
            catch (Exception e)
            {
                SWLogger.LogWarning($"Couldn't follow a flight: {e.Message}");
            }
        }

        private const float OrbitCheckInterval = 2f;
        private float _nextOrbitCheck;

        /// <summary>
        /// A vessel that drops from one world's orbit straight into another's (Kerbin to Minmus, say) never stops
        /// "orbiting", so no situation change announces the new orbit. Look at the flights' vessels now and then.
        /// </summary>
        private void CheckOrbits()
        {
            var universe = Game?.UniverseModel;
            if (_data == null || universe == null) return;
            try
            {
                foreach (var flight in _data.Flights)
                    foreach (string key in flight.ActiveIds)
                    {
                        var guid = default(IGGuid);
                        guid.SetGuid(Guid.ParseExact(key, "N"));
                        var vessel = universe.FindVesselComponent(guid);
                        if (vessel == null || vessel.IsKerbalEVA || vessel.Situation != VesselSituations.Orbiting) continue;
                        string body = BodyOf(vessel);
                        if (body != null && !flight.Events.Exists(e => e.Kind == FlightEventKind.Orbit && e.Body == body))
                            AddEvent(flight, FlightEventKind.Orbit, body);
                    }
            }
            catch (Exception e)
            {
                SWLogger.LogWarning($"Couldn't check flights' orbits: {e.Message}");
            }
        }

        private void OnSoiEntered(MessageCenterMessage message)
        {
            if (!(message is SOIEnteredMessage entered)) return;
            try
            {
                var flight = FlightOf(entered.vessel);
                string body = entered.bodyEntered?.bodyName;
                if (flight == null || body == null || entered.vessel.IsKerbalEVA) return;
                if (!flight.Events.Exists(e => e.Kind == FlightEventKind.EnteredSoi && e.Body == body))
                    AddEvent(flight, FlightEventKind.EnteredSoi, body);
            }
            catch (Exception e)
            {
                SWLogger.LogWarning($"Couldn't follow a flight: {e.Message}");
            }
        }

        /// <summary>A lander or probe that separates carries the flight on; spent stages and debris don't.</summary>
        private static bool CarriesFlight(VesselComponent vessel)
        {
            try { return vessel != null && !vessel.IsKerbalEVA && !vessel.IsDebris() && vessel.HasCommandModule; }
            catch (Exception) { return false; }
        }

        private void OnVesselSplit(MessageCenterMessage message)
        {
            if (!(message is VesselSplitMessage split)) return;
            try
            {
                var flight = FlightOf(split.remainingVessel);
                if (flight != null && CarriesFlight(split.newVessel)) Follow(flight, split.newVessel.GlobalId);
            }
            catch (Exception e)
            {
                SWLogger.LogWarning($"Couldn't follow a flight: {e.Message}");
            }
        }

        private void OnVesselUndocked(MessageCenterMessage message)
        {
            if (!(message is VesselUndockedMessage undocked)) return;
            try
            {
                var one = undocked.VesselOne?.Model;
                var two = undocked.VesselTwo?.Model;
                var flight = FlightOf(one) ?? FlightOf(two);
                if (flight == null) return;
                if (CarriesFlight(one)) Follow(flight, one.GlobalId);
                if (CarriesFlight(two)) Follow(flight, two.GlobalId);
            }
            catch (Exception e)
            {
                SWLogger.LogWarning($"Couldn't follow a flight: {e.Message}");
            }
        }

        private void OnVesselDocked(MessageCenterMessage message)
        {
            if (!(message is VesselDockedMessage docked)) return;
            try
            {
                var one = docked.VesselOne?.Model;
                var two = docked.VesselTwo?.Model;
                string body = BodyOf(one) ?? BodyOf(two);
                // Each flight involved gets the docking; the merged vessel carries both on.
                foreach (var flight in new[] { FlightOf(one), FlightOf(two) }.Distinct())
                {
                    if (flight == null) continue;
                    AddEvent(flight, FlightEventKind.Docked, body);
                    if (one != null) Follow(flight, one.GlobalId);
                    if (two != null) Follow(flight, two.GlobalId);
                }
            }
            catch (Exception e)
            {
                SWLogger.LogWarning($"Couldn't follow a flight: {e.Message}");
            }
        }

        private void OnVesselRecovered(MessageCenterMessage message)
        {
            if (!(message is VesselRecoveredMessage recovered)) return;
            try
            {
                var flight = FlightOf(recovered.VesselID);
                if (flight == null) return;
                string body = BodyOf(Game?.UniverseModel?.FindVesselComponent(recovered.VesselID)) ?? HomeWorldName;
                // Recovery removes the vessel, and that may have been reported a moment earlier as its destruction.
                flight.Events.RemoveAll(e => e.Kind == FlightEventKind.Destroyed && UniverseTime - e.UT < 5);
                AddEvent(flight, FlightEventKind.Recovered, body);
                flight.ActiveIds.Remove(Key(recovered.VesselID));
            }
            catch (Exception e)
            {
                SWLogger.LogWarning($"Couldn't follow a flight: {e.Message}");
            }
        }

        private void OnVesselDestroyed(MessageCenterMessage message)
        {
            if (!(message is VesselDestroyedMessage destroyed)) return;
            try
            {
                var flight = FlightOf(destroyed.Guid);
                string key = Key(destroyed.Guid);
                // A docked vessel's old id goes away too, but that isn't a loss.
                if (flight == null || !flight.ActiveIds.Contains(key)) return;
                flight.ActiveIds.Remove(key);
                if (!IsDockingMerge(flight, key)) AddEvent(flight, FlightEventKind.Destroyed, BodyOf(destroyed.Vessel));
            }
            catch (Exception e)
            {
                SWLogger.LogWarning($"Couldn't follow a flight: {e.Message}");
            }
        }

        /// <summary>True if the vessel went away within moments of the flight docking: it merged, it wasn't lost.</summary>
        private bool IsDockingMerge(FlightRecord flight, string key)
        {
            var dock = flight.Events.FindLast(e => e.Kind == FlightEventKind.Docked);
            return dock != null && UniverseTime - dock.UT < 2 && flight.ActiveIds.Count > 0;
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
                var record = new LossRecord { Name = kerbal.NameKey, UT = UniverseTime, Joined = kerbal.EnrollmentUT };
                FlightRecord flight = null;
                try
                {
                    record.VesselName = Game?.UniverseModel?.FindVesselComponent(kerbal.Location.SimObjectId)?.Name;
                    flight = FlightOf(kerbal.Location.SimObjectId);
                }
                catch (Exception) { /* the vessel may already be gone */ }
                // Lost on EVA, or aboard a vessel the flight didn't follow: their latest flight that's still out.
                flight ??= _data.Flights.FindLast(f => f.Crew.Contains(kerbal.NameKey) && f.ActiveIds.Count > 0);
                if (flight != null && !flight.LostCrew.Contains(kerbal.NameKey)) flight.LostCrew.Add(kerbal.NameKey);
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
            if (_data == null || !_travelLogReady) return;
            var log = TravelLogSnapshot();
            if (log?.ObjectEvents == null) return;
            bool added = false;
            foreach (var launch in log.ObjectEvents)
            {
                if (launch == null || launch.EventKey != LaunchEvent) continue;
                // The same launch may already be recorded from VesselLaunchedMessage, a moment apart. Flights recorded
                // before they were followed learn their vessels here, while the travel log still knows them.
                var known = _data.Flights.Find(f => Math.Abs(f.UT - launch.UT) < SameLaunchSeconds);
                if (known != null)
                {
                    if (!known.IsFollowed()) FollowFromTravelLog(known, launch.TravelObjectIds);
                    continue;
                }

                // A spent stage the travel log counted as a launch when it separated. A launch whose vessels are all
                // gone can't be told apart from a wreck fragment (they're "launched" too as a craft breaks up), and
                // a real one was recorded from VesselLaunchedMessage when it happened.
                var vessels = LaunchedVessels(launch.TravelObjectIds).ToList();
                if (vessels.Count == 0 || vessels.Any(v => !IsNewMission(v))) continue;

                var record = new FlightRecord { UT = launch.UT };
                FollowFromTravelLog(record, launch.TravelObjectIds);
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
            ImportTravelLogEvents(log);
            // Saves from before the settle check may hold a "landing" right after liftoff.
            string home = HomeWorldName;
            foreach (var flight in _data.Flights)
                flight.Events.RemoveAll(e => (e.Kind == FlightEventKind.Landed || e.Kind == FlightEventKind.Splashed)
                    && e.Body == home && e.UT - flight.UT < SettleSeconds);
            // Saves from before the check above may hold a nameless "launch" of a wreck fragment.
            _data.Flights.RemoveAll(f => string.IsNullOrEmpty(f.VesselName) && f.Crew.Count == 0 && f.Parts == 0);
            PruneVanishedVessels();
        }

        /// <summary>The vessels of a travel log event that still exist.</summary>
        private IEnumerable<VesselComponent> LaunchedVessels(List<Guid> ids)
        {
            foreach (var id in ids ?? new List<Guid>())
            {
                var guid = default(IGGuid);
                guid.SetGuid(id);
                var vessel = Game?.UniverseModel?.FindVesselComponent(guid);
                if (vessel != null) yield return vessel;
            }
        }

        private void FollowFromTravelLog(FlightRecord flight, List<Guid> ids)
        {
            foreach (var id in ids ?? new List<Guid>())
            {
                string key = id.ToString("N");
                if (!flight.VesselIds.Contains(key)) flight.VesselIds.Add(key);
                var guid = default(IGGuid);
                guid.SetGuid(id);
                if (Game?.UniverseModel?.FindVesselComponent(guid) != null && !flight.ActiveIds.Contains(key))
                    flight.ActiveIds.Add(key);
            }
        }

        /// <summary>
        /// Landings, SOI entries, recoveries and losses the travel log holds for followed vessels, so a flight that
        /// was already out when the mod started following flights still shows where it went.
        /// </summary>
        private void ImportTravelLogEvents(SerializedTravelLog log)
        {
            foreach (var logged in log.ObjectEvents)
            {
                if (logged?.TravelObjectIds == null) continue;
                FlightEventKind kind;
                if (logged.EventKey == TravelLogManager.LOG_VESSEL_LANDED) kind = FlightEventKind.Landed;
                else if (logged.EventKey == TravelLogManager.LOG_VESSEL_ENTERED_SOI) kind = FlightEventKind.EnteredSoi;
                else if (logged.EventKey == TravelLogManager.LOG_VESSEL_RECOVERED) kind = FlightEventKind.Recovered;
                else if (logged.EventKey == TravelLogManager.LOG_VESSEL_DESTROYED) kind = FlightEventKind.Destroyed;
                else continue;
                // Landings and SOI entries name the body first; recoveries happen at home.
                string body = kind == FlightEventKind.Landed || kind == FlightEventKind.EnteredSoi
                    ? logged.FlightReportArgs?.FirstOrDefault(a => !string.IsNullOrEmpty(a))
                    : kind == FlightEventKind.Recovered ? HomeWorldName : null;

                foreach (var id in logged.TravelObjectIds)
                {
                    string key = id.ToString("N");
                    var flight = _data.Flights.FindLast(f => f.VesselIds.Contains(key));
                    if (flight == null) continue;
                    // Already recorded as it happened (a splashdown is a landing to the travel log).
                    if (flight.Events.Exists(e => Math.Abs(e.UT - logged.UT) < SameLaunchSeconds
                            && (e.Kind == kind || (kind == FlightEventKind.Landed && e.Kind == FlightEventKind.Splashed))))
                        continue;
                    flight.Events.Add(new FlightEvent { UT = logged.UT, Kind = kind, Body = body });
                    flight.Events.Sort((a, b) => a.UT.CompareTo(b.UT));
                    if (kind == FlightEventKind.Recovered || kind == FlightEventKind.Destroyed) flight.ActiveIds.Remove(key);
                }
            }
        }

        /// <summary>
        /// Drops followed vessels that no longer exist without the game saying why (merged by docking, or gone while
        /// the mod wasn't running). Only safe once a save has finished loading and its vessels are all there.
        /// </summary>
        private void PruneVanishedVessels()
        {
            var universe = Game?.UniverseModel;
            if (_data == null || !_travelLogReady || universe == null) return;
            foreach (var flight in _data.Flights)
                flight.ActiveIds.RemoveAll(key =>
                {
                    var guid = default(IGGuid);
                    guid.SetGuid(Guid.ParseExact(key, "N"));
                    return universe.FindVesselComponent(guid) == null;
                });
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
                if (_data != _announced && _data != null && _data.Eras.Count > 0)
                {
                    _announced = _data;
                    HeritageApi.RaiseLoaded();
                }
                if (_importPending && _travelLogReady)
                {
                    _importPending = false;
                    ImportTravelLogLaunches();
                }
            }
            // Mod windows draw over the game's own dialogs (the quit confirmation, for one), so step aside while the
            // pause or settings menu is up, as the game's own windows do.
            bool menusOpen = inCampaign && GameMenusOpen();
            if (!inCampaign) _pendingRebrand = null;
            else if (_pendingRebrand is { } pending && !menusOpen) OpenRebrand(pending.Flag, pending.Base, pending.Accent);
            _ui.Visible = _open && inCampaign;
            _ui.Suspended = menusOpen;
            if (inCampaign && _travelLogReady && Time.unscaledTime >= _nextOrbitCheck)
            {
                _nextOrbitCheck = Time.unscaledTime + OrbitCheckInterval;
                CheckOrbits();
            }
            if (_pendingBlueprint != null && Time.unscaledTime >= _pendingBlueprintAt) FilePendingBlueprint();
            if (_ui.Visible && Time.unscaledTime >= _nextLayoutCheck)
            {
                _nextLayoutCheck = Time.unscaledTime + LayoutCheckInterval;
                _ui.CheckLayout();
            }
        }

        private void OnWindowLayoutChanged(Rect layout)
        {
            _windowX.Value = layout.x;
            _windowY.Value = layout.y;
            _windowWidth.Value = layout.width;
            _windowHeight.Value = layout.height;
            SWConfiguration.Save();
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
        /// new era. Returns a translated error message, or null on success.
        /// </summary>
        internal string Rebrand(string name, string flagSpriteLocation, Color colorBase, Color colorAccent, string note)
        {
            name = name?.Trim();
            if (string.IsNullOrEmpty(name)) return Loc.T("Error/NoName");
            if (_data == null || Session == null) return Loc.T("Error/NoCampaign");
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
            HeritageApi.RaiseRebranded(outgoing, _data.CurrentEra());
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

        /// <summary>True when a rebrand can be made: a campaign is loaded and its history is ready.</summary>
        internal bool CanRebrand => _ui != null && _data != null && Session != null && InCampaign();

        /// <summary>Whether the stock flag setting and VAB agency colors button open the Rebrand page.</summary>
        internal bool RedirectStockIdentity => _redirectStockIdentity?.Value ?? true;

        /// <summary>
        /// Opens the window on the Rebrand page; non-null arguments replace the agency's current values. Mod windows
        /// draw under the pause and settings menus, so while those are open it waits until the player closes them.
        /// </summary>
        internal void OpenRebrand(string flagSpriteLocation, Color? colorBase, Color? colorAccent)
        {
            if (GameMenusOpen())
            {
                var pending = _pendingRebrand ?? (null, null, null);
                _pendingRebrand = (flagSpriteLocation ?? pending.Flag, colorBase ?? pending.Base, colorAccent ?? pending.Accent);
                return;
            }
            _pendingRebrand = null;
            SetOpen(true);
            _ui.ShowRebrand(flagSpriteLocation, colorBase, colorAccent);
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
