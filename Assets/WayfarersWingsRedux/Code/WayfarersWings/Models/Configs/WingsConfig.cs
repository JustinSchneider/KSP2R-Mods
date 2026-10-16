#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using WayfarersWings.Models.Conditions;
using WayfarersWings.Models.Conditions.Json;
using WayfarersWings.Models.Configs.Template;
using ErrorEventArgs = Newtonsoft.Json.Serialization.ErrorEventArgs;


/// <summary>
/// Allows for the configuration of the wings dynamically, through JSON config
/// files in addressables.
/// </summary>

namespace WayfarersWings.Models.Configs
{
    [Serializable]
    public class WingsConfig
    {
        public List<WingConfig> wings = new();
        public List<WingTemplateConfig> templates = new();

        private static JsonSerializerSettings _jsonSerializerSettings = new()
        {
            NullValueHandling = NullValueHandling.Include,
            Formatting = Formatting.Indented,
        };

        public static WingsConfig? Deserialize(string rawJson, out List<Exception> exceptions)
        {
            List<Exception> foundExceptions = new();
            var jsonSerializerSettings = new JsonSerializerSettings()
            {
                NullValueHandling = NullValueHandling.Include,
                Formatting = Formatting.Indented,

                Error = delegate(object sender, ErrorEventArgs args)
                {
                    if (args.CurrentObject != args.ErrorContext.OriginalObject) return;

                    foundExceptions.Add(args.ErrorContext.Error);
                    args.ErrorContext.Handled = true;
                },
                Converters = new List<JsonConverter>()
                {
                    new BaseConditionJsonConverter()
                }
            };

            var result = JsonConvert.DeserializeObject<WingsConfig>(rawJson, jsonSerializerSettings);
            exceptions = foundExceptions;
            return result;
        }
    }
}
