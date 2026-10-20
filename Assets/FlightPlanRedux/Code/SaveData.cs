using System.Collections.Generic;
using FlightPlanRedux.Planning;

namespace FlightPlanRedux
{
    /// <summary>
    /// What the player last typed and picked, kept per vessel in each save. The original kept these in static fields,
    /// so they leaked from one save into the next.
    /// </summary>
    public class FlightPlanSaveData
    {
        public int Version = 1;
        public List<VesselPlanState> Vessels = new List<VesselPlanState>();

        // Methods and fields only: when a save loads, Redux copies every public property onto this object, and a
        // read-only one makes that copy throw and abort the whole load.
        public VesselPlanState For(string vesselId)
        {
            var state = Vessels.Find(v => v.VesselId == vesselId);
            if (state != null) return state;
            state = new VesselPlanState { VesselId = vesselId };
            Vessels.Add(state);
            return state;
        }
    }

    public class VesselPlanState
    {
        public string VesselId;
        public PlanInputs Inputs = new PlanInputs();
        public int Tab;
        public int Maneuver;
        public int TimeRef;
    }
}
