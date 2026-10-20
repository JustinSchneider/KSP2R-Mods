using System;
using System.Collections.Generic;
using KSP.Sim.impl;
using MuMech;

namespace FlightPlanRedux.Planning
{
    /// <summary>The vessel, its orbit and its target, as seen at one moment.</summary>
    public sealed class PlanContext
    {
        public VesselComponent Vessel;
        public PatchedConicsOrbit Orbit;
        public CelestialBodyComponent Body;
        public double UT;

        public SimulationObjectModel Target;
        public PatchedConicsOrbit TargetOrbit;

        /// <summary>The target's body when the target is a celestial body, else null.</summary>
        public CelestialBodyComponent TargetBody;

        public bool HasTarget => TargetOrbit != null;
        public bool TargetIsVessel => Target != null && (Target.IsVessel || Target.IsPart);

        /// <summary>
        /// When the vessel already has nodes, pass the trajectory after the last one as <paramref name="orbit"/> and
        /// that node's time as <paramref name="ut"/>, so the new burn is planned from where those leave the vessel.
        /// </summary>
        public static PlanContext For(VesselComponent vessel, double ut, PatchedConicsOrbit orbit = null)
        {
            orbit ??= vessel?.Orbit.ToPatchedConics();
            if (orbit == null) return null;
            var ctx = new PlanContext { Vessel = vessel, Orbit = orbit, Body = orbit.referenceBody, UT = ut };
            var target = vessel.TargetObject;
            if (target == null) return ctx;
            ctx.Target = target;
            // Vessel targets go through VesselComponent.Orbit: SimulationObjectModel.Orbit is only an IKeplerOrbit.
            if (target.IsCelestialBody)
            {
                ctx.TargetBody = target.CelestialBody;
                ctx.TargetOrbit = target.CelestialBody?.Orbit.ToPatchedConics();
            }
            else if (target.IsVessel)
                ctx.TargetOrbit = target.Vessel?.Orbit.ToPatchedConics();
            else if (target.IsPart)
                ctx.TargetOrbit = target.Part?.PartOwner?.SimulationObject?.Vessel?.Orbit.ToPatchedConics();
            return ctx;
        }
    }

    /// <summary>
    /// Works out burns without touching the game. A port of the maneuver methods in Flight Plan's
    /// <c>FlightPlanPlugin</c> and <c>BurnTimeOption</c>, returning a <see cref="BurnPlan"/> instead of making a node.
    /// </summary>
    public static class Planner
    {
        /// <summary>The burn-time choices for a maneuver, first one being the default (original <c>SetOptionsList</c>).</summary>
        public static List<TimeReference> Options(ManeuverType type, PatchedConicsOrbit orbit)
        {
            bool closed = orbit != null && orbit.eccentricity < 1;
            var o = new List<TimeReference>();
            switch (type)
            {
                case ManeuverType.Circularize:
                    if (closed) o.Add(TimeReference.Apoapsis);
                    o.Add(TimeReference.Periapsis);
                    o.Add(TimeReference.Altitude);
                    o.Add(TimeReference.FixedTime);
                    break;
                case ManeuverType.NewPe:
                    if (closed) o.Add(TimeReference.Apoapsis);
                    o.Add(TimeReference.Periapsis);
                    o.Add(TimeReference.FixedTime);
                    o.Add(TimeReference.Altitude);
                    break;
                case ManeuverType.NewAp:
                    o.Add(TimeReference.Periapsis);
                    if (closed) o.Add(TimeReference.Apoapsis);
                    o.Add(TimeReference.FixedTime);
                    o.Add(TimeReference.Altitude);
                    break;
                case ManeuverType.NewPeAp:
                    if (closed) o.Add(TimeReference.Apoapsis);
                    o.Add(TimeReference.Periapsis);
                    o.Add(TimeReference.FixedTime);
                    o.Add(TimeReference.Altitude);
                    o.Add(TimeReference.EqAscending);
                    o.Add(TimeReference.EqDescending);
                    break;
                case ManeuverType.NewInc:
                    o.Add(TimeReference.EqCheapestNode);
                    o.Add(TimeReference.EqNearestNode);
                    o.Add(TimeReference.EqAscending);
                    o.Add(TimeReference.EqDescending);
                    o.Add(TimeReference.FixedTime);
                    if (closed) o.Add(TimeReference.Apoapsis);
                    break;
                case ManeuverType.NewLan:
                case ManeuverType.NewSma:
                    if (closed) o.Add(TimeReference.Apoapsis);
                    o.Add(TimeReference.Periapsis);
                    o.Add(TimeReference.FixedTime);
                    break;
                case ManeuverType.MatchPlanes:
                    o.Add(TimeReference.RelCheapestNode);
                    o.Add(TimeReference.RelNearestNode);
                    o.Add(TimeReference.RelAscending);
                    o.Add(TimeReference.RelDescending);
                    break;
                case ManeuverType.HohmannTransfer:
                case ManeuverType.CourseCorrection:
                case ManeuverType.MoonReturn:
                    o.Add(TimeReference.Computed);
                    break;
                case ManeuverType.InterceptTarget:
                    o.Add(TimeReference.FixedTime);
                    break;
                case ManeuverType.MatchVelocity:
                    o.Add(TimeReference.ClosestApproach);
                    o.Add(TimeReference.FixedTime);
                    break;
                case ManeuverType.PlanetaryTransfer:
                    o.Add(TimeReference.NextWindow);
                    o.Add(TimeReference.Asap);
                    break;
                case ManeuverType.FixAp:
                    o.Add(TimeReference.Periapsis);
                    o.Add(TimeReference.FixedTime);
                    break;
                case ManeuverType.FixPe:
                    if (closed) o.Add(TimeReference.Apoapsis);
                    o.Add(TimeReference.FixedTime);
                    break;
            }
            if (o.Count == 0) o.Add(TimeReference.None);
            return o;
        }

        /// <summary>
        /// The UT a time reference points at (original <c>BurnTimeOption.SetBurnTime</c>). NaN when it can't be
        /// worked out here: Computed/NextWindow/Asap come from the maneuver itself, and target references need a target.
        /// </summary>
        public static double BurnTime(TimeReference tr, PlanContext c, PlanInputs inputs)
        {
            var o = c.Orbit;
            double ut = c.UT;
            var tgt = c.TargetOrbit;
            switch (tr)
            {
                case TimeReference.Apoapsis: return o.eccentricity < 1 ? o.NextApoapsisTime(ut) : double.NaN;
                case TimeReference.Periapsis: return o.NextPeriapsisTime(ut);
                case TimeReference.ClosestApproach: return tgt != null ? o.NextClosestApproachTime(tgt, ut + 2) : double.NaN;
                case TimeReference.EqAscending: return o.TimeOfAscendingNodeEquatorial(ut);
                case TimeReference.EqDescending: return o.TimeOfDescendingNodeEquatorial(ut);
                case TimeReference.RelAscending: return tgt != null ? o.TimeOfAscendingNode(tgt, ut) : double.NaN;
                case TimeReference.RelDescending: return tgt != null ? o.TimeOfDescendingNode(tgt, ut) : double.NaN;
                case TimeReference.FixedTime: return ut + Math.Max(0, inputs.FixedTime);
                case TimeReference.Altitude:
                {
                    // Altitude above sea level, clamped to the orbit's Pe..Ap (as the original's input was).
                    double r = inputs.Altitude + o.referenceBody.radius;
                    if (o.eccentricity < 1) r = Math.Min(r, o.Apoapsis);
                    r = Math.Max(r, o.Periapsis);
                    return o.NextTimeOfRadius(ut, r);
                }
                case TimeReference.EqNearestNode:
                    return Math.Min(o.TimeOfAscendingNodeEquatorial(ut), o.TimeOfDescendingNodeEquatorial(ut));
                case TimeReference.EqCheapestNode:
                {
                    double an = o.TimeOfAscendingNodeEquatorial(ut), dn = o.TimeOfDescendingNodeEquatorial(ut);
                    return o.Radius(an) > o.Radius(dn) ? an : dn;
                }
                case TimeReference.RelNearestNode:
                    return tgt != null ? Math.Min(o.TimeOfAscendingNode(tgt, ut), o.TimeOfDescendingNode(tgt, ut)) : double.NaN;
                case TimeReference.RelCheapestNode:
                {
                    if (tgt == null) return double.NaN;
                    double an = o.TimeOfAscendingNode(tgt, ut), dn = o.TimeOfDescendingNode(tgt, ut);
                    return o.Radius(an) > o.Radius(dn) ? an : dn;
                }
                default: return double.NaN;
            }
        }

        public static bool NeedsTarget(ManeuverType type) => type switch
        {
            ManeuverType.MatchPlanes or ManeuverType.HohmannTransfer or ManeuverType.CourseCorrection
                or ManeuverType.InterceptTarget or ManeuverType.MatchVelocity or ManeuverType.PlanetaryTransfer => true,
            _ => false
        };

        /// <summary>Plans one maneuver. Never throws; failures come back as an Error status.</summary>
        public static BurnPlan Plan(ManeuverType type, TimeReference tr, PlanContext c, PlanInputs inputs, ResonantPlan resonant = null)
        {
            var plan = new BurnPlan { Maneuver = type, TimeRef = tr };
            if (c == null) { plan.Status = new PlanStatus(StatusLevel.Error, "NoVessel"); return plan; }
            // On the ground the "orbit" is a ballistic arc through the planet; nothing planned from it makes sense.
            if (c.Vessel != null && (c.Vessel.LandedOrSplashed || c.Vessel.Situation == VesselSituations.PreLaunch))
            {
                plan.Status = new PlanStatus(StatusLevel.Error, "OnSurface");
                return plan;
            }
            if (NeedsTarget(type) && !c.HasTarget) { plan.Status = new PlanStatus(StatusLevel.Error, "NoTarget"); return plan; }
            try
            {
                Solve(plan, c, inputs, resonant);
            }
            catch (Exception e)
            {
                SWLog.Warning($"Planning {type} {tr} failed: {e}");
                plan.UT = double.NaN;
                plan.Status = new PlanStatus(StatusLevel.Error, "NoSolution");
                return plan;
            }

            if (double.IsNaN(plan.UT) || double.IsInfinity(plan.UT) || plan.DeltaV == Vector3d.zero || !IsFinite(plan.DeltaV))
            {
                plan.UT = double.NaN;
                plan.Status ??= new PlanStatus(StatusLevel.Error, "NoSolution");
                if (plan.Status.Level != StatusLevel.Error) plan.Status = new PlanStatus(StatusLevel.Error, "NoSolution");
                return plan;
            }
            if (plan.UT < c.UT) plan.Warnings.Add(new PlanStatus(StatusLevel.Warning, "InPast"));

            plan.BurnVector = c.Orbit.DeltaVToManeuverNodeCoordinates(plan.UT, plan.DeltaV);
            try { plan.Result = c.Orbit.PerturbedOrbit(plan.UT, plan.DeltaV); }
            catch (Exception e) { SWLog.Warning($"Predicting the orbit after {type} failed: {e.Message}"); }
            plan.Status ??= new PlanStatus(StatusLevel.Ok, "Ready");
            return plan;
        }

        private static void Solve(BurnPlan p, PlanContext c, PlanInputs i, ResonantPlan resonant)
        {
            var o = c.Orbit;
            double R = c.Body.radius;
            var tgt = c.TargetOrbit;
            double ut = BurnTime(p.TimeRef, c, i);

            switch (p.Maneuver)
            {
                case ManeuverType.Circularize:
                    p.UT = ut;
                    if (!double.IsNaN(ut)) p.DeltaV = OrbitalManeuverCalculator.DeltaVToCircularize(o, ut);
                    break;

                case ManeuverType.NewPe:
                case ManeuverType.FixPe:
                {
                    double pe = p.Maneuver == ManeuverType.FixPe ? resonant?.Pe ?? double.NaN : i.NewPe + R;
                    if (double.IsNaN(pe)) break;
                    if (o.eccentricity < 1 && pe - R >= o.ApoapsisArl && p.Maneuver == ManeuverType.NewPe)
                    {
                        p.Status = new PlanStatus(StatusLevel.Error, "PeAboveAp");
                        break;
                    }
                    p.UT = ut;
                    if (!double.IsNaN(ut)) p.DeltaV = OrbitalManeuverCalculator.DeltaVToChangePeriapsis(o, ut, pe);
                    break;
                }

                case ManeuverType.NewAp:
                case ManeuverType.FixAp:
                {
                    double ap = p.Maneuver == ManeuverType.FixAp ? resonant?.Ap ?? double.NaN : i.NewAp + R;
                    if (double.IsNaN(ap)) break;
                    if (ap - R <= o.PeriapsisArl && p.Maneuver == ManeuverType.NewAp)
                    {
                        p.Status = new PlanStatus(StatusLevel.Error, "ApBelowPe");
                        break;
                    }
                    p.UT = ut;
                    if (!double.IsNaN(ut)) p.DeltaV = OrbitalManeuverCalculator.DeltaVToChangeApoapsis(o, ut, ap);
                    break;
                }

                case ManeuverType.NewPeAp:
                {
                    double pe = i.NewPe + R, ap = i.NewAp + R;
                    if (pe > ap)
                    {
                        (pe, ap) = (ap, pe);
                        p.Warnings.Add(new PlanStatus(StatusLevel.Warning, "PeAboveApSwapped"));
                    }
                    p.UT = ut;
                    if (!double.IsNaN(ut)) p.DeltaV = OrbitalManeuverCalculator.DeltaVToEllipticize(o, ut, pe, ap);
                    break;
                }

                case ManeuverType.NewInc:
                    p.UT = ut;
                    if (!double.IsNaN(ut)) p.DeltaV = OrbitalManeuverCalculator.DeltaVToChangeInclination(o, ut, i.NewInc);
                    break;

                case ManeuverType.NewLan:
                    p.Warnings.Add(Math.Abs(o.inclination) < 10
                        ? new PlanStatus(StatusLevel.Warning, "LowInclination", o.inclination)
                        : new PlanStatus(StatusLevel.Warning, "Experimental"));
                    p.UT = ut;
                    if (!double.IsNaN(ut)) p.DeltaV = OrbitalManeuverCalculator.DeltaVToShiftLAN(o, ut, i.NewLan);
                    break;

                case ManeuverType.NewSma:
                    p.UT = ut;
                    if (!double.IsNaN(ut)) p.DeltaV = OrbitalManeuverCalculator.DeltaVForSemiMajorAxis(o, ut, i.NewSma + R);
                    break;

                case ManeuverType.MatchPlanes:
                {
                    bool ascending = p.TimeRef switch
                    {
                        TimeReference.RelAscending => true,
                        TimeReference.RelDescending => false,
                        TimeReference.RelNearestNode => o.TimeOfAscendingNode(tgt, c.UT) < o.TimeOfDescendingNode(tgt, c.UT),
                        _ => o.Radius(o.TimeOfAscendingNode(tgt, c.UT)) > o.Radius(o.TimeOfDescendingNode(tgt, c.UT))
                    };
                    double burnUT;
                    p.DeltaV = ascending
                        ? OrbitalManeuverCalculator.DeltaVAndTimeToMatchPlanesAscending(o, tgt, c.UT, out burnUT)
                        : OrbitalManeuverCalculator.DeltaVAndTimeToMatchPlanesDescending(o, tgt, c.UT, out burnUT);
                    p.UT = burnUT;
                    break;
                }

                case ManeuverType.HohmannTransfer:
                {
                    // As the original: rendezvous and capture on, coplanar off, only the first burn becomes a node.
                    var (dv1, ut1, _, _) = OrbitalManeuverCalculator.DeltaVAndTimeForHohmannTransfer(o, tgt, c.UT,
                        0.0, false, false, true, true);
                    p.DeltaV = dv1;
                    p.UT = ut1;
                    if (o.RelativeInclination(tgt) > 1) p.Warnings.Add(new PlanStatus(StatusLevel.Warning, "MatchPlanesFirst"));
                    break;
                }

                case ManeuverType.CourseCorrection:
                {
                    double burnUT;
                    if (c.TargetBody != null)
                    {
                        double peR = (i.InterceptDistanceCelestial < 0 ? 50_000 : i.InterceptDistanceCelestial) + c.TargetBody.radius;
                        p.DeltaV = OrbitalManeuverCalculator.DeltaVAndTimeForCheapestCourseCorrection(o, c.UT, tgt, c.TargetBody, peR, out burnUT);
                    }
                    else
                    {
                        double ca = i.InterceptDistanceVessel < 0 ? 100 : i.InterceptDistanceVessel;
                        p.DeltaV = OrbitalManeuverCalculator.DeltaVAndTimeForCheapestCourseCorrection(o, c.UT, tgt, ca, out burnUT);
                    }
                    p.UT = burnUT;
                    break;
                }

                case ManeuverType.InterceptTarget:
                {
                    // Experimental in the original ("places node at wrong time"); kept behind the Experimental setting.
                    p.Warnings.Add(new PlanStatus(StatusLevel.Warning, "Experimental"));
                    double offset = c.TargetBody != null ? tgt.referenceBody.radius + 50_000 : 100;
                    p.UT = ut;
                    if (!double.IsNaN(ut))
                        (p.DeltaV, _) = OrbitalManeuverCalculator.DeltaVToInterceptAtTime(o, ut, tgt, c.UT + i.InterceptTime, offset);
                    break;
                }

                case ManeuverType.MatchVelocity:
                    p.UT = ut;
                    if (!double.IsNaN(ut)) p.DeltaV = OrbitalManeuverCalculator.DeltaVToMatchVelocities(o, ut, tgt);
                    break;

                case ManeuverType.MoonReturn:
                {
                    if (o.eccentricity > 0.2)
                    {
                        p.Status = new PlanStatus(StatusLevel.Error, "TooEccentric", o.eccentricity);
                        break;
                    }
                    var primary = c.Body.Orbit?.referenceBody;
                    if (primary == null || c.Body.IsStar) { p.Status = new PlanStatus(StatusLevel.Error, "NotAtMoon"); break; }
                    var (dv, burnUT) = OrbitalManeuverCalculator.DeltaVAndTimeForMoonReturnEjection(o, c.UT, i.MoonReturnPe + primary.radius);
                    p.DeltaV = dv;
                    p.UT = burnUT;
                    break;
                }

                case ManeuverType.PlanetaryTransfer:
                {
                    if (c.Body.Orbit == null || c.Body.referenceBody == null || c.Body.IsStar)
                    {
                        p.Status = new PlanStatus(StatusLevel.Error, "NotAtPlanet");
                        break;
                    }
                    if (c.Body.Orbit.ToPatchedConics().RelativeInclination(tgt) > 30)
                        p.Warnings.Add(new PlanStatus(StatusLevel.Warning, "TargetInclined", c.Body.Orbit.ToPatchedConics().RelativeInclination(tgt)));
                    else if (Vector3d.Angle(o.OrbitNormal(), c.Body.Orbit.OrbitNormal()) > 10)
                        p.Warnings.Add(new PlanStatus(StatusLevel.Warning, "ParkingInclined", Vector3d.Angle(o.OrbitNormal(), c.Body.Orbit.OrbitNormal())));
                    else if (o.eccentricity > 0.2)
                        p.Warnings.Add(new PlanStatus(StatusLevel.Warning, "ParkingEccentric", o.eccentricity));
                    p.Warnings.Add(new PlanStatus(StatusLevel.Warning, "CourseCorrectionLikely"));
                    // The original read the dropdown's text to decide this; Asap skips waiting for the phase angle.
                    p.DeltaV = OrbitalManeuverCalculator.DeltaVAndTimeForInterplanetaryTransferEjection(o, c.UT, tgt,
                        p.TimeRef != TimeReference.Asap, out double burnUT);
                    p.UT = burnUT;
                    break;
                }
            }
        }

        private static bool IsFinite(Vector3d v) =>
            !double.IsNaN(v.x) && !double.IsNaN(v.y) && !double.IsNaN(v.z) &&
            !double.IsInfinity(v.x) && !double.IsInfinity(v.y) && !double.IsInfinity(v.z);
    }
}
