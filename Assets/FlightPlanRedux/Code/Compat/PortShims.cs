// Stand-ins for the original mods' plugin classes, so the vendored MechJeb and Node Manager code keeps its
// logging calls unchanged. Everything goes to Flight Plan Redux's logger.
namespace FlightPlanRedux
{
    internal sealed class LogSource
    {
        private readonly string _prefix;

        internal LogSource(string name) => _prefix = name;

        public void LogDebug(object data)
        {
            if (FlightPlanReduxPlugin.DebugLogging) SWLog.Info($"[{_prefix}] {data}");
        }

        public void LogInfo(object data) => SWLog.Info($"[{_prefix}] {data}");
        public void LogWarning(object data) => SWLog.Warning($"[{_prefix}] {data}");
        public void LogError(object data) => SWLog.Error($"[{_prefix}] {data}");
    }
}

namespace FlightPlan
{
    internal static class FlightPlanPlugin
    {
        internal static readonly FlightPlanRedux.LogSource Logger = new("MechJeb");
    }
}

namespace NodeManager
{
    internal static class NodeManagerPlugin
    {
        internal static readonly FlightPlanRedux.LogSource Logger = new("OrbitExtensions");
    }
}
