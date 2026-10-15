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
        /// <summary>The same layers in the 1x pixel art (60×16), for small ribbons.</summary>
        public List<string> SmallLayers = new List<string>();
    }

    /// <summary>
    /// Optional support for Wayfarer's Wings Redux (the port of Kerbalight's rank ribbons). It goes through the
    /// mod's public <c>WingsApi</c> by reflection, so Agency Heritage neither requires it nor breaks when it's
    /// missing or its API moves on: the museum then simply shows no ribbons.
    /// </summary>
    public static class WingsBridge
    {
        private const string ApiType = "WayfarersWingsRedux.WingsApi";
        /// <summary>The <c>WingsApi.ApiVersion</c> this bridge was written against.</summary>
        private const int SupportedApiVersion = 1;
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private static bool _searched;
        private static MethodInfo _allAwards, _layerAddress, _openKerbal;
        private static bool _loggedFailure;

        public static bool IsInstalled
        {
            get
            {
                if (!_searched)
                {
                    _searched = true;
                    var api = AppDomain.CurrentDomain.GetAssemblies()
                        .Select(a => a.GetType(ApiType, false))
                        .FirstOrDefault(t => t != null);
                    if (api == null) return false;
                    var version = api.GetField("ApiVersion", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                    if (!(version is int v) || v != SupportedApiVersion)
                    {
                        SWLog.Warning($"Wayfarer's Wings API version {version} isn't supported (expected {SupportedApiVersion}); ribbons are hidden");
                        return false;
                    }
                    _allAwards = api.GetMethod("AllAwards", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
                    _layerAddress = api.GetMethod("LayerAddress", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string), typeof(bool) }, null);
                    _openKerbal = api.GetMethod("OpenKerbal", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string) }, null);
                    if (_allAwards != null) SWLog.Info("Wayfarer's Wings found: showing ribbons in the museum");
                }
                return _allAwards != null;
            }
        }

        /// <summary>Every ribbon earned in the loaded campaign, oldest first. Empty if Wayfarer's Wings isn't installed.</summary>
        public static List<WingAward> AllAwards()
        {
            var awards = new List<WingAward>();
            if (!IsInstalled) return awards;
            try
            {
                if (!(_allAwards.Invoke(null, null) is IEnumerable list)) return awards;
                foreach (var info in list)
                {
                    // A superseded ribbon was replaced by a higher rank of the same wing.
                    if (Get(info, "Superseded") is bool superseded && superseded) continue;
                    var award = new WingAward
                    {
                        KerbalName = Get(info, "KerbalName") as string,
                        UT = Get(info, "UT") is double ut ? ut : 0,
                        WingName = Get(info, "WingName") as string ?? Get(info, "WingCode") as string,
                        IsFirst = Get(info, "IsFirst") is bool first && first,
                        Points = Get(info, "Points") is int points ? points : 0,
                    };
                    if (Get(info, "ImageLayers") is IEnumerable<string> layers)
                    {
                        award.ImageLayers.AddRange(layers);
                        award.SmallLayers.AddRange(award.ImageLayers.Select(Small));
                    }
                    awards.Add(award);
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

        /// <summary>True if Wayfarer's Wings can open its window on a kerbal.</summary>
        public static bool CanOpenKerbal => IsInstalled && _openKerbal != null;

        /// <summary>Opens Wayfarer's Wings on a kerbal's record; false if it can't.</summary>
        public static bool OpenKerbal(string kerbalName)
        {
            if (!CanOpenKerbal) return false;
            try { return _openKerbal.Invoke(null, new object[] { kerbalName }) is bool opened && opened; }
            catch (Exception e)
            {
                SWLog.Warning($"Couldn't open Wayfarer's Wings on {kerbalName}: {e.Message}");
                return false;
            }
        }

        private static string Small(string layer)
        {
            if (_layerAddress == null) return layer;
            try { return _layerAddress.Invoke(null, new object[] { layer, false }) as string ?? layer; }
            catch (Exception) { return layer; }
        }

        private static object Get(object target, string name)
        {
            if (target == null) return null;
            var type = target.GetType();
            var field = type.GetField(name, Any);
            if (field != null) return field.GetValue(target);
            return type.GetProperty(name, Any)?.GetValue(target);
        }
    }
}
