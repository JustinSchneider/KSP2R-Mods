using System;
using System.Collections.Generic;
using System.Linq;
using KSP.Sim.impl;
using WayfarersWings.Managers;
using WayfarersWings.Managers.Messages;
using WayfarersWings.Models.Session;
using WayfarersWings.Models.Wings;

namespace WayfarersWingsRedux
{
    /// <summary>
    /// One ribbon held by one kerbal, as other mods see it. A copy: changing it changes nothing in the save.
    /// Plain fields only, so a mod that reaches Wayfarer's Wings by reflection can read them without referencing it.
    /// </summary>
    public sealed class WingAwardInfo
    {
        /// <summary>The kerbal's roster id, as <c>IGGuid.ToString()</c> prints it.</summary>
        public string KerbalId;
        /// <summary>The kerbal's full name. Kept even after the kerbal has left the roster.</summary>
        public string KerbalName;
        /// <summary>The wing's stable code, as stored in saves.</summary>
        public string WingCode;
        /// <summary>The wing's name in the game's current language.</summary>
        public string WingName;
        public string Description;
        /// <summary>In-game time the ribbon was awarded.</summary>
        public double UT;
        /// <summary>True for a "first ever" ribbon, which only one crew can earn.</summary>
        public bool IsFirst;
        public int Points;
        /// <summary>A higher rank of the same chain has replaced this ribbon on the kerbal's rack.</summary>
        public bool Superseded;
        /// <summary>
        /// Addressable sprite keys of the ribbon's layers, bottom first, at full (2x) resolution.
        /// Use <see cref="WingsApi.LayerAddress"/> for the 1x versions.
        /// </summary>
        public List<string> ImageLayers;
    }

    /// <summary>
    /// The public face of Wayfarer's Wings Redux for other mods. Everything else in this assembly may change between
    /// versions; this won't without <see cref="ApiVersion"/> going up.
    /// <para>Ribbons are kept per save, so they change wholesale when a save loads: listen to <see cref="Loaded"/>
    /// as well as <see cref="Awarded"/> and <see cref="Revoked"/>.</para>
    /// </summary>
    public static class WingsApi
    {
        /// <summary>Goes up whenever a member here changes or is removed. Additions don't change it.</summary>
        public const int ApiVersion = 1;

        /// <summary>True once the ribbon definitions are loaded and a campaign's ribbons are in the session.</summary>
        public static bool IsReady => Core.Instance.WingsPool.IsInitialized;

        /// <summary>Every ribbon held by every kerbal of the loaded campaign, oldest first.</summary>
        public static List<WingAwardInfo> AllAwards()
        {
            var awards = new List<WingAwardInfo>();
            if (!IsReady) return awards;
            foreach (var profile in WingsSessionManager.Instance.KerbalProfiles.Values)
                foreach (var entry in profile.Entries)
                    awards.Add(Snapshot(profile, entry));
            return awards.OrderBy(a => a.UT).ToList();
        }

        /// <summary>The ribbons of one kerbal, by full name, oldest first.</summary>
        public static List<WingAwardInfo> AwardsFor(string kerbalName) =>
            AllAwards().Where(a => a.KerbalName == kerbalName).ToList();

        /// <summary>The addressable key of a ribbon layer at 2x (<paramref name="hiDpi"/>) or 1x resolution.</summary>
        public static string LayerAddress(string layer, bool hiDpi) =>
            hiDpi || layer == null ? layer : layer.Replace("Layers/", "Layers/1x/");

        /// <summary>
        /// Opens the Wayfarer's Wings window on a kerbal's record. False if no kerbal has that full name, in the roster
        /// or among the profiles of kerbals who have left it.
        /// </summary>
        public static bool OpenKerbal(string kerbalName)
        {
            if (!IsReady || string.IsNullOrEmpty(kerbalName) || WayfarersWingsReduxPlugin.Instance == null) return false;
            var roster = WingsSessionManager.Roster;
            if (roster != null && roster.TryGetKerbalByName(kerbalName, out var kerbal) && kerbal != null)
                return WayfarersWingsReduxPlugin.Instance.ShowKerbal(kerbal.Id);
            var profile = WingsSessionManager.Instance.KerbalProfiles.Values.FirstOrDefault(p => p.fullName == kerbalName);
            return profile != null && WayfarersWingsReduxPlugin.Instance.ShowKerbal(profile.kerbalId);
        }

        /// <summary>A kerbal earned a ribbon.</summary>
        public static event Action<WingAwardInfo> Awarded;

        /// <summary>A ribbon was taken back from a kerbal (by hand, with the wing cheats setting on).</summary>
        public static event Action<WingAwardInfo> Revoked;

        /// <summary>A campaign's ribbons are ready: a save was loaded or a new campaign began.</summary>
        public static event Action Loaded;

        internal static void RaiseLoaded() => Raise(Loaded, handler => ((Action)handler)());

        internal static void RaiseAwarded(WingAwardedMessage message)
        {
            if (Awarded == null || message.KerbalInfo == null || message.AwardedWing == null) return;
            var profile = WingsSessionManager.Instance.GetKerbalProfile(message.KerbalInfo.Id);
            var entry = profile.Entries.LastOrDefault(e => e.Wing == message.AwardedWing);
            if (entry == null) return;
            var info = Snapshot(profile, entry);
            Raise(Awarded, handler => ((Action<WingAwardInfo>)handler)(info));
        }

        internal static void RaiseRevoked(WingRevokedMessage message)
        {
            if (Revoked == null || message.RevokedWing == null) return;
            var profile = WingsSessionManager.Instance.GetKerbalProfile(message.KerbalId);
            var info = Snapshot(profile, message.RevokedWing, Core.GetUniverseTime(), false);
            Raise(Revoked, handler => ((Action<WingAwardInfo>)handler)(info));
        }

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

        private static WingAwardInfo Snapshot(KerbalProfile profile, KerbalWingEntry entry) =>
            Snapshot(profile, entry.Wing, entry.universeTime, entry.isSuperseeded);

        private static WingAwardInfo Snapshot(KerbalProfile profile, Wing wing, double ut, bool superseded)
        {
            var config = wing.config;
            return new WingAwardInfo
            {
                KerbalId = profile.kerbalId.ToString(),
                KerbalName = profile.fullName ?? Roster(profile.kerbalId),
                WingCode = config.name,
                WingName = wing.DisplayName ?? config.name,
                Description = wing.Description,
                UT = ut,
                IsFirst = config.isFirst,
                Points = config.points,
                Superseded = superseded,
                ImageLayers = config.imageLayers?.ToList() ?? new List<string>(),
            };
        }

        private static string Roster(IGGuid id)
        {
            var roster = WingsSessionManager.Roster;
            return roster != null && roster.TryGetKerbalByID(id, out var kerbal) ? kerbal.Attributes.GetFullName() : null;
        }
    }
}
