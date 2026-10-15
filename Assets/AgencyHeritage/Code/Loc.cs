using System;
using System.Globalization;
using ReduxLib.GameInterfaces;

namespace AgencyHeritage
{
    /// <summary>
    /// Looks up the mod's strings in Copied/localizations/agency_heritage.csv (SpaceWarp loads it at startup).
    /// Keys are given without the "AgencyHeritage/" prefix.
    /// </summary>
    internal static class Loc
    {
        private const string Prefix = "AgencyHeritage/";
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
        /// formatting happens here; numbers are pre-formatted by the caller and stay invariant like the stock UI.
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
                // A translation with a broken placeholder shouldn't take the window down.
                return template;
            }
        }

        /// <summary>One of the game's own strings (full key), or the fallback when the game has no such key.</summary>
        public static string Game(string key, string fallback)
        {
            HookLanguageChange();
            string text = ILocalizer.Instance?.GetTranslation(key, Array.Empty<object>());
            return string.IsNullOrEmpty(text) || text == key ? fallback : text;
        }

        /// <summary>A celestial body's name as the game shows it in the current language.</summary>
        public static string Body(string name) =>
            string.IsNullOrEmpty(name) ? name : Game("CelestialBody/" + name, name);

        /// <summary>
        /// The culture of the game language, for real-world dates. The OS culture would show a German player's
        /// dates in English if their Windows is set to English, or the other way round.
        /// </summary>
        public static CultureInfo Culture
        {
            get
            {
                try { return I2.Loc.LocalizationManager.CurrentCulture ?? CultureInfo.InvariantCulture; }
                catch (Exception) { return CultureInfo.InvariantCulture; }
            }
        }

        private static void HookLanguageChange()
        {
            if (_hooked || ILocalizer.Instance == null) return;
            ILocalizer.Instance.OnLocalize += () => Changed?.Invoke();
            _hooked = true;
        }
    }
}
