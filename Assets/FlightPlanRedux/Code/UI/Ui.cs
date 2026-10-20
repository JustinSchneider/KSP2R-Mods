using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace FlightPlanRedux.UI
{
    /// <summary>Colors, small building blocks and number formats shared by the window.</summary>
    internal static class Ui
    {
        public static readonly Color Panel2Color = new Color(0.09f, 0.106f, 0.15f);
        public static readonly Color LineColor = new Color(0.2f, 0.23f, 0.3f);
        public static readonly Color InkColor = new Color32(230, 235, 244, 255);
        public static readonly Color Ink2Color = new Color(0.64f, 0.66f, 0.72f);
        public static readonly Color Ink3Color = new Color32(127, 133, 152, 255);
        public static readonly Color SignalColor = new Color32(140, 146, 230, 255);
        public static readonly Color GoodColor = new Color(0.45f, 0.9f, 0.4f);
        public static readonly Color WarnColor = new Color(1f, 0.74f, 0.2f);
        public static readonly Color BadColor = new Color(0.95f, 0.33f, 0.27f);
        public static readonly Color RowHover = new Color(1, 1, 1, 0.04f);
        public static readonly Color RowSelected = new Color(0.55f, 0.57f, 0.9f, 0.16f);

        private const double SecondsPerDay = 21600;   // Kerbin: 6-hour days
        private const double DaysPerYear = 426;
        public static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        public static Label Text(string text, float size, Color color, bool bold = false)
        {
            var label = new Label(text);
            label.style.fontSize = size;
            label.style.color = color;
            label.style.marginLeft = label.style.marginRight = label.style.marginTop = label.style.marginBottom = 0;
            label.style.paddingLeft = label.style.paddingRight = label.style.paddingTop = label.style.paddingBottom = 0;
            if (bold) label.style.unityFontStyleAndWeight = FontStyle.Bold;
            return label;
        }

        public static Label Section()
        {
            var label = new Label();
            label.style.color = SignalColor;
            label.style.fontSize = 11;
            label.style.letterSpacing = 2;
            label.style.marginTop = 10;
            label.style.marginBottom = 2;
            label.style.paddingBottom = 2;
            label.style.borderBottomWidth = 1;
            label.style.borderBottomColor = new Color(1, 1, 1, 0.06f);
            return label;
        }

        public static VisualElement Row()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            return row;
        }

        public static VisualElement WrapRow()
        {
            var wrap = new VisualElement();
            wrap.style.flexDirection = FlexDirection.Row;
            wrap.style.flexWrap = Wrap.Wrap;
            wrap.style.alignItems = Align.FlexStart;
            return wrap;
        }

        public static VisualElement Spacer()
        {
            var spacer = new VisualElement();
            spacer.style.flexGrow = 1;
            return spacer;
        }

        public static Button TabButton(Action clicked)
        {
            var tab = new Button(clicked);
            tab.style.fontSize = 11;
            tab.style.unityFontStyleAndWeight = FontStyle.Bold;
            tab.style.height = 24;
            tab.style.marginLeft = 4;
            tab.style.marginRight = 0;
            tab.style.flexShrink = 0;
            tab.style.backgroundColor = Color.clear;
            SetBorder(tab, Color.clear, 1);
            SetRadius(tab, 3);
            return tab;
        }

        public static void SetTabActive(Button tab, bool active)
        {
            tab.style.color = active ? InkColor : Ink3Color;
            tab.style.backgroundColor = active ? Panel2Color : Color.clear;
            SetBorder(tab, active ? LineColor : Color.clear, 1);
        }

        /// <summary>A pick-one chip, used for maneuvers and step sizes.</summary>
        public static Button Chip(Action clicked)
        {
            var chip = new Button(clicked);
            chip.style.fontSize = 11;
            chip.style.height = 22;
            chip.style.marginLeft = 0;
            chip.style.marginRight = 4;
            chip.style.marginBottom = 4;
            chip.style.flexShrink = 0;
            SetPadding(chip, 0, 8);
            return chip;
        }

        public static void SetChipActive(Button chip, bool active)
        {
            chip.style.color = active ? InkColor : Ink2Color;
            if (active) chip.style.backgroundColor = RowSelected;
            else chip.style.backgroundColor = StyleKeyword.Null;
            SetBorder(chip, active ? SignalColor : LineColor, 1);
        }

        public static Button SmallButton(string text, Action clicked)
        {
            var button = new Button(clicked) { text = text };
            button.style.fontSize = 11;
            button.style.height = 22;
            button.style.marginLeft = button.style.marginRight = 0;
            button.style.flexShrink = 0;
            SetPadding(button, 0, 8);
            return button;
        }

        public static void Shown(VisualElement element, bool shown) =>
            element.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;

        public static void SetPadding(VisualElement e, float vertical, float horizontal)
        {
            e.style.paddingTop = e.style.paddingBottom = vertical;
            e.style.paddingLeft = e.style.paddingRight = horizontal;
        }

        public static void SetBorder(VisualElement e, Color color, float width)
        {
            e.style.borderTopColor = e.style.borderBottomColor = e.style.borderLeftColor = e.style.borderRightColor = color;
            e.style.borderTopWidth = e.style.borderBottomWidth = e.style.borderLeftWidth = e.style.borderRightWidth = width;
        }

        public static void SetRadius(VisualElement e, float radius)
        {
            e.style.borderTopLeftRadius = e.style.borderTopRightRadius = radius;
            e.style.borderBottomLeftRadius = e.style.borderBottomRightRadius = radius;
        }

        public static Color StatusColor(Planning.StatusLevel level) => level switch
        {
            Planning.StatusLevel.Ok => GoodColor,
            Planning.StatusLevel.Warning => WarnColor,
            _ => BadColor
        };

        // Numbers stay invariant, like the stock UI.
        public static string Speed(double v) => double.IsNaN(v) ? "—" : v.ToString("N1", Invariant) + " m/s";
        public static string Deg(double d, int decimals = 2) => double.IsNaN(d) ? "—" : d.ToString("F" + decimals, Invariant) + "°";
        public static string Km(double metres) => double.IsNaN(metres) ? "—" : (metres / 1000).ToString("N1", Invariant) + " km";
        public static string Metres(double metres) =>
            double.IsNaN(metres) ? "—" : Math.Abs(metres) >= 10_000 ? Km(metres) : metres.ToString("N0", Invariant) + " m";
        public static string Number(double v, int decimals) => double.IsNaN(v) ? "—" : v.ToString("F" + decimals, Invariant);

        /// <summary>Kerbin calendar: 6-hour days, 426-day years. Negative durations get a minus sign.</summary>
        public static string Duration(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds)) return "—";
            string sign = seconds < 0 ? "-" : "";
            var s = (long)Math.Round(Math.Abs(seconds));
            long day = (long)SecondsPerDay, year = (long)(SecondsPerDay * DaysPerYear);
            long years = s / year, days = s % year / day, hours = s % day / 3600, minutes = s % 3600 / 60, secs = s % 60;
            if (years > 0) return sign + Loc.F("Time/YearsDaysHours", years, days, hours);
            if (days > 0) return sign + Loc.F("Time/DaysHoursMinutes", days, hours, minutes);
            if (hours > 0) return sign + Loc.F("Time/HoursMinutes", hours, minutes);
            return sign + Loc.F("Time/MinutesSeconds", minutes, secs);
        }

        /// <summary>Reads a number typed in either the invariant or the player's own format.</summary>
        public static bool TryParse(string text, out double value)
        {
            text = text?.Trim() ?? "";
            return double.TryParse(text, NumberStyles.Float, Invariant, out value)
                   || double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
        }

        /// <summary>Seconds, or h:m:s / m:s.</summary>
        public static bool TryParseTime(string text, out double seconds)
        {
            seconds = 0;
            var parts = (text ?? "").Trim().Split(':');
            if (parts.Length > 3) return false;
            foreach (var part in parts)
            {
                if (!TryParse(part, out double v) || v < 0) return false;
                seconds = seconds * 60 + v;
            }
            return true;
        }

        public static string TimeText(double seconds)
        {
            var s = (long)Math.Round(Math.Max(0, seconds));
            return s >= 3600 ? $"{s / 3600}:{s % 3600 / 60:00}:{s % 60:00}" : s >= 60 ? $"{s / 60}:{s % 60:00}" : s.ToString(Invariant);
        }
    }

    /// <summary>The original Flight Plan's icons, loaded once from the mod's assets/images folder.</summary>
    internal static class Icons
    {
        internal static string Folder;
        private static readonly System.Collections.Generic.Dictionary<string, Texture2D> Cache =
            new System.Collections.Generic.Dictionary<string, Texture2D>();

        public static Texture2D Get(string name)
        {
            if (Cache.TryGetValue(name, out var texture)) return texture;
            texture = null;
            try
            {
                string path = System.IO.Path.Combine(Folder ?? "", name + ".png");
                if (System.IO.File.Exists(path))
                {
                    texture = new Texture2D(2, 2) { filterMode = FilterMode.Bilinear };
                    texture.LoadImage(System.IO.File.ReadAllBytes(path));
                }
                else SWLog.Warning($"Icon {name}.png is missing");
            }
            catch (Exception e)
            {
                SWLog.Warning($"Couldn't load icon {name}: {e.Message}");
            }
            Cache[name] = texture;
            return texture;
        }

        /// <summary>An element showing an icon scaled to fit.</summary>
        public static VisualElement Image(string name, float size)
        {
            var image = new VisualElement { pickingMode = PickingMode.Ignore };
            image.style.width = image.style.height = size;
            image.style.flexShrink = 0;
            var texture = Get(name);
            if (texture != null) image.style.backgroundImage = new StyleBackground(texture);
            image.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
            return image;
        }
    }

    /// <summary>
    /// Hover tips. UI Toolkit's own tooltip shows nothing in the game's panels, so the window floats one label
    /// beside whatever the pointer is over.
    /// </summary>
    internal sealed class Tips
    {
        private readonly VisualElement _root;
        private Label _tip;

        public Tips(VisualElement root) => _root = root;

        /// <summary>Shows text beside an element while the pointer is over it. The text is read when shown.</summary>
        public void Add(VisualElement target, Func<string> text)
        {
            target.RegisterCallback<PointerEnterEvent>(_ => Show(target.worldBound, text()));
            target.RegisterCallback<PointerLeaveEvent>(_ => Hide());
            target.RegisterCallback<DetachFromPanelEvent>(_ => Hide());
        }

        private void Show(Rect worldBounds, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (_tip == null)
            {
                _tip = Ui.Text("", 11, Ui.InkColor);
                _tip.pickingMode = PickingMode.Ignore;
                _tip.style.position = Position.Absolute;
                _tip.style.maxWidth = 260;
                _tip.style.whiteSpace = WhiteSpace.Normal;
                _tip.style.backgroundColor = new Color(0.04f, 0.05f, 0.08f, 0.96f);
                Ui.SetBorder(_tip, Ui.LineColor, 1);
                Ui.SetRadius(_tip, 3);
                Ui.SetPadding(_tip, 4, 8);
            }
            if (_tip.parent != _root) _root.Add(_tip);
            _tip.text = text;
            _tip.style.display = DisplayStyle.Flex;
            _tip.BringToFront();
            var bounds = _root.WorldToLocal(worldBounds);
            float width = _root.layout.width, height = _root.layout.height;
            _tip.style.left = Mathf.Clamp(bounds.x, 4, Math.Max(4, width - 268));
            _tip.style.top = bounds.yMax + 6 < height - 60 ? bounds.yMax + 6 : bounds.y - 52;
        }

        public void Hide()
        {
            if (_tip != null) _tip.style.display = DisplayStyle.None;
        }
    }
}
