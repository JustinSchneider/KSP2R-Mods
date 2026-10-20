using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UitkForKsp2.API;
using UitkForKsp2.API.Manipulator;
using UitkForKsp2.Controls;
using UnityEngine;
using UnityEngine.UIElements;

namespace TransferWindowRedux
{
    /// <summary>
    /// The mod's window, built in code with UI Toolkit (no UXML/Addressables needed for a window this small).
    /// It uses the same <see cref="AppShell"/> template and KerbalUI classes as the stock windows (Kerbal Manager
    /// and friends), so the frame, header, close button and resize handle come from the game's theme.
    /// </summary>
    public class TransferWindowUI
    {
        public event Action CloseClicked;
        public event Action WarpClicked;
        public event Action CreateNodeClicked;
        /// <summary>Hand the transfer over to Flight Plan Redux (only offered when it's installed).</summary>
        public event Action FlightPlanClicked;
        public event Action<string> TargetSelected;
        /// <summary>
        /// The player moved or resized the window: top-left corner in panel coordinates, and the size
        /// (height -1 until the window is first resized, so it follows the content).
        /// </summary>
        public event Action<Rect> LayoutChanged;

        private const double SecondsPerDay = 21600;   // Kerbin: 6-hour days
        private const double DaysPerYear = 426;
        private const double InclinationWarning = 2;  // degrees
        private const float DefaultWidth = 340;
        private const float MinWidth = 300;
        private const float MinHeight = 200;

        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
        private static readonly Color SectionColor = new Color32(140, 146, 230, 255);
        private static readonly Color ValueColor = new Color32(230, 235, 244, 255);
        private static readonly Color DimColor = new Color32(127, 133, 152, 255);
        private static readonly Color Good = new Color(0.45f, 0.9f, 0.4f);
        private static readonly Color Warn = new Color(1f, 0.74f, 0.2f);
        private static readonly Color Bad = new Color(0.95f, 0.33f, 0.27f);

        private readonly AppShell _root;
        private readonly DropdownField _destination;
        private readonly Label _route, _note, _message, _hint;
        private readonly VisualElement _stats, _actions;
        private readonly Stat _phase, _windowIn, _windowAt, _nextWindow, _transferTime, _inclination;
        private readonly Stat _ejection, _ejectionAngle, _capture, _budget;
        private readonly VisualElement _budgetFill;
        private readonly Button _nodeButton, _warpButton, _flightPlanButton;
        private readonly List<Stat> _allStats = new List<Stat>();
        private readonly List<(Label Label, string Key)> _sections = new List<(Label, string)>();
        private Stat _hovered;
        private Rect? _savedLayout, _pendingLayout;

        private class Stat
        {
            public VisualElement Row;
            public Label Name;
            public Label Value;
            public string Key;
        }

        /// <param name="savedLayout">Position and size from the config; negative values mean "use the default".</param>
        public TransferWindowUI(Texture2D icon, Rect savedLayout)
        {
            _root = new AppShell { name = "TransferWindowRedux", IconTexture = icon };
            _root.CloseClicked += () => CloseClicked?.Invoke();
            _root.style.width = savedLayout.width > 0 ? Math.Max(MinWidth, savedLayout.width) : DefaultWidth;
            if (savedLayout.height > 0) _root.style.height = Math.Max(MinHeight, savedLayout.height);

            _destination = new DropdownField { choices = new List<string>() };
            WideDropdownMenu.Apply(_destination);
            _destination.style.flexGrow = 0;
            _destination.style.marginBottom = 8;
            _destination.RegisterValueChangedCallback(evt =>
            {
                if (_destination.choices.Contains(evt.newValue)) TargetSelected?.Invoke(evt.newValue);
            });
            _root.Add(_destination);

            // The notched dark panel the stock windows put their content in.
            var box = new InvertedCornerBox();
            box.AddToClassList("oab-window-body");
            box.style.flexGrow = 1;
            box.style.flexShrink = 1;
            box.style.marginBottom = 8;
            _root.Add(box);

            // Scrolls only when the player shrinks the window below its content.
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.style.flexGrow = 1;
            scroll.style.flexShrink = 1;
            scroll.style.height = StyleKeyword.Auto;
            scroll.style.backgroundColor = Color.clear;
            SetPadding(scroll, 0, 0);
            box.Add(scroll);

            _route = SmallLabel(DimColor);
            scroll.Add(_route);
            _note = SmallLabel(Warn);
            _note.style.marginTop = 2;
            scroll.Add(_note);

            _message = new Label();
            _message.style.whiteSpace = WhiteSpace.Normal;
            scroll.Add(_message);

            _stats = new VisualElement();
            scroll.Add(_stats);

            Section("Window");
            _phase = AddStat("Phase");
            _windowIn = AddStat("WindowIn");
            _windowAt = AddStat("WindowAt");
            _nextWindow = AddStat("NextWindow");

            Section("Transfer");
            _transferTime = AddStat("TransferTime");
            _inclination = AddStat("Inclination");

            Section("Burns");
            _ejection = AddStat("Ejection");
            _ejectionAngle = AddStat("EjectionAngle");
            _capture = AddStat("Capture");
            _budget = AddStat("Budget");

            // Same colours as the theme's progress bar track.
            var track = new VisualElement();
            track.style.height = 4;
            track.style.marginTop = 2;
            track.style.backgroundColor = (Color)new Color32(41, 47, 57, 255);
            SetRadius(track, 2);
            _budgetFill = new VisualElement();
            _budgetFill.style.height = Length.Percent(100);
            SetRadius(_budgetFill, 2);
            track.Add(_budgetFill);
            _stats.Add(track);

            _hint = SmallLabel(DimColor);
            _hint.style.minHeight = 30;
            _hint.style.marginTop = 8;
            scroll.Add(_hint);

            // Action row under the panel, as in the stock dialogs.
            _actions = new VisualElement();
            _actions.AddToClassList("oab-window-actions");
            _actions.style.flexShrink = 0;
            _nodeButton = new Button(() => CreateNodeClicked?.Invoke());
            _warpButton = new Button(() => WarpClicked?.Invoke());
            _flightPlanButton = new Button(() => FlightPlanClicked?.Invoke());
            foreach (var button in new[] { _nodeButton, _warpButton, _flightPlanButton })
            {
                button.AddToClassList("oab-window-action-button");
                button.style.flexBasis = 0;
                // Translations can outgrow half the window: shrink the text to fit, then cut it off with "…"
                // rather than letting it spill over the neighbouring button.
                button.style.paddingLeft = button.style.paddingRight = 4;
                button.style.overflow = Overflow.Hidden;
                button.style.whiteSpace = WhiteSpace.NoWrap;
                button.style.textOverflow = TextOverflow.Ellipsis;
                button.style.unityTextAutoSize = new TextAutoSize(TextAutoSizeMode.BestFit, 10, 14);
                _actions.Add(button);
            }
            _nodeButton.style.marginRight = 8;
            _flightPlanButton.style.marginLeft = 8;
            _flightPlanButton.style.display = DisplayStyle.None;
            _root.Add(_actions);

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

            ApplyFixedText();
            Loc.Changed += ApplyFixedText;

            // ResizeOptions in WindowOptions would put the handle in AppShell's content area, a padding's width in
            // from the corner. Parenting it to the shell itself puts it in the corner like the stock windows.
            var resizeHandle = new VisualElement();
            _root.hierarchy.Add(resizeHandle);
            _root.AddManipulator(new ResizeManipulator(
                new ResizeOptions { IsResizingEnabled = true, CheckScreenBounds = true, MinWidth = MinWidth, MinHeight = MinHeight },
                resizeHandle));

            _root.style.position = Position.Absolute;
            if (savedLayout.x >= 0 && savedLayout.y >= 0)
            {
                _savedLayout = CurrentLayout(savedLayout.position);
                _root.style.left = savedLayout.x;
                _root.style.top = savedLayout.y;
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

        /// <summary>Text that's set once rather than on every refresh; re-applied when the language changes.</summary>
        private void ApplyFixedText()
        {
            _root.Title = Loc.T("Title");
            if (!_destination.choices.Contains(_destination.value)) _destination.SetValueWithoutNotify(Loc.T("Placeholder"));
            foreach (var (label, key) in _sections) label.text = Loc.T("Section/" + key).ToUpper(CultureInfo.CurrentCulture);
            foreach (var stat in _allStats) stat.Name.text = Loc.T("Stat/" + stat.Key);
            _nodeButton.text = Loc.T("Button/CreateNode");
            _warpButton.text = Loc.T("Button/Warp");
            _flightPlanButton.text = Loc.T("Button/FlightPlan");
            if (_hovered != null) _hint.text = Loc.T("Hint/" + _hovered.Key);
        }

        public bool Visible
        {
            get => _root.style.display != DisplayStyle.None;
            set => _root.style.display = value ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>
        /// Reports a drag or resize once the window has stayed put for a refresh, so the layout is saved without
        /// writing config on every pointer move.
        /// </summary>
        public void CheckLayout()
        {
            var position = _root.worldBound.position;
            if (float.IsNaN(position.x) || float.IsNaN(position.y)) return;
            var layout = CurrentLayout(position);
            if (_savedLayout == null)
            {
                // First layout: remember where the window started without saving it.
                _savedLayout = layout;
                return;
            }
            if (Approximately(layout, _savedLayout.Value)) return;
            if (_pendingLayout != null && Approximately(layout, _pendingLayout.Value))
            {
                _savedLayout = layout;
                _pendingLayout = null;
                LayoutChanged?.Invoke(layout);
                return;
            }
            _pendingLayout = layout;
        }

        /// <summary>
        /// The size comes from the inline style, which only the resize handle (or a saved size) sets, so a window
        /// that grows with its content isn't mistaken for a resize.
        /// </summary>
        private Rect CurrentLayout(Vector2 position)
        {
            float width = _root.style.width.keyword == StyleKeyword.Undefined ? _root.style.width.value.value : -1;
            float height = _root.style.height.keyword == StyleKeyword.Undefined ? _root.style.height.value.value : -1;
            return new Rect(position.x, position.y, width, height);
        }

        /// <summary>Fills the destination list and shows the current target (or a prompt) without raising events.</summary>
        public void SetTargets(List<string> names, string current)
        {
            if (!_destination.choices.SequenceEqual(names)) _destination.choices = names;
            _destination.SetEnabled(names.Count > 0);
            _destination.SetValueWithoutNotify(current ?? Loc.T("Placeholder"));
        }

        public void ShowProblem(string text)
        {
            _message.text = text;
            _message.style.display = DisplayStyle.Flex;
            _stats.style.display = DisplayStyle.None;
            _actions.style.display = DisplayStyle.None;
            _route.style.display = DisplayStyle.None;
            _note.style.display = DisplayStyle.None;
        }

        public void ShowTransfer(TransferInfo info, double ut, bool isWarping, string nodeButtonText, bool flightPlanInstalled)
        {
            _message.style.display = DisplayStyle.None;
            _stats.style.display = DisplayStyle.Flex;
            _actions.style.display = DisplayStyle.Flex;
            _route.style.display = DisplayStyle.Flex;

            _route.text = $"{info.OriginName}  →  {info.TargetName}" + (info.IsReturn ? "  " + Loc.T("Route/Return") : "");
            _note.text = !info.FromParkingOrbit ? ""
                : info.OriginIsVessel || info.IsReturn ? Loc.F("Note/ReachOrbit", Km(info.ParkingAltitude))
                : Loc.F("Note/NotInOrbit", Km(info.ParkingAltitude));
            _note.style.display = info.FromParkingOrbit ? DisplayStyle.Flex : DisplayStyle.None;

            // Window. A return has no phase angle: a window comes round once per orbit.
            SetShown(_phase.Row, !info.IsReturn);
            SetShown(_inclination.Row, !info.IsReturn);
            _phase.Value.text = info.WindowKnown
                ? $"{Deg(info.PhaseAngle)}  →  {Deg(info.DesiredPhaseAngle)}"
                : Loc.F("Value/Needs", Deg(info.DesiredPhaseAngle));
            _windowIn.Value.text = info.WindowKnown ? FormatDuration(info.TimeToWindow) : "—";
            _windowAt.Value.text = info.WindowKnown ? FormatDate(ut + info.TimeToWindow) : "—";
            _nextWindow.Value.text = double.IsNaN(info.SynodicPeriod) ? "—"
                : info.WindowKnown ? Loc.F("Value/In", FormatDuration(info.TimeToWindow + info.SynodicPeriod))
                : Loc.F("Value/Every", FormatDuration(info.SynodicPeriod));

            // Transfer
            _transferTime.Value.text = FormatDuration(info.TransferTime);
            _inclination.Value.text = double.IsNaN(info.RelativeInclination) ? "—" : Deg(info.RelativeInclination);
            _inclination.Value.style.color = info.RelativeInclination > InclinationWarning ? Warn : ValueColor;

            // Burns
            _ejection.Value.text = double.IsNaN(info.EjectionDeltaV) ? Loc.T("Value/NotFromHere") : Speed(info.EjectionDeltaV);
            SetShown(_ejectionAngle.Row, !double.IsNaN(info.EjectionAngle));
            _ejectionAngle.Value.text = Loc.F(info.EjectionFromRetrograde ? "Value/ToRetrograde" : "Value/ToPrograde", Deg(info.EjectionAngle, 0));
            if (info.CaptureByAerobraking)
            {
                _capture.Name.text = Loc.F("Stat/Reentry", Km(info.CaptureAltitude));
                _capture.Value.text = Loc.T("Value/Aerobrake");
            }
            else
            {
                _capture.Name.text = Loc.F("Stat/CaptureAt", Km(info.CaptureAltitude));
                _capture.Value.text = Speed(info.CaptureDeltaV) + (info.TargetHasAtmosphere ? " · " + Loc.T("Value/Aero") : "");
            }
            ShowBudget(info);

            _nodeButton.text = nodeButtonText;
            _nodeButton.SetEnabled(info.CanPlaceNode);
            _warpButton.text = Loc.T(isWarping ? "Button/StopWarp" : "Button/Warp");
            _warpButton.SetEnabled(isWarping || info.WindowKnown);
            SetShown(_flightPlanButton, flightPlanInstalled);
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
            if (years > 0) return Loc.F("Time/YearsDaysHours", years, days, hours);
            if (days > 0) return Loc.F("Time/DaysHoursMinutes", days, hours, minutes);
            if (hours > 0) return Loc.F("Time/HoursMinutes", hours, minutes);
            return Loc.F("Time/MinutesSeconds", minutes, secs);
        }

        /// <summary>An absolute time in the same Year/Day form as the stock time warp display.</summary>
        public static string FormatDate(double ut)
        {
            if (double.IsNaN(ut) || double.IsInfinity(ut)) return "—";
            var s = (long)Math.Max(0, Math.Floor(ut));
            long day = (long)SecondsPerDay, year = (long)(SecondsPerDay * DaysPerYear);
            long years = s / year + 1, days = s % year / day + 1, hours = s % day / 3600, minutes = s % 3600 / 60;
            return Loc.F("Time/Date", years, days, $"{hours:00}:{minutes:00}");
        }

        private void Section(string key)
        {
            var label = new Label();
            _sections.Add((label, key));
            label.style.color = SectionColor;
            label.style.fontSize = 11;
            label.style.letterSpacing = 2;
            label.style.marginTop = 10;
            label.style.marginBottom = 2;
            label.style.paddingBottom = 2;
            label.style.borderBottomWidth = 1;
            label.style.borderBottomColor = new Color(1, 1, 1, 0.06f);
            _stats.Add(label);
        }

        /// <summary>A label/value row; its name is Stat/{key} and its hover hint Hint/{key} in the CSV.</summary>
        private Stat AddStat(string key)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.justifyContent = Justify.SpaceBetween;
            row.style.alignItems = Align.Center;
            row.style.minHeight = 20;

            // Translated names can run long: let the name wrap and keep the figure on one line.
            var label = new Label();
            label.style.flexShrink = 1;
            label.style.whiteSpace = WhiteSpace.Normal;
            var value = new Label("—");
            value.style.color = ValueColor;
            value.style.unityTextAlign = TextAnchor.MiddleRight;
            value.style.flexShrink = 0;
            value.style.marginLeft = 8;
            row.Add(label);
            row.Add(value);

            var stat = new Stat { Row = row, Name = label, Value = value, Key = key };
            _allStats.Add(stat);

            // A hint line at the bottom explains whichever row is under the pointer.
            row.RegisterCallback<PointerEnterEvent>(_ =>
            {
                _hovered = stat;
                _hint.text = Loc.T("Hint/" + key);
                row.style.backgroundColor = new Color(1, 1, 1, 0.04f);
            });
            row.RegisterCallback<PointerLeaveEvent>(_ =>
            {
                _hovered = null;
                _hint.text = "";
                row.style.backgroundColor = Color.clear;
            });

            _stats.Add(row);
            return stat;
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

        private static bool Approximately(Rect a, Rect b) =>
            (a.position - b.position).sqrMagnitude < 1f && (a.size - b.size).sqrMagnitude < 1f;

        private static void SetShown(VisualElement element, bool shown) =>
            element.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;

        private static void SetPadding(VisualElement element, float vertical, float horizontal)
        {
            element.style.paddingTop = element.style.paddingBottom = vertical;
            element.style.paddingLeft = element.style.paddingRight = horizontal;
        }

        private static void SetRadius(VisualElement element, float radius)
        {
            element.style.borderTopLeftRadius = element.style.borderTopRightRadius = radius;
            element.style.borderBottomLeftRadius = element.style.borderBottomRightRadius = radius;
        }
    }
}
