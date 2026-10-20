using System;
using System.Collections.Generic;
using System.Linq;
using KSP.Sim;
using KSP.Sim.impl;

namespace TransferWindowRedux
{
    /// <summary>Why a transfer can't be calculated for the current vessel and target.</summary>
    public enum TransferProblem
    {
        None,
        NoVessel,
        NoTarget,
        TargetNotBody,
        TargetIsStar,
        AlreadyInTargetSoi,
        TargetNotReachable,
    }

    /// <summary>
    /// Hohmann transfer figures between the vessel's position in the body hierarchy and a target body.
    /// The "origin" is whatever orbits the target's parent on the vessel's side: a planet when
    /// targeting another planet, or the vessel itself when targeting a moon of the body it orbits.
    /// Assumes roughly circular, coplanar, prograde orbits, like the original mod.
    /// </summary>
    public class TransferInfo
    {
        public TransferProblem Problem;
        public string TargetName;
        public string OriginName;
        public bool OriginIsVessel;
        /// <summary>
        /// Leaving a moon for the body it orbits. There's no phase angle: the window is the next point on the
        /// vessel's orbit where an escape burn sends it backwards along the moon's path, once per orbit.
        /// </summary>
        public bool IsReturn;

        /// <summary>The vessel isn't in a closed orbit, so departure figures assume a low circular parking orbit.</summary>
        public bool FromParkingOrbit;
        /// <summary>Altitude of that assumed parking orbit, in metres.</summary>
        public double ParkingAltitude;

        /// <summary>How far the target is ahead of the origin, in degrees (-180, 180], or NaN if unknown.</summary>
        public double PhaseAngle;
        /// <summary>The phase angle a Hohmann transfer needs at departure, in degrees (-180, 180].</summary>
        public double DesiredPhaseAngle;
        /// <summary>Seconds until the phase angle matches, or NaN if unknown or it never changes.</summary>
        public double TimeToWindow;
        /// <summary>Seconds between successive windows, or NaN if the phase angle never changes.</summary>
        public double SynodicPeriod;
        /// <summary>Half the transfer ellipse's period, in seconds.</summary>
        public double TransferTime;
        /// <summary>Angle between the origin's and the target's orbital planes, in degrees, or NaN if unknown.</summary>
        public double RelativeInclination;

        /// <summary>Departure burn from the vessel's orbit, in m/s, or NaN when it can't be estimated.</summary>
        public double EjectionDeltaV;
        /// <summary>
        /// For an escape from the origin planet: how far before the planet's prograde (or retrograde, for inward
        /// transfers) the burn happens, in degrees. NaN otherwise.
        /// </summary>
        public double EjectionAngle;
        public bool EjectionFromRetrograde;

        /// <summary>When to burn for this window, or NaN when a node can't be placed from here.</summary>
        public double BurnUT;
        /// <summary>Prograde Δv for the node at <see cref="BurnUT"/>, in m/s.</summary>
        public double BurnDeltaV;

        /// <summary>Burn to enter a low circular orbit at the target, in m/s. Zero when aerobraking.</summary>
        public double CaptureDeltaV;
        /// <summary>Altitude of that orbit, or of the arrival periapsis for a return, in metres.</summary>
        public double CaptureAltitude;
        public bool TargetHasAtmosphere;
        /// <summary>A return that dips into the atmosphere to slow down instead of burning.</summary>
        public bool CaptureByAerobraking;

        /// <summary>The vessel's total vacuum Δv, in m/s, or NaN if the game hasn't calculated it.</summary>
        public double AvailableDeltaV;

        public bool WindowKnown => !double.IsNaN(TimeToWindow);
        public bool CanPlaceNode => !double.IsNaN(BurnUT) && !double.IsNaN(BurnDeltaV);
        public double TotalDeltaV => (double.IsNaN(EjectionDeltaV) ? 0 : EjectionDeltaV) + CaptureDeltaV;
    }

    public static class TransferMath
    {
        /// <summary>Default clearance above the surface or atmosphere for assumed parking and capture orbits.</summary>
        public const double DefaultOrbitMargin = 10_000;
        /// <summary>Returns to a body with an atmosphere aim this far down into it, as a fraction of its depth.</summary>
        private const double ReentryDepthFraction = 0.5;
        /// <summary>Don't plan a return burn this close to now; it can't be lined up in time.</summary>
        private const double MinimumBurnLead = 120;

        /// <param name="returnPeriapsis">
        /// Arrival periapsis altitude for returns from a moon, in metres. Negative picks one: halfway into the
        /// atmosphere for reentry, or the parking orbit for airless bodies.
        /// </param>
        public static TransferInfo Calculate(VesselComponent vessel, double ut, double orbitMargin = DefaultOrbitMargin,
            double returnPeriapsis = -1)
        {
            var info = new TransferInfo
            {
                PhaseAngle = double.NaN,
                DesiredPhaseAngle = double.NaN,
                TimeToWindow = double.NaN,
                SynodicPeriod = double.NaN,
                RelativeInclination = double.NaN,
                EjectionDeltaV = double.NaN,
                EjectionAngle = double.NaN,
                BurnUT = double.NaN,
                BurnDeltaV = double.NaN,
                AvailableDeltaV = double.NaN,
            };
            if (vessel?.Orbit == null) return Fail(info, TransferProblem.NoVessel);
            if (!vessel.HasTargetObject || vessel.TargetObject == null) return Fail(info, TransferProblem.NoTarget);
            if (!vessel.TargetObject.IsCelestialBody || vessel.TargetObject.CelestialBody == null)
                return Fail(info, TransferProblem.TargetNotBody);

            var target = vessel.TargetObject.CelestialBody;
            info.TargetName = target.Name;
            var parent = target.referenceBody;
            if (parent == null || target.Orbit == null) return Fail(info, TransferProblem.TargetIsStar);

            var vesselBody = vessel.Orbit.referenceBody;
            var vesselDeltaV = vessel.VesselDeltaV;
            if (vesselDeltaV != null) info.AvailableDeltaV = vesselDeltaV.TotalDeltaVVac;

            // Landed, flying or sub-orbital vessels have a meaningless orbit, so plan from a parking orbit instead.
            bool inOrbit = vessel.Situation == VesselSituations.Orbiting;
            info.FromParkingOrbit = !inOrbit;
            info.ParkingAltitude = ParkingRadius(vesselBody, orbitMargin) - vesselBody.radius;

            if (SameBody(vesselBody.referenceBody, target))
                return CalculateReturn(info, vessel, vesselBody, target, ut, inOrbit, orbitMargin, returnPeriapsis);

            // Walk up from the body the vessel orbits until we reach something orbiting the target's parent.
            CelestialBodyComponent originBody = null;
            if (!SameBody(vesselBody, parent))
            {
                originBody = vesselBody;
                while (originBody != null && !SameBody(originBody.referenceBody, parent))
                    originBody = originBody.referenceBody;
                if (originBody == null) return Fail(info, TransferProblem.TargetNotReachable);
                if (SameBody(originBody, target)) return Fail(info, TransferProblem.AlreadyInTargetSoi);
            }

            info.OriginIsVessel = originBody == null;
            info.OriginName = info.OriginIsVessel ? vessel.Name : originBody.Name;
            IKeplerPatch originOrbit = info.OriginIsVessel ? vessel.Orbit : originBody.Orbit;
            IKeplerPatch targetOrbit = target.Orbit;
            double mu = parent.gravParameter;

            // Zup positions and velocities share one inertial frame, relative to each orbit's parent.
            Vector3d tPos = targetOrbit.GetRelativePositionAtUTZup(ut);
            Vector3d tNormal = Vector3d.Cross(tPos, targetOrbit.GetOrbitalVelocityAtUTZup(ut)).normalized;

            double r1, originPeriod;
            bool originKnown = !(info.OriginIsVessel && !inOrbit);
            if (originKnown)
            {
                // Phase angle, measured in the origin's direction of motion so the result doesn't depend
                // on the game's axis conventions.
                Vector3d oPos = originOrbit.GetRelativePositionAtUTZup(ut);
                Vector3d oNormal = Vector3d.Cross(oPos, originOrbit.GetOrbitalVelocityAtUTZup(ut)).normalized;
                info.PhaseAngle = Rad2Deg(Math.Atan2(Vector3d.Dot(oNormal, Vector3d.Cross(oPos, tPos)), Vector3d.Dot(oPos, tPos)));
                info.RelativeInclination = Rad2Deg(Math.Acos(Clamp(Vector3d.Dot(oNormal, tNormal), -1, 1)));
                r1 = originOrbit.semiMajorAxis;
                originPeriod = originOrbit.period;
            }
            else
            {
                r1 = ParkingRadius(parent, orbitMargin);
                originPeriod = 2 * Math.PI * Math.Sqrt(r1 * r1 * r1 / mu);
            }

            double r2 = targetOrbit.semiMajorAxis;
            double transferA = (r1 + r2) / 2;
            info.TransferTime = Math.PI * Math.Sqrt(transferA * transferA * transferA / mu);
            info.DesiredPhaseAngle = Wrap180(180.0 - 360.0 * info.TransferTime / targetOrbit.period);

            // The target gains on the origin at (its mean motion - the origin's), in degrees per second.
            double relativeRate = 360.0 / targetOrbit.period - 360.0 / originPeriod;
            if (Math.Abs(relativeRate) >= 1e-12)
            {
                info.SynodicPeriod = 360.0 / Math.Abs(relativeRate);
                if (originKnown)
                {
                    double gap = relativeRate > 0
                        ? Wrap360(info.DesiredPhaseAngle - info.PhaseAngle)
                        : Wrap360(info.PhaseAngle - info.DesiredPhaseAngle);
                    info.TimeToWindow = gap / Math.Abs(relativeRate);
                }
            }

            // Hyperbolic excess speed (or, for a moon, the whole first Hohmann burn).
            double vInfinity = Math.Abs(Math.Sqrt(mu / r1) * (Math.Sqrt(2 * r2 / (r1 + r2)) - 1));
            double windowUT = ut + info.TimeToWindow;
            if (info.OriginIsVessel)
            {
                info.EjectionDeltaV = vInfinity;
                if (inOrbit && info.WindowKnown)
                {
                    // Raise (or lower) the far side of the orbit to the target's altitude from wherever we are then.
                    info.BurnUT = windowUT;
                    double r = vessel.Orbit.GetRelativePositionAtUTZup(windowUT).magnitude;
                    double speed = vessel.Orbit.GetOrbitalVelocityAtUTZup(windowUT).magnitude;
                    info.BurnDeltaV = Math.Sqrt(mu * (2 / r - 2 / (r + r2))) - speed;
                    info.EjectionDeltaV = Math.Abs(info.BurnDeltaV);
                }
            }
            else if (SameBody(vesselBody, originBody))
            {
                // Burn from the current orbit onto an escape hyperbola that leaves the SOI at vInfinity.
                double muOrigin = originBody.gravParameter;
                double soi = originBody.sphereOfInfluence;
                double r = inOrbit ? vessel.Orbit.radius : ParkingRadius(originBody, orbitMargin);
                double speed = inOrbit ? OrbitalSpeed(vessel.Orbit) : Math.Sqrt(muOrigin / r);
                info.EjectionDeltaV = EscapeSpeed(vInfinity, muOrigin, r, soi) - speed;

                // The hyperbola's asymptote is this far ahead of the burn point.
                double e = 1 + r * vInfinity * vInfinity / muOrigin;
                double asymptote = Rad2Deg(Math.Acos(-1 / e));
                info.EjectionAngle = asymptote;
                info.EjectionFromRetrograde = r2 < r1;

                if (inOrbit && info.WindowKnown)
                {
                    Vector3d escapeDir = originOrbit.GetOrbitalVelocityAtUTZup(windowUT).normalized;
                    if (info.EjectionFromRetrograde) escapeDir = -escapeDir;
                    info.BurnUT = FindEjectionBurn(vessel.Orbit, escapeDir, asymptote, ut, windowUT);
                    double burnR = vessel.Orbit.GetRelativePositionAtUTZup(info.BurnUT).magnitude;
                    double burnSpeed = vessel.Orbit.GetOrbitalVelocityAtUTZup(info.BurnUT).magnitude;
                    info.BurnDeltaV = EscapeSpeed(vInfinity, muOrigin, burnR, soi) - burnSpeed;
                }
            }
            // Otherwise we orbit a moon of the origin: the escape goes through two SOIs, so don't guess.

            // Capture into a low circular orbit at the target from the speed we arrive with.
            double vArrive = Math.Sqrt(mu * (2 / r2 - 1 / transferA));
            double vArriveInfinity = Math.Abs(Math.Sqrt(mu / r2) - vArrive);
            double captureR = ParkingRadius(target, orbitMargin);
            double muTarget = target.gravParameter;
            info.CaptureDeltaV = EscapeSpeed(vArriveInfinity, muTarget, captureR, target.sphereOfInfluence)
                                 - Math.Sqrt(muTarget / captureR);
            info.CaptureAltitude = captureR - target.radius;
            info.TargetHasAtmosphere = target.hasAtmosphere;

            return info;
        }

        /// <summary>
        /// Escape from a moon backwards along its orbit, onto an ellipse whose periapsis grazes the parent body:
        /// low enough to reenter or aerobrake where there's an atmosphere, or a parking orbit to circularise into.
        /// </summary>
        private static TransferInfo CalculateReturn(TransferInfo info, VesselComponent vessel, CelestialBodyComponent moon,
            CelestialBodyComponent planet, double ut, bool inOrbit, double orbitMargin, double returnPeriapsis)
        {
            info.IsReturn = true;
            info.OriginName = moon.Name;

            info.CaptureByAerobraking = planet.hasAtmosphere && returnPeriapsis < 0;
            double rp = returnPeriapsis >= 0 ? planet.radius + returnPeriapsis
                : info.CaptureByAerobraking ? planet.radius + planet.atmosphereDepth * ReentryDepthFraction
                : ParkingRadius(planet, orbitMargin);
            info.CaptureAltitude = rp - planet.radius;
            info.TargetHasAtmosphere = planet.hasAtmosphere;

            double mu = planet.gravParameter;
            IKeplerPatch moonOrbit = moon.Orbit;
            double rMoon = moonOrbit.radius;
            double transferA = (rMoon + rp) / 2;
            info.TransferTime = Math.PI * Math.Sqrt(transferA * transferA * transferA / mu);

            // Leaving the moon's SOI at the ellipse's apoapsis means shedding the difference from the moon's own speed.
            double vInfinity = Math.Abs(OrbitalSpeed(moonOrbit) - Math.Sqrt(mu * (2 / rMoon - 1 / transferA)));
            double muMoon = moon.gravParameter;
            double soi = moon.sphereOfInfluence;
            double r = inOrbit ? vessel.Orbit.radius : ParkingRadius(moon, orbitMargin);
            double speed = inOrbit ? OrbitalSpeed(vessel.Orbit) : Math.Sqrt(muMoon / r);
            info.EjectionDeltaV = EscapeSpeed(vInfinity, muMoon, r, soi) - speed;

            double e = 1 + r * vInfinity * vInfinity / muMoon;
            double asymptote = Rad2Deg(Math.Acos(-1 / e));
            info.EjectionAngle = asymptote;
            info.EjectionFromRetrograde = true;

            if (inOrbit)
            {
                // The moon's direction drifts a little while we wait, so aim at where it'll be at the burn.
                double earliest = ut + MinimumBurnLead;
                double burnUT = earliest;
                for (int i = 0; i < 2; i++)
                {
                    Vector3d escapeDir = -moonOrbit.GetOrbitalVelocityAtUTZup(burnUT).normalized;
                    burnUT = FindEjectionBurn(vessel.Orbit, escapeDir, asymptote, earliest, earliest);
                }
                info.BurnUT = burnUT;
                double burnR = vessel.Orbit.GetRelativePositionAtUTZup(burnUT).magnitude;
                double burnSpeed = vessel.Orbit.GetOrbitalVelocityAtUTZup(burnUT).magnitude;
                info.BurnDeltaV = EscapeSpeed(vInfinity, muMoon, burnR, soi) - burnSpeed;
                info.TimeToWindow = burnUT - ut;
                info.SynodicPeriod = vessel.Orbit.period;
            }

            // Circularise at periapsis unless the atmosphere does the work.
            info.CaptureDeltaV = info.CaptureByAerobraking ? 0
                : Math.Sqrt(mu * (2 / rp - 1 / transferA)) - Math.Sqrt(mu / rp);
            return info;
        }

        /// <summary>
        /// Bodies the vessel can calculate a transfer to: the planet it can return to from a moon first, then moons
        /// of the body it orbits, then that body's siblings, and so on up to the star, each group ordered outward.
        /// </summary>
        public static List<CelestialBodyComponent> ReachableBodies(VesselComponent vessel, IEnumerable<CelestialBodyComponent> allBodies)
        {
            var result = new List<CelestialBodyComponent>();
            var ancestors = new List<CelestialBodyComponent>();
            for (var body = vessel?.Orbit?.referenceBody; body != null; body = body.referenceBody)
                ancestors.Add(body);
            if (ancestors.Count == 0 || allBodies == null) return result;

            // From a moon, the planet it orbits (never the star).
            if (ancestors.Count > 1 && !ancestors[1].IsStar) result.Add(ancestors[1]);

            var candidates = allBodies.Where(b => b?.Orbit != null && !ancestors.Any(a => SameBody(a, b))).ToList();
            foreach (var ancestor in ancestors)
                result.AddRange(candidates
                    .Where(b => SameBody(b.referenceBody, ancestor))
                    .OrderBy(b => b.Orbit.semiMajorAxis));
            return result;
        }

        /// <summary>Radius of a low circular orbit just clear of the body's surface or atmosphere.</summary>
        public static double ParkingRadius(CelestialBodyComponent body, double margin) =>
            body.radius + (body.hasAtmosphere ? body.atmosphereDepth : 0) + margin;

        /// <summary>
        /// Finds when the vessel reaches the point that puts its escape asymptote along <paramref name="escapeDir"/>:
        /// where the escape direction is <paramref name="asymptoteDeg"/> ahead of the vessel along its orbit.
        /// Starts half an orbit before the window so the burn lands as close to it as possible.
        /// </summary>
        private static double FindEjectionBurn(IKeplerPatch orbit, Vector3d escapeDir, double asymptoteDeg, double now, double windowUT)
        {
            double period = orbit.period;
            double rate = 360.0 / period;
            Vector3d normal = Vector3d.Cross(orbit.GetRelativePositionAtUTZup(now), orbit.GetOrbitalVelocityAtUTZup(now)).normalized;

            double AngleAhead(double t)
            {
                Vector3d p = orbit.GetRelativePositionAtUTZup(t);
                return Rad2Deg(Math.Atan2(Vector3d.Dot(normal, Vector3d.Cross(p, escapeDir)), Vector3d.Dot(p, escapeDir)));
            }

            // The angle ahead shrinks as the vessel moves; step forward to the first match, then refine for eccentricity.
            double t = Math.Max(now, windowUT - period / 2);
            t += Wrap360(AngleAhead(t) - asymptoteDeg) / rate;
            for (int i = 0; i < 4; i++)
                t += Wrap180(AngleAhead(t) - asymptoteDeg) / rate;
            return Math.Max(now, t);
        }

        /// <summary>Current orbital speed. Redux's live orbits are IKeplerPatch, which has no orbitalSpeed.</summary>
        private static double OrbitalSpeed(IKeplerPatch orbit) => orbit.GetOrbitalSpeedAtDistance(orbit.radius);

        /// <summary>Speed at radius r on a hyperbola that leaves the SOI at vInfinity.</summary>
        private static double EscapeSpeed(double vInfinity, double mu, double r, double soi) =>
            Math.Sqrt(vInfinity * vInfinity + 2 * mu * (1 / r - 1 / soi));

        private static TransferInfo Fail(TransferInfo info, TransferProblem problem)
        {
            info.Problem = problem;
            return info;
        }

        private static bool SameBody(CelestialBodyComponent a, CelestialBodyComponent b) =>
            a != null && b != null && (ReferenceEquals(a, b) || a.Name == b.Name);

        private static double Rad2Deg(double radians) => radians * 180.0 / Math.PI;

        private static double Clamp(double value, double min, double max) => Math.Max(min, Math.Min(max, value));

        private static double Wrap360(double degrees)
        {
            degrees %= 360.0;
            return degrees < 0 ? degrees + 360.0 : degrees;
        }

        private static double Wrap180(double degrees)
        {
            degrees = Wrap360(degrees);
            return degrees > 180.0 ? degrees - 360.0 : degrees;
        }
    }
}
