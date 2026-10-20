using System;
using KSP.Sim.impl;
using MuMech;

namespace FlightPlanRedux.Planning
{
    /// <summary>
    /// A resonant deployment orbit for a constellation (original Resonant Orbit tab, from <c>FpUiController</c>).
    /// Radii are from the body's centre in metres.
    /// </summary>
    public sealed class ResonantPlan
    {
        public int N, M;
        public double SynchronousAlt = -1, SemiSynchronousAlt = -1, SoiAlt, MinLosAlt = -1;
        public double SatellitePeriod, DeploymentPeriod, Ap, Pe, Eccentricity, InjectionDv;
        public bool DiveAllowed;
        public bool Dive;

        /// <summary>Which of Ap and Pe the current orbit still needs moved, matching the original's fix rows.</summary>
        public bool NeedsAp, NeedsPe;

        public static ResonantPlan For(VesselComponent vessel, PlanInputs i)
        {
            var body = vessel.mainBody;
            var orbit = vessel.Orbit;
            double mu = body.gravParameter, R = body.radius;
            var p = new ResonantPlan();

            double sync = Sma(body.rotationPeriod, mu), semi = Sma(body.rotationPeriod / 2, mu);
            if (sync <= body.sphereOfInfluence) p.SynchronousAlt = sync - R;
            if (semi <= body.sphereOfInfluence) p.SemiSynchronousAlt = semi - R;
            p.SoiAlt = body.sphereOfInfluence - R;

            int sats = Math.Max(1, i.Satellites), orbits = Math.Max(1, i.OrbitsBetween);
            if (sats > 2)
            {
                double occ = i.Occlusion ? (body.hasAtmosphere ? i.OcclusionAtm : i.OcclusionVac) : 1;
                p.MinLosAlt = R * occ / Math.Cos(0.5 * (2.0 * Math.PI / sats)) - R;
            }

            double sSma = i.SatelliteAltitude + R;
            p.SatellitePeriod = Period(sSma, mu);
            p.M = sats * orbits;

            // A diving deployment orbit is only allowed if its Pe stays clear of the atmosphere (or the ground).
            double diveSma = Sma((double)(p.M - 1) / p.M * p.SatellitePeriod, mu);
            p.DiveAllowed = 2.0 * diveSma - sSma >= R + body.atmosphereDepth;
            p.Dive = i.Dive && p.DiveAllowed;
            p.N = p.Dive ? p.M - 1 : p.M + 1;

            p.DeploymentPeriod = (double)p.N / p.M * p.SatellitePeriod;
            double sma2 = Sma(p.DeploymentPeriod, mu);
            if (p.Dive)
            {
                p.Ap = sSma;
                p.Pe = 2.0 * sma2 - sSma;
            }
            else
            {
                p.Pe = sSma;
                p.Ap = 2.0 * sma2 - sSma;
            }
            p.Eccentricity = (p.Ap - p.Pe) / (p.Ap + p.Pe);
            p.InjectionDv = BurnCalc(sSma, p.Ap, sma2, p.Eccentricity, mu);

            if (orbit.Apoapsis < p.Pe) p.NeedsAp = true;
            else if (orbit.Periapsis > p.Ap) p.NeedsPe = true;
            else
            {
                p.NeedsAp = true;
                p.NeedsPe = p.Pe > R;
            }
            return p;
        }

        public static double Sma(double period, double mu) => Math.Pow(period * Math.Sqrt(mu) / (2.0 * Math.PI), 2.0 / 3.0);
        public static double Period(double sma, double mu) => 2.0 * Math.PI * Math.Pow(sma, 1.5) / Math.Sqrt(mu);

        // The original's BurnCalc, with its unused parameters dropped: the speed change between the circular satellite
        // orbit and the deployment orbit at the release point.
        private static double BurnCalc(double sSma, double cAp, double cSma, double ce, double mu)
        {
            double sr = sSma;
            double sdv = Math.Sqrt(mu * (2 / sr - 1 / sSma));
            double cdv = Math.Sqrt(mu * (2 / sr - 1 / cSma));
            return Math.Abs(sdv - cdv);
        }
    }

    /// <summary>Figures about the vessel and its target (original target-relative and transfer tab readouts).</summary>
    public sealed class TargetInfo
    {
        public double SynodicPeriod, NextWindow, ClosestApproachUT, ClosestApproach, RelativeSpeedNow, RelativeSpeedCA,
            RelativeInclination, PhaseAngle, TransferAngle, TransferTime, Distance, EjectionDv;

        /// <summary>Between the vessel and a target orbiting the same body.</summary>
        public static TargetInfo Local(PlanContext c)
        {
            var o = c.Orbit;
            var t = c.TargetOrbit;
            double ut = c.UT;
            var i = new TargetInfo
            {
                SynodicPeriod = o.SynodicPeriod(t),
                ClosestApproachUT = o.NextClosestApproachTime(t, ut + 1),
                RelativeInclination = o.RelativeInclination(t),
                PhaseAngle = o.PhaseAngle(t, ut),
                Distance = (o.WorldPositionAtUT(ut) - t.WorldPositionAtUT(ut)).magnitude
            };
            i.ClosestApproach = o.RelativeDistance(t, i.ClosestApproachUT);
            i.RelativeSpeedCA = o.RelativeSpeed(t, i.ClosestApproachUT);
            var dp = o.WorldBCIPositionAtUT(ut) - t.WorldBCIPositionAtUT(ut);
            i.RelativeSpeedNow = Vector3d.Dot(o.WorldOrbitalVelocityAtUT(ut) - t.WorldOrbitalVelocityAtUT(ut), dp) / dp.magnitude;
            i.TransferAngle = o.Transfer(t, out i.TransferTime);
            i.NextWindow = Window(i.SynodicPeriod, i.PhaseAngle, i.TransferAngle, t.semiMajorAxis > o.semiMajorAxis);
            return i;
        }

        /// <summary>Between the planet the vessel is at and a target planet (original OTM Planet tab).</summary>
        public static TargetInfo Planetary(PlanContext c)
        {
            var from = c.Body.Orbit.ToPatchedConics();
            var t = c.TargetOrbit;
            var i = new TargetInfo
            {
                SynodicPeriod = from.SynodicPeriod(t),
                RelativeInclination = c.Orbit.RelativeInclination(t),
                PhaseAngle = from.PhaseAngle(t, c.UT)
            };
            i.TransferAngle = from.Transfer(t, out i.TransferTime);
            i.NextWindow = Window(i.SynodicPeriod, i.PhaseAngle, i.TransferAngle, t.semiMajorAxis > from.semiMajorAxis);
            i.EjectionDv = Ejection(c);
            return i;
        }

        /// <summary>
        /// What to do next to meet a target vessel, as a key under <c>Next/</c>. The original showed this as a sentence on
        /// its ship-to-ship tab; the thresholds are its own.
        /// </summary>
        public static string NextStep(PlanContext c, TargetInfo t)
        {
            const double near = 3000, close = 100, docking = 1, maxPhasingOrbits = 5;
            if (t.Distance < near)
            {
                if (Math.Abs(t.RelativeSpeedCA) < docking) return t.Distance < close ? "ReadyToDock" : "GetCloser";
                return t.RelativeSpeedCA > 0 ? "StopDrift" : "MatchVelocity";
            }
            if (t.RelativeInclination > 1) return "MatchPlanes";
            if (t.ClosestApproach < near) return "MatchVelocity";
            if (t.ClosestApproach < 3 * near) return "CourseCorrection";
            if (c.Orbit.eccentricity < 1 && t.NextWindow / c.Orbit.period > maxPhasingOrbits) return "PhasingOrbit";
            return "HohmannTransfer";
        }

        private static double Window(double synodic, double phase, double transfer, bool outward) =>
            synodic * MuUtils.ClampDegrees360(outward ? phase - transfer : transfer - phase) / 360;

        // The original's DeltaV(): Hohmann ejection speed from the current orbit, done in doubles here.
        private static double Ejection(PlanContext c)
        {
            var cur = c.Body;
            var target = c.TargetBody;
            while (cur.Orbit != null && cur.Orbit.referenceBody != target.Orbit.referenceBody)
                cur = cur.Orbit.referenceBody;
            if (cur.Orbit == null) return double.NaN;
            double aT = target.Orbit.semiMajorAxis, aC = cur.Orbit.semiMajorAxis;
            double star = target.Orbit.referenceBody.gravParameter;
            double sunEject = Math.Sqrt(star / aC) * (Math.Sqrt(aT / ((aT + aC) / 2)) - 1);
            double eject = Math.Sqrt(2 * cur.gravParameter * (1 / c.Orbit.radius - 1 / cur.sphereOfInfluence) + sunEject * sunEject);
            return eject - c.Orbit.orbitalSpeed;
        }
    }
}
