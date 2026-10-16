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
    public class WingVesselHasMovedOnSurfaceUpdatedMessage : WingVesselUpdatedMessage
    {
        public bool HasMovedOnSurface { get; }

        public WingVesselHasMovedOnSurfaceUpdatedMessage(VesselComponent vessel, bool hasMovedOnSurface) : base(vessel)
        {
            HasMovedOnSurface = hasMovedOnSurface;
        }
    }
}
