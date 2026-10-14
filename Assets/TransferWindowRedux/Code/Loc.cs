using System;
using System.Globalization;
using ReduxLib.GameInterfaces;

namespace TransferWindowRedux
{
    /// <summary>
    /// Looks up the mod's strings in Copied/localizations/transfer_window_redux.csv (SpaceWarp loads it at startup).
    /// Keys are given without the "TransferWindowRedux/" prefix.
    /// </summary>
    internal static class Loc
    {
        private const string Prefix = "TransferWindowRedux/";
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

        private static void HookLanguageChange()
        {
            if (_hooked || ILocalizer.Instance == null) return;
            ILocalizer.Instance.OnLocalize += () => Changed?.Invoke();
            _hooked = true;
        }
    }
}
