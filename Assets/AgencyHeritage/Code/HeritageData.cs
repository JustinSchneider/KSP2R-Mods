using System;
using System.Collections.Generic;
using UnityEngine;

namespace AgencyHeritage
{
    /// <summary>
    /// The agency's brand history, stored per save so that loading an older save also rolls the history back.
    /// SpaceWarp2 serializes this with $type, so keep it plain fields and keep the class names stable.
    /// </summary>
    public class HeritageSaveData
    {
        public int Version = 1;
        public List<BrandEra> Eras = new List<BrandEra>();
        /// <summary>Launches recorded since the mod was installed, with the names the travel log doesn't keep.</summary>
        public List<FlightRecord> Flights = new List<FlightRecord>();
        /// <summary>Kerbals lost in the line of duty. The game deletes them from the roster, so this is all that's left.</summary>
        public List<LossRecord> Losses = new List<LossRecord>();
        /// <summary>Performance figures caught from the VAB each time a blueprint was saved; the files don't keep them.</summary>
        public List<BlueprintStats> Blueprints = new List<BlueprintStats>();

        // Methods, not properties: when a save loads, Redux copies every public property onto this object, and a
        // read-only one makes that copy throw and abort the whole load.
        public BrandEra CurrentEra() => Eras.Count > 0 ? Eras[Eras.Count - 1] : null;

        public BrandEra FindEra(string id) => Eras.Find(e => e.Id == id);

        /// <summary>The in-game time an era ended, or +∞ for the current era.</summary>
        public double EndUT(BrandEra era)
        {
            int index = Eras.IndexOf(era);
            return index >= 0 && index + 1 < Eras.Count ? Eras[index + 1].StartUT : double.PositiveInfinity;
        }

        /// <summary>
        /// The era that was current at a real-world time. A blueprint belongs to the era it was last saved in,
        /// which its .meta SaveTime tells us, so blueprints need no tagging of their own.
        /// </summary>
        public BrandEra EraAtRealTime(DateTime time)
        {
            BrandEra found = Eras.Count > 0 ? Eras[0] : null;
            foreach (var era in Eras)
                if (era.StartRealTime <= time) found = era;
            return found;
        }
    }

    /// <summary>One period of the agency's identity, from a rebrand (or its founding) to the next.</summary>
    public class BrandEra
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Name;
        public Color Base;
        public Color Accent;
        public string FlagSpriteLocation;
        /// <summary>In-game time the era began; 0 for the founding era.</summary>
        public double StartUT;
        public DateTime StartRealTime;
        public string Note;
        /// <summary>Set once the era has ended and its blueprint thumbnails were copied to the era archive.</summary>
        public bool ThumbnailsArchived;
    }

    /// <summary>A launch, as recorded when it happened, and what became of it.</summary>
    public class FlightRecord
    {
        public double UT;
        public string VesselName;
        public List<string> Crew = new List<string>();
        /// <summary>
        /// The flight's vessels (Guid "N" strings): the one launched, plus any crewed or controllable vessel that
        /// split or undocked from it. Empty for a launch recorded before flights were followed.
        /// </summary>
        public List<string> VesselIds = new List<string>();
        /// <summary>The flight's vessels that still exist; the flight is over when this empties.</summary>
        public List<string> ActiveIds = new List<string>();
        public List<FlightEvent> Events = new List<FlightEvent>();
        /// <summary>Part count at launch; 0 if unknown.</summary>
        public int Parts;
        /// <summary>Mass at launch in tonnes; 0 if unknown.</summary>
        public double Mass;
        /// <summary>Kerbals lost on this flight.</summary>
        public List<string> LostCrew = new List<string>();

        // Methods, not properties, for the same reason as HeritageSaveData's.
        public bool IsFollowed() => VesselIds.Count > 0;
        public bool Has(FlightEventKind kind) => Events.Exists(e => e.Kind == kind);
    }

    /// <summary>What can happen to a flight after launch. Saved by number: only ever add to the end.</summary>
    public enum FlightEventKind { Landed, Splashed, Orbit, EnteredSoi, Docked, Recovered, Destroyed }

    public class FlightEvent
    {
        public double UT;
        public FlightEventKind Kind;
        /// <summary>The celestial body it happened at (internal name, e.g. "Mun"); null if unknown.</summary>
        public string Body;
    }

    /// <summary>A blueprint's performance as the VAB worked it out when it was saved.</summary>
    public class BlueprintStats
    {
        /// <summary>The workspace's name, which is also its file name.</summary>
        public string Workspace;
        /// <summary>The .meta file's SaveTime, so a re-saved blueprint gets its new figures.</summary>
        public long SaveTime;
        /// <summary>Metres per second.</summary>
        public double DeltaVVac, DeltaVAsl;
        public float LaunchTwr;
        /// <summary>Seconds.</summary>
        public double BurnTime;
    }

    /// <summary>A kerbal who was lost, recorded just before the game removed them from the roster.</summary>
    public class LossRecord
    {
        public string Name;
        public double UT;
        public string VesselName;
        /// <summary>When they joined the agency; the roster forgets it along with them. Null in older saves.</summary>
        public double? Joined;
        /// <summary>File name of their portrait in the campaign's AgencyHeritage/portraits folder; null if none was saved.</summary>
        public string PortraitFile;
    }
}
