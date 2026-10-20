using System;
using System.Linq;
using System.Reflection;
using SpaceWarp2.API.Features;

namespace TransferWindowRedux
{
    /// <summary>
    /// Optional Flight Plan Redux support: hands a transfer over to Flight Plan, which can plan it in more detail.
    /// Found by name through reflection, so neither mod needs the other installed.
    /// </summary>
    internal static class FlightPlanBridge
    {
        private const string ModId = "FlightPlanRedux";
        private const string ApiType = "FlightPlanRedux.FlightPlanApi";
        private const int SupportedApiVersion = 1;

        private static bool _looked;
        private static MethodInfo _openTransfer;

        /// <summary>Flight Plan Redux is installed and the loaded campaign plays with it.</summary>
        public static bool IsAvailable => IsInstalled && ActiveFeatures.IsModEnabled(ModId);

        private static bool IsInstalled
        {
            get
            {
                if (_looked) return _openTransfer != null;
                _looked = true;
                try
                {
                    var type = AppDomain.CurrentDomain.GetAssemblies()
                        .Select(a => a.GetType(ApiType, false)).FirstOrDefault(t => t != null);
                    if (type == null) return false;
                    var version = type.GetField("ApiVersion", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                    if (!(version is int v) || v != SupportedApiVersion)
                    {
                        SWLog.Warning($"Flight Plan Redux API version {version} isn't the supported {SupportedApiVersion}; hand-off is off");
                        return false;
                    }
                    _openTransfer = type.GetMethod("OpenTransfer", new[] { typeof(string) });
                    return _openTransfer != null;
                }
                catch (Exception e)
                {
                    SWLog.Warning($"Looking for Flight Plan Redux failed: {e.Message}");
                    return false;
                }
            }
        }

        /// <summary>Opens Flight Plan on a transfer to a body (by internal name). False if it couldn't.</summary>
        public static bool OpenTransfer(string bodyName)
        {
            if (!IsAvailable || string.IsNullOrEmpty(bodyName)) return false;
            try
            {
                return _openTransfer.Invoke(null, new object[] { bodyName }) is true;
            }
            catch (Exception e)
            {
                SWLog.Warning($"Flight Plan Redux OpenTransfer failed: {(e as TargetInvocationException)?.InnerException?.Message ?? e.Message}");
                return false;
            }
        }
    }
}
