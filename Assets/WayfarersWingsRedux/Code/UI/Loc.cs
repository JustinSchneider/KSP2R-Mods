using System;
using System.Globalization;
using UitkForKsp2.API;

namespace WayfarersWingsRedux.UI
{
    /// <summary>
    /// Looks up the mod's strings in Copied/localizations/wayfarers_wings.csv (SpaceWarp loads it at startup).
    /// Keys are given without the "WayfarersWings/" prefix, which the original mod's keys use.
    /// </summary>
    internal static class Loc
    {
        private const string Prefix = "WayfarersWings/";
        private static bool _hooked;

        /// <summary>The player changed the game language.</summary>
        public static event Action Changed;

        /// <summary>The translation for the current language, or the key itself if it's missing (easy to spot in testing).</summary>
        public static string T(string key)
        {
            HookLanguageChange();
            string text = ILocalizer.Instance?.GetTranslation(Prefix + key, Array.Empty<object>());
            return string.IsNullOrEmpty(text) ? key : text;
        }

        /// <summary>
        /// A translated template with {0}-style placeholders filled in. The localizer ignores its parameters, so
        /// formatting happens here.
        /// </summary>
        public static string F(string key, params object[] args)
        {
            string template = T(key);
            try
            {
                return string.Format(CultureInfo.InvariantCulture, template, args);
            }
            catch (FormatException)
            {
                return template;
            }
        }

        /// <summary>A count with its noun: keys ending in .One are used for exactly one, .Many for any other number.</summary>
        public static string Count(string key, int n) =>
            F(key + (n == 1 ? ".One" : ".Many"), n.ToString(CultureInfo.InvariantCulture));

        private static void HookLanguageChange()
        {
            if (_hooked || ILocalizer.Instance == null) return;
            ILocalizer.Instance.OnLocalize += () => Changed?.Invoke();
            _hooked = true;
        }
    }
}
