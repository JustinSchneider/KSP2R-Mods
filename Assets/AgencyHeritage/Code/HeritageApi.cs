using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using SpaceWarp2.API.Features;

namespace AgencyHeritage
{
    /// <summary>
    /// One era of the agency's identity, as other mods see it. A copy: changing it changes nothing in the save.
    /// Plain fields only, so a mod that reaches Agency Heritage by reflection can read them without referencing it.
    /// </summary>
    public sealed class EraInfo
    {
        /// <summary>Stable id of the era, kept in the save.</summary>
        public string Id;
        /// <summary>1 for the founding era, counting up with each rebrand.</summary>
        public int Number;
        public string Name;
        /// <summary>The flag, as the game's agency flag setting names it.</summary>
        public string FlagSpriteLocation;
        public Color Base;
        public Color Accent;
        /// <summary>The motto entered at the rebrand; null if none.</summary>
        public string Motto;
        /// <summary>In-game time the era began; 0 for the founding era.</summary>
        public double StartUT;
        /// <summary>In-game time the next era began; +∞ for the current era.</summary>
        public double EndUT;
        public bool IsCurrent;
    }

    /// <summary>
    /// The public face of Agency Heritage for other mods. Everything else in this assembly may change between
    /// versions; this won't without <see cref="ApiVersion"/> going up.
    /// <para>History is kept per save, so it changes wholesale when a save loads: listen to <see cref="Loaded"/> as
    /// well as <see cref="Rebranded"/> to follow the current era.</para>
    /// </summary>
    public static class HeritageApi
    {
        /// <summary>Goes up whenever a member here changes or is removed. Additions don't change it.</summary>
        public const int ApiVersion = 1;

        internal static AgencyHeritagePlugin Plugin;

        /// <summary>True while a campaign that plays with the mod is loaded and its history is ready to read.</summary>
        public static bool IsReady => ActiveFeatures.IsModEnabled(AgencyHeritagePlugin.ModGuid) && Plugin?.Data != null && Plugin.Data.Eras.Count > 0;

        /// <summary>The era the agency is in now; null when <see cref="IsReady"/> is false.</summary>
        public static EraInfo CurrentEra => IsReady ? Snapshot(Plugin.Data.CurrentEra()) : null;

        /// <summary>Every era of the loaded campaign, founding era first; empty when <see cref="IsReady"/> is false.</summary>
        public static IReadOnlyList<EraInfo> Eras =>
            IsReady ? Plugin.Data.Eras.Select(Snapshot).ToList() : new List<EraInfo>();

        /// <summary>The era that was current at an in-game time; null when <see cref="IsReady"/> is false.</summary>
        public static EraInfo EraAt(double ut)
        {
            if (!IsReady) return null;
            var data = Plugin.Data;
            var found = data.Eras[0];
            foreach (var era in data.Eras)
                if (era.StartUT <= ut) found = era;
            return Snapshot(found);
        }

        /// <summary>The agency was rebranded: (the era that ended, the era that began).</summary>
        public static event Action<EraInfo, EraInfo> Rebranded;

        /// <summary>A campaign's history is ready: a save was loaded, or a new campaign recorded its founding era.</summary>
        public static event Action Loaded;

        internal static void RaiseRebranded(BrandEra previous, BrandEra current) =>
            Raise(Rebranded, handler => ((Action<EraInfo, EraInfo>)handler)(Snapshot(previous), Snapshot(current)));

        internal static void RaiseLoaded() => Raise(Loaded, handler => ((Action)handler)());

        /// <summary>Calls each listener on its own, so one mod's exception doesn't keep the others from hearing.</summary>
        private static void Raise(Delegate listeners, Action<Delegate> call)
        {
            if (listeners == null) return;
            foreach (var handler in listeners.GetInvocationList())
            {
                try { call(handler); }
                catch (Exception e)
                {
                    SWLog.Warning($"A listener in {handler.Method.DeclaringType?.Assembly.GetName().Name} failed: {e}");
                }
            }
        }

        private static EraInfo Snapshot(BrandEra era)
        {
            if (era == null) return null;
            var data = Plugin.Data;
            return new EraInfo
            {
                Id = era.Id,
                Number = data.Eras.IndexOf(era) + 1,
                Name = era.Name,
                FlagSpriteLocation = era.FlagSpriteLocation,
                Base = era.Base,
                Accent = era.Accent,
                Motto = era.Note,
                StartUT = era.StartUT,
                EndUT = data.EndUT(era),
                IsCurrent = era == data.CurrentEra(),
            };
        }
    }
}
