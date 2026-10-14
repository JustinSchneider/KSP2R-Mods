using System;
using System.Collections.Generic;
using System.Linq;
using KSP.Game;
using KSP.Sim;

namespace AgencyHeritage
{
    /// <summary>A dated line in an exhibit: a milestone or a flight.</summary>
    public class ExhibitEntry
    {
        public double UT;
        public string Title;
        public string Detail;
        /// <summary>The celestial body a milestone happened at, for its color on the timeline.</summary>
        public string Body;
        public int CrewCount;
    }

    public enum CrewRole { Flew, Founder, Recruit }

    /// <summary>A kerbal on an era's crew roll.</summary>
    public class CrewMember
    {
        public string Name;
        public CrewRole Role;
        /// <summary>Set if the kerbal was lost during the era.</summary>
        public LossRecord Lost;
        /// <summary>A veteran the game brought back after they were lost.</summary>
        public bool Returned;
        /// <summary>The game's portrait of the kerbal, when it has made one.</summary>
        public UnityEngine.Sprite Portrait;
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
        public int RecruitCount => Crew.Count(c => c.Role != CrewRole.Flew);

        /// <summary>Kerbals enrolled within this long of the campaign's start are its founding crew.</summary>
        private const double FoundingWindow = 21600;

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
                AddFirsts(exhibit, firsts.CBLaunched, "First launch", key => $"from {Body(key)}", Body, InEra);
                AddFirsts(exhibit, firsts.SOIReached, "First visit", key => Body(key), Body, InEra);
                AddFirsts(exhibit, firsts.CBLanded, "First landing", key => $"on {Body(key)}", Body, InEra);
                AddFirsts(exhibit, firsts.WalkedOn, "First steps", key => $"on {Body(key)}", Body, InEra);
                AddFirsts(exhibit, firsts.DiscoverableReached, "Discovered", Discoverable, DiscoverableBody, InEra);
                // Launching from, landing on and visiting the home world are a given, as is "discovering" the KSC.
                exhibit.Milestones.RemoveAll(m => m.Body == homeWorld && (m.Title != "Discovered" || m.Detail.StartsWith("KSC")));
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
                exhibit.Flights.Add(new ExhibitEntry
                {
                    UT = flight.UT,
                    // A launch copied from the travel log after its vessel was gone has no name or crew to show.
                    Title = string.IsNullOrEmpty(flight.VesselName) ? "Unidentified" : flight.VesselName,
                    Detail = flight.Crew.Count > 0 ? string.Join(", ", flight.Crew.Select(ShortName))
                        : string.IsNullOrEmpty(flight.VesselName) ? "Crew not recorded" : "Uncrewed",
                    CrewCount = flight.Crew.Count,
                });
                foreach (var name in flight.Crew) Member(name, CrewRole.Flew);
            }
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
                    var member = Member(kerbal.NameKey, kerbal.EnrollmentUT < FoundingWindow ? CrewRole.Founder : CrewRole.Recruit);
                    // Veterans the game respawns come back under the same name as a fresh hire.
                    if (data.Losses.Exists(l => l.Name == kerbal.NameKey && l.UT < kerbal.EnrollmentUT)) member.Returned = true;
                }
            }

            var byName = new Dictionary<string, KerbalInfo>();
            if (roster != null)
                foreach (var kerbal in roster)
                    if (kerbal?.NameKey != null) byName[kerbal.NameKey] = kerbal;

            foreach (var member in crew.Values)
            {
                if (byName.TryGetValue(member.Name, out var info))
                {
                    try { member.Portrait = info.Portrait; }
                    catch (Exception) { /* no portrait yet; the UI draws one */ }
                }
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

        private static string ShortName(string name) =>
            name != null && name.EndsWith(" Kerman") ? name.Substring(0, name.Length - 7) : name;

        private static void AddFirsts(EraExhibit exhibit, List<SerializedTravelSimObjectFirst> firsts, string title,
            Func<string, string> detail, Func<string, string> body, Func<double, bool> inEra)
        {
            if (firsts == null) return;
            foreach (var first in firsts)
            {
                if (first == null || !inEra(first.UT)) continue;
                exhibit.Milestones.Add(new ExhibitEntry
                {
                    UT = first.UT, Title = title, Detail = detail(first.ObjectKey), Body = body(first.ObjectKey),
                });
            }
        }

        private static string DiscoverableBody(string key)
        {
            int underscore = key?.IndexOf('_') ?? -1;
            return underscore > 0 ? key.Substring(0, underscore) : key;
        }

        /// <summary>"CelestialBody/Mun" or "Mun" → "Mun".</summary>
        private static string Body(string key)
        {
            if (string.IsNullOrEmpty(key)) return "somewhere new";
            int slash = key.LastIndexOf('/');
            return slash >= 0 ? key.Substring(slash + 1) : key;
        }

        /// <summary>"Kerbin_KerbinKSC" → "KSC on Kerbin".</summary>
        private static string Discoverable(string key)
        {
            if (string.IsNullOrEmpty(key)) return "a new site";
            int underscore = key.IndexOf('_');
            if (underscore <= 0) return key;
            string body = key.Substring(0, underscore);
            string site = key.Substring(underscore + 1);
            if (site.StartsWith(body) && site.Length > body.Length) site = site.Substring(body.Length);
            return $"{SplitWords(site)} on {body}";
        }

        /// <summary>"KSCLaunchpad" → "KSC Launchpad".</summary>
        private static string SplitWords(string text)
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
