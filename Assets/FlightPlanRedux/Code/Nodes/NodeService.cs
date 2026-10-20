using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using KSP.Game;
using KSP.Map;
using KSP.Sim;
using KSP.Sim.impl;
using KSP.Sim.Maneuver;
using MuMech;
using UnityEngine;

namespace FlightPlanRedux.Nodes
{
    /// <summary>What happened to a node request.</summary>
    public sealed class NodeResult
    {
        public bool Ok;

        /// <summary>Localization key under <c>Status/</c> when it failed.</summary>
        public string Problem;

        public ManeuverNodeData Node;

        /// <summary>The trajectory after the node, once the game has worked it out (may be null).</summary>
        public PatchedConicsOrbit After;
    }

    /// <summary>
    /// Maneuver node handling for Redux, replacing Node Manager. Node creation follows the path proven in
    /// Transfer Window Redux: find the trajectory patch that holds the burn time, initialise the node's transform
    /// before adding it, never alter the vessel's own orbit, and remove a half-built node on failure.
    /// </summary>
    internal sealed class NodeService
    {
        /// <summary>The game's maneuver solver breaks for good on a tenth node.</summary>
        public const int MaxNodes = 9;

        private readonly MonoBehaviour _host;

        public NodeService(MonoBehaviour host) => _host = host;

        private static GameInstance Game => GameManager.Instance?.Game;

        public event Action NodesChanged;

        public static List<ManeuverNodeData> Nodes(VesselComponent vessel)
        {
            var list = vessel == null ? null : Game?.SpaceSimulation?.Maneuvers?.GetNodesForVessel(vessel.GlobalId);
            // A copy, sorted by time: the game's list is its own internal one.
            return list == null ? new List<ManeuverNodeData>() : list.OrderBy(n => n.Time).ToList();
        }

        private static ManeuverPlanComponent Plan(VesselComponent vessel) =>
            vessel?.SimulationObject?.FindComponent<ManeuverPlanComponent>();

        /// <summary>
        /// The orbit a new node would start from: the trajectory after the last planned node, or the vessel's own
        /// orbit when there are none. Also gives the earliest time a new node can go.
        /// </summary>
        public static PatchedConicsOrbit PlanningOrbit(VesselComponent vessel, double now, out double earliestUT)
        {
            earliestUT = now;
            var nodes = Nodes(vessel);
            if (nodes.Count == 0) return vessel.Orbit.ToPatchedConics();
            var last = nodes[nodes.Count - 1];
            earliestUT = Math.Max(now, last.Time + 1);
            var patch = PatchAfter(vessel, earliestUT);
            return patch ?? vessel.Orbit.ToPatchedConics();
        }

        /// <summary>The maneuver trajectory patch that holds a time, or null.</summary>
        public static PatchedConicsOrbit PatchAfter(VesselComponent vessel, double ut)
        {
            var solver = vessel?.Orbiter?.ManeuverPlanSolver;
            var list = solver?.PatchedConicsList;
            if (list == null || list.Count == 0) return null;
            solver.FindPatchContainingUt(ut, list, out IPatchedOrbit patch, out int _);
            return patch as PatchedConicsOrbit;
        }

        /// <summary>
        /// Makes a node for a burn given as a world-frame Δv (as the MechJeb calculator returns it), planned on
        /// <paramref name="planningOrbit"/>. With <paramref name="replaceExisting"/> the vessel's nodes go first.
        /// </summary>
        public void Create(VesselComponent vessel, Vector3d worldDeltaV, double burnUT, PatchedConicsOrbit planningOrbit,
            double burnOffsetFactor, bool replaceExisting, Action<NodeResult> done)
        {
            _host.StartCoroutine(CreateRoutine(vessel, worldDeltaV, burnUT, planningOrbit, burnOffsetFactor, replaceExisting, done));
        }

        private IEnumerator CreateRoutine(VesselComponent vessel, Vector3d worldDeltaV, double burnUT,
            PatchedConicsOrbit planningOrbit, double burnOffsetFactor, bool replaceExisting, Action<NodeResult> done)
        {
            var result = new NodeResult();
            var game = Game;
            var maneuvers = game?.SpaceSimulation?.Maneuvers;
            if (vessel == null || maneuvers == null) { Fail(result, "NodeFailed", done); yield break; }

            if (replaceExisting && Nodes(vessel).Count > 0)
            {
                Delete(vessel, Nodes(vessel));
                // Removing nodes reshapes the predicted trajectory; let the solver catch up first.
                yield return new WaitForFixedUpdate();
            }

            var existing = Nodes(vessel);
            if (existing.Count >= MaxNodes) { Fail(result, "TooManyNodes", done); yield break; }

            double now = game.UniverseModel.UniverseTime;
            if (burnUT < now + 1) burnUT = now + 1;
            if (existing.Any(n => Math.Abs(n.Time - burnUT) < 30)) { Fail(result, "NodeTooClose", done); yield break; }

            // New nodes go after the planned ones, on the maneuver trajectory; the game has no tidy way to insert one
            // in front, so the window offers to replace them instead.
            bool onManeuver = existing.Count > 0;
            if (onManeuver && burnUT < existing[existing.Count - 1].Time) { Fail(result, "BeforeLastNode", done); yield break; }

            IKeplerPatch patch = onManeuver
                ? PatchAfter(vessel, burnUT)
                : vessel.Orbiter?.PatchedConicSolver?.FindPatchContainingUT(burnUT);
            if (patch == null)
            {
                SWLog.Warning($"No trajectory patch contains the burn time {burnUT:F0}; not creating a node");
                Fail(result, "BurnPastPath", done);
                yield break;
            }

            var orbit = planningOrbit ?? patch.ToPatchedConics();
            var node = new ManeuverNodeData(vessel.GlobalId, onManeuver, burnUT);
            try
            {
                node.InitializeTransform();
                // The live current patch is Redux's CurrentPatchedConicsOrbit, which SetManeuverState can't take;
                // a node on it only needs the transform.
                if (patch is PatchedConicsOrbit conics) node.SetManeuverState(conics);
                node.BurnVector = orbit.DeltaVToManeuverNodeCoordinates(burnUT, worldDeltaV);
                maneuvers.AddNodeToVessel(node);
            }
            catch (Exception e)
            {
                SWLog.Error($"Couldn't create the maneuver node: {e}");
                // AddNode stores the node before finishing it; don't leave a half-built one behind.
                try { maneuvers.RemoveNodesFromVessel(vessel.GlobalId, new List<ManeuverNodeData> { node }); }
                catch (Exception) { /* it was never added */ }
                Fail(result, "NodeFailed", done);
                yield break;
            }
            result.Node = node;

            // The game works out the burn duration over the next few steps.
            for (int i = 0; i < 20 && node.BurnDuration <= 0; i++)
                yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            // Centre the burn on the planned time, as Flight Plan did: start earlier by a share of the burn duration
            // and re-aim it so the same Δv is applied at the new time.
            if (burnOffsetFactor != 0 && node.BurnDuration > 0)
            {
                try
                {
                    double newUT = Math.Max(now + 1, node.Time + node.BurnDuration * burnOffsetFactor);
                    if (onManeuver) newUT = Math.Max(newUT, existing[existing.Count - 1].Time + 1);
                    var plan = Plan(vessel);
                    if (plan != null && Math.Abs(newUT - node.Time) > 0.01)
                    {
                        plan.UpdateTimeOnNode(node, newUT);
                        Vector3d burn = orbit.DeltaVToManeuverNodeCoordinates(newUT, worldDeltaV);
                        Vector3d change = burn - node.BurnVector;
                        plan.UpdateChangeOnNode(node, new Vector3((float)change.x, (float)change.y, (float)change.z));
                        plan.UpdateNodeDetails(node);
                    }
                }
                catch (Exception e)
                {
                    SWLog.Warning($"Couldn't centre the burn on its time: {e.Message}");
                }
                yield return new WaitForFixedUpdate();
            }

            RefreshMapGizmo(node);
            result.After = PatchAfter(vessel, node.Time + 1);
            result.Ok = true;
            NodesChanged?.Invoke();
            done?.Invoke(result);
        }

        private static void Fail(NodeResult result, string problem, Action<NodeResult> done)
        {
            result.Ok = false;
            result.Problem = problem;
            done?.Invoke(result);
        }

        public void Delete(VesselComponent vessel, IEnumerable<ManeuverNodeData> nodes)
        {
            var list = nodes?.ToList();
            if (vessel == null || list == null || list.Count == 0) return;
            try
            {
                Game.SpaceSimulation.Maneuvers.RemoveNodesFromVessel(vessel.GlobalId, list);
            }
            catch (Exception e)
            {
                SWLog.Warning($"Couldn't remove maneuver nodes: {e.Message}");
            }
            NodesChanged?.Invoke();
        }

        public void DeleteAll(VesselComponent vessel) => Delete(vessel, Nodes(vessel));

        public void DeletePast(VesselComponent vessel)
        {
            double now = Game.UniverseModel.UniverseTime;
            Delete(vessel, Nodes(vessel).Where(n => n.Time < now));
        }

        /// <summary>Adds to a node's burn, in node coordinates (radial, normal, prograde).</summary>
        public void Nudge(VesselComponent vessel, ManeuverNodeData node, Vector3d change)
        {
            var plan = Plan(vessel);
            if (plan == null || node == null) return;
            try
            {
                plan.UpdateChangeOnNode(node, new Vector3((float)change.x, (float)change.y, (float)change.z));
                plan.UpdateNodeDetails(node);
                RefreshMapGizmo(node);
            }
            catch (Exception e)
            {
                SWLog.Warning($"Couldn't change the node's burn: {e.Message}");
            }
            NodesChanged?.Invoke();
        }

        /// <summary>Moves a node in time, keeping it after the previous node and in the future.</summary>
        public void Shift(VesselComponent vessel, ManeuverNodeData node, double seconds)
        {
            var plan = Plan(vessel);
            if (plan == null || node == null) return;
            var nodes = Nodes(vessel);
            int i = nodes.FindIndex(n => n.NodeID == node.NodeID);
            double min = Game.UniverseModel.UniverseTime + 1;
            if (i > 0) min = Math.Max(min, nodes[i - 1].Time + 1);
            double max = i >= 0 && i < nodes.Count - 1 ? nodes[i + 1].Time - 1 : double.MaxValue;
            double ut = Math.Min(Math.Max(node.Time + seconds, min), max);
            if (Math.Abs(ut - node.Time) < 0.001) return;
            try
            {
                plan.UpdateTimeOnNode(node, ut);
                plan.UpdateNodeDetails(node);
                RefreshMapGizmo(node);
            }
            catch (Exception e)
            {
                SWLog.Warning($"Couldn't move the node: {e.Message}");
            }
            NodesChanged?.Invoke();
        }

        /// <summary>The map view only picks up a node added or changed from code once told to.</summary>
        private static void RefreshMapGizmo(ManeuverNodeData node)
        {
            try
            {
                MapCore mapCore = null;
                if (Game.Map.TryGetMapCore(out mapCore) && mapCore != null)
                {
                    mapCore.map3D.ManeuverManager.GetNodeDataForVessels();
                    mapCore.map3D.ManeuverManager.UpdatePositionForGizmo(node.NodeID);
                }
            }
            catch (Exception e)
            {
                SWLog.Warning($"Couldn't refresh the map's node marker: {e.Message}");
            }
        }
    }
}
