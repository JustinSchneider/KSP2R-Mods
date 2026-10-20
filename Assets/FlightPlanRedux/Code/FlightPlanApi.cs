using System;
using System.Collections.Generic;
using System.Linq;
using FlightPlanRedux.Nodes;
using FlightPlanRedux.UI;
using KSP.Sim.impl;
using SpaceWarp2.API.Features;

namespace FlightPlanRedux
{
    /// <summary>A copy of one of the active vessel's maneuver nodes.</summary>
    public sealed class NodeInfo
    {
        /// <summary>Position in time order, from 0.</summary>
        public int Index;
        public Guid Id;
        public double UT;
        /// <summary>Burn in node coordinates: x radial, y normal, z prograde (m/s).</summary>
        public Vector3d BurnVector;
        /// <summary>Seconds; 0 until the game has worked it out.</summary>
        public double BurnDuration;
    }

    /// <summary>
    /// The public face of Flight Plan Redux for other mods. Everything else in this assembly may change between
    /// versions; this won't without <see cref="ApiVersion"/> going up. It also stands in for Node Manager, which has
    /// no working Redux port: other mods can make and remove the active vessel's nodes through it.
    /// <para>Look the type up by name ("FlightPlanRedux.FlightPlanApi") so your mod runs without this one.</para>
    /// </summary>
    public static class FlightPlanApi
    {
        /// <summary>Goes up whenever a member here changes or is removed. Additions don't change it.</summary>
        public const int ApiVersion = 1;

        internal static FlightPlanReduxPlugin Plugin;

        /// <summary>The plugin, or null in a campaign that doesn't play with the mod: then every call does nothing.</summary>
        private static FlightPlanReduxPlugin Host => ActiveFeatures.IsModEnabled(FlightPlanReduxPlugin.ModGuid) ? Plugin : null;

        /// <summary>
        /// True once the mod has started and the loaded campaign plays with it; the window and nodes work in the
        /// flight and map views.
        /// </summary>
        public static bool IsReady => Host != null;

        public static bool IsOpen => Host?.IsOpen ?? false;

        public static void Open() => Host?.SetOpen(true);
        public static void Close() => Host?.SetOpen(false);

        /// <summary>
        /// Opens a tab by name: "Orbit", "Target", "Moon", "Planet", "Resonant" or "Nodes". False for an unknown name.
        /// The window falls back to Orbit when the tab doesn't apply where the vessel is.
        /// </summary>
        public static bool OpenTab(string tab)
        {
            if (Host == null || !Enum.TryParse(tab, true, out Tab parsed)) return false;
            Host.OpenTab(parsed);
            return true;
        }

        /// <summary>
        /// Targets a celestial body (by its internal name, e.g. "Duna") and opens the tab that plans a transfer to it:
        /// Planet for another planet, Target for a body orbiting the same one as the vessel. False if there's no such
        /// body or no active vessel.
        /// </summary>
        public static bool OpenTransfer(string bodyName)
        {
            if (Host == null || !Host.TargetBody(bodyName)) return false;
            var vessel = Host.Vessel;
            var from = vessel?.Orbit?.referenceBody;
            var target = vessel?.TargetObject?.CelestialBody;
            bool planetary = from != null && target != null && !from.IsStar && from.referenceBody != null
                             && from.referenceBody.IsStar && target.referenceBody == from.referenceBody;
            Host.OpenTab(planetary ? Tab.Planet : Tab.Target);
            return true;
        }

        /// <summary>The active vessel's nodes in time order; empty without a vessel.</summary>
        public static IReadOnlyList<NodeInfo> Nodes()
        {
            var nodes = NodeService.Nodes(Host?.Vessel);
            return nodes.Select((n, i) => new NodeInfo
            {
                Index = i, Id = n.NodeID, UT = n.Time, BurnVector = n.BurnVector, BurnDuration = n.BurnDuration
            }).ToList();
        }

        /// <summary>
        /// Adds a node to the active vessel after any it already has (as Node Manager's CreateManeuverNodeAtUT did).
        /// <paramref name="burnVector"/> is in node coordinates (x radial, y normal, z prograde) at
        /// <paramref name="ut"/>, on the trajectory after the last node. <paramref name="burnOffsetFactor"/> moves the
        /// node by that share of the burn's duration once known: -0.5 centres the burn on <paramref name="ut"/>.
        /// <paramref name="done"/> gets true once the node is in place, false if it couldn't be made.
        /// </summary>
        public static void CreateNodeAtUT(Vector3d burnVector, double ut, double burnOffsetFactor = -0.5, Action<bool> done = null)
        {
            var vessel = Host?.Vessel;
            if (vessel == null) { Call(done, false); return; }
            var orbit = NodeService.PlanningOrbit(vessel, Host.UT, out _);
            Vector3d worldDv;
            try { worldDv = MuMech.OrbitExtensions.BurnVecToDv(orbit, ut, burnVector); }
            catch (Exception e)
            {
                SWLog.Warning($"CreateNodeAtUT: couldn't convert the burn: {e.Message}");
                Call(done, false);
                return;
            }
            Host.Nodes.Create(vessel, worldDv, ut, orbit, burnOffsetFactor, false, result =>
            {
                if (!result.Ok) SWLog.Info($"CreateNodeAtUT: no node ({result.Problem})");
                Call(done, result.Ok);
            });
        }

        /// <summary>Removes the node at a position in time order. False if there's no such node.</summary>
        public static bool DeleteNode(int index)
        {
            var vessel = Host?.Vessel;
            var nodes = NodeService.Nodes(vessel);
            if (index < 0 || index >= nodes.Count) return false;
            Host.Nodes.Delete(vessel, new[] { nodes[index] });
            return true;
        }

        public static void DeleteNodes() => Host?.Nodes.DeleteAll(Host.Vessel);

        public static void DeletePastNodes() => Host?.Nodes.DeletePast(Host.Vessel);

        /// <summary>The active vessel's nodes were added, removed or changed through Flight Plan.</summary>
        public static event Action NodesChanged;

        internal static void RaiseNodesChanged() => Raise(NodesChanged, handler => ((Action)handler)());

        private static void Call(Action<bool> done, bool ok)
        {
            if (done == null) return;
            try { done(ok); }
            catch (Exception e) { SWLog.Warning($"A CreateNodeAtUT callback failed: {e}"); }
        }

        /// <summary>Calls each listener on its own, so one mod's exception doesn't keep the others from hearing.</summary>
        private static void Raise(Delegate listeners, Action<Delegate> call)
        {
            if (listeners == null) return;
            foreach (var handler in listeners.GetInvocationList())
            {
                try { call(handler); }
                catch (Exception e)
                {
                    SWLog.Warning($"A listener in {handler.Method.DeclaringType?.Assembly.GetName().Name} failed: {e}");
                }
            }
        }
    }
}
