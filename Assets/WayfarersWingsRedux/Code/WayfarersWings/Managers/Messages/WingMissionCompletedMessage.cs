#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KSP.Sim.impl;


/// <summary>
/// Message sent when a vessel is recovered, after all Wayfarers Wings conditions are checked.
/// And mission wings are already awarded.
/// Furthermore every Kerbal is updated with the mission count, etc.
/// </summary>

namespace WayfarersWings.Managers.Messages
{
    public class WingMissionCompletedMessage : WingBaseMessage
    {
        public VesselComponent? Vessel { get; set; }

        public WingMissionCompletedMessage(VesselComponent? vessel)
        {
            Vessel = vessel;
        }
    }
}
