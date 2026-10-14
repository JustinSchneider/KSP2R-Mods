using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace AgencyHeritage
{
    /// <summary>A Wayfarer's Wings ribbon a kerbal earned.</summary>
    public class WingAward
    {
        public string KerbalName;
        public string WingName;
        public double UT;
        public bool IsFirst;
        public int Points;
        /// <summary>Addressable sprite keys, drawn on top of each other to make the ribbon.</summary>
        public List<string> ImageLayers = new List<string>();
    }

    /// <summary>
    /// Optional support for Wayfarer's Wings (Kerbalight's rank ribbons). Everything goes through reflection, so
    /// Agency Heritage neither requires it nor breaks when it's missing or its internals change: the museum then
    /// simply shows no ribbons.
    /// </summary>
    public static class WingsBridge
    {
        private const string SessionManagerType = "WayfarersWings.Models.Session.WingsSessionManager";
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private static bool _searched;
        private static Type _sessionManager;
        private static bool _loggedFailure;

        public static bool IsInstalled
        {
            get
            {
                if (!_searched)
                {
                    _searched = true;
                    _sessionManager = AppDomain.CurrentDomain.GetAssemblies()
                        .Select(a => a.GetType(SessionManagerType, false))
                        .FirstOrDefault(t => t != null);
                    if (_sessionManager != null) SWLog.Info("Wayfarer's Wings found: showing ribbons in the museum");
                }
                return _sessionManager != null;
            }
        }

        /// <summary>Every ribbon earned in the loaded campaign, oldest first. Empty if Wayfarer's Wings isn't installed.</summary>
        public static List<WingAward> AllAwards()
        {
            var awards = new List<WingAward>();
            if (!IsInstalled) return awards;
            try
            {
                var instance = Get(null, _sessionManager, "Instance");
                if (!(Get(instance, "KerbalProfiles") is IDictionary profiles)) return awards;

                foreach (var profile in profiles.Values)
                {
                    string kerbal = Get(profile, "fullName") as string;
                    if (!(Get(profile, "Entries") is IEnumerable entries)) continue;
                    foreach (var entry in entries)
                    {
                        // A superseded ribbon was replaced by a higher rank of the same wing.
                        if (Get(entry, "isSuperseeded") is bool superseded && superseded) continue;
                        var wing = Get(entry, "Wing");
                        var config = Get(wing, "config");
                        var award = new WingAward
                        {
                            KerbalName = kerbal,
                            UT = Get(entry, "universeTime") is double ut ? ut : 0,
                            WingName = Get(wing, "DisplayName") as string ?? Get(entry, "wingCode") as string,
                            IsFirst = Get(config, "isFirst") is bool first && first,
                            Points = Get(config, "points") is int points ? points : 0,
                        };
                        if (Get(config, "imageLayers") is IEnumerable<string> layers) award.ImageLayers.AddRange(layers);
                        awards.Add(award);
                    }
                }
            }
            catch (Exception e)
            {
                if (!_loggedFailure) SWLog.Warning($"Couldn't read Wayfarer's Wings ribbons: {e.Message}");
                _loggedFailure = true;
            }
            awards.Sort((a, b) => a.UT.CompareTo(b.UT));
            return awards;
        }

        private static object Get(object target, string name) => target == null ? null : Get(target, target.GetType(), name);

        private static object Get(object target, Type type, string name)
        {
            for (var t = type; t != null; t = t.BaseType)
            {
                var property = t.GetProperty(name, Any);
                if (property != null) return property.GetValue(target);
                var field = t.GetField(name, Any);
                if (field != null) return field.GetValue(target);
            }
            return null;
        }
    }
}
