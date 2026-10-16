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
    public class WingVesselGeeForceUpdatedMessage : WingVesselUpdatedMessage
    {
        public int GeeForce { get; }

        public WingVesselGeeForceUpdatedMessage(VesselComponent vessel, int geeForce) : base(vessel)
        {
            GeeForce = geeForce;
        }
    }
}
