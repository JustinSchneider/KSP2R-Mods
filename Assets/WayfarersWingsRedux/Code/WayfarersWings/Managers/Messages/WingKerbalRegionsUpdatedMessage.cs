#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WayfarersWings.Models.Session;


/// <summary>
/// Used in ConditionEventRegistry to process the condition when a KerbalProfile
/// discoverables/biomes are updated.
/// </summary>

namespace WayfarersWings.Managers.Messages
{
    public class WingKerbalRegionsUpdatedMessage : WingKerbalProfileUpdatedMessage
    {
        public WingKerbalRegionsUpdatedMessage(KerbalProfile kerbalProfile) : base(kerbalProfile) { }
    }
}
