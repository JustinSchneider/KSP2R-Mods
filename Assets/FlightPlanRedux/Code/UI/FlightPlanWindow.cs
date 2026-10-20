using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FlightPlanRedux.Bridges;
using FlightPlanRedux.Nodes;
using FlightPlanRedux.Planning;
using KSP.Sim.impl;
using KSP.Sim.Maneuver;
using UitkForKsp2.API;
using UitkForKsp2.API.Manipulator;
using UitkForKsp2.Controls;
using UnityEngine;
using UnityEngine.UIElements;

namespace FlightPlanRedux.UI
{
    internal enum Tab { Orbit, Target, Moon, Planet, Resonant, Nodes }

    /// <summary>What the window needs from the plugin.</summary>
    internal interface IFlightPlanHost
    {
        VesselComponent Vessel { get; }
        double UT { get; }
        /// <summary>The active vessel's saved inputs and picks; null without a vessel or a save.</summary>
        VesselPlanState State { get; }
        NodeService Nodes { get; }
        bool Experimental { get; }
        double SmallErrorPercent { get; }
        double LargeErrorPercent { get; }
        void SetTarget(IGGuid id);
        void WarpTo(double ut);
    }

    /// <summary>
    /// Flight Plan's window, on the stock <see cref="AppShell"/> template but laid out like the original: situation
    /// and target picker on top, icon tabs, the burn time, then each maneuver as a button with its value beside it.
    /// Below that a preview of the burn, the status line, and Make Node / Replace / K2-D2.
    /// </summary>
    internal sealed class FlightPlanWindow
    {
        public event Action CloseClicked;
        public event Action<Rect> LayoutChanged;

        private const float DefaultWidth = 380, DefaultHeight = 640, MinWidth = 340, MinHeight = 360;
        private const float ConfirmSeconds = 4, ResultSeconds = 10;
        private const float FieldWidth = 96;

        private enum TargetKind { Celestial, Vessel, Port }

        private static readonly Dictionary<Tab, string> TabIcons = new Dictionary<Tab, string>
        {
            { Tab.Orbit, "OSM_50" }, { Tab.Target, "TRM_50_Ship2Ship" }, { Tab.Moon, "OTM_50_Moon" },
            { Tab.Planet, "OTM_50_Planet" }, { Tab.Resonant, "ROM_50" }
        };

        /// <summary>The maneuver buttons of each tab, in the original's order.</summary>
        private static ManeuverType[] ManeuversFor(Tab tab, bool vesselTarget) => tab switch
        {
            Tab.Orbit => new[]
            {
                ManeuverType.Circularize, ManeuverType.NewPe, ManeuverType.NewAp, ManeuverType.NewPeAp,
                ManeuverType.NewInc, ManeuverType.NewLan, ManeuverType.NewSma
            },
            Tab.Target => vesselTarget
                ? new[]
                {
                    ManeuverType.MatchPlanes, ManeuverType.NewAp, ManeuverType.Circularize, ManeuverType.HohmannTransfer,
                    ManeuverType.CourseCorrection, ManeuverType.MatchVelocity, ManeuverType.InterceptTarget
                }
                : new[]
                {
                    ManeuverType.MatchPlanes, ManeuverType.HohmannTransfer, ManeuverType.CourseCorrection,
                    ManeuverType.InterceptTarget, ManeuverType.MatchVelocity
                },
            Tab.Moon => new[] { ManeuverType.MoonReturn },
            Tab.Planet => new[] { ManeuverType.PlanetaryTransfer },
            Tab.Resonant => new[] { ManeuverType.FixAp, ManeuverType.FixPe },
            _ => Array.Empty<ManeuverType>()
        };

        /// <summary>The MechJeb solvers behind these take a while, so their preview isn't redone every refresh.</summary>
        private static readonly HashSet<ManeuverType> Heavy = new HashSet<ManeuverType>
        {
            ManeuverType.HohmannTransfer, ManeuverType.CourseCorrection, ManeuverType.MoonReturn,
            ManeuverType.PlanetaryTransfer, ManeuverType.InterceptTarget
        };

        private static readonly double[] DvSteps = { 0.1, 1, 10, 100 };
        private static readonly double[] TimeSteps = { 1, 10, 60, 600, -1 };   // -1 = one orbit

        private readonly IFlightPlanHost _host;
        private readonly AppShell _root;
        private readonly Tips _tips;
        private readonly List<Action> _fixedText = new List<Action>();
        private Rect? _savedLayout, _pendingLayout;

        // Top: situation, target picker, tabs
        private readonly Label _situation;
        private readonly Button _kindButton;
        private readonly DropdownField _targetDropdown;
        private readonly List<(string Label, IGGuid Id)> _targets = new List<(string, IGGuid)>();
        private TargetKind _kind;
        private bool _kindChosen;
        private readonly Dictionary<Tab, Button> _tabButtons = new Dictionary<Tab, Button>();
        private readonly VisualElement _targetTabIcon;
        private string _targetTabIconName;

        // Planner pane
        private readonly VisualElement _plannerPane, _timeRow, _maneuverList, _resonantBox;
        private readonly Label _tabTitle, _statusLabel, _k2d2Line;
        private readonly DropdownField _timeDropdown;
        private readonly StatList _info, _resonantStats, _preview;
        private readonly Dictionary<ManeuverType, ManeuverRow> _maneuverRows = new Dictionary<ManeuverType, ManeuverRow>();
        private readonly Dictionary<string, InputRow> _inputRows = new Dictionary<string, InputRow>();
        private readonly Toggle _diveToggle, _occlusionToggle;
        private readonly VisualElement _occlusionFactors;
        private readonly Label _satellitesValue, _orbitsValue;
        private List<TimeReference> _timeOptions = new List<TimeReference>();
        private Tab? _maneuverRowsTab;
        private bool _maneuverRowsForVessel;

        // Nodes pane
        private readonly VisualElement _nodesPane, _nodeList, _nudgeBox;
        private readonly Label _nodesEmpty, _nudgeTime;
        private readonly Dictionary<string, Label> _nudgeValues = new Dictionary<string, Label>();
        private readonly List<Button> _dvStepChips = new List<Button>(), _timeStepChips = new List<Button>();
        private readonly List<NodeRow> _nodeRows = new List<NodeRow>();
        private int _dvStep = 1, _timeStep = 1;
        private Guid? _selectedNode;

        // Actions
        private readonly VisualElement _plannerActions, _nodeActions;
        private readonly Button _makeButton, _replaceButton, _k2d2Button, _warpButton, _deleteButton, _executeButton;

        // State
        private Tab _tab;
        private ManeuverType _maneuver;
        private TimeReference _timeRef;
        private VesselPlanState _shownState;
        private BurnPlan _plan;
        private bool _planDirty = true;
        private float _nextPlanAt, _nextTargetsAt;
        private float _confirmReplaceUntil;
        private PlanStatus _result;
        private float _resultUntil;
        private bool _creating;

        private sealed class ManeuverRow
        {
            public VisualElement Row;
            public Button Button;
            /// <summary>Extra text beside the button (the resonant fix rows say where to raise or lower to).</summary>
            public Label Note;
        }

        private sealed class InputRow
        {
            public VisualElement Row;
            public Label Name, Unit;
            public TextField Field;
            public string Key;
            public Func<PlanInputs, string> Read;
            public Func<PlanInputs, string, bool> Write;
        }

        private sealed class NodeRow
        {
            public VisualElement Row;
            public Label Index, When, DeltaV, Duration;
            public Guid Id;
        }

        public FlightPlanWindow(Texture2D icon, Rect savedLayout, IFlightPlanHost host)
        {
            _host = host;
            _root = new AppShell { name = "FlightPlanRedux", IconTexture = icon };
            _root.CloseClicked += () => CloseClicked?.Invoke();
            _root.style.width = savedLayout.width > 0 ? Math.Max(MinWidth, savedLayout.width) : DefaultWidth;
            _root.style.height = savedLayout.height > 0 ? Math.Max(MinHeight, savedLayout.height) : DefaultHeight;
            _tips = new Tips(_root);

            // ---- Situation and target, above the tabs as in the original ----
            var situationRow = Ui.Row();
            situationRow.style.flexShrink = 0;
            situationRow.style.justifyContent = Justify.SpaceBetween;
            var situationName = new Label();
            Fixed(() => situationName.text = Loc.T("Field/Situation"));
            _situation = new Label();
            _situation.style.unityTextAlign = TextAnchor.MiddleRight;
            situationRow.Add(situationName);
            situationRow.Add(_situation);
            _root.Add(situationRow);

            var targetRow = Ui.Row();
            targetRow.style.flexShrink = 0;
            targetRow.style.marginTop = 2;
            _kindButton = new Button(CycleTargetKind);
            _kindButton.style.width = 110;
            _kindButton.style.flexShrink = 0;
            _kindButton.style.marginLeft = 0;
            _tips.Add(_kindButton, () => Loc.Optional("Tip/TargetKind"));
            _targetDropdown = new DropdownField { choices = new List<string>() };
            WideDropdownMenu.Apply(_targetDropdown);
            _targetDropdown.style.flexGrow = 1;
            _targetDropdown.style.flexShrink = 1;
            _targetDropdown.RegisterValueChangedCallback(e => OnTargetPicked(e.newValue));
            targetRow.Add(_kindButton);
            targetRow.Add(_targetDropdown);
            _root.Add(targetRow);

            // Icon tabs, sharing the width like the original's tab bar.
            var tabs = Ui.Row();
            tabs.style.flexShrink = 0;
            tabs.style.marginTop = 6;
            tabs.style.marginBottom = 6;
            foreach (Tab tab in Enum.GetValues(typeof(Tab)))
            {
                var t = tab;
                var button = new Button(() => SelectTab(t));
                button.style.flexGrow = 1;
                button.style.flexBasis = 0;
                button.style.height = 44;
                button.style.marginLeft = 0;
                button.style.marginRight = tab == Tab.Nodes ? 0 : 4;
                Ui.SetPadding(button, 4, 4);
                button.style.alignItems = Align.Center;
                button.style.justifyContent = Justify.Center;
                VisualElement glyph = tab == Tab.Nodes ? (VisualElement)new NodeGlyph(32) : Icons.Image(TabIcons[tab], 34);
                if (tab == Tab.Target) _targetTabIcon = glyph;
                button.Add(glyph);
                _tips.Add(button, () => Loc.T("TabTitle/" + TabTitleKey(t)));
                _tabButtons[tab] = button;
                tabs.Add(button);
            }
            _root.Add(tabs);

            var box = new InvertedCornerBox();
            box.AddToClassList("oab-window-body");
            box.style.flexGrow = 1;
            box.style.flexShrink = 1;
            box.style.marginBottom = 8;
            _root.Add(box);

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.style.flexGrow = 1;
            scroll.style.flexShrink = 1;
            scroll.style.backgroundColor = Color.clear;
            box.Add(scroll);

            // ---- Planner pane ----
            _plannerPane = new VisualElement();
            scroll.Add(_plannerPane);

            _tabTitle = Ui.Section();
            _tabTitle.style.marginTop = 0;
            _plannerPane.Add(_tabTitle);

            _timeRow = Ui.Row();
            _timeRow.style.marginTop = 2;
            var timeName = new Label();
            timeName.style.minWidth = 70;
            Fixed(() => timeName.text = Loc.T("Field/BurnTime"));
            _timeDropdown = new DropdownField { choices = new List<string>() };
            WideDropdownMenu.Apply(_timeDropdown);
            _timeDropdown.style.flexGrow = 1;
            _timeDropdown.style.flexShrink = 1;
            _timeDropdown.RegisterValueChangedCallback(e => OnTimePicked());
            _timeRow.Add(timeName);
            _timeRow.Add(_timeDropdown);
            _plannerPane.Add(_timeRow);
            AddTime(_plannerPane, "FixedTime", i => i.FixedTime, (i, v) => i.FixedTime = v);
            AddKm(_plannerPane, "Altitude", i => i.Altitude, (i, v) => i.Altitude = v, 0);

            _info = new StatList(_tips);
            _info.Root.style.marginTop = 6;
            _plannerPane.Add(_info.Root);

            // Resonant orbit: the constellation it is for, then what it takes.
            _resonantBox = new VisualElement();
            _resonantBox.style.marginTop = 6;
            _plannerPane.Add(_resonantBox);
            _satellitesValue = AddStepper(_resonantBox, "Satellites", i => i.Satellites, (i, v) => i.Satellites = Mathf.Clamp(v, 1, 50));
            _orbitsValue = AddStepper(_resonantBox, "OrbitsBetween", i => i.OrbitsBetween, (i, v) => i.OrbitsBetween = Mathf.Clamp(v, 1, 20));
            AddKm(_resonantBox, "SatelliteAltitude", i => i.SatelliteAltitude, (i, v) => i.SatelliteAltitude = v, 0);
            _resonantStats = new StatList(_tips);
            _resonantBox.Add(_resonantStats.Root);
            _occlusionToggle = AddToggle(_resonantBox, "Occlusion", i => i.Occlusion, (i, v) => i.Occlusion = v);
            _occlusionFactors = new VisualElement();
            _resonantBox.Add(_occlusionFactors);
            AddNumber(_occlusionFactors, "OcclusionAtm", "", i => i.OcclusionAtm, (i, v) => i.OcclusionAtm = v, 0.1, 1);
            AddNumber(_occlusionFactors, "OcclusionVac", "", i => i.OcclusionVac, (i, v) => i.OcclusionVac = v, 0.1, 1);
            _diveToggle = AddToggle(_resonantBox, "Dive", i => i.Dive, (i, v) => i.Dive = v);

            // Maneuver buttons, each with its value beside it.
            _maneuverList = new VisualElement();
            _maneuverList.style.marginTop = 6;
            _plannerPane.Add(_maneuverList);
            foreach (ManeuverType m in Enum.GetValues(typeof(ManeuverType)))
                if (m != ManeuverType.None) _maneuverRows[m] = NewManeuverRow(m);
            AddKm(_maneuverRows[ManeuverType.NewPe].Row, "NewPe", i => i.NewPe, (i, v) => i.NewPe = v, -1e9, inline: true);
            AddKm(_maneuverRows[ManeuverType.NewAp].Row, "NewAp", i => i.NewAp, (i, v) => i.NewAp = v, -1e9, inline: true);
            AddNumber(_maneuverRows[ManeuverType.NewInc].Row, "NewInc", "°", i => i.NewInc, (i, v) => i.NewInc = v, -180, 180, inline: true);
            AddNumber(_maneuverRows[ManeuverType.NewLan].Row, "NewLan", "°", i => i.NewLan, (i, v) => i.NewLan = v, 0, 360, inline: true);
            AddKm(_maneuverRows[ManeuverType.NewSma].Row, "NewSma", i => i.NewSma, (i, v) => i.NewSma = v, -1e9, inline: true);
            AddKm(_maneuverRows[ManeuverType.MoonReturn].Row, "MoonReturnPe", i => i.MoonReturnPe, (i, v) => i.MoonReturnPe = v, 0, inline: true);
            AddNumber(_maneuverRows[ManeuverType.CourseCorrection].Row, "InterceptDistanceVessel", "m",
                i => i.InterceptDistanceVessel, (i, v) => i.InterceptDistanceVessel = v, 0, 1e7, inline: true);
            AddKm(_maneuverRows[ManeuverType.CourseCorrection].Row, "InterceptDistanceCelestial",
                i => i.InterceptDistanceCelestial, (i, v) => i.InterceptDistanceCelestial = v, 0, inline: true);
            AddTime(_maneuverRows[ManeuverType.InterceptTarget].Row, "InterceptTime", i => i.InterceptTime, (i, v) => i.InterceptTime = v, inline: true);

            var previewSection = Ui.Section();
            Fixed(() => previewSection.text = Loc.T("Section/Preview").ToUpper(CultureInfo.CurrentCulture));
            _plannerPane.Add(previewSection);
            _preview = new StatList(_tips);
            _plannerPane.Add(_preview.Root);

            _statusLabel = Ui.Text("", 12, Ui.GoodColor);
            _statusLabel.style.whiteSpace = WhiteSpace.Normal;
            _statusLabel.style.marginTop = 8;
            _statusLabel.style.minHeight = 18;
            _plannerPane.Add(_statusLabel);
            _k2d2Line = Ui.Text("", 11, Ui.Ink2Color);
            _k2d2Line.style.whiteSpace = WhiteSpace.Normal;
            _k2d2Line.style.marginTop = 2;
            _plannerPane.Add(_k2d2Line);

            // ---- Nodes pane ----
            _nodesPane = new VisualElement();
            scroll.Add(_nodesPane);
            var listHeader = Ui.Row();
            var nodesSection = Ui.Section();
            nodesSection.style.flexGrow = 1;
            nodesSection.style.marginTop = 0;
            Fixed(() => nodesSection.text = Loc.T("Section/Nodes").ToUpper(CultureInfo.CurrentCulture));
            listHeader.Add(nodesSection);
            var deleteAll = Ui.SmallButton("", () => { _host.Nodes.DeleteAll(_host.Vessel); _selectedNode = null; });
            Fixed(() => deleteAll.text = Loc.T("Button/DeleteAll"));
            listHeader.Add(deleteAll);
            _nodesPane.Add(listHeader);
            _nodesEmpty = Ui.Text("", 12, Ui.Ink3Color);
            _nodesEmpty.style.marginTop = 4;
            Fixed(() => _nodesEmpty.text = Loc.T("Nodes/None"));
            _nodesPane.Add(_nodesEmpty);
            _nodeList = new VisualElement();
            _nodesPane.Add(_nodeList);

            _nudgeBox = new VisualElement();
            _nodesPane.Add(_nudgeBox);
            var nudgeSection = Ui.Section();
            Fixed(() => nudgeSection.text = Loc.T("Section/Adjust").ToUpper(CultureInfo.CurrentCulture));
            _nudgeBox.Add(nudgeSection);
            var dvSteps = Ui.WrapRow();
            for (int s = 0; s < DvSteps.Length; s++)
            {
                int index = s;
                var chip = Ui.Chip(() => { _dvStep = index; UpdateStepChips(); });
                chip.text = DvSteps[s].ToString(Ui.Invariant) + " m/s";
                _dvStepChips.Add(chip);
                dvSteps.Add(chip);
            }
            _nudgeBox.Add(dvSteps);
            AddNudgeRow("Prograde", new Vector3d(0, 0, 1));
            AddNudgeRow("Normal", new Vector3d(0, 1, 0));
            AddNudgeRow("Radial", new Vector3d(1, 0, 0));
            var timeSteps = Ui.WrapRow();
            timeSteps.style.marginTop = 6;
            for (int s = 0; s < TimeSteps.Length; s++)
            {
                int index = s;
                var chip = Ui.Chip(() => { _timeStep = index; UpdateStepChips(); });
                double step = TimeSteps[s];
                Fixed(() => chip.text = step < 0 ? Loc.T("Step/Orbit") : Ui.Duration(step));
                _timeStepChips.Add(chip);
                timeSteps.Add(chip);
            }
            _nudgeBox.Add(timeSteps);
            _nudgeTime = AddNudgeTimeRow();
            UpdateStepChips();

            // ---- Action rows ----
            _plannerActions = ActionRow();
            _makeButton = ActionButton(_plannerActions, () => MakeNode(false));
            _replaceButton = ActionButton(_plannerActions, () => MakeNode(true));
            _replaceButton.style.marginLeft = 8;
            // K2-D2 sits at the end of the row, as in the original; it flies the first node.
            _k2d2Button = new Button(ToggleExecute);
            _k2d2Button.AddToClassList("oab-window-action-button");
            _k2d2Button.style.flexGrow = 0;
            _k2d2Button.style.width = 52;
            _k2d2Button.style.marginLeft = 8;
            Ui.SetPadding(_k2d2Button, 2, 2);
            _k2d2Button.style.alignItems = Align.Center;
            _k2d2Button.style.justifyContent = Justify.Center;
            _k2d2Button.Add(Icons.Image("k2d2_big_icon", 30));
            _tips.Add(_k2d2Button, () => Loc.T(K2D2Bridge.IsRunning ? "Tip/K2D2Stop" : "Tip/K2D2Execute"));
            _plannerActions.Add(_k2d2Button);
            _root.Add(_plannerActions);

            _nodeActions = ActionRow();
            _warpButton = ActionButton(_nodeActions, WarpToSelected);
            _deleteButton = ActionButton(_nodeActions, DeleteSelected);
            _executeButton = ActionButton(_nodeActions, ToggleExecute);
            _deleteButton.style.marginLeft = _executeButton.style.marginLeft = 8;
            Fixed(() => _warpButton.text = Loc.T("Button/WarpTo"));
            Fixed(() => _deleteButton.text = Loc.T("Button/Delete"));
            _root.Add(_nodeActions);

            var document = Window.Create(new WindowOptions
            {
                WindowId = "FlightPlanRedux_Main",
                Parent = null,
                IsHidingEnabled = true,
                UseStockScale = true,
                BringToFrontOnPointerDown = true,
                DisableGameInputForTextFields = true,
                MoveOptions = new MoveOptions { IsMovingEnabled = true, CheckScreenBounds = true, HandleElementName = "header" },
            }, _root);
            document.EnableUiSounds();

            ApplyFixedText();
            Loc.Changed += OnLanguageChanged;

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
                _root.SetDefaultPosition(size =>
                {
                    var panel = _root.panel?.visualTree?.contentRect ?? new Rect(0, 0, 1920, 1080);
                    return new Vector2(panel.width - size.x - 260, 110);
                });
            }

            ShowTab(Tab.Orbit);
        }

        // ------------------------------------------------------------------ window plumbing

        private void Fixed(Action apply) => _fixedText.Add(apply);

        private void ApplyFixedText()
        {
            _root.Title = Loc.T("Title");
            foreach (var apply in _fixedText) apply();
            foreach (var row in _inputRows.Values) if (row.Name != null) row.Name.text = Loc.T("Field/" + row.Key);
            foreach (var pair in _maneuverRows) pair.Value.Button.text = Loc.T("Maneuver/" + pair.Key);
            _kindButton.text = Loc.T("TargetKind/" + _kind);
            UpdateTabTitle();
        }

        private void OnLanguageChanged()
        {
            ApplyFixedText();
            _timeOptions = new List<TimeReference>();   // relabel the burn-time choices
            _planDirty = true;
            _nextTargetsAt = 0;
        }

        public bool Visible
        {
            get => _root.style.display != DisplayStyle.None;
            set
            {
                if (value == Visible) return;
                _root.style.display = value ? DisplayStyle.Flex : DisplayStyle.None;
                if (value) { _planDirty = true; _nextTargetsAt = 0; }
                else _tips.Hide();
            }
        }

        /// <summary>True while a text field has the keyboard, so the toggle keybind doesn't fire while typing.</summary>
        public bool TypingInField
        {
            get
            {
                var focused = _root.focusController?.focusedElement as VisualElement;
                return focused != null && (focused is TextField || focused.GetFirstAncestorOfType<TextField>() != null);
            }
        }

        /// <summary>Hides the window for a while (behind the pause menu) without closing it.</summary>
        public bool Suspended
        {
            get => _root.style.visibility == Visibility.Hidden;
            set { if (value != Suspended) _root.style.visibility = value ? Visibility.Hidden : Visibility.Visible; }
        }

        public void CheckLayout()
        {
            var position = _root.worldBound.position;
            if (float.IsNaN(position.x) || float.IsNaN(position.y)) return;
            var layout = CurrentLayout(position);
            if (_savedLayout == null) { _savedLayout = layout; return; }
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

        private Rect CurrentLayout(Vector2 position)
        {
            float width = _root.style.width.keyword == StyleKeyword.Undefined ? _root.style.width.value.value : -1;
            float height = _root.style.height.keyword == StyleKeyword.Undefined ? _root.style.height.value.value : -1;
            return new Rect(position.x, position.y, width, height);
        }

        private static bool Approximately(Rect a, Rect b) =>
            (a.position - b.position).sqrMagnitude < 1f && (a.size - b.size).sqrMagnitude < 1f;

        /// <summary>Opens a tab from outside (the API).</summary>
        public void Show(Tab tab)
        {
            _nextTargetsAt = 0;
            SelectTab(tab);
        }

        // ------------------------------------------------------------------ refresh

        /// <summary>Called by the plugin a few times a second while the window is open.</summary>
        public void Refresh()
        {
            var vessel = _host.Vessel;
            var state = _host.State;
            if (state != _shownState) LoadState(state);

            double ut = _host.UT;
            var ctx = vessel == null ? null : PlanContext.For(vessel, ut);
            _situation.text = SituationText(vessel);
            if (Time.unscaledTime >= _nextTargetsAt) RefreshTargets(ctx);
            UpdateTabAvailability(ctx);

            if (_tab == Tab.Nodes)
            {
                RefreshNodes(vessel, ut);
                return;
            }

            UpdateManeuverRows(ctx);
            RefreshInfo(ctx);
            RefreshPlan(vessel, ut);
        }

        private void LoadState(VesselPlanState state)
        {
            _shownState = state;
            var inputs = state?.Inputs ?? new PlanInputs();
            foreach (var row in _inputRows.Values)
            {
                row.Field.SetValueWithoutNotify(row.Read(inputs));
                row.Field.style.color = Ui.InkColor;
            }
            _diveToggle.SetValueWithoutNotify(inputs.Dive);
            _occlusionToggle.SetValueWithoutNotify(inputs.Occlusion);
            if (state != null)
            {
                _tab = (Tab)Mathf.Clamp(state.Tab, 0, (int)Tab.Nodes);
                _maneuver = (ManeuverType)state.Maneuver;
                _timeRef = (TimeReference)state.TimeRef;
            }
            _kindChosen = false;
            ShowTab(_tab);
            _planDirty = true;
            _nextTargetsAt = 0;
        }

        private void SaveState()
        {
            var state = _host.State;
            if (state == null) return;
            state.Tab = (int)_tab;
            state.Maneuver = (int)_maneuver;
            state.TimeRef = (int)_timeRef;
        }

        private static string SituationText(VesselComponent vessel)
        {
            if (vessel == null) return "—";
            string body = Loc.Body(vessel.mainBody?.bodyName);
            return Loc.F("Situation/" + vessel.Situation, body);
        }

        // ------------------------------------------------------------------ target picker

        private void CycleTargetKind()
        {
            var target = _host.Vessel?.TargetObject;
            bool canPort = target != null && (target.IsVessel || target.IsPart);
            _kind = _kind switch
            {
                TargetKind.Celestial => TargetKind.Vessel,
                TargetKind.Vessel => canPort ? TargetKind.Port : TargetKind.Celestial,
                _ => TargetKind.Celestial
            };
            _kindChosen = true;
            _kindButton.text = Loc.T("TargetKind/" + _kind);
            _nextTargetsAt = 0;
            Refresh();
        }

        private void RefreshTargets(PlanContext ctx)
        {
            _nextTargetsAt = Time.unscaledTime + 2;
            var target = ctx?.Target;
            if (!_kindChosen)
            {
                // Follow the target until the player picks a kind themselves.
                _kind = target == null || target.IsCelestialBody ? TargetKind.Celestial
                    : target.IsPart ? TargetKind.Port : TargetKind.Vessel;
                _kindButton.text = Loc.T("TargetKind/" + _kind);
            }

            _targets.Clear();
            var game = KSP.Game.GameManager.Instance?.Game;
            if (ctx != null)
            {
                switch (_kind)
                {
                    case TargetKind.Celestial:
                        var star = game?.UniverseModel?.GetAllCelestialBodies()?.FirstOrDefault(b => b.IsStar);
                        if (star != null) AddBodies(star, 0);
                        break;
                    case TargetKind.Vessel:
                        var vessels = game?.UniverseModel?.GetAllVessels();
                        if (vessels != null)
                            foreach (var v in vessels.Where(v => v != ctx.Vessel && !v.IsDebris())
                                         .OrderBy(v => v.mainBody != ctx.Body).ThenBy(v => v.Name))
                                _targets.Add((v.Name, v.SimulationObject.GlobalId));
                        break;
                    case TargetKind.Port:
                        var owner = target?.IsPart == true ? target.Part?.PartOwner?.SimulationObject : target;
                        var parts = owner?.PartOwner?.Parts;
                        if (parts != null)
                        {
                            int n = 0;
                            foreach (var part in parts)
                            {
                                if (!part.TryGetModule(typeof(PartComponentModule_DockingNode), out _)) continue;
                                n++;
                                _targets.Add(($"{n}. {part.SimulationObject?.Name ?? part.PartName}", part.SimulationObject.GlobalId));
                            }
                        }
                        break;
                }
            }

            var names = _targets.Select(t => t.Label).ToList();
            if (!_targetDropdown.choices.SequenceEqual(names)) _targetDropdown.choices = names;
            _targetDropdown.SetEnabled(names.Count > 0);
            string current = null;
            if (target != null)
            {
                var match = _targets.FirstOrDefault(t => t.Id != null && t.Id.Equals(target.GlobalId));
                current = match.Label;
            }
            _targetDropdown.SetValueWithoutNotify(current ?? Loc.T("Field/PickTarget"));
        }

        /// <summary>The body's children, planets then their moons indented under them.</summary>
        private void AddBodies(CelestialBodyComponent parent, int depth)
        {
            foreach (var body in parent.orbitingBodies.OrderBy(b => b.Orbit?.semiMajorAxis ?? 0))
            {
                // Em spaces, so the indent survives the dropdown's text trimming.
                _targets.Add((new string(' ', depth) + Loc.Body(body.bodyName), body.SimulationObject.GlobalId));
                AddBodies(body, depth + 1);
            }
        }

        private void OnTargetPicked(string label)
        {
            var match = _targets.FirstOrDefault(t => t.Label == label);
            if (match.Id == null) return;
            _host.SetTarget(match.Id);
            _planDirty = true;
            _nextTargetsAt = 0;
        }

        // ------------------------------------------------------------------ tabs and maneuvers

        private static bool TabAvailable(Tab tab, PlanContext ctx)
        {
            if (ctx == null) return tab == Tab.Orbit || tab == Tab.Nodes;
            var body = ctx.Body;
            switch (tab)
            {
                case Tab.Target:
                    // As the original: a target that orbits the same body as the vessel.
                    return ctx.HasTarget && ctx.TargetOrbit.referenceBody == body;
                case Tab.Moon:
                    return !body.IsStar && body.referenceBody != null && !body.referenceBody.IsStar && ctx.Orbit.eccentricity < 1;
                case Tab.Planet:
                    return !body.IsStar && body.referenceBody != null && body.referenceBody.IsStar
                           && ctx.TargetBody != null && ctx.TargetBody != body && ctx.TargetBody.referenceBody == body.referenceBody;
                case Tab.Resonant:
                    return ctx.Orbit.eccentricity < 1;
                default:
                    return true;
            }
        }

        private void UpdateTabAvailability(PlanContext ctx)
        {
            foreach (var pair in _tabButtons) Ui.Shown(pair.Value, TabAvailable(pair.Key, ctx));
            // The target tab's icon says what kind of target it's for.
            string icon = ctx?.TargetBody != null ? "TRM_50_Ship2Celestial" : "TRM_50_Ship2Ship";
            if (icon != _targetTabIconName)
            {
                _targetTabIconName = icon;
                var texture = Icons.Get(icon);
                if (texture != null) _targetTabIcon.style.backgroundImage = new StyleBackground(texture);
                UpdateTabTitle();
            }
            if (!TabAvailable(_tab, ctx)) SelectTab(Tab.Orbit);
        }

        private string TabTitleKey(Tab tab) =>
            tab == Tab.Target ? (_targetTabIconName == "TRM_50_Ship2Celestial" ? "TargetCelestial" : "TargetVessel") : tab.ToString();

        private void UpdateTabTitle() => _tabTitle.text = Loc.T("TabTitle/" + TabTitleKey(_tab)).ToUpper(CultureInfo.CurrentCulture);

        private void SelectTab(Tab tab)
        {
            if (tab == _tab) return;
            _tab = tab;
            ShowTab(tab);
            SaveState();
            _planDirty = true;
        }

        private void ShowTab(Tab tab)
        {
            _tab = tab;
            foreach (var pair in _tabButtons) SetTabStyle(pair.Value, pair.Key == tab);
            bool nodes = tab == Tab.Nodes;
            Ui.Shown(_plannerPane, !nodes);
            Ui.Shown(_plannerActions, !nodes);
            Ui.Shown(_nodesPane, nodes);
            Ui.Shown(_nodeActions, nodes);
            UpdateTabTitle();
            if (nodes) return;
            Ui.Shown(_resonantBox, tab == Tab.Resonant);
            _maneuverRowsTab = null;   // re-laid out on the next refresh
            _planDirty = true;
        }

        private static void SetTabStyle(Button button, bool active)
        {
            button.style.backgroundColor = active ? Ui.RowSelected : new Color(0, 0, 0, 0.25f);
            Ui.SetBorder(button, active ? Ui.GoodColor : Ui.LineColor, 1);
            Ui.SetRadius(button, 4);
        }

        /// <summary>Puts the current tab's maneuver rows in the list, and keeps the selection on one of them.</summary>
        private void UpdateManeuverRows(PlanContext ctx)
        {
            bool vesselTarget = ctx?.TargetIsVessel == true;
            if (_maneuverRowsTab != _tab || _maneuverRowsForVessel != vesselTarget)
            {
                _maneuverRowsTab = _tab;
                _maneuverRowsForVessel = vesselTarget;
                _maneuverList.Clear();
                var offered = new List<ManeuverType>();
                foreach (var m in ManeuversFor(_tab, vesselTarget))
                {
                    // Intercept was "not working at all" in the original; only offered with Experimental on.
                    if (m == ManeuverType.InterceptTarget && !_host.Experimental) continue;
                    _maneuverList.Add(_maneuverRows[m].Row);
                    offered.Add(m);
                }
                SelectManeuver(offered.Contains(_maneuver) ? _maneuver : offered.FirstOrDefault(), false);
            }

            // Course correction is in metres to a vessel, as a periapsis in km at a body.
            Ui.Shown(_inputRows["InterceptDistanceVessel"].Row, vesselTarget);
            Ui.Shown(_inputRows["InterceptDistanceCelestial"].Row, !vesselTarget);
        }

        private ManeuverRow NewManeuverRow(ManeuverType type)
        {
            var row = new ManeuverRow { Row = Ui.Row() };
            row.Row.style.marginTop = 3;
            row.Button = new Button(() => SelectManeuver(type));
            row.Button.style.flexGrow = 1;
            row.Button.style.flexBasis = 0;
            row.Button.style.height = 26;
            row.Button.style.marginLeft = 0;
            row.Button.style.marginRight = 0;
            _tips.Add(row.Button, () => Loc.Optional("ManeuverTip/" + type));
            row.Row.Add(row.Button);
            if (type == ManeuverType.FixAp || type == ManeuverType.FixPe)
            {
                row.Note = Ui.Text("", 12, Ui.InkColor);
                row.Note.style.width = 150;
                row.Note.style.marginLeft = 8;
                row.Row.Add(row.Note);
            }
            return row;
        }

        private void SelectManeuver(ManeuverType type, bool save = true)
        {
            _maneuver = type;
            foreach (var pair in _maneuverRows)
            {
                bool active = pair.Key == type;
                var b = pair.Value.Button;
                b.style.color = active ? Ui.GoodColor : Ui.InkColor;
                Ui.SetBorder(b, active ? Ui.GoodColor : Ui.LineColor, 1);
            }
            _timeOptions = new List<TimeReference>();   // rebuilt on the next refresh for the current orbit
            UpdateBurnInputs();
            _planDirty = true;
            _confirmReplaceUntil = 0;
            if (save) SaveState();
        }

        private void RebuildTimeOptions(PatchedConicsOrbit orbit)
        {
            var options = Planner.Options(_maneuver, orbit);
            if (options.SequenceEqual(_timeOptions) && _timeDropdown.choices.Count == options.Count) return;
            _timeOptions = options;
            _timeDropdown.choices = options.Select(o => Loc.T("When/" + o)).ToList();
            if (!options.Contains(_timeRef)) _timeRef = options[0];
            _timeDropdown.SetValueWithoutNotify(Loc.T("When/" + _timeRef));
            _timeDropdown.SetEnabled(options.Count > 1);
            UpdateBurnInputs();
        }

        private void OnTimePicked()
        {
            int index = _timeDropdown.choices.IndexOf(_timeDropdown.value);
            if (index < 0 || index >= _timeOptions.Count) return;
            _timeRef = _timeOptions[index];
            UpdateBurnInputs();
            _planDirty = true;
            SaveState();
        }

        /// <summary>The fixed-time and altitude rows under the burn choice, when it uses them.</summary>
        private void UpdateBurnInputs()
        {
            Ui.Shown(_inputRows["FixedTime"].Row, _timeRef == TimeReference.FixedTime);
            Ui.Shown(_inputRows["Altitude"].Row, _timeRef == TimeReference.Altitude);
        }

        // ------------------------------------------------------------------ readouts

        private void RefreshInfo(PlanContext ctx)
        {
            _info.Begin();
            _resonantStats.Begin();
            if (ctx == null) { _info.End(); _resonantStats.End(); return; }
            try
            {
                switch (_tab)
                {
                    case Tab.Moon:
                        if (ctx.Body.referenceBody != null) _info.Set("ReturnTo", Loc.Body(ctx.Body.referenceBody.bodyName));
                        break;

                    case Tab.Target:
                    {
                        if (!ctx.HasTarget || ctx.TargetOrbit.referenceBody != ctx.Body) break;
                        var t = TargetInfo.Local(ctx);
                        _info.Set("TargetOrbit", OrbitText(ctx.TargetOrbit));
                        _info.Set("CurrentOrbit", OrbitText(ctx.Orbit));
                        _info.Set("RelativeInclination", Ui.Deg(t.RelativeInclination), t.RelativeInclination > 1 ? Ui.WarnColor : (Color?)null);
                        if (ctx.TargetIsVessel)
                        {
                            _info.Set("SynodicPeriod", Ui.Duration(t.SynodicPeriod));
                            _info.Set("NextWindow", Ui.Duration(t.NextWindow));
                            _info.Set("ClosestApproachIn", Ui.Duration(t.ClosestApproachUT - ctx.UT));
                            _info.Set("ClosestApproach", Ui.Metres(t.ClosestApproach));
                            _info.Set("RelativeSpeed", Ui.Speed(t.ClosestApproach > 1000 ? t.RelativeSpeedCA : t.RelativeSpeedNow));
                            _info.Set("Next", Loc.T("Next/" + TargetInfo.NextStep(ctx, t)), Ui.SignalColor);
                        }
                        else
                        {
                            _info.Set("PhaseAngle", Ui.Deg(t.PhaseAngle, 1));
                            _info.Set("TransferAngle", Ui.Deg(t.TransferAngle, 1));
                            _info.Set("TransferTime", Ui.Duration(t.TransferTime));
                            _info.Set("SynodicPeriod", Ui.Duration(t.SynodicPeriod));
                            _info.Set("NextWindow", Ui.Duration(t.NextWindow));
                            _info.Set("ClosestApproachIn", Ui.Duration(t.ClosestApproachUT - ctx.UT));
                        }
                        break;
                    }

                    case Tab.Planet:
                    {
                        var p = TargetInfo.Planetary(ctx);
                        _info.Set("RelativeInclination", Ui.Deg(p.RelativeInclination));
                        _info.Set("PhaseAngle", Ui.Deg(p.PhaseAngle, 1));
                        _info.Set("TransferAngle", Ui.Deg(p.TransferAngle, 1));
                        _info.Set("TransferTime", Ui.Duration(p.TransferTime));
                        _info.Set("SynodicPeriod", Ui.Duration(p.SynodicPeriod));
                        _info.Set("NextWindow", Ui.Duration(p.NextWindow));
                        _info.Set("EjectionDv", Ui.Speed(p.EjectionDv));
                        break;
                    }

                    case Tab.Resonant:
                        RefreshResonant(ctx);
                        break;
                }
            }
            catch (Exception e)
            {
                SWLog.Warning($"Readouts failed: {e.Message}");
            }
            _info.End();
            _resonantStats.End();
        }

        private void RefreshResonant(PlanContext ctx)
        {
            var inputs = _host.State?.Inputs ?? new PlanInputs();
            var r = ResonantPlan.For(ctx.Vessel, inputs);
            double R = ctx.Body.radius;
            _satellitesValue.text = inputs.Satellites.ToString(Ui.Invariant);
            _orbitsValue.text = inputs.OrbitsBetween.ToString(Ui.Invariant);
            _diveToggle.SetEnabled(r.DiveAllowed);
            Ui.Shown(_occlusionFactors, inputs.Occlusion);

            // Altitudes with a "use" button set the constellation's altitude to them, as in the original.
            var s = _resonantStats;
            s.Set("Resonance", $"{r.N}/{r.M}");
            s.Set("CurrentAp", Ui.Km(ctx.Orbit.ApoapsisArl), use: () => UseAltitude(ctx.Orbit.ApoapsisArl));
            s.Set("CurrentPe", Ui.Km(ctx.Orbit.PeriapsisArl), use: () => UseAltitude(ctx.Orbit.PeriapsisArl));
            if (r.SynchronousAlt > 0) s.Set("SynchronousAlt", Ui.Km(r.SynchronousAlt), use: () => UseAltitude(r.SynchronousAlt));
            else s.Set("SynchronousAlt", Loc.T("Value/OutsideSoi"));
            if (r.SemiSynchronousAlt > 0) s.Set("SemiSynchronousAlt", Ui.Km(r.SemiSynchronousAlt), use: () => UseAltitude(r.SemiSynchronousAlt));
            else s.Set("SemiSynchronousAlt", Loc.T("Value/OutsideSoi"));
            s.Set("SoiAlt", Ui.Km(r.SoiAlt));
            if (r.MinLosAlt > 0) s.Set("MinLosAlt", Ui.Km(r.MinLosAlt), use: () => UseAltitude(r.MinLosAlt));
            else s.Set("MinLosAlt", "—");
            s.Set("DeploymentPeriod", Ui.Duration(r.DeploymentPeriod));
            s.Set("DeploymentAp", Ui.Km(r.Ap - R));
            s.Set("DeploymentPe", Ui.Km(r.Pe - R));
            s.Set("Eccentricity", Ui.Number(r.Eccentricity, 3));
            s.Set("InjectionDv", Ui.Speed(r.InjectionDv));

            // Fix rows, as the original: only the ones the current orbit needs, saying which way and where to.
            var fixAp = _maneuverRows[ManeuverType.FixAp];
            var fixPe = _maneuverRows[ManeuverType.FixPe];
            Ui.Shown(fixAp.Row, r.NeedsAp);
            Ui.Shown(fixPe.Row, r.NeedsPe);
            fixAp.Note.text = Loc.F(r.Ap > ctx.Orbit.Apoapsis ? "Value/RaiseTo" : "Value/LowerTo", Ui.Km(r.Ap - R));
            fixPe.Note.text = Loc.F(r.Pe > ctx.Orbit.Periapsis ? "Value/RaiseTo" : "Value/LowerTo", Ui.Km(r.Pe - R));
            if ((_maneuver == ManeuverType.FixAp && !r.NeedsAp) || (_maneuver == ManeuverType.FixPe && !r.NeedsPe))
                SelectManeuver(r.NeedsAp ? ManeuverType.FixAp : ManeuverType.FixPe, false);
        }

        private void UseAltitude(double metres)
        {
            var state = _host.State;
            if (state == null || double.IsNaN(metres)) return;
            state.Inputs.SatelliteAltitude = Math.Round(metres);
            var row = _inputRows["SatelliteAltitude"];
            row.Field.SetValueWithoutNotify(row.Read(state.Inputs));
            row.Field.style.color = Ui.InkColor;
            _planDirty = true;
        }

        private static string OrbitText(PatchedConicsOrbit o) =>
            o == null ? "—" : o.eccentricity < 1
                ? Loc.F("Value/PeAp", Ui.Km(o.PeriapsisArl), Ui.Km(o.ApoapsisArl))
                : Loc.F("Value/Escape", Ui.Km(o.PeriapsisArl));

        // ------------------------------------------------------------------ preview and node creation

        private void RefreshPlan(VesselComponent vessel, double ut)
        {
            bool k2d2 = K2D2Bridge.IsAvailable;
            bool running = k2d2 && K2D2Bridge.IsRunning;
            Ui.Shown(_k2d2Button, k2d2);
            Ui.Shown(_k2d2Line, running);
            if (running) _k2d2Line.text = Loc.F("Nodes/K2D2", K2D2Bridge.Status);

            if (vessel == null)
            {
                ShowStatus(new PlanStatus(StatusLevel.Error, "NoVessel"));
                _preview.Begin();
                _preview.End();
                _makeButton.SetEnabled(false);
                Ui.Shown(_replaceButton, false);
                _k2d2Button.SetEnabled(false);
                return;
            }

            var orbit = NodeService.PlanningOrbit(vessel, ut, out double earliest);
            RebuildTimeOptions(orbit);

            float interval = Heavy.Contains(_maneuver) ? 5f : 1f;
            if (_planDirty || Time.unscaledTime >= _nextPlanAt)
            {
                _planDirty = false;
                _nextPlanAt = Time.unscaledTime + interval;
                _plan = PlanNow(vessel, ut, orbit, earliest);
            }

            ShowPreview(_plan, ut, orbit);
            int nodeCount = NodeService.Nodes(vessel).Count;
            bool confirming = Time.unscaledTime < _confirmReplaceUntil;
            _makeButton.text = Loc.T(nodeCount > 0 ? "Button/AddNode" : "Button/MakeNode");
            _replaceButton.text = Loc.T(confirming ? "Button/ConfirmReplace" : "Button/Replace");
            _makeButton.SetEnabled(!_creating && _plan != null && _plan.Valid && nodeCount < NodeService.MaxNodes);
            Ui.Shown(_replaceButton, nodeCount > 0);
            _replaceButton.SetEnabled(!_creating);
            _k2d2Button.SetEnabled(running || nodeCount > 0);

            if (Time.unscaledTime < _resultUntil && _result != null) ShowStatus(_result);
            else if (_plan == null) ShowStatus(null);
            else if (!_plan.Valid || _plan.Warnings.Count == 0) ShowStatus(_plan.Status);
            else ShowStatus(_plan.Warnings[0]);
        }

        private BurnPlan PlanNow(VesselComponent vessel, double ut, PatchedConicsOrbit orbit, double earliest)
        {
            if (_maneuver == ManeuverType.None) return null;
            var inputs = _host.State?.Inputs ?? new PlanInputs();
            var ctx = PlanContext.For(vessel, Math.Max(ut, earliest), orbit);
            ResonantPlan resonant = null;
            if (_maneuver == ManeuverType.FixAp || _maneuver == ManeuverType.FixPe)
                resonant = ResonantPlan.For(vessel, inputs);
            return Planner.Plan(_maneuver, _timeRef, ctx, inputs, resonant);
        }

        private void ShowPreview(BurnPlan plan, double ut, PatchedConicsOrbit orbit)
        {
            _preview.Begin();
            if (plan != null && plan.Valid)
            {
                _preview.Set("BurnIn", Ui.Duration(plan.UT - ut));
                _preview.Set("DeltaV", Ui.Speed(plan.DeltaV.magnitude));
                var b = plan.BurnVector;
                _preview.Set("Components", Loc.F("Value/Components", Ui.Number(b.z, 1), Ui.Number(b.y, 1), Ui.Number(b.x, 1)));
                _preview.Set("Before", OrbitText(orbit));
                var r = plan.Result;
                if (r != null)
                {
                    _preview.Set("After", OrbitText(r));
                    _preview.Set("AfterInclination", Ui.Deg(r.inclination));
                }
            }
            _preview.End();
        }

        private void ShowStatus(PlanStatus status)
        {
            if (status == null)
            {
                _statusLabel.text = "";
                return;
            }
            var args = (status.Args ?? Array.Empty<object>())
                .Select(a => a is double d ? d.ToString("F2", Ui.Invariant) : a).ToArray();
            _statusLabel.text = Loc.F("Status/" + status.Key, args);
            _statusLabel.style.color = Ui.StatusColor(status.Level);
        }

        private void ShowResult(PlanStatus status)
        {
            _result = status;
            _resultUntil = Time.unscaledTime + ResultSeconds;
        }

        private void MakeNode(bool replace)
        {
            var vessel = _host.Vessel;
            if (vessel == null || _creating) return;
            var existing = NodeService.Nodes(vessel);
            if (replace && existing.Count > 0 && Time.unscaledTime >= _confirmReplaceUntil)
            {
                // Ask first: a second click within a few seconds confirms.
                _confirmReplaceUntil = Time.unscaledTime + ConfirmSeconds;
                return;
            }
            _confirmReplaceUntil = 0;

            double ut = _host.UT;
            PatchedConicsOrbit orbit;
            double earliest;
            if (replace)
            {
                orbit = vessel.Orbit.ToPatchedConics();
                earliest = ut;
            }
            else orbit = NodeService.PlanningOrbit(vessel, ut, out earliest);

            // Plan afresh: the preview may be a few seconds old.
            var plan = PlanNow(vessel, ut, orbit, earliest);
            _plan = plan;
            if (plan == null || !plan.Valid)
            {
                ShowResult(plan?.Status ?? new PlanStatus(StatusLevel.Error, "NoSolution"));
                return;
            }

            _creating = true;
            _host.Nodes.Create(vessel, plan.DeltaV, plan.UT, orbit, -0.5, replace, result =>
            {
                _creating = false;
                _planDirty = true;
                if (!result.Ok)
                {
                    ShowResult(new PlanStatus(StatusLevel.Error, result.Problem ?? "NodeFailed"));
                    return;
                }
                ShowResult(Quality(plan, result));
            });
        }

        /// <summary>
        /// How close the trajectory after the new node is to what was planned, against the error thresholds in the
        /// settings. The original had this check, but it never ran.
        /// </summary>
        private PlanStatus Quality(BurnPlan plan, NodeResult result)
        {
            var want = plan.Result;
            var got = result.After;
            if (want == null || got == null || got.referenceBody != want.referenceBody)
                return new PlanStatus(StatusLevel.Ok, "NodeMade");

            double Error(double target, double actual) =>
                Math.Abs(target) < 1e-9 ? 0 : Math.Abs(actual - target) / Math.Abs(target) * 100;

            double error;
            switch (plan.Maneuver)
            {
                case ManeuverType.NewPe:
                case ManeuverType.FixPe:
                    error = Error(want.Periapsis, got.Periapsis);
                    break;
                case ManeuverType.NewAp:
                case ManeuverType.FixAp:
                    error = want.eccentricity < 1 && got.eccentricity < 1 ? Error(want.Apoapsis, got.Apoapsis) : 0;
                    break;
                case ManeuverType.NewPeAp:
                case ManeuverType.Circularize:
                    error = Math.Max(Error(want.Periapsis, got.Periapsis),
                        want.eccentricity < 1 && got.eccentricity < 1 ? Error(want.Apoapsis, got.Apoapsis) : 0);
                    break;
                case ManeuverType.NewInc:
                case ManeuverType.MatchPlanes:
                    error = Math.Abs(got.inclination - want.inclination) / Math.Max(1, Math.Abs(want.inclination)) * 100;
                    break;
                case ManeuverType.NewSma:
                    error = Error(want.semiMajorAxis, got.semiMajorAxis);
                    break;
                default:
                    return new PlanStatus(StatusLevel.Ok, "NodeMade");
            }

            if (error < _host.SmallErrorPercent) return new PlanStatus(StatusLevel.Ok, "NodeMade");
            return new PlanStatus(error < _host.LargeErrorPercent ? StatusLevel.Warning : StatusLevel.Error, "NodeOff", error);
        }

        // ------------------------------------------------------------------ nodes tab

        private void RefreshNodes(VesselComponent vessel, double ut)
        {
            var nodes = NodeService.Nodes(vessel);
            Ui.Shown(_nodesEmpty, nodes.Count == 0);

            // Rows are rebuilt only when the set of nodes changes; their text is updated every refresh.
            if (_nodeRows.Count != nodes.Count || _nodeRows.Where((r, i) => r.Id != nodes[i].NodeID).Any())
            {
                _nodeList.Clear();
                _nodeRows.Clear();
                foreach (var node in nodes) _nodeRows.Add(NewNodeRow(node.NodeID));
            }
            if (_selectedNode != null && nodes.All(n => n.NodeID != _selectedNode)) _selectedNode = null;
            if (_selectedNode == null && nodes.Count > 0) _selectedNode = nodes[0].NodeID;

            ManeuverNodeData selected = null;
            for (int i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                var row = _nodeRows[i];
                row.Index.text = (i + 1).ToString(Ui.Invariant);
                row.When.text = Loc.F("Value/TMinus", Ui.Duration(node.Time - ut));
                row.DeltaV.text = Ui.Speed(node.BurnVector.magnitude);
                row.Duration.text = node.BurnDuration > 0 ? Ui.Duration(node.BurnDuration) : "—";
                bool isSelected = node.NodeID == _selectedNode;
                row.Row.style.backgroundColor = isSelected ? Ui.RowSelected : Color.clear;
                if (isSelected) selected = node;
            }

            Ui.Shown(_nudgeBox, selected != null);
            if (selected != null)
            {
                var b = selected.BurnVector;
                _nudgeValues["Prograde"].text = Ui.Speed(b.z);
                _nudgeValues["Normal"].text = Ui.Speed(b.y);
                _nudgeValues["Radial"].text = Ui.Speed(b.x);
                _nudgeTime.text = Loc.F("Value/TMinus", Ui.Duration(selected.Time - ut));
            }

            bool k2d2 = K2D2Bridge.IsAvailable;
            bool running = k2d2 && K2D2Bridge.IsRunning;
            bool first = selected != null && nodes.Count > 0 && nodes[0].NodeID == selected.NodeID;
            Ui.Shown(_executeButton, k2d2);
            _executeButton.text = Loc.T(running ? "Button/Stop" : "Button/Execute");
            _executeButton.SetEnabled(running || first);

            _warpButton.SetEnabled(selected != null && selected.Time > ut + 5);
            _deleteButton.SetEnabled(selected != null);
        }

        private NodeRow NewNodeRow(Guid id)
        {
            var row = new NodeRow { Id = id, Row = Ui.Row() };
            row.Row.style.minHeight = 22;
            Ui.SetPadding(row.Row, 1, 4);
            Ui.SetRadius(row.Row, 3);
            row.Index = Ui.Text("", 12, Ui.SignalColor, true);
            row.Index.style.width = 18;
            row.When = Ui.Text("", 12, Ui.InkColor);
            row.When.style.flexGrow = 1;
            row.DeltaV = Ui.Text("", 12, Ui.InkColor);
            row.DeltaV.style.width = 90;
            row.DeltaV.style.unityTextAlign = TextAnchor.MiddleRight;
            row.Duration = Ui.Text("", 12, Ui.Ink2Color);
            row.Duration.style.width = 70;
            row.Duration.style.unityTextAlign = TextAnchor.MiddleRight;
            row.Row.Add(row.Index);
            row.Row.Add(row.When);
            row.Row.Add(row.DeltaV);
            row.Row.Add(row.Duration);
            _tips.Add(row.Duration, () => Loc.Optional("Tip/BurnDuration"));
            row.Row.RegisterCallback<ClickEvent>(_ => { _selectedNode = id; Refresh(); });
            _nodeList.Add(row.Row);
            return row;
        }

        private ManeuverNodeData SelectedNode(VesselComponent vessel) =>
            _selectedNode == null ? null : NodeService.Nodes(vessel).FirstOrDefault(n => n.NodeID == _selectedNode);

        private void AddNudgeRow(string key, Vector3d axis)
        {
            var row = Ui.Row();
            row.style.marginTop = 2;
            var name = new Label();
            name.style.flexGrow = 1;
            Fixed(() => name.text = Loc.T("Field/" + key));
            var value = Ui.Text("—", 12, Ui.InkColor);
            value.style.width = 90;
            value.style.unityTextAlign = TextAnchor.MiddleRight;
            value.style.marginRight = 8;
            _nudgeValues[key] = value;
            var minus = Ui.SmallButton("−", () => Nudge(axis * -DvSteps[_dvStep]));
            var plus = Ui.SmallButton("+", () => Nudge(axis * DvSteps[_dvStep]));
            minus.style.width = plus.style.width = 28;
            plus.style.marginLeft = 4;
            row.Add(name);
            row.Add(value);
            row.Add(minus);
            row.Add(plus);
            _nudgeBox.Add(row);
        }

        private Label AddNudgeTimeRow()
        {
            var row = Ui.Row();
            row.style.marginTop = 2;
            var name = new Label();
            name.style.flexGrow = 1;
            Fixed(() => name.text = Loc.T("Field/NodeTime"));
            var value = Ui.Text("—", 12, Ui.InkColor);
            value.style.width = 110;
            value.style.unityTextAlign = TextAnchor.MiddleRight;
            value.style.marginRight = 8;
            var minus = Ui.SmallButton("−", () => Shift(-1));
            var plus = Ui.SmallButton("+", () => Shift(1));
            minus.style.width = plus.style.width = 28;
            plus.style.marginLeft = 4;
            row.Add(name);
            row.Add(value);
            row.Add(minus);
            row.Add(plus);
            _nudgeBox.Add(row);
            return value;
        }

        private void UpdateStepChips()
        {
            for (int i = 0; i < _dvStepChips.Count; i++) Ui.SetChipActive(_dvStepChips[i], i == _dvStep);
            for (int i = 0; i < _timeStepChips.Count; i++) Ui.SetChipActive(_timeStepChips[i], i == _timeStep);
        }

        private void Nudge(Vector3d change)
        {
            var vessel = _host.Vessel;
            var node = SelectedNode(vessel);
            if (node == null) return;
            _host.Nodes.Nudge(vessel, node, change);
            Refresh();
        }

        private void Shift(int direction)
        {
            var vessel = _host.Vessel;
            var node = SelectedNode(vessel);
            if (node == null) return;
            double step = TimeSteps[_timeStep];
            if (step < 0)
            {
                // One orbit of the trajectory the node sits on.
                var patch = node.IsOnManeuverTrajectory ? NodeService.PatchAfter(vessel, node.Time - 1) : vessel.Orbit;
                step = patch != null && patch.eccentricity < 1 ? patch.period : 0;
                if (step <= 0) return;
            }
            _host.Nodes.Shift(vessel, node, direction * step);
            Refresh();
        }

        private void WarpToSelected()
        {
            var node = SelectedNode(_host.Vessel);
            if (node == null) return;
            // Arrive with half the burn plus a margin to spare, so there's time to point the ship.
            double lead = Math.Max(0, node.BurnDuration) / 2 + 30;
            _host.WarpTo(node.Time - lead);
        }

        private void DeleteSelected()
        {
            var vessel = _host.Vessel;
            var node = SelectedNode(vessel);
            if (node == null) return;
            _host.Nodes.Delete(vessel, new[] { node });
            _selectedNode = null;
            Refresh();
        }

        private void ToggleExecute()
        {
            if (K2D2Bridge.IsRunning) K2D2Bridge.StopFlyNode();
            else K2D2Bridge.FlyNode();
            Refresh();
        }

        // ------------------------------------------------------------------ input rows

        private static bool Set(Action apply)
        {
            apply();
            return true;
        }

        /// <summary>
        /// A text field bound to one of the inputs. Inline fields sit beside a maneuver button (no name of their
        /// own, as in the original); the others get a labelled row.
        /// </summary>
        private void AddInput(VisualElement parent, string key, string unit, Func<PlanInputs, string> read,
            Func<PlanInputs, string, bool> write, bool inline = false)
        {
            var row = new InputRow { Key = key, Read = read, Write = write };
            row.Field = new TextField { maxLength = 16 };
            row.Field.style.width = FieldWidth;
            row.Field.style.flexShrink = 0;
            row.Field.style.marginRight = 0;
            row.Unit = Ui.Text(unit, 12, Ui.Ink2Color);
            row.Unit.style.width = 24;
            row.Unit.style.marginLeft = 4;
            row.Field.RegisterValueChangedCallback(e =>
            {
                var state = _host.State;
                if (state == null) return;
                bool ok = row.Write(state.Inputs, e.newValue);
                row.Field.style.color = ok ? Ui.InkColor : Ui.BadColor;
                if (ok) _planDirty = true;
            });
            _tips.Add(row.Field, () => Loc.Optional("FieldTip/" + key));

            if (inline)
            {
                // The field belongs to the maneuver row; hiding the "row" hides just the field and its unit.
                var group = Ui.Row();
                group.style.flexShrink = 0;
                group.style.marginLeft = 6;
                group.Add(row.Field);
                group.Add(row.Unit);
                row.Row = group;
                parent.Add(group);
            }
            else
            {
                row.Row = Ui.Row();
                row.Row.style.marginTop = 4;
                row.Name = new Label();
                row.Name.style.flexGrow = 1;
                row.Name.style.flexShrink = 1;
                row.Name.style.whiteSpace = WhiteSpace.Normal;
                row.Row.Add(row.Name);
                row.Row.Add(row.Field);
                row.Row.Add(row.Unit);
                parent.Add(row.Row);
            }
            _inputRows[key] = row;
        }

        private void AddKm(VisualElement parent, string key, Func<PlanInputs, double> get, Action<PlanInputs, double> set,
            double min, bool inline = false) =>
            AddInput(parent, key, "km",
                i => (get(i) / 1000).ToString("0.###", Ui.Invariant),
                (i, s) => Ui.TryParse(s, out double km) && km * 1000 >= min && Set(() => set(i, km * 1000)), inline);

        private void AddNumber(VisualElement parent, string key, string unit, Func<PlanInputs, double> get, Action<PlanInputs, double> set,
            double min, double max, bool inline = false) =>
            AddInput(parent, key, unit,
                i => get(i).ToString("0.###", Ui.Invariant),
                (i, s) => Ui.TryParse(s, out double v) && v >= min && v <= max && Set(() => set(i, v)), inline);

        private void AddTime(VisualElement parent, string key, Func<PlanInputs, double> get, Action<PlanInputs, double> set,
            bool inline = false) =>
            AddInput(parent, key, "",
                i => Ui.TimeText(get(i)),
                (i, s) => Ui.TryParseTime(s, out double v) && Set(() => set(i, v)), inline);

        /// <summary>A whole-number row with − and + buttons (satellites, orbits between releases).</summary>
        private Label AddStepper(VisualElement parent, string key, Func<PlanInputs, int> get, Action<PlanInputs, int> set)
        {
            var row = Ui.Row();
            row.style.marginTop = 4;
            var name = new Label();
            name.style.flexGrow = 1;
            Fixed(() => name.text = Loc.T("Field/" + key));
            var value = Ui.Text("", 13, Ui.InkColor);
            value.style.width = 36;
            value.style.unityTextAlign = TextAnchor.MiddleCenter;
            void Step(int by)
            {
                var state = _host.State;
                if (state == null) return;
                set(state.Inputs, get(state.Inputs) + by);
                value.text = get(state.Inputs).ToString(Ui.Invariant);
                _planDirty = true;
            }
            var minus = Ui.SmallButton("−", () => Step(-1));
            var plus = Ui.SmallButton("+", () => Step(1));
            minus.style.width = plus.style.width = 28;
            row.Add(name);
            row.Add(minus);
            row.Add(value);
            row.Add(plus);
            parent.Add(row);
            return value;
        }

        private Toggle AddToggle(VisualElement parent, string key, Func<PlanInputs, bool> get, Action<PlanInputs, bool> set)
        {
            var toggle = new Toggle();
            toggle.style.marginTop = 4;
            Fixed(() => toggle.label = Loc.T("Field/" + key));
            toggle.RegisterValueChangedCallback(e =>
            {
                var state = _host.State;
                if (state == null) return;
                set(state.Inputs, e.newValue);
                _planDirty = true;
            });
            _tips.Add(toggle, () => Loc.Optional("FieldTip/" + key));
            parent.Add(toggle);
            return toggle;
        }

        private static VisualElement ActionRow()
        {
            var actions = new VisualElement();
            actions.AddToClassList("oab-window-actions");
            actions.style.flexShrink = 0;
            return actions;
        }

        private static Button ActionButton(VisualElement row, Action clicked)
        {
            var button = new Button(clicked);
            button.AddToClassList("oab-window-action-button");
            button.style.flexBasis = 0;
            // Translations can outgrow the button: shrink the text to fit, then cut it off with "…".
            button.style.paddingLeft = button.style.paddingRight = 4;
            button.style.overflow = Overflow.Hidden;
            button.style.whiteSpace = WhiteSpace.NoWrap;
            button.style.textOverflow = TextOverflow.Ellipsis;
            button.style.unityTextAutoSize = new TextAutoSize(TextAutoSizeMode.BestFit, 10, 14);
            row.Add(button);
            return button;
        }

        /// <summary>
        /// Label/value rows made on first use and hidden when a refresh doesn't set them, so each tab only lists what
        /// applies right now. Names are Stat/{key} and hover tips StatTip/{key} in the CSV. A row can carry a small
        /// "use" button (the original's orbit icon) that does something with its value.
        /// </summary>
        private sealed class StatList
        {
            public readonly VisualElement Root = new VisualElement();
            private readonly Tips _tips;
            private readonly Dictionary<string, Stat> _rows = new Dictionary<string, Stat>();
            private readonly HashSet<string> _set = new HashSet<string>();

            private sealed class Stat
            {
                public VisualElement Row;
                public Label Name, Value;
                public Button Use;
                public Action UseAction;
            }

            public StatList(Tips tips) => _tips = tips;

            public void Begin() => _set.Clear();

            public void Set(string key, string value, Color? color = null, Action use = null)
            {
                if (!_rows.TryGetValue(key, out var row))
                {
                    row = new Stat { Row = Ui.Row() };
                    row.Row.style.minHeight = 20;
                    row.Name = new Label();
                    row.Name.style.flexGrow = 1;
                    row.Name.style.flexShrink = 1;
                    row.Name.style.whiteSpace = WhiteSpace.Normal;
                    var stat = row;
                    row.Use = new Button(() => stat.UseAction?.Invoke());
                    row.Use.style.width = row.Use.style.height = 18;
                    Ui.SetPadding(row.Use, 1, 1);
                    row.Use.style.marginLeft = 4;
                    row.Use.style.marginRight = 4;
                    row.Use.Add(Icons.Image("Orbit", 14));
                    _tips.Add(row.Use, () => Loc.T("Tip/UseAltitude"));
                    row.Value = new Label();
                    row.Value.style.unityTextAlign = TextAnchor.MiddleRight;
                    row.Value.style.flexShrink = 0;
                    row.Value.style.marginLeft = 8;
                    row.Row.Add(row.Name);
                    row.Row.Add(row.Use);
                    row.Row.Add(row.Value);
                    _tips.Add(row.Name, () => Loc.Optional("StatTip/" + key));
                    Root.Add(row.Row);
                    _rows[key] = row;
                }
                row.Name.text = Loc.T("Stat/" + key);
                row.Value.text = value;
                row.Value.style.color = color ?? Ui.InkColor;
                row.UseAction = use;
                Ui.Shown(row.Use, use != null);
                _set.Add(key);
            }

            public void End()
            {
                foreach (var pair in _rows) Ui.Shown(pair.Value.Row, _set.Contains(pair.Key));
            }
        }

        /// <summary>A maneuver node marker for the Nodes tab, drawn in the same blue as the original's icons.</summary>
        private sealed class NodeGlyph : VisualElement
        {
            private static readonly Color Blue = new Color32(30, 144, 255, 255);

            public NodeGlyph(float size)
            {
                pickingMode = PickingMode.Ignore;
                style.width = style.height = size;
                style.flexShrink = 0;
                generateVisualContent += Draw;
            }

            private void Draw(MeshGenerationContext ctx)
            {
                var p = ctx.painter2D;
                var r = contentRect;
                var c = r.center;
                float radius = Mathf.Min(r.width, r.height) * 0.28f;
                p.strokeColor = Blue;
                p.lineWidth = 3;
                p.BeginPath();
                p.Arc(c, radius, 0, 360);
                p.Stroke();
                // Four burn handles, like the stock node gizmo.
                float inner = radius + 3, outer = Mathf.Min(r.width, r.height) * 0.48f;
                for (int i = 0; i < 4; i++)
                {
                    float a = i * Mathf.PI / 2;
                    var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                    p.BeginPath();
                    p.MoveTo(c + d * inner);
                    p.LineTo(c + d * outer);
                    p.Stroke();
                }
                p.fillColor = Blue;
                p.BeginPath();
                p.Arc(c, radius * 0.35f, 0, 360);
                p.Fill();
            }
        }
    }
}
