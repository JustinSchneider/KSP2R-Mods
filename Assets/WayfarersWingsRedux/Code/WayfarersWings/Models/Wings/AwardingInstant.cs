#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// A class to represent the exact instant when a bunch of kerbals
/// are awarded a wing.
/// </summary>

namespace WayfarersWings.Models.Wings
{
    public class AwardingInstant
    {
        /// <summary>
        /// Used internally by WingSessionManager to track which first wings
        /// have been unlocked.
        /// This allows to unlock first wings for all kerbals in a vessel
        /// </summary>
        public List<string> FirstWingsUnlocked { get; set; } = new();

        public AwardingInstant() { }
    }
}
