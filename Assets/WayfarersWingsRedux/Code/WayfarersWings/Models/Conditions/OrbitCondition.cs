#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using KSP.Messages;
using WayfarersWings.Models.Conditions.Events;
using WayfarersWings.Models.Wings;


/// <summary>
/// Check the vessel's orbit parameters
/// </summary>

using WayfarersWings.Utility;

namespace WayfarersWings.Models.Conditions
{
    [Serializable]
    [ConditionTriggerEvent(typeof(StableOrbitCreatedMessage))]
    public class OrbitCondition : BaseCondition
    {
        /// Check if the vessel is in a stable orbit
        public bool? isStable;

        /// Check if the vessel's eccentricity is less than this value
        public float? maxEccentricity;

        /// Check if the vessel's eccentricity is greater than this value
        public float? minEccentricity;

        public override bool IsValid(Transaction transaction)
        {
            // if (transaction.Message is not StableOrbitCreatedMessage message) ;
            if (isStable == true && !(GameInternals.IsStableOrbit(transaction.Vessel)))
                return false;
            if (maxEccentricity != null && transaction.Vessel?.Orbit.eccentricity > maxEccentricity)
                return false;
            if (minEccentricity != null && transaction.Vessel?.Orbit.eccentricity < minEccentricity)
                return false;

            // Avoid triggering on orbits that are too low. We can't check for exact 0 since this mean just the telemetry isn't initialized
            if ((transaction.Vessel?.AltitudeFromTerrain < 100 && transaction.Vessel?.AltitudeFromTerrain != 0) ||
                transaction.Vessel?.Splashed == true ||
                transaction.Vessel?.Landed == true)
                return false;

            return true;
        }

        public override void Configure() { }
    }
}
