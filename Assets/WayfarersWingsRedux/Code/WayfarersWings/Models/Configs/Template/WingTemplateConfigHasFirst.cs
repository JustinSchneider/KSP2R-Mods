#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace WayfarersWings.Models.Configs.Template
{
    [Serializable]
    public class WingTemplateConfigHasFirst
    {
        public string? description;
        public string imageLayer = null!;
    }
}
