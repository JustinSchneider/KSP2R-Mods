#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace WayfarersWings.Models.Configs.Planets
{
    [Serializable]
    public class PlanetsConfig
    {
        public List<CelestialBodyConfig> celestialBodies = new();

        public static PlanetsConfig? Deserialize(string rawJson)
        {
            return JsonConvert.DeserializeObject<PlanetsConfig>(rawJson);
        }
    }
}
