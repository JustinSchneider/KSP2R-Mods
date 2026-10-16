#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WayfarersWings.Models.Conditions;

namespace WayfarersWings.Models.Configs.Template
{
    [Serializable]
    public class WingTemplateConfigRanked
    {
        public List<WingConfig>? partials;
    }
}
