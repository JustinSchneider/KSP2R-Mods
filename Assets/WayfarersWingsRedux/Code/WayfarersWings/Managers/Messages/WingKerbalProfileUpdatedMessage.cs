#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WayfarersWings.Models.Session;

namespace WayfarersWings.Managers.Messages
{
    public class WingKerbalProfileUpdatedMessage : WingBaseMessage
    {
        public WingKerbalProfileUpdatedMessage(KerbalProfile kerbalProfile)
        {
            KerbalProfile = kerbalProfile;
        }

        public KerbalProfile KerbalProfile { get; set; }
    }
}
