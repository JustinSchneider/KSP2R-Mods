using System.Collections.Generic;
using KSP.Sim.impl;

namespace FlightPlanRedux.Planning
{
    /// <summary>The maneuvers Flight Plan can plan. Names follow the original's <c>ManeuverType</c>.</summary>
    public enum ManeuverType
    {
        None,
        Circularize,
        NewPe,
        NewAp,
        NewPeAp,
        NewInc,
        NewLan,
        NewSma,
        MatchPlanes,
        HohmannTransfer,
        CourseCorrection,
        InterceptTarget,
        MatchVelocity,
        MoonReturn,
        PlanetaryTransfer,
        FixAp,
        FixPe
    }

    /// <summary>When to burn. Names follow the original's <c>TimeReference</c>; the advanced-transfer ones are gone.</summary>
    public enum TimeReference
    {
        None,
        Computed,
        Apoapsis,
        Periapsis,
        ClosestApproach,
        EqAscending,
        EqDescending,
        RelAscending,
        RelDescending,
        FixedTime,
        Altitude,
        EqNearestNode,
        EqCheapestNode,
        RelNearestNode,
        RelCheapestNode,
        NextWindow,
        Asap
    }

    public enum StatusLevel { Ok, Warning, Error }

    /// <summary>A status line: a localization key (under <c>Status/</c>) and its format arguments.</summary>
    public sealed class PlanStatus
    {
        public StatusLevel Level;
        public string Key;
        public object[] Args;

        public PlanStatus(StatusLevel level, string key, params object[] args)
        {
            Level = level;
            Key = key;
            Args = args;
        }
    }

    /// <summary>
    /// Everything the player types in. Distances are altitudes above sea level in metres, angles in degrees, times
    /// in seconds. Kept per save and vessel (see SaveData), so fields only.
    /// </summary>
    public sealed class PlanInputs
    {
        public double NewPe = 100_000;
        public double NewAp = 200_000;
        public double NewSma = 200_000;
        public double NewInc;
        public double NewLan;
        public double FixedTime = 30;
        public double Altitude = 100_000;
        public double MoonReturnPe = 100_000;
        public double InterceptTime = 3600;
        public double InterceptDistanceVessel = 100;
        public double InterceptDistanceCelestial = 100_000;
        public int Satellites = 3;
        public int OrbitsBetween = 1;
        public double SatelliteAltitude = 600_000;
        public bool Dive;
        public bool Occlusion = true;
        public double OcclusionAtm = 0.75;
        public double OcclusionVac = 0.9;

        public PlanInputs Clone() => (PlanInputs)MemberwiseClone();
    }

    /// <summary>The planner's answer: when to burn and how hard, plus what the orbit will look like after.</summary>
    public sealed class BurnPlan
    {
        public ManeuverType Maneuver;
        public TimeReference TimeRef;

        /// <summary>Burn time (UT); NaN when there's no solution.</summary>
        public double UT = double.NaN;

        /// <summary>Burn as a world-frame Δv vector (the form the MechJeb calculator returns).</summary>
        public Vector3d DeltaV;

        /// <summary>The same burn in maneuver node coordinates (radial, normal, prograde).</summary>
        public Vector3d BurnVector;

        /// <summary>Orbit right after the burn, or null.</summary>
        public PatchedConicsOrbit Result;

        public PlanStatus Status;
        public readonly List<PlanStatus> Warnings = new();

        public bool Valid => !double.IsNaN(UT) && DeltaV != Vector3d.zero && Status?.Level != StatusLevel.Error;
    }
}
