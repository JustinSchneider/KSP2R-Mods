#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KSP.Game;
using WayfarersWings.Models.Wings;

namespace WayfarersWings.Managers.Messages
{
    public class WingAwardedMessage : WingBaseMessage
    {
        public KerbalInfo KerbalInfo { get; set; }
        public Wing AwardedWing { get; set; }

        public WingAwardedMessage(KerbalInfo kerbalInfo, Wing awardedWing)
        {
            KerbalInfo = kerbalInfo;
            AwardedWing = awardedWing;
        }
    }
}
