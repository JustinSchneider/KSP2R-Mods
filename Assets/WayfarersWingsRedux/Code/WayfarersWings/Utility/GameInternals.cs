#nullable enable
using System;
using System.Reflection;
using KSP.Game;
using KSP.Sim.impl;
using KSP.UI.Flight;

namespace WayfarersWings.Utility
{
    /// <summary>
    /// Non-public game members the original reached through a publicized Assembly-CSharp.
    /// Looked up once by reflection; a member that's gone falls back to a safe answer.
    /// </summary>
    internal static class GameInternals
    {
        private const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly PropertyInfo? IsStableOrbitProperty =
            typeof(VesselComponent).GetProperty("IsStableOrbit", AnyInstance);

        private static readonly MethodInfo? CanShowFlightReportMethod =
            typeof(FlightReportUIManager).GetMethod("CanShowFlightReport", AnyInstance, null, new[] { typeof(GameState) }, null);

        public static bool IsStableOrbit(VesselComponent? vessel)
        {
            if (vessel == null || IsStableOrbitProperty == null) return false;
            try { return (bool)IsStableOrbitProperty.GetValue(vessel); }
            catch (Exception) { return false; }
        }

        public static bool CanShowFlightReport(FlightReportUIManager? flightReport, GameState? state)
        {
            if (flightReport == null || state == null || CanShowFlightReportMethod == null) return false;
            try { return (bool)CanShowFlightReportMethod.Invoke(flightReport, new object[] { state.Value }); }
            catch (Exception) { return false; }
        }
    }
}
