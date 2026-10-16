#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KSP.Sim.impl;

namespace WayfarersWings.Managers.Observer.Properties.Events
{
    public class WingVesselAtmDensityUpdatedMessage : WingVesselUpdatedMessage
    {
        public int AtmDensity { get; }

        public WingVesselAtmDensityUpdatedMessage(VesselComponent vessel, int atmDensity) : base(vessel)
        {
            AtmDensity = atmDensity;
        }
    }
}
