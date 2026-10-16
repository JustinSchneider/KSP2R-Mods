#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WayfarersWings.Logging;
using WayfarersWings.Models.Conditions;
using WayfarersWings.Models.Configs;

namespace WayfarersWings.Models.Wings
{
    public class Wing
    {
        private static readonly ManualLogSource Logger = WayfarersWings.Logging.Logger.CreateLogSource("WayfarersWings.Wing");

        // ReSharper disable once InconsistentNaming
        public WingConfig config { get; set; }

        public string DisplayName { get; set; } = null!;
        public string Description { get; set; } = null!;

        private readonly List<BaseCondition> _conditions = new();

        public Wing(WingConfig config)
        {
            this.config = config;
            VerifyConfig();

            foreach (var condition in this.config.conditions)
            {
                condition.Configure();
                _conditions.Add(condition);
            }
        }

        /// <summary>
        /// Ensure that the wing config is valid
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        private void VerifyConfig()
        {
            if (config.conditions.Count == 0)
                Logger.LogWarning($"Wing '{config.name}' has no conditions");

            if (config.name == null)
                throw new InvalidOperationException("Wing name is null");

            if (config.isFirst && !config.name.EndsWith("_first"))
                throw new InvalidOperationException(
                    $"Wing '{config.name}' is marked as first but does not end with '_first'");
        }

        public bool Check(Transaction transaction)
        {
            foreach (var condition in _conditions)
            {
                if (!condition.IsValid(transaction)) return false;
            }

            return true;
        }
    }
}
