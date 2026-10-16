#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KSP.Sim.impl;
using WayfarersWings.Managers.Messages;

namespace WayfarersWings.Managers.Observer.Properties.Events
{
    public abstract class WingVesselUpdatedMessage : WingBaseMessage
    {
        public VesselComponent Vessel { get; }

        protected WingVesselUpdatedMessage(VesselComponent vessel)
        {
            Vessel = vessel;
        }
    }
}
