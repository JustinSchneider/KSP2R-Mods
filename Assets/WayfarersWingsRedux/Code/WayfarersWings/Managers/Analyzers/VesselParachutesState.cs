#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace WayfarersWings.Managers.Analyzers
{
    public class VesselParachutesState
    {
        public int deployedCount = 0;
        public int stowedCount = 0;
        public int semiDeployedCount = 0;
        public int activeCount = 0;
        public int cutCount = 0;


        public bool DidUseParachutes => deployedCount > 0 || semiDeployedCount > 0 || activeCount > 0 || cutCount > 0;
    }
}
