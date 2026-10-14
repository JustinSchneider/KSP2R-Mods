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

    /// <summary>A launch, as recorded when it happened.</summary>
    public class FlightRecord
    {
        public double UT;
        public string VesselName;
        public List<string> Crew = new List<string>();
    }

    /// <summary>A kerbal who was lost, recorded just before the game removed them from the roster.</summary>
    public class LossRecord
    {
        public string Name;
        public double UT;
        public string VesselName;
        /// <summary>File name of their portrait in the campaign's AgencyHeritage/portraits folder; null if none was saved.</summary>
        public string PortraitFile;
    }
}
