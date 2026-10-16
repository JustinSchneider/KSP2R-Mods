#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace WayfarersWings.Models.Configs
{
    [Serializable]
    public class CelestialBodyConfig
    {
        public string name = null!;
        public string code = null!;
        public string imageLayer = null!;
        public int points;
    }
}
