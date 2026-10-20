#nullable enable
using System;
using System.ComponentModel;
using SpaceWarp2.API.Configuration;

namespace WayfarersWings.Utility
{
    public static class Settings
    {
        public enum HiDPIDisplayMode
        {
            [Description("Normal (1x)")]
            Normal,

            [Description("HiDPI (2x)")]
            Retina,

            [Description("Detect automatically")]
            Auto
        }

        // UI
        public static ConfigValue<bool> ShowAlwaysBigRibbons { get; private set; } = null!;
        public static ConfigValue<HiDPIDisplayMode> RibbonHiDPIDisplayMode { get; private set; } = null!;

        // Gameplay
        public static ConfigValue<bool> ShowFlightReportSummary { get; private set; } = null!;
        public static ConfigValue<bool> AllowWingCheats { get; private set; } = null!;

        /// <summary>Raised when a setting that changes how the windows look is changed.</summary>
        public static event Action? Changed;

        public static void SetupConfig(IConfigFile config)
        {
            ShowAlwaysBigRibbons = new ConfigValue<bool>(config.Bind(
                "UI",
                "Show always big ribbons",
                false,
                "Ribbons are always shown at full size.",
                null
            ));
            ShowAlwaysBigRibbons.RegisterCallback((_, _) => Changed?.Invoke());

            RibbonHiDPIDisplayMode = new ConfigValue<HiDPIDisplayMode>(config.Bind(
                "UI",
                "HiDPI ribbons display mode",
                HiDPIDisplayMode.Auto,
                "Normal (1x), HiDPI (2x), or detect from the screen.",
                null
            ));
            RibbonHiDPIDisplayMode.RegisterCallback((_, _) => Changed?.Invoke());

            ShowFlightReportSummary = new ConfigValue<bool>(config.Bind(
                "Gameplay",
                "Show flight report summary",
                true,
                "Show the ribbons earned on a mission when its vessel is recovered.",
                null
            ));

            AllowWingCheats = new ConfigValue<bool>(config.Bind(
                "Gameplay",
                "Allow wing cheats",
                false,
                "Award and revoke ribbons by hand from a kerbal's window.",
                null
            ));
            AllowWingCheats.RegisterCallback((_, _) => Changed?.Invoke());
        }
    }
}
