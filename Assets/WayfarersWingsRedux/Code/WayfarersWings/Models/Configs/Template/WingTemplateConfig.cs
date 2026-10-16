#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace WayfarersWings.Models.Configs.Template
{
    [Serializable]
    public class WingTemplateConfig
    {
        [JsonRequired]
        public string name = null!;

        public WingTemplateConfigCelestialBody? celestialBody;
        public WingTemplateConfigHasFirst? hasFirst;
        public WingTemplateConfigRanked? ranked;

        [JsonRequired]
        public WingConfig template = null!;
    }
}
