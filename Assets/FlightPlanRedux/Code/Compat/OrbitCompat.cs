using KSP.Game;
using KSP.Sim;
using KSP.Sim.impl;

namespace FlightPlanRedux
{
    /// <summary>
    /// Since Redux 26w39a, vessel and body orbits are typed IKeplerPatch, and a live orbit is Redux's ECS-backed
    /// CurrentPatchedConicsOrbit, which isn't a PatchedConicsOrbit and can't be cast to one. The vendored MechJeb and
    /// Node Manager math is written against PatchedConicsOrbit, so it gets a copy instead.
    /// </summary>
    internal static class OrbitCompat
    {
        public static PatchedConicsOrbit ToPatchedConics(this IKeplerPatch orbit)
        {
            if (orbit == null || orbit is PatchedConicsOrbit) return orbit as PatchedConicsOrbit;

            var universe = GameManager.Instance.Game.UniverseModel;
            var copy = new PatchedConicsOrbit(universe);
            copy.UpdateFromOrbitAtUT(orbit, universe.UniverseTime, orbit.referenceBody);
            copy.StartUT = orbit.StartUT;
            copy.EndUT = orbit.EndUT;
            copy.ActivePatch = orbit.ActivePatch;
            copy.PatchStartTransition = orbit.PatchStartTransition;
            copy.PatchEndTransition = orbit.PatchEndTransition;
            copy.PreviousPatch = orbit.PreviousPatch;
            copy.NextPatch = orbit.NextPatch;
            return copy;
        }
    }
}
