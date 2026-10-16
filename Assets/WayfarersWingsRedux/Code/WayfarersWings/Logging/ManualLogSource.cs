#nullable enable
using WayfarersWingsRedux;

namespace WayfarersWings.Logging
{
    /// <summary>
    /// Stands in for BepInEx's named log sources, so the ported code keeps its logging calls.
    /// Everything goes to the mod's SpaceWarp logger with the source name in front.
    /// </summary>
    public sealed class ManualLogSource
    {
        private readonly string _prefix;

        internal ManualLogSource(string name)
        {
            _prefix = name.StartsWith("WayfarersWings.") ? name.Substring("WayfarersWings.".Length) : name;
        }

        public void LogDebug(object? data)
        {
            if (WayfarersWingsReduxPlugin.DebugLogging) SWLog.Info($"[{_prefix}] {data}");
        }

        public void LogInfo(object? data) => SWLog.Info($"[{_prefix}] {data}");
        public void LogWarning(object? data) => SWLog.Warning($"[{_prefix}] {data}");
        public void LogError(object? data) => SWLog.Error($"[{_prefix}] {data}");
    }

    public static class Logger
    {
        public static ManualLogSource CreateLogSource(string name) => new ManualLogSource(name);
    }
}
