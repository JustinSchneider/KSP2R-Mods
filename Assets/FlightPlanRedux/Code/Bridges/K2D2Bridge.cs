using System;
using System.Linq;
using System.Reflection;
using SpaceWarp2.API.Features;

namespace FlightPlanRedux.Bridges
{
    /// <summary>
    /// Optional K2-D2 support (the Redux port, mod ID "K2D2"), through reflection so neither mod needs the other.
    /// K2-D2 always flies the vessel's first node, so Execute is only offered for that one.
    /// </summary>
    internal static class K2D2Bridge
    {
        private const string ModId = "K2D2";
        private const string PluginType = "K2D2.K2D2_Plugin";

        private static bool _looked;
        private static PropertyInfo _instance;
        private static MethodInfo _flyNode, _stopFlyNode, _isRunning, _getStatus;
        private static bool _failed;

        /// <summary>K2-D2 is installed and the loaded campaign plays with it.</summary>
        public static bool IsAvailable => IsInstalled && ActiveFeatures.IsModEnabled(ModId);

        private static bool IsInstalled
        {
            get
            {
                if (_looked) return _flyNode != null && !_failed;
                _looked = true;
                try
                {
                    var type = AppDomain.CurrentDomain.GetAssemblies()
                        .Select(a => a.GetType(PluginType, false)).FirstOrDefault(t => t != null);
                    if (type == null) return false;
                    _instance = type.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                    _flyNode = type.GetMethod("FlyNode", Type.EmptyTypes);
                    _stopFlyNode = type.GetMethod("StopFlyNode", Type.EmptyTypes);
                    _isRunning = type.GetMethod("IsFlyNodeRunning", Type.EmptyTypes);
                    _getStatus = type.GetMethod("GetStatus", Type.EmptyTypes);
                    if (_instance == null || _flyNode == null || _stopFlyNode == null || _isRunning == null)
                    {
                        SWLog.Warning("K2-D2 found, but not the node execution calls this version expects; Execute is off");
                        _flyNode = null;
                        return false;
                    }
                    SWLog.Info("K2-D2 found; Execute is available");
                    return true;
                }
                catch (Exception e)
                {
                    SWLog.Warning($"Looking for K2-D2 failed: {e.Message}");
                    _flyNode = null;
                    return false;
                }
            }
        }

        public static void FlyNode() => Call(_flyNode);
        public static void StopFlyNode() => Call(_stopFlyNode);
        public static bool IsRunning => Call(_isRunning) is true;

        /// <summary>K2-D2's own status line ("Burning: …", "Done", …), untranslated.</summary>
        public static string Status => Call(_getStatus) as string ?? "";

        private static object Call(MethodInfo method)
        {
            if (!IsAvailable || method == null) return null;
            try
            {
                var plugin = _instance.GetValue(null);
                return plugin == null ? null : method.Invoke(plugin, null);
            }
            catch (Exception e)
            {
                var inner = (e as TargetInvocationException)?.InnerException ?? e;
                SWLog.Warning($"K2-D2 {method.Name} failed: {inner.Message}");
                // A version whose calls throw would spam the log every refresh; give up on it.
                if (!(e is TargetInvocationException)) _failed = true;
                return null;
            }
        }
    }
}
