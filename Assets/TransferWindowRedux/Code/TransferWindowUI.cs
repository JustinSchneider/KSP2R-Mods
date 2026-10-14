using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UitkForKsp2.API;
using UnityEngine;
using UnityEngine.UIElements;

namespace TransferWindowRedux
{
    /// <summary>
    /// The mod's window, built in code with UI Toolkit (no UXML/Addressables needed for a window this small).
    /// Styled after the stock flight HUD panels: a dark panel with a header strip and an uppercase title.
    /// </summary>
    public class TransferWindowUI
    {
        public event Action CloseClicked;
        public event Action WarpClicked;
        public event Action CreateNodeClicked;
        public event Action<string> TargetSelected;
        /// <summary>The player dragged the window; the new top-left corner in panel coordinates.</summary>
        public event Action<Vector2> Moved;

        private const string Placeholder = "Choose a destination…";
        private const double SecondsPerDay = 21600;   // Kerbin: 6-hour days
        private const double DaysPerYear = 426;
        private const double InclinationWarning = 2;  // degrees

        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
        private static readonly Color PanelColor = new Color(0.08f, 0.09f, 0.12f, 0.94f);
        private static readonly Color HeaderColor = new Color(0.13f, 0.15f, 0.2f, 1f);
        private static readonly Color BorderColor = new Color(0.25f, 0.28f, 0.36f, 1f);
        private static readonly Color TitleColor = new Color(0.82f, 0.85f, 0.92f);
        private static readonly Color SectionColor = new Color(0.47f, 0.55f, 0.95f);
        private static readonly Color LabelColor = new Color(0.62f, 0.65f, 0.72f);
        private static readonly Color ValueColor = new Color(0.93f, 0.94f, 0.96f);
        private static readonly Color DimColor = new Color(0.5f, 0.53f, 0.6f);
        private static readonly Color Good = new Color(0.45f, 0.9f, 0.4f);
        private static readonly Color Warn = new Color(1f, 0.74f, 0.2f);
        private static readonly Color Bad = new Color(0.95f, 0.33f, 0.27f);

        private readonly VisualElement _root;
        private readonly DropdownField _destination;
        private readonly Label _route, _note, _message, _hint;
        private readonly VisualElement _stats;
        private readonly Stat _phase, _windowIn, _windowAt, _nextWindow, _transferTime, _inclination;
        private readonly Stat _ejection, _ejectionAngle, _capture, _budget;
        private readonly VisualElement _budgetFill;
        private readonly Button _nodeButton, _warpButton;
        private Vector2? _savedPosition, _pendingPosition;

        private class Stat
        {
            public VisualElement Row;
            public Label Name;
            public Label Value;
        }

        public TransferWindowUI(Texture2D icon, Vector2 savedPosition)
        {
            _root = new VisualElement { name = "transfer-window" };
            _root.style.width = 300;
            _root.style.backgroundColor = PanelColor;
            SetBorder(_root, BorderColor, 1);
            SetRadius(_root, 4);
            _root.style.overflow = Overflow.Hidden;

            _root.Add(BuildHeader(icon));

            var body = new VisualElement();
            SetPadding(body, 8, 10);
            _root.Add(body);

            _destination = new DropdownField { choices = new List<string>(), value = Placeholder };
            _destination.style.marginLeft = _destination.style.marginRight = 0;
            _destination.RegisterValueChangedCallback(evt =>
            {
                if (_destination.choices.Contains(evt.newValue)) TargetSelected?.Invoke(evt.newValue);
            });
            body.Add(_destination);

            _route = SmallLabel(DimColor);
            _route.style.marginTop = 4;
            body.Add(_route);
            _note = SmallLabel(Warn);
            body.Add(_note);

            _message = new Label();
            _message.style.whiteSpace = WhiteSpace.Normal;
            _message.style.color = LabelColor;
            _message.style.marginTop = 6;
            body.Add(_message);

            _stats = new VisualElement();
            body.Add(_stats);

            Section("Window");
            _phase = AddStat("Phase angle", "Where the target is relative to you now, and where it needs to be when you leave.");
            _windowIn = AddStat("Window in", "Time until the phase angle lines up.");
            _windowAt = AddStat("Window at", "The date of the window on the Kerbin calendar.");
            _nextWindow = AddStat("Next window", "If you miss this one, the following window is this far away.");

            Section("Transfer");
            _transferTime = AddStat("Transfer time", "Coast time from departure to arrival on a Hohmann transfer.");
            _inclination = AddStat("Inclination", "Angle between your orbit and the target's. Above about 2° you'll need a plane change or a correction burn.");

            Section("Burns");
            _ejection = AddStat("Ejection Δv", "Departure burn from your orbit. Not shown when orbiting a moon of the departure planet: that escape crosses two spheres of influence.");
            _ejectionAngle = AddStat("Ejection angle", "Burn this far before the prograde or retrograde direction of the body you're leaving. The node button places it for you.");
            _capture = AddStat("Capture Δv", "Burn to enter a low circular orbit at the target. Aerobraking can replace most of it where there's an atmosphere; returns home aim halfway into it and need no burn (change \"Return periapsis\" in the settings).");
            _budget = AddStat("Total / vessel", "Ejection plus capture, against your vessel's total vacuum Δv. Doesn't include getting to orbit.");

            var track = new VisualElement();
            track.style.height = 4;
            track.style.marginTop = 2;
            track.style.backgroundColor = new Color(1, 1, 1, 0.08f);
            SetRadius(track, 2);
            _budgetFill = new VisualElement();
            _budgetFill.style.height = Length.Percent(100);
            SetRadius(_budgetFill, 2);
            track.Add(_budgetFill);
            _stats.Add(track);

            var buttons = new VisualElement();
            buttons.style.flexDirection = FlexDirection.Row;
            buttons.style.marginTop = 10;
            _nodeButton = new Button(() => CreateNodeClicked?.Invoke()) { text = "Create node" };
            _warpButton = new Button(() => WarpClicked?.Invoke()) { text = "Warp to window" };
            foreach (var button in new[] { _nodeButton, _warpButton })
            {
                button.style.flexGrow = 1;
                button.style.flexBasis = 0;
                button.style.marginLeft = button.style.marginRight = 0;
                buttons.Add(button);
            }
            _nodeButton.style.marginRight = 6;
            _stats.Add(buttons);

            _hint = SmallLabel(DimColor);
            _hint.style.whiteSpace = WhiteSpace.Normal;
            _hint.style.minHeight = 30;
            _hint.style.marginTop = 8;
            body.Add(_hint);

            var document = Window.Create(new WindowOptions
            {
                WindowId = "TransferWindowRedux_Main",
                Parent = null,
                IsHidingEnabled = true,
                UseStockScale = true,
                BringToFrontOnPointerDown = true,
                MoveOptions = new MoveOptions { IsMovingEnabled = true, CheckScreenBounds = true, HandleElementName = "header" },
            }, _root);
            document.EnableUiSounds();

            _root.style.position = Position.Absolute;
            if (savedPosition.x >= 0 && savedPosition.y >= 0)
            {
                _savedPosition = savedPosition;
                _root.style.left = savedPosition.x;
                _root.style.top = savedPosition.y;
            }
            else
            {
                // Top right, left of the stock crew and resources panels.
                _root.SetDefaultPosition(size =>
                {
                    var panel = _root.panel?.visualTree?.contentRect ?? new Rect(0, 0, 1920, 1080);
                    return new Vector2(panel.width - size.x - 260, 110);
                });
            }
        }

        public bool Visible
        {
            get => _root.style.display != DisplayStyle.None;
            set => _root.style.display = value ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>
        /// Reports a drag once the window has stayed put for a refresh, so the position is saved without
        /// writing config on every pointer move.
        /// </summary>
        public void CheckMoved()
        {
            var position = _root.worldBound.position;
            if (float.IsNaN(position.x) || float.IsNaN(position.y)) return;
            if (_savedPosition == null)
            {
                // First layout: remember where the window started without saving it.
                _savedPosition = position;
                return;
            }
            if (Approximately(position, _savedPosition.Value)) return;
            if (_pendingPosition != null && Approximately(position, _pendingPosition.Value))
            {
                _savedPosition = position;
                _pendingPosition = null;
                Moved?.Invoke(position);
                return;
            }
            _pendingPosition = position;
        }

        /// <summary>Fills the destination list and shows the current target (or a prompt) without raising events.</summary>
        public void SetTargets(List<string> names, string current)
        {
            if (!_destination.choices.SequenceEqual(names)) _destination.choices = names;
            _destination.SetEnabled(names.Count > 0);
            _destination.SetValueWithoutNotify(current ?? Placeholder);
        }

        public void ShowProblem(string text)
        {
            _message.text = text;
            _message.style.display = DisplayStyle.Flex;
            _stats.style.display = DisplayStyle.None;
            _route.style.display = DisplayStyle.None;
            _note.style.display = DisplayStyle.None;
        }

        public void ShowTransfer(TransferInfo info, double ut, bool isWarping, string nodeButtonText)
        {
            _message.style.display = DisplayStyle.None;
            _stats.style.display = DisplayStyle.Flex;
            _route.style.display = DisplayStyle.Flex;

            _route.text = $"{info.OriginName}  →  {info.TargetName}" + (info.IsReturn ? "  (return)" : "");
            string parking = $"{Km(info.ParkingAltitude)} km parking orbit";
            _note.text = !info.FromParkingOrbit ? ""
                : info.OriginIsVessel || info.IsReturn ? $"Reach orbit to see the window. Δv assumes a {parking}."
                : $"Not in orbit: departure figures assume a {parking}.";
            _note.style.display = info.FromParkingOrbit ? DisplayStyle.Flex : DisplayStyle.None;

            // Window. A return has no phase angle: a window comes round once per orbit.
            SetShown(_phase.Row, !info.IsReturn);
            SetShown(_inclination.Row, !info.IsReturn);
            _phase.Value.text = info.WindowKnown
                ? $"{Deg(info.PhaseAngle)}  →  {Deg(info.DesiredPhaseAngle)}"
                : $"needs {Deg(info.DesiredPhaseAngle)}";
            _windowIn.Value.text = info.WindowKnown ? FormatDuration(info.TimeToWindow) : "—";
            _windowAt.Value.text = info.WindowKnown ? FormatDate(ut + info.TimeToWindow) : "—";
            _nextWindow.Value.text = double.IsNaN(info.SynodicPeriod) ? "—"
                : info.WindowKnown ? $"in {FormatDuration(info.TimeToWindow + info.SynodicPeriod)}"
                : $"every {FormatDuration(info.SynodicPeriod)}";

            // Transfer
            _transferTime.Value.text = FormatDuration(info.TransferTime);
            _inclination.Value.text = double.IsNaN(info.RelativeInclination) ? "—" : Deg(info.RelativeInclination);
            _inclination.Value.style.color = info.RelativeInclination > InclinationWarning ? Warn : ValueColor;

            // Burns
            _ejection.Value.text = double.IsNaN(info.EjectionDeltaV) ? "n/a from here" : Speed(info.EjectionDeltaV);
            SetShown(_ejectionAngle.Row, !double.IsNaN(info.EjectionAngle));
            _ejectionAngle.Value.text = $"{Deg(info.EjectionAngle, 0)} to {(info.EjectionFromRetrograde ? "retrograde" : "prograde")}";
            if (info.CaptureByAerobraking)
            {
                _capture.Name.text = $"Reentry (Pe {Km(info.CaptureAltitude)} km)";
                _capture.Value.text = "aerobrake";
            }
            else
            {
                _capture.Name.text = $"Capture Δv ({Km(info.CaptureAltitude)} km)";
                _capture.Value.text = Speed(info.CaptureDeltaV) + (info.TargetHasAtmosphere ? " · aero" : "");
            }
            ShowBudget(info);

            _nodeButton.text = nodeButtonText;
            _nodeButton.SetEnabled(info.CanPlaceNode);
            _warpButton.text = isWarping ? "Stop warp" : "Warp to window";
            _warpButton.SetEnabled(isWarping || info.WindowKnown);
        }

        private void ShowBudget(TransferInfo info)
        {
            double need = info.TotalDeltaV;
            double have = info.AvailableDeltaV;
            if (double.IsNaN(have))
            {
                _budget.Value.text = $"{Speed(need)} / —";
                _budget.Value.style.color = ValueColor;
                _budgetFill.style.width = 0;
                return;
            }

            Color color = have >= need * 1.1 ? Good : have >= need ? Warn : Bad;
            _budget.Value.text = $"{need.ToString("N0", Invariant)} / {Speed(have)}";
            _budget.Value.style.color = color;
            _budgetFill.style.backgroundColor = color;
            _budgetFill.style.width = Length.Percent(have <= 0 ? 100 : (float)Math.Min(100, 100 * need / have));
        }

        /// <summary>Kerbin calendar: 6-hour days, 426-day years.</summary>
        public static string FormatDuration(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds)) return "—";
            var s = (long)Math.Max(0, Math.Round(seconds));
            long day = (long)SecondsPerDay, year = (long)(SecondsPerDay * DaysPerYear);
            long years = s / year, days = s % year / day, hours = s % day / 3600, minutes = s % 3600 / 60, secs = s % 60;
            if (years > 0) return $"{years}y {days}d {hours}h";
            if (days > 0) return $"{days}d {hours}h {minutes}m";
            if (hours > 0) return $"{hours}h {minutes}m";
            return $"{minutes}m {secs}s";
        }

        /// <summary>An absolute time in the same Year/Day form as the stock time warp display.</summary>
        public static string FormatDate(double ut)
        {
            if (double.IsNaN(ut) || double.IsInfinity(ut)) return "—";
            var s = (long)Math.Max(0, Math.Floor(ut));
            long day = (long)SecondsPerDay, year = (long)(SecondsPerDay * DaysPerYear);
            long years = s / year + 1, days = s % year / day + 1, hours = s % day / 3600, minutes = s % 3600 / 60;
            return $"Y{years}, D{days}, {hours:00}:{minutes:00}";
        }

        private VisualElement BuildHeader(Texture2D icon)
        {
            var header = new VisualElement { name = "header" };
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.backgroundColor = HeaderColor;
            header.style.borderBottomWidth = 1;
            header.style.borderBottomColor = BorderColor;
            SetPadding(header, 5, 8);

            if (icon != null)
            {
                var image = new Image { image = icon };
                image.style.width = image.style.height = 16;
                image.style.marginRight = 6;
                header.Add(image);
            }

            var title = new Label("TRANSFER WINDOW");
            title.style.flexGrow = 1;
            title.style.color = TitleColor;
            title.style.fontSize = 13;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.letterSpacing = 1;
            header.Add(title);

            var close = new Button(() => CloseClicked?.Invoke()) { text = "×" };
            close.style.backgroundColor = Color.clear;
            SetBorder(close, Color.clear, 0);
            SetPadding(close, 0, 4);
            close.style.marginLeft = close.style.marginRight = 0;
            close.style.fontSize = 18;
            close.style.color = TitleColor;
            close.RegisterCallback<PointerEnterEvent>(_ => close.style.color = Bad);
            close.RegisterCallback<PointerLeaveEvent>(_ => close.style.color = TitleColor);
            header.Add(close);
            return header;
        }

        private void Section(string name)
        {
            var label = new Label(name.ToUpperInvariant());
            label.style.color = SectionColor;
            label.style.fontSize = 11;
            label.style.letterSpacing = 1;
            label.style.marginTop = 8;
            label.style.marginBottom = 2;
            label.style.paddingBottom = 2;
            label.style.borderBottomWidth = 1;
            label.style.borderBottomColor = new Color(1, 1, 1, 0.06f);
            _stats.Add(label);
        }

        private Stat AddStat(string name, string description)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.justifyContent = Justify.SpaceBetween;
            row.style.alignItems = Align.Center;
            row.style.minHeight = 20;

            var label = new Label(name);
            label.style.color = LabelColor;
            var value = new Label("—");
            value.style.color = ValueColor;
            value.style.unityTextAlign = TextAnchor.MiddleRight;
            row.Add(label);
            row.Add(value);

            // A hint line at the bottom explains whichever row is under the pointer.
            row.RegisterCallback<PointerEnterEvent>(_ =>
            {
                _hint.text = description;
                row.style.backgroundColor = new Color(1, 1, 1, 0.04f);
            });
            row.RegisterCallback<PointerLeaveEvent>(_ =>
            {
                _hint.text = "";
                row.style.backgroundColor = Color.clear;
            });

            _stats.Add(row);
            return new Stat { Row = row, Name = label, Value = value };
        }

        private static Label SmallLabel(Color color)
        {
            var label = new Label();
            label.style.fontSize = 11;
            label.style.color = color;
            label.style.whiteSpace = WhiteSpace.Normal;
            return label;
        }

        private static string Deg(double degrees, int decimals = 1) =>
            double.IsNaN(degrees) ? "—" : degrees.ToString(decimals == 0 ? "F0" : "F1", Invariant) + "°";

        private static string Speed(double metresPerSecond) =>
            double.IsNaN(metresPerSecond) ? "—" : metresPerSecond.ToString("N0", Invariant) + " m/s";

        private static string Km(double metres) => (metres / 1000).ToString("N0", Invariant);

        private static bool Approximately(Vector2 a, Vector2 b) => (a - b).sqrMagnitude < 1f;

        private static void SetShown(VisualElement element, bool shown) =>
            element.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;

        private static void SetPadding(VisualElement element, float vertical, float horizontal)
        {
            element.style.paddingTop = element.style.paddingBottom = vertical;
            element.style.paddingLeft = element.style.paddingRight = horizontal;
        }

        private static void SetBorder(VisualElement element, Color color, float width)
        {
            element.style.borderTopWidth = element.style.borderBottomWidth = width;
            element.style.borderLeftWidth = element.style.borderRightWidth = width;
            element.style.borderTopColor = element.style.borderBottomColor = color;
            element.style.borderLeftColor = element.style.borderRightColor = color;
        }

        private static void SetRadius(VisualElement element, float radius)
        {
            element.style.borderTopLeftRadius = element.style.borderTopRightRadius = radius;
            element.style.borderBottomLeftRadius = element.style.borderBottomRightRadius = radius;
        }
    }
}
