using System;
using System.Collections.Generic;
using System.Linq;
using KSP.Game;
using KSP.Sim;

namespace AgencyHeritage
{
    public enum MilestoneKind { Launch, Visit, Landing, Steps, Discovered }

    /// <summary>How a flight ended, or that it hasn't.</summary>
    public enum FlightFate { Unknown, InFlight, Recovered, Ended, Destroyed, CrewLost }

    /// <summary>The furthest a flight got: what its patch shows.</summary>
    public enum FlightReach { Unknown, Suborbital, Orbit, Flyby, Landed }

    /// <summary>A world a flight went to, and the furthest it got there.</summary>
    public class FlightStop
    {
        public string Body;
        public FlightReach Reach;
    }

    /// <summary>
    /// A dated line in an exhibit: a milestone or a flight. It holds the game's own names, not display text, so the
    /// window can word it in the current language.
    /// </summary>
    public class ExhibitEntry
    {
        public double UT;
        public MilestoneKind Kind;
        /// <summary>The celestial body a milestone happened at (internal name, e.g. "Mun"); null if unknown.</summary>
        public string Body;
        /// <summary>For a discovery: the site's region id, e.g. "KerbinKSC".</summary>
        public string Site;
        /// <summary>For a flight: the vessel's name, or null for a launch copied from the travel log after it was gone.</summary>
        public string VesselName;
        /// <summary>For a flight: the crew's first names.</summary>
        public List<string> Crew = new List<string>();
        public int CrewCount;

        // For a flight, from following it after launch:
        public FlightRecord Record;
        public FlightFate Fate;
        public FlightReach Reach;
        /// <summary>Where it got furthest: the body it landed on, orbited or flew by (the home world if nowhere else).</summary>
        public string Destination;
        public bool Docked;
        /// <summary>Every world away from home it went to, in the order it got there.</summary>
        public List<FlightStop> Stops = new List<FlightStop>();
        /// <summary>The era's firsts this flight earned (the milestones themselves).</summary>
        public List<ExhibitEntry> Firsts = new List<ExhibitEntry>();
        /// <summary>First names of the crew lost on it.</summary>
        public List<string> LostCrew = new List<string>();
    }

    public enum CrewRole { Flew, Recruit }

    /// <summary>A kerbal on an era's crew roll.</summary>
    public class CrewMember
    {
        public string Name;
        public CrewRole Role;
        /// <summary>Set if the kerbal was lost during the era.</summary>
        public LossRecord Lost;
        /// <summary>A veteran the game brought back after they were lost.</summary>
        public bool Returned;
        /// <summary>When they joined the agency; null if the roster no longer has them.</summary>
        public double? Joined;
        /// <summary>Joined during this era (whether or not they've flown since).</summary>
        public bool Recruited;
        /// <summary>The game's portrait of the kerbal, when it has made one.</summary>
        public UnityEngine.Sprite Portrait;
        /// <summary>The portrait the mod saved when they were lost (in this era or a later one); null if none.</summary>
        public string SavedPortrait;
        /// <summary>Wayfarer's Wings ribbons earned during the era, best first.</summary>
        public List<WingAward> Ribbons = new List<WingAward>();
    }

    /// <summary>
    /// What an era's exhibit shows, gathered from the game's travel log (firsts, kept for the whole campaign), the
    /// kerbal roster (enrollment dates and portraits), and the mod's own flight records. Launches come only from those
    /// records: the travel log forgets a launch once its vessel is recovered or destroyed.
    /// </summary>
    public class EraExhibit
    {
        public BrandEra Era;
        public int Number;
        public double StartUT;
        public double EndUT;
        public bool IsCurrent;

        public int Launches => Flights.Count;
        public List<ExhibitEntry> Milestones = new List<ExhibitEntry>();
        public List<ExhibitEntry> Flights = new List<ExhibitEntry>();
        public List<CrewMember> Crew = new List<CrewMember>();
        /// <summary>Wayfarer's Wings ribbons earned during the era; empty when that mod isn't installed.</summary>
        public List<WingAward> Ribbons = new List<WingAward>();
        public List<double> LaunchTimes = new List<double>();
        public List<BlueprintCard> Blueprints = new List<BlueprintCard>();
        public List<LossRecord> Losses = new List<LossRecord>();

        public int CrewFlownCount => Crew.Count(c => c.Role == CrewRole.Flew);
        public int RecruitCount => Crew.Count(c => c.Recruited);


        public static EraExhibit Build(HeritageSaveData data, BrandEra era, SerializedTravelLog travelLog,
            IEnumerable<KerbalInfo> roster, IEnumerable<WingAward> ribbons, string homeWorld, double now)
        {
            var exhibit = new EraExhibit
            {
                Era = era,
                Number = data.Eras.IndexOf(era) + 1,
                StartUT = era.StartUT,
                EndUT = data.EndUT(era),
                IsCurrent = era == data.CurrentEra(),
            };
            if (exhibit.IsCurrent) exhibit.EndUT = Math.Max(now, era.StartUT);
            bool InEra(double ut) => ut >= exhibit.StartUT && (exhibit.IsCurrent || ut < exhibit.EndUT);

            if (travelLog?.Firsts != null)
            {
                var firsts = travelLog.Firsts;
                AddFirsts(exhibit, firsts.CBLaunched, MilestoneKind.Launch, InEra);
                AddFirsts(exhibit, firsts.SOIReached, MilestoneKind.Visit, InEra);
                AddFirsts(exhibit, firsts.CBLanded, MilestoneKind.Landing, InEra);
                AddFirsts(exhibit, firsts.WalkedOn, MilestoneKind.Steps, InEra);
                AddFirsts(exhibit, firsts.DiscoverableReached, MilestoneKind.Discovered, InEra);
                // Launching from, landing on and visiting the home world are a given, as is "discovering" the KSC.
                exhibit.Milestones.RemoveAll(m => m.Body == homeWorld
                    && (m.Kind != MilestoneKind.Discovered || SiteWithoutBody(m.Site, m.Body).StartsWith("KSC")));
                exhibit.Milestones.Sort((a, b) => a.UT.CompareTo(b.UT));
            }

            var crew = new Dictionary<string, CrewMember>();
            CrewMember Member(string name, CrewRole role)
            {
                if (!crew.TryGetValue(name, out var member))
                    crew[name] = member = new CrewMember { Name = name, Role = role };
                else if (role == CrewRole.Flew)
                    member.Role = CrewRole.Flew;
                return member;
            }

            foreach (var flight in data.Flights.Where(f => InEra(f.UT)).OrderBy(f => f.UT))
            {
                var entry = new ExhibitEntry
                {
                    UT = flight.UT,
                    // A launch copied from the travel log after its vessel was gone has no name or crew to show.
                    VesselName = string.IsNullOrEmpty(flight.VesselName) ? null : flight.VesselName,
                    Crew = flight.Crew.Select(ShortName).ToList(),
                    CrewCount = flight.Crew.Count,
                    Record = flight,
                    LostCrew = flight.LostCrew.Select(ShortName).ToList(),
                };
                Describe(entry, flight, homeWorld);
                exhibit.Flights.Add(entry);
                foreach (var name in flight.Crew) Member(name, CrewRole.Flew);
            }
            CreditFirsts(exhibit);
            exhibit.LaunchTimes = exhibit.Flights.Select(f => f.UT).ToList();
            exhibit.Flights.Reverse();   // newest first, like a mission wall

            // Ribbons are only earned in flight, so they also name crews of launches made before the mod was installed.
            if (ribbons != null)
            {
                exhibit.Ribbons = ribbons.Where(r => r != null && InEra(r.UT) && !string.IsNullOrEmpty(r.KerbalName)).ToList();
                foreach (var ribbon in exhibit.Ribbons) Member(ribbon.KerbalName, CrewRole.Flew).Ribbons.Add(ribbon);
            }

            // The lost flew on their final mission, so they count as crew flown too.
            exhibit.Losses = data.Losses.Where(l => l != null && InEra(l.UT) && !string.IsNullOrEmpty(l.Name)).OrderBy(l => l.UT).ToList();
            foreach (var loss in exhibit.Losses) Member(loss.Name, CrewRole.Flew).Lost = loss;

            if (roster != null)
            {
                foreach (var kerbal in roster.Where(k => k != null && InEra(k.EnrollmentUT)).OrderBy(k => k.EnrollmentUT))
                {
                    // The kerbals an agency starts with count as its first recruits.
                    var member = Member(kerbal.NameKey, CrewRole.Recruit);
                    member.Recruited = true;
                    // Veterans the game respawns come back under the same name as a fresh hire.
                    if (data.Losses.Exists(l => l.Name == kerbal.NameKey && l.UT < kerbal.EnrollmentUT)) member.Returned = true;
                }
            }
            // The roster forgets a kerbal once they're lost, so their loss record says when they joined.
            foreach (var loss in data.Losses.Where(l => l?.Joined != null && !string.IsNullOrEmpty(l.Name) && InEra(l.Joined.Value)))
            {
                var member = Member(loss.Name, CrewRole.Recruit);
                member.Recruited = true;
                member.Joined = loss.Joined;
            }

            var byName = new Dictionary<string, KerbalInfo>();
            if (roster != null)
                foreach (var kerbal in roster)
                    if (kerbal?.NameKey != null) byName[kerbal.NameKey] = kerbal;

            foreach (var member in crew.Values)
            {
                if (byName.TryGetValue(member.Name, out var info))
                {
                    member.Joined ??= info.EnrollmentUT;
                    try { member.Portrait = info.Portrait; }
                    catch (Exception) { /* no portrait yet; the UI draws one */ }
                }
                member.SavedPortrait = data.Losses.LastOrDefault(l => l?.Name == member.Name && l.PortraitFile != null)?.PortraitFile;
                member.Ribbons.Sort((a, b) => b.IsFirst != a.IsFirst ? (b.IsFirst ? 1 : -1) : b.Points.CompareTo(a.Points));
            }
            exhibit.Crew = crew.Values
                .OrderByDescending(c => c.Lost != null && !c.Returned)
                .ThenBy(c => c.Role)
                .ThenByDescending(c => c.Ribbons.Count)
                .ThenBy(c => c.Name, StringComparer.CurrentCulture)
                .ToList();
            return exhibit;
        }

        /// <summary>How a flight ended, or that it hasn't.</summary>
        public static FlightFate FateOf(FlightRecord flight)
        {
            if (flight.LostCrew.Count > 0) return FlightFate.CrewLost;
            if (!flight.IsFollowed()) return FlightFate.Unknown;
            if (flight.Has(FlightEventKind.Recovered)) return FlightFate.Recovered;
            if (flight.ActiveIds.Count > 0) return FlightFate.InFlight;
            if (flight.Has(FlightEventKind.Destroyed)) return FlightFate.Destroyed;
            return FlightFate.Ended;
        }

        /// <summary>
        /// True if a vessel was launched from a blueprint: the game names each launch after the vehicle with a number
        /// ("Kerbal-K2b" flies as "Kerbal-K2b-1", "Kerbal-K2b-2", …).
        /// </summary>
        public static bool IsFlightOf(string vesselName, string vehicleName)
        {
            if (string.IsNullOrEmpty(vesselName) || string.IsNullOrEmpty(vehicleName)) return false;
            if (vesselName == vehicleName) return true;
            return vesselName.Length > vehicleName.Length + 1 && vesselName.StartsWith(vehicleName + "-")
                && vesselName.Substring(vehicleName.Length + 1).All(char.IsDigit);
        }

        /// <summary>Works out a flight's fate, and the furthest it got, from what happened to it after launch.</summary>
        private static void Describe(ExhibitEntry entry, FlightRecord flight, string homeWorld)
        {
            entry.Docked = flight.Has(FlightEventKind.Docked);
            entry.Fate = FateOf(flight);

            // Away from home, a landing beats an orbit beats a flyby; the latest of the best kind wins.
            FlightEvent Latest(params FlightEventKind[] kinds) =>
                flight.Events.LastOrDefault(e => kinds.Contains(e.Kind) && e.Body != null && e.Body != homeWorld);
            entry.Stops = StopsOf(flight, homeWorld, false);

            var away = Latest(FlightEventKind.Landed, FlightEventKind.Splashed)
                ?? Latest(FlightEventKind.Orbit)
                ?? Latest(FlightEventKind.EnteredSoi);
            if (away != null)
            {
                entry.Destination = away.Body;
                entry.Reach = away.Kind == FlightEventKind.Orbit ? FlightReach.Orbit
                    : away.Kind == FlightEventKind.EnteredSoi ? FlightReach.Flyby
                    : FlightReach.Landed;
                return;
            }

            entry.Destination = homeWorld;
            if (flight.Has(FlightEventKind.Orbit)) entry.Reach = FlightReach.Orbit;
            else if (flight.IsFollowed() && flight.Events.Count > 0) entry.Reach = FlightReach.Suborbital;
            else entry.Reach = FlightReach.Unknown;
        }

        /// <summary>
        /// The worlds a flight went to, in the order it got there, each with the furthest it got. Passing through the
        /// Sun's SOI on the way somewhere is leaving home, not visiting the Sun, so a star only counts if orbited.
        /// With includeHome, an orbit of the home world counts too (landing there is just coming back).
        /// </summary>
        public static List<FlightStop> StopsOf(FlightRecord flight, string homeWorld, bool includeHome)
        {
            var stops = new List<FlightStop>();
            foreach (var e in flight.Events.Where(e => e.Body != null).OrderBy(e => e.UT))
            {
                var reach = e.Kind == FlightEventKind.Landed || e.Kind == FlightEventKind.Splashed ? FlightReach.Landed
                    : e.Kind == FlightEventKind.Orbit ? FlightReach.Orbit
                    : e.Kind == FlightEventKind.EnteredSoi ? FlightReach.Flyby
                    : FlightReach.Unknown;
                if (reach == FlightReach.Unknown) continue;
                if (e.Body == homeWorld && (!includeHome || reach != FlightReach.Orbit)) continue;
                var stop = stops.Find(st => st.Body == e.Body);
                if (stop == null) stops.Add(stop = new FlightStop { Body = e.Body, Reach = reach });
                else if (Rank(reach) > Rank(stop.Reach)) stop.Reach = reach;
            }
            stops.RemoveAll(st => st.Reach == FlightReach.Flyby && st.Body == StarName);
            return stops;
        }

        /// <summary>When a flight ended (its last landing, recovery or loss); null while it's still out or unknown.</summary>
        public static double? EndOf(FlightRecord flight)
        {
            var ends = flight.Events.Where(e => e.Kind == FlightEventKind.Landed || e.Kind == FlightEventKind.Splashed
                || e.Kind == FlightEventKind.Recovered || e.Kind == FlightEventKind.Destroyed).ToList();
            return ends.Count == 0 ? (double?)null : ends.Max(e => e.UT);
        }

        /// <summary>A landing beats an orbit beats a flyby.</summary>
        public static int Rank(FlightReach reach) =>
            reach == FlightReach.Landed ? 3 : reach == FlightReach.Orbit ? 2 : reach == FlightReach.Flyby ? 1 : 0;

        /// <summary>The star's internal name. Stock KSP2 has only the one.</summary>
        internal const string StarName = "Kerbol";

        /// <summary>How far apart a first and the flight event behind it can be logged.</summary>
        private const double FirstMatchSeconds = 60;

        /// <summary>Gives each of the era's firsts to the flight that earned it.</summary>
        private static void CreditFirsts(EraExhibit exhibit)
        {
            foreach (var milestone in exhibit.Milestones)
            {
                ExhibitEntry earner = null;
                switch (milestone.Kind)
                {
                    case MilestoneKind.Landing:
                        earner = FlightWith(exhibit, milestone, e => e.Kind == FlightEventKind.Landed || e.Kind == FlightEventKind.Splashed);
                        break;
                    case MilestoneKind.Visit:
                        earner = FlightWith(exhibit, milestone, e => e.Kind == FlightEventKind.EnteredSoi);
                        break;
                    case MilestoneKind.Steps:
                    case MilestoneKind.Launch:
                        // Walking on or launching from a body: the crewed flight that most recently landed there.
                        earner = exhibit.Flights
                            .Where(f => f.Record != null && f.CrewCount > 0)
                            .Select(f => (Flight: f, Landing: f.Record.Events.LastOrDefault(e =>
                                (e.Kind == FlightEventKind.Landed || e.Kind == FlightEventKind.Splashed)
                                && e.Body == milestone.Body && e.UT <= milestone.UT + FirstMatchSeconds)))
                            .Where(x => x.Landing != null)
                            .OrderByDescending(x => x.Landing.UT)
                            .Select(x => x.Flight)
                            .FirstOrDefault();
                        break;
                }
                if (earner != null && !earner.Firsts.Contains(milestone)) earner.Firsts.Add(milestone);
            }
        }

        private static ExhibitEntry FlightWith(EraExhibit exhibit, ExhibitEntry milestone, Func<FlightEvent, bool> match) =>
            exhibit.Flights.FirstOrDefault(f => f.Record != null && f.Record.Events.Any(e =>
                match(e) && e.Body == milestone.Body && Math.Abs(e.UT - milestone.UT) < FirstMatchSeconds));

        internal static string ShortName(string name) =>
            name != null && name.EndsWith(" Kerman") ? name.Substring(0, name.Length - 7) : name;

        private static void AddFirsts(EraExhibit exhibit, List<SerializedTravelSimObjectFirst> firsts, MilestoneKind kind,
            Func<double, bool> inEra)
        {
            if (firsts == null) return;
            foreach (var first in firsts)
            {
                if (first == null || !inEra(first.UT)) continue;
                var entry = new ExhibitEntry { UT = first.UT, Kind = kind };
                string key = first.ObjectKey;
                if (kind == MilestoneKind.Discovered)
                {
                    // "Kerbin_KerbinKSC": the body, then the site's region id.
                    int underscore = key?.IndexOf('_') ?? -1;
                    entry.Body = underscore > 0 ? key.Substring(0, underscore) : null;
                    entry.Site = underscore > 0 ? key.Substring(underscore + 1) : key;
                }
                else if (!string.IsNullOrEmpty(key))
                {
                    // "CelestialBody/Mun" or "Mun"
                    int slash = key.LastIndexOf('/');
                    entry.Body = slash >= 0 ? key.Substring(slash + 1) : key;
                }
                exhibit.Milestones.Add(entry);
            }
        }

        /// <summary>"KerbinKSCLaunchpad" on Kerbin → "KSCLaunchpad".</summary>
        internal static string SiteWithoutBody(string site, string body)
        {
            if (string.IsNullOrEmpty(site)) return "";
            return !string.IsNullOrEmpty(body) && site.StartsWith(body) && site.Length > body.Length
                ? site.Substring(body.Length) : site;
        }

        /// <summary>"KSCLaunchpad" → "KSC Launchpad".</summary>
        internal static string SplitWords(string text)
        {
            var result = new System.Text.StringBuilder();
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (i > 0 && char.IsUpper(c) && char.IsLower(text[i - 1])) result.Append(' ');
                else if (i > 0 && char.IsUpper(c) && i + 1 < text.Length && char.IsLower(text[i + 1]) && char.IsUpper(text[i - 1])) result.Append(' ');
                result.Append(c == '_' ? ' ' : c);
            }
            return result.ToString();
        }
    }
}
