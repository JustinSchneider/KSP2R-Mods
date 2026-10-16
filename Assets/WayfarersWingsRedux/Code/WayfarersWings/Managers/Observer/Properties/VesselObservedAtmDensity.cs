#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KSP.Sim.impl;
using WayfarersWings.Managers.Messages;
using WayfarersWings.Managers.Observer.Properties.Events;
using WayfarersWings.Managers.Observer.Properties.Types;


/// <summary>
/// Atmospheric density, expressed in mg/L, rounded to int
/// </summary>

namespace WayfarersWings.Managers.Observer.Properties
{
    public class VesselObservedAtmDensity : VesselObservedDiscreteProperty
    {
        protected override int GetValue(VesselComponent vessel)
        {
            return (int)(vessel.AtmDensity * 1000);
        }

        protected override WingVesselUpdatedMessage CreateTriggeredMessage(VesselComponent vessel)
        {
            return new WingVesselAtmDensityUpdatedMessage(vessel, Value);
        }
    }
}
