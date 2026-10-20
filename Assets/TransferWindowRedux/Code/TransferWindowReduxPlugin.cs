using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KSP.Game;
using KSP.Map;
using KSP.Sim;
using KSP.Sim.impl;
using KSP.Sim.Maneuver;
using KSP.UI.Binding;
using Redux.ExtraModTypes;
using SpaceWarp2.API.Configuration;
using SpaceWarp2.UI.API.Appbar;
using UnityEngine;

namespace TransferWindowRedux
{
    /// <summary>Static access to the mod's logger for classes that aren't the plugin.</summary>
    internal static class SWLog
    {
        internal static SpaceWarp2.API.Logging.ILogger Logger;
        internal static void Warning(string text) => Logger?.LogWarning(text);
    }

    /// <summary>
    /// Transfer Window Redux: a KSP2 Redux port of ABritInSpace's Transfer Window
    /// (https://github.com/ABritInSpace/TransferCalculator-KSP2, MIT).
    /// </summary>
    public class TransferWindowReduxPlugin : KerbalMod
    {
        public const string ModGuid = "TransferWindowRedux";
        internal const string AppbarButtonId = "BTN-TransferWindowRedux";

        // Recalculating every frame is wasteful; a few times a second keeps the numbers live.
        private const float RefreshInterval = 0.25f;
        // How long the "replace existing nodes?" confirmation stays armed.
        private const float ConfirmSeconds = 4f;

        private Texture2D _icon;
        private TransferWindowUI _ui;
        private bool _open;
        private float _nextRefresh;
        private TransferInfo _info;
        private float _confirmReplaceUntil;
        private string _nodeStatus;
        private float _nodeStatusUntil;

        private ConfigValue<double> _orbitMarginKm;
        private ConfigValue<double> _returnPeriapsisKm;
        private ConfigValue<double> _warpLeadSeconds;
        private ConfigValue<float> _windowX, _windowY, _windowWidth, _windowHeight;

        public override void OnPreInitialized()
        {
            SWLog.Logger = SWLogger;
            _icon = LoadIcon();
        }

        public override void OnInitialized()
        {
            BindConfig();

            _ui = new TransferWindowUI(_icon, new Rect(_windowX.Value, _windowY.Value, _windowWidth.Value, _windowHeight.Value));
            _ui.CloseClicked += () => SetOpen(false);
            _ui.WarpClicked += OnWarpClicked;
            _ui.CreateNodeClicked += OnCreateNodeClicked;
            _ui.FlightPlanClicked += () => FlightPlanBridge.OpenTransfer(_info?.TargetName);
            _ui.TargetSelected += OnTargetSelected;
            _ui.LayoutChanged += OnWindowLayoutChanged;
            _ui.Visible = false;

            Appbar.RegisterAppButton(Loc.T("Name"), AppbarButtonId, _icon, SetOpen);
        }

        private void BindConfig()
        {
            _orbitMarginKm = new ConfigValue<double>(SWConfiguration.Bind("Transfer", "Orbit margin (km)", 10.0,
                "Height above the surface or atmosphere of the parking and capture orbits the estimates assume", null));
            _returnPeriapsisKm = new ConfigValue<double>(SWConfiguration.Bind("Transfer", "Return periapsis (km)", -1.0,
                "Arrival periapsis when returning from a moon; -1 aims halfway into the atmosphere for reentry, or at the parking orbit for airless bodies", null));
            _warpLeadSeconds = new ConfigValue<double>(SWConfiguration.Bind("Transfer", "Warp lead time (s)", 60.0,
                "How long before the window or node 'Warp to window' stops. Long waits stop up to 1% earlier, capped at an hour", null));
            _windowX = new ConfigValue<float>(SWConfiguration.Bind("Window", "X", -1f, "Window position; -1 uses the default", null));
            _windowY = new ConfigValue<float>(SWConfiguration.Bind("Window", "Y", -1f, "Window position; -1 uses the default", null));
            _windowWidth = new ConfigValue<float>(SWConfiguration.Bind("Window", "Width", -1f, "Window size; -1 uses the default", null));
            _windowHeight = new ConfigValue<float>(SWConfiguration.Bind("Window", "Height", -1f, "Window size; -1 fits the content", null));
        }

        private void Update()
        {
            if (_ui == null) return;

            bool inFlight = IsFlightScene();
            _ui.Visible = _open && inFlight;
            if (!_ui.Visible || Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + RefreshInterval;
            _ui.CheckLayout();

            try
            {
                var vessel = ActiveVessel;
                double ut = Game.UniverseModel.UniverseTime;
                _ui.SetTargets(
                    TransferMath.ReachableBodies(vessel, Game.UniverseModel.GetAllCelestialBodies()).Select(b => b.Name).ToList(),
                    vessel?.TargetObject?.IsCelestialBody == true ? vessel.TargetObject.CelestialBody.Name : null);

                double returnPeriapsis = _returnPeriapsisKm.Value < 0 ? -1 : _returnPeriapsisKm.Value * 1000;
                _info = TransferMath.Calculate(vessel, ut, _orbitMarginKm.Value * 1000, returnPeriapsis);
                if (_info.Problem == TransferProblem.None)
                    _ui.ShowTransfer(_info, ut, Game.ViewController.TimeWarp.IsWarping, NodeButtonText(vessel), FlightPlanBridge.IsAvailable);
                else
                    _ui.ShowProblem(Loc.T("Problem/" + _info.Problem));
            }
            catch (Exception e)
            {
                _info = null;
                _ui.ShowProblem(Loc.T("Problem/CalculationFailed"));
                SWLogger.LogError($"Transfer calculation failed: {e}");
                // Don't flood the log at the refresh rate.
                _nextRefresh = Time.unscaledTime + 5f;
            }
        }

        private VesselComponent ActiveVessel => Game?.ViewController?.GetActiveSimVessel(true);

        private void SetOpen(bool open)
        {
            _open = open;
            _nextRefresh = 0;
            // Keep the app bar button in step when the window is closed from its own X button.
            GameObject.Find(AppbarButtonId)?.GetComponent<UIValue_WriteBool_Toggle>()?.SetValue(open);
        }

        private void OnTargetSelected(string bodyName)
        {
            var vessel = ActiveVessel;
            var body = Game?.UniverseModel?.FindCelestialBodyByName(bodyName);
            if (vessel == null || body == null) return;
            vessel.SetTargetByID(body.GlobalId);
            _nextRefresh = 0;
        }

        private void OnWindowLayoutChanged(Rect layout)
        {
            _windowX.Value = layout.x;
            _windowY.Value = layout.y;
            _windowWidth.Value = layout.width;
            _windowHeight.Value = layout.height;
            SWConfiguration.Save();
        }

        private void OnWarpClicked()
        {
            var timeWarp = Game?.ViewController?.TimeWarp;
            if (timeWarp == null) return;

            if (timeWarp.IsWarping)
            {
                timeWarp.StopTimeWarp(false);
                return;
            }

            if (_info == null || _info.Problem != TransferProblem.None || !_info.WindowKnown) return;
            double now = Game.UniverseModel.UniverseTime;
            double arrival = now + _info.TimeToWindow;

            // Prefer a planned node that comes before the window, so warp stops in time to burn it.
            var vessel = ActiveVessel;
            var nodes = vessel == null ? null : Game.SpaceSimulation.Maneuvers.GetNodesForVessel(vessel.GlobalId);
            if (nodes != null && nodes.Count > 0)
            {
                double firstNode = nodes.Min(n => n.Time);
                if (firstNode > now && firstNode < arrival) arrival = firstNode;
            }

            double wait = arrival - now;
            double lead = Math.Min(Math.Max(_warpLeadSeconds.Value, wait * 0.01), 3600);
            if (wait <= lead) return;
            timeWarp.WarpTo(now + wait - lead);
        }

        private string NodeButtonText(VesselComponent vessel)
        {
            if (Time.unscaledTime < _nodeStatusUntil) return _nodeStatus;
            if (Time.unscaledTime < _confirmReplaceUntil) return Loc.T("Button/ConfirmReplace");
            return Loc.T(ExistingNodes(vessel).Count > 0 ? "Button/ReplaceNode" : "Button/CreateNode");
        }

        private void ShowNodeStatus(string text)
        {
            _nodeStatus = text;
            _nodeStatusUntil = Time.unscaledTime + ConfirmSeconds;
            _nextRefresh = 0;
        }

        private List<ManeuverNodeData> ExistingNodes(VesselComponent vessel) =>
            vessel == null
                ? new List<ManeuverNodeData>()
                : Game.SpaceSimulation.Maneuvers.GetNodesForVessel(vessel.GlobalId) ?? new List<ManeuverNodeData>();

        private void OnCreateNodeClicked()
        {
            var vessel = ActiveVessel;
            if (vessel == null || _info == null || _info.Problem != TransferProblem.None || !_info.CanPlaceNode) return;

            var existing = ExistingNodes(vessel);
            if (existing.Count > 0)
            {
                // Ask first: the second click within a few seconds confirms.
                if (Time.unscaledTime >= _confirmReplaceUntil)
                {
                    _confirmReplaceUntil = Time.unscaledTime + ConfirmSeconds;
                    _nextRefresh = 0;
                    return;
                }
                Game.SpaceSimulation.Maneuvers.RemoveNodesFromVessel(vessel.GlobalId, new List<ManeuverNodeData>(existing));
            }
            _confirmReplaceUntil = 0;
            StartCoroutine(CreateNode(vessel, _info.BurnUT, _info.BurnDeltaV, existing.Count > 0));
        }

        private IEnumerator CreateNode(VesselComponent vessel, double burnUT, double burnDeltaV, bool removedNodes)
        {
            // Removing nodes reshapes the predicted trajectory; let the solver catch up first.
            if (removedNodes) yield return new WaitForFixedUpdate();

            // The game places ordinary nodes on whichever trajectory patch contains their time. Don't alter the
            // vessel's orbit to make one: an orbit that ends in "Maneuver" instead of "Final" stops covering later
            // times, and AddNode then fails on a missing patch.
            var patch = vessel.Orbiter?.PatchedConicSolver?.FindPatchContainingUT(burnUT);
            if (patch == null)
            {
                SWLogger.LogWarning($"No trajectory patch contains the burn time {burnUT:F0}; not creating a node");
                ShowNodeStatus(Loc.T("Status/BurnPastPath"));
                yield break;
            }

            var maneuvers = Game.SpaceSimulation.Maneuvers;
            var node = new ManeuverNodeData(vessel.GlobalId, false, burnUT);
            try
            {
                // AddNode positions the node's transform, which the constructor doesn't create.
                node.InitializeTransform();
                // The live current patch is Redux's CurrentPatchedConicsOrbit, which SetManeuverState can't take;
                // a node on it only needs the transform.
                if (patch is PatchedConicsOrbit conics) node.SetManeuverState(conics);
                // Node burn vectors are (radial, normal, prograde).
                node.BurnVector = new Vector3d(0, 0, burnDeltaV);
                maneuvers.AddNodeToVessel(node);
            }
            catch (Exception e)
            {
                SWLogger.LogError($"Couldn't create the maneuver node: {e}");
                ShowNodeStatus(Loc.T("Status/NodeFailed"));
                // AddNode stores the node before finishing it; don't leave a half-built one behind.
                try { maneuvers.RemoveNodesFromVessel(vessel.GlobalId, new List<ManeuverNodeData> { node }); }
                catch (Exception) { /* it was never added */ }
                yield break;
            }
            _nextRefresh = 0;

            // The map view only picks up a node added from code once the simulation has stepped.
            yield return new WaitForFixedUpdate();
            RefreshMapGizmo(node);
        }

        private void RefreshMapGizmo(ManeuverNodeData node)
        {
            MapCore mapCore = null;
            if (Game.Map.TryGetMapCore(out mapCore) && mapCore != null)
            {
                mapCore.map3D.ManeuverManager.GetNodeDataForVessels();
                mapCore.map3D.ManeuverManager.UpdatePositionForGizmo(node.NodeID);
            }
        }

        /// <summary>In flight or map view, in a campaign that plays with the mod (each campaign picks its mods).</summary>
        private bool IsFlightScene()
        {
            if (!Enabled) return false;
            var state = Game?.GlobalGameState?.GetGameState();
            if (state == null) return false;
            return state.GameState == GameState.FlightView || state.GameState == GameState.Map3DView;
        }

        private Texture2D LoadIcon()
        {
            var icon = new Texture2D(2, 2);
            try
            {
                var path = Path.Combine(SWMetadata.Folder.FullName, "assets", "images", "icon.png");
                icon.LoadImage(File.ReadAllBytes(path));
            }
            catch (Exception e)
            {
                SWLogger.LogWarning($"Couldn't load the app bar icon: {e.Message}");
            }
            return icon;
        }
    }
}
