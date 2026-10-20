using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using KSP.Game;
using KSP.Sim.impl;
using UitkForKsp2.API;
using UitkForKsp2.API.Order;
using UitkForKsp2.API.Manipulator;
using UitkForKsp2.Controls;
using UnityEngine;
using UnityEngine.UIElements;
using WayfarersWings.Managers;
using WayfarersWings.Models.Session;
using WayfarersWings.Models.Wings;
using WayfarersWings.UI.Logic;
using WayfarersWings.Utility;
using WayfarersWings.Utility.Serialization;

namespace WayfarersWingsRedux.UI
{
    /// <summary>
    /// The main window. The Kerbals tab lists the roster beside the selected kerbal's record (stats, ribbon rack and,
    /// with the wing cheats setting on, awarding by hand). The Ribbons tab shows every ribbon there is to earn.
    /// Built on the stock <see cref="AppShell"/>, so the frame, header, close button and resize handle match the game.
    /// </summary>
    internal sealed class WingsWindow
    {
        public event Action CloseClicked;
        /// <summary>The player moved or resized the window: top-left corner in panel coordinates, and the size.</summary>
        public event Action<Rect> LayoutChanged;

        private const float DefaultWidth = 900;
        private const float DefaultHeight = 620;
        private const float MinWidth = 700;
        private const float MinHeight = 440;
        private const float ListWidth = 330;
        private const int AwardResults = 30;

        private enum Page { Kerbals, Ribbons }

        private readonly AppShell _root;
        private readonly Tips _tips;
        private readonly PanelRenderer _document;
        private readonly Button _kerbalsTab, _ribbonsTab;
        private readonly VisualElement _kerbalsPage, _ribbonsPage;
        private readonly ScrollView _kerbalList, _detail, _ribbonGrid;
        private readonly TextField _nameSearch, _ribbonSearch;
        private readonly DropdownField _filter, _sort;
        private readonly Button _direction;
        private readonly KerbalProfileQuery.Query _query = new KerbalProfileQuery.Query();
        private Page _page = Page.Kerbals;
        private IGGuid? _selected;
        private string _awardSearch = "";
        private Rect? _savedLayout, _pendingLayout;

        public WingsWindow(Texture2D icon, Rect savedLayout)
        {
            _root = new AppShell { name = "WayfarersWingsRedux", IconTexture = icon };
            _root.CloseClicked += () => CloseClicked?.Invoke();
            _root.style.width = savedLayout.width > 0 ? Math.Max(MinWidth, savedLayout.width) : DefaultWidth;
            _root.style.height = savedLayout.height > 0 ? Math.Max(MinHeight, savedLayout.height) : DefaultHeight;
            _tips = new Tips(_root);

            // Page tabs sit in the stock header, between the title's dashes and the close button.
            var tabs = Ui.Row();
            tabs.style.flexShrink = 0;   // the dashes give way instead, or the last tab slides under the close button
            tabs.style.marginRight = 8;
            _kerbalsTab = Ui.TabButton(() => Show(Page.Kerbals));
            _ribbonsTab = Ui.TabButton(() => Show(Page.Ribbons));
            tabs.Add(_kerbalsTab);
            tabs.Add(_ribbonsTab);
            var header = _root.Q("header");
            var close = _root.Q("header-close-container");
            if (header != null && close != null) header.Insert(header.IndexOf(close), tabs);
            else _root.Add(tabs);

            // The notched dark panel the stock windows put their content in.
            var body = new InvertedCornerBox();
            body.AddToClassList("oab-window-body");
            body.style.flexGrow = 1;
            body.style.flexShrink = 1;
            _root.Add(body);

            // Kerbals page: the roster on the left, the selected kerbal's record on the right.
            _kerbalsPage = Ui.Row();
            _kerbalsPage.style.alignItems = Align.Stretch;
            _kerbalsPage.style.flexGrow = 1;
            body.Add(_kerbalsPage);

            var listColumn = Ui.Column();
            listColumn.style.width = ListWidth;
            listColumn.style.flexShrink = 0;
            listColumn.style.borderRightWidth = 1;
            listColumn.style.borderRightColor = Ui.LineColor;
            _kerbalsPage.Add(listColumn);

            var toolbar = Ui.Column();
            Ui.SetPadding(toolbar, 8, 8);
            toolbar.style.flexShrink = 0;
            toolbar.style.backgroundColor = Ui.DeepColor;
            listColumn.Add(toolbar);

            _nameSearch = SearchField(value => { _query.nameSearch = value; RebuildList(); });
            toolbar.Add(_nameSearch);

            var queryRow = Ui.Row();
            queryRow.style.marginTop = 6;
            toolbar.Add(queryRow);
            _filter = new DropdownField();
            WideDropdownMenu.Apply(_filter);
            _filter.style.flexGrow = 1;
            _filter.style.marginLeft = 0;
            _filter.RegisterValueChangedCallback(e =>
            {
                _query.SetFilterStatus(_filter.choices.IndexOf(e.newValue));
                RebuildList();
            });
            queryRow.Add(_filter);
            _sort = new DropdownField();
            WideDropdownMenu.Apply(_sort);
            _sort.style.flexGrow = 1;
            _sort.RegisterValueChangedCallback(e =>
            {
                _query.SetSort(_sort.choices.IndexOf(e.newValue));
                RebuildList();
            });
            queryRow.Add(_sort);
            _direction = Ui.SmallButton("", () =>
            {
                _query.direction = _query.direction == KerbalProfileQuery.Direction.Ascending
                    ? KerbalProfileQuery.Direction.Descending
                    : KerbalProfileQuery.Direction.Ascending;
                RebuildList();
            });
            _direction.style.width = 26;
            _direction.style.marginRight = 0;
            queryRow.Add(_direction);

            _kerbalList = new ScrollView(ScrollViewMode.Vertical);
            _kerbalList.style.flexGrow = 1;
            _kerbalList.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            listColumn.Add(_kerbalList);

            _detail = new ScrollView(ScrollViewMode.Vertical);
            _detail.style.flexGrow = 1;
            _detail.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            Ui.SetPadding(_detail.contentContainer, 12, 16);
            _kerbalsPage.Add(_detail);

            // Ribbons page: every ribbon there is, as a gallery.
            _ribbonsPage = Ui.Column();
            _ribbonsPage.style.flexGrow = 1;
            body.Add(_ribbonsPage);

            var ribbonToolbar = Ui.Row();
            Ui.SetPadding(ribbonToolbar, 8, 8);
            ribbonToolbar.style.flexShrink = 0;
            ribbonToolbar.style.backgroundColor = Ui.DeepColor;
            _ribbonsPage.Add(ribbonToolbar);
            _ribbonSearch = SearchField(_ => RebuildRibbons());
            _ribbonSearch.style.width = ListWidth - 16;
            ribbonToolbar.Add(_ribbonSearch);

            _ribbonGrid = new ScrollView(ScrollViewMode.Vertical);
            _ribbonGrid.style.flexGrow = 1;
            _ribbonGrid.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            _ribbonGrid.contentContainer.style.flexDirection = FlexDirection.Row;
            _ribbonGrid.contentContainer.style.flexWrap = Wrap.Wrap;
            Ui.SetPadding(_ribbonGrid.contentContainer, 10, 10);
            _ribbonsPage.Add(_ribbonGrid);

            var document = _document = Window.Create(new WindowOptions
            {
                WindowId = "WayfarersWingsRedux_Main",
                Parent = null,
                IsHidingEnabled = true,
                UseStockScale = true,
                BringToFrontOnPointerDown = true,
                DisableGameInputForTextFields = true,
                MoveOptions = new MoveOptions { IsMovingEnabled = true, CheckScreenBounds = true, HandleElementName = "header" },
            }, _root);
            document.EnableUiSounds();

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
                _root.SetDefaultPosition(size =>
                {
                    var panel = _root.panel?.visualTree?.contentRect ?? new Rect(0, 0, 1920, 1080);
                    return new Vector2((panel.width - size.x) / 2, Mathf.Max(40, (panel.height - size.y) / 2));
                });
            }

            ApplyFixedText();
            Loc.Changed += () =>
            {
                ApplyFixedText();
                if (Visible) Refresh();
            };
        }

        public bool Visible
        {
            get => _root.style.display != DisplayStyle.None;
            set
            {
                if (value == Visible) return;
                _root.style.display = value ? DisplayStyle.Flex : DisplayStyle.None;
                if (value) Refresh();
                else _tips.Hide();
            }
        }

        /// <summary>Hides the window for a while (behind the pause menu) without closing it or losing what's on it.</summary>
        public bool Suspended
        {
            get => _root.style.visibility == Visibility.Hidden;
            set { if (value != Suspended) _root.style.visibility = value ? Visibility.Hidden : Visibility.Visible; }
        }

        /// <summary>Shows a kerbal's record, opening the Kerbals tab.</summary>
        public void SelectKerbal(IGGuid kerbalId)
        {
            _selected = kerbalId;
            _awardSearch = "";
            Show(Page.Kerbals);
            BringToFront();
        }

        /// <summary>
        /// Raises this window's panel above every other mod window. Each window is its own panel, so another
        /// mod's window (the one whose button opened this) would otherwise stay on top.
        /// </summary>
        private void BringToFront()
        {
            var settings = _document != null ? _document.panelSettings : null;
            if (settings == null) return;
            try
            {
                OrderManager.Register(settings);   // no-op if it's already known
                OrderManager.BringToFront(settings);
            }
            catch (System.Exception e)
            {
                SWLog.Warning($"Couldn't bring the window to the front: {e.Message}");
            }
        }

        /// <summary>A new campaign is loaded: forget the kerbal picked in the last one.</summary>
        public void Reset()
        {
            _selected = null;
            _awardSearch = "";
        }

        /// <summary>Rebuilds the open page from the current profiles.</summary>
        public void Refresh() => Show(_page);

        private void Show(Page page)
        {
            _page = page;
            Ui.SetTabActive(_kerbalsTab, page == Page.Kerbals);
            Ui.SetTabActive(_ribbonsTab, page == Page.Ribbons);
            _kerbalsPage.style.display = page == Page.Kerbals ? DisplayStyle.Flex : DisplayStyle.None;
            _ribbonsPage.style.display = page == Page.Ribbons ? DisplayStyle.Flex : DisplayStyle.None;
            _tips.Hide();
            if (page == Page.Kerbals) RebuildList();
            else RebuildRibbons();
        }

        private void ApplyFixedText()
        {
            _root.Title = Loc.T("UI/Achievements");
            _kerbalsTab.text = Loc.T("UI/Kerbals");
            _ribbonsTab.text = Loc.T("UI/Ribbons");
            _filter.choices = KerbalProfileQuery.FilterStatusOptions;
            _filter.SetValueWithoutNotify(_query.GetFilterStatusChoice());
            _sort.choices = KerbalProfileQuery.SortOptions;
            _sort.SetValueWithoutNotify(_query.GetSortChoice());
            _nameSearch.textEdition.placeholder = Loc.T("UI/SearchKerbals");
            _ribbonSearch.textEdition.placeholder = Loc.T("UI/Search");
        }

        // ───────────────────────────── Kerbals ─────────────────────────────

        private void RebuildList()
        {
            _direction.text = _query.direction == KerbalProfileQuery.Direction.Ascending ? "▲" : "▼";
            _kerbalList.Clear();
            if (!WingsApi.IsReady || WingsSessionManager.Roster == null)
            {
                _detail.Clear();
                return;
            }

            var profiles = WingsSessionManager.Instance.GetAllKerbalsProfiles();
            _query.Apply(profiles);
            if (_selected == null && profiles.Count > 0) _selected = profiles[0].kerbalId;
            foreach (var profile in profiles) _kerbalList.Add(KerbalRow(profile));
            RebuildDetail();
        }

        private VisualElement KerbalRow(KerbalProfile profile)
        {
            var kerbal = profile.KerbalInfo;
            bool selected = _selected.HasValue && profile.kerbalId == _selected.Value;

            var row = Ui.Row();
            row.style.alignItems = Align.FlexStart;
            Ui.SetPadding(row, 6, 8);
            row.style.borderBottomWidth = 1;
            row.style.borderBottomColor = Ui.LineColor;
            row.style.backgroundColor = selected ? Ui.Panel2Color : Color.clear;
            row.RegisterCallback<ClickEvent>(_ =>
            {
                _selected = profile.kerbalId;
                _awardSearch = "";
                RebuildList();
            });

            var portrait = Ui.Portrait(kerbal, 40);
            portrait.Add(StatusDot(profile));
            row.Add(portrait);

            var text = Ui.Column();
            text.style.flexGrow = 1;
            text.style.flexShrink = 1;
            text.style.marginLeft = 8;
            row.Add(text);

            var nameRow = Ui.Row();
            var name = Ui.OneLine(Ui.Text(kerbal?.Attributes.GetFullName() ?? profile.fullName ?? "?", 13, Ui.InkColor, true));
            name.style.flexShrink = 1;
            nameRow.Add(name);
            nameRow.Add(Ui.Spacer());
            nameRow.Add(StarButton(profile, () => RebuildList()));
            text.Add(nameRow);

            text.Add(Ui.Text($"{Loc.F("UI/KerbalMissionsCount", profile.missionsCount)}  ·  {profile.totalPoints}", 11, Ui.Ink3Color));

            var ribbons = Ui.WrapRow();
            ribbons.style.marginTop = 4;
            foreach (var entry in profile.Entries.Where(e => !e.isSuperseeded))
            {
                var ribbon = Ui.Ribbon(entry.Wing, false, _root);
                ribbon.style.marginRight = 2;
                ribbon.style.marginBottom = 2;
                _tips.Add(ribbon, () => entry.Wing.DisplayName);
                ribbons.Add(ribbon);
            }
            text.Add(ribbons);
            return row;
        }

        private void RebuildDetail()
        {
            _detail.Clear();
            if (!_selected.HasValue) return;
            var profile = WingsSessionManager.Instance.GetKerbalProfile(_selected.Value);
            var kerbal = profile.KerbalInfo;

            var head = Ui.Row();
            head.style.alignItems = Align.FlexStart;
            var portrait = Ui.Portrait(kerbal, 72);
            head.Add(portrait);
            var title = Ui.Column();
            title.style.marginLeft = 12;
            title.style.flexGrow = 1;
            title.style.flexShrink = 1;
            var nameRow = Ui.Row();
            nameRow.Add(Ui.OneLine(Ui.Text(kerbal?.Attributes.GetFullName() ?? profile.fullName ?? "?", 18, Ui.InkColor, true)));
            nameRow.Add(Ui.Spacer());
            nameRow.Add(StarButton(profile, RebuildList));
            title.Add(nameRow);
            var status = StatusText(profile);
            status.style.marginTop = 4;
            title.Add(status);
            var points = Ui.Text($"{profile.totalPoints}", 22, Ui.GoldColor, true);
            points.style.marginTop = 6;
            _tips.Add(points, () => Loc.T("UI/Prestige"));
            title.Add(points);
            head.Add(title);
            _detail.Add(head);

            _detail.Add(Ui.Caption(Loc.T("UI/Statistics")));
            var stats = Ui.WrapRow();
            stats.Add(Stat(Loc.T("UI/MissionsCompleted"), profile.missionsCount.ToString(CultureInfo.InvariantCulture)));
            stats.Add(Stat(Loc.T("UI/TotalMissionTime"), GameTimeSpan.FromSeconds(profile.totalMissionTime).Format()));
            stats.Add(Stat(Loc.T("UI/TotalEvaTime"), GameTimeSpan.FromSeconds(profile.TotalEvaTime).Format()));
            stats.Add(Stat(Loc.T("UI/VisitedBodiesCount"), profile.visitedBodies.Count.ToString(CultureInfo.InvariantCulture)));
            stats.Add(Stat(Loc.T("UI/VisitedBiomesCount"), profile.visitedBiomes.Count.ToString(CultureInfo.InvariantCulture)));
            stats.Add(Stat(Loc.T("UI/VisitedDiscoverablesCount"), profile.VisitedDiscoverablesWithoutKSC.ToString(CultureInfo.InvariantCulture)));
            if (profile.GetStatus() == KerbalStatus.Assigned || profile.GetStatus() == KerbalStatus.AssignedActive)
            {
                stats.Add(Stat($"{Loc.T("UI/ActiveMission")}: {Loc.T("UI/CelestialBodies")}",
                    profile.missionBodies.Count.ToString(CultureInfo.InvariantCulture)));
                stats.Add(Stat($"{Loc.T("UI/ActiveMission")}: {Loc.T("UI/VisitedRegionsCount")}",
                    profile.missionRegions.Count.ToString(CultureInfo.InvariantCulture)));
            }
            _detail.Add(stats);

            _detail.Add(Ui.Caption(Loc.T("UI/Ribbons")));
            var entries = profile.Entries.Where(e => !e.isSuperseeded).OrderByDescending(e => e.universeTime).ToList();
            if (entries.Count == 0) _detail.Add(Ui.Text(Loc.T("UI/NoRibbons"), 12, Ui.Ink2Color));
            foreach (var entry in entries) _detail.Add(EntryRow(entry.Wing, entry, profile));

            if (Settings.AllowWingCheats.Value && kerbal != null) BuildAward(profile, kerbal);
        }

        private VisualElement EntryRow(Wing wing, KerbalWingEntry entry, KerbalProfile profile)
        {
            var row = Ui.Row();
            row.style.alignItems = Align.FlexStart;
            row.style.marginBottom = 8;
            row.Add(Ui.Ribbon(wing, true, _root));

            var text = Ui.Column();
            text.style.marginLeft = 10;
            text.style.flexGrow = 1;
            text.style.flexShrink = 1;
            text.Add(Ui.Text(wing.DisplayName, 13, Ui.InkColor, true));
            var description = Ui.Text(wing.Description ?? "", 11, Ui.Ink2Color);
            description.style.whiteSpace = WhiteSpace.Normal;
            text.Add(description);
            if (entry != null)
            {
                var date = Ui.Text(DateTimeLogic.FormatUniverseTime(entry.universeTime), 10, Ui.Ink3Color);
                date.style.marginTop = 2;
                text.Add(date);
            }
            row.Add(text);

            if (entry != null && Settings.AllowWingCheats.Value)
            {
                // Two clicks to revoke: the first turns the button into a confirmation.
                Button revoke = null;
                revoke = Ui.SmallButton(Loc.T("UI/Revoke"), () =>
                {
                    if (revoke.userData == null)
                    {
                        revoke.userData = true;
                        revoke.text = Loc.T("UI/ConfirmRevokeButton");
                        return;
                    }
                    WingsSessionManager.Instance.Revoke(entry, profile.kerbalId);
                });
                revoke.style.marginLeft = 8;
                row.Add(revoke);
            }
            return row;
        }

        private void BuildAward(KerbalProfile profile, KerbalInfo kerbal)
        {
            _detail.Add(Ui.Caption(Loc.T("UI/AwardWingTitle")));
            var results = Ui.Column();
            var search = SearchField(value =>
            {
                _awardSearch = value;
                FillAwardResults(results, profile, kerbal);
            });
            search.style.maxWidth = 320;
            search.style.marginBottom = 8;
            search.SetValueWithoutNotify(_awardSearch);
            search.textEdition.placeholder = Loc.T("UI/Search");
            _detail.Add(search);
            _detail.Add(results);
            FillAwardResults(results, profile, kerbal);
        }

        private void FillAwardResults(VisualElement results, KerbalProfile profile, KerbalInfo kerbal)
        {
            results.Clear();
            if (string.IsNullOrWhiteSpace(_awardSearch)) return;
            var matches = Core.Instance.WingsPool.Wings
                .Where(w => !profile.HasWing(w) && (w.DisplayName ?? "").IndexOf(_awardSearch, StringComparison.OrdinalIgnoreCase) >= 0)
                .Take(AwardResults);
            foreach (var wing in matches)
            {
                var row = EntryRow(wing, null, profile);
                var award = Ui.SmallButton(Loc.T("UI/AwardToKerbal"), () => WingsSessionManager.Instance.Award(wing, kerbal));
                award.style.marginLeft = 8;
                row.Add(award);
                results.Add(row);
            }
        }

        private static VisualElement Stat(string label, string value)
        {
            var stat = Ui.Column();
            stat.style.width = 170;
            stat.style.marginRight = 10;
            stat.style.marginBottom = 8;
            var name = Ui.Text(label, 10, Ui.Ink3Color);
            name.style.whiteSpace = WhiteSpace.Normal;
            stat.Add(name);
            stat.Add(Ui.Text(value, 15, Ui.GoldColor, true));
            return stat;
        }

        private static Label StatusText(KerbalProfile profile)
        {
            switch (profile.GetStatus())
            {
                case KerbalStatus.Assigned: return Ui.Text(Loc.T("UI/OnMission"), 12, Ui.OnMissionColor);
                case KerbalStatus.AssignedActive: return Ui.Text(Loc.T("UI/ActiveVessel"), 12, Ui.ActiveColor);
                case KerbalStatus.Available: return Ui.Text(Loc.T("UI/Available"), 12, Ui.GoodColor);
                default: return Ui.Text(Loc.T("UI/Dead"), 12, Ui.Ink3Color);
            }
        }

        private VisualElement StatusDot(KerbalProfile profile)
        {
            var dot = new VisualElement();
            dot.style.position = Position.Absolute;
            dot.style.right = -3;
            dot.style.bottom = -3;
            dot.style.width = dot.style.height = 10;
            Ui.SetRadius(dot, 5);
            Ui.SetBorder(dot, Ui.DeepColor, 2);
            switch (profile.GetStatus())
            {
                case KerbalStatus.Assigned: dot.style.backgroundColor = Ui.OnMissionColor; break;
                case KerbalStatus.AssignedActive: dot.style.backgroundColor = Ui.ActiveColor; break;
                case KerbalStatus.Available: dot.style.backgroundColor = Ui.GoodColor; break;
                default: dot.style.backgroundColor = Ui.Ink3Color; break;
            }
            _tips.Add(dot, () => StatusText(profile).text);
            return dot;
        }

        private Button StarButton(KerbalProfile profile, Action changed)
        {
            var star = new Button { text = profile.isStarred ? "★" : "☆" };
            star.style.fontSize = 14;
            star.style.width = 22;
            star.style.height = 20;
            star.style.marginLeft = 4;
            star.style.marginRight = 0;
            star.style.flexShrink = 0;
            Ui.SetPadding(star, 0, 0);
            star.style.backgroundColor = Color.clear;
            Ui.SetBorder(star, Color.clear, 0);
            star.style.color = profile.isStarred ? Ui.GoldColor : Ui.Ink3Color;
            star.clicked += () =>
            {
                profile.isStarred = !profile.isStarred;
                changed();
            };
            star.RegisterCallback<ClickEvent>(e => e.StopPropagation());
            _tips.Add(star, () => Loc.T("UI/Starred"));
            return star;
        }

        private static TextField SearchField(Action<string> changed)
        {
            var field = new TextField();
            field.style.marginLeft = field.style.marginRight = 0;
            field.style.flexShrink = 0;
            field.RegisterValueChangedCallback(e => changed(e.newValue ?? ""));
            return field;
        }

        // ───────────────────────────── Ribbons ─────────────────────────────

        private void RebuildRibbons()
        {
            _ribbonGrid.Clear();
            if (!WingsApi.IsReady) return;

            var holders = new Dictionary<Wing, int>();
            foreach (var profile in WingsSessionManager.Instance.KerbalProfiles.Values)
                foreach (var entry in profile.Entries)
                    holders[entry.Wing] = holders.TryGetValue(entry.Wing, out int n) ? n + 1 : 1;

            string search = _ribbonSearch.value ?? "";
            foreach (var wing in Core.Instance.WingsPool.Wings)
            {
                if (search.Length > 0 && (wing.DisplayName ?? "").IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0) continue;
                int held = holders.TryGetValue(wing, out int count) ? count : 0;

                var tile = Ui.Column();
                tile.style.width = 136;
                tile.style.marginRight = 8;
                tile.style.marginBottom = 10;
                tile.style.alignItems = Align.Center;
                var ribbon = Ui.Ribbon(wing, true, _root);
                if (held == 0) ribbon.style.opacity = 0.45f;
                tile.Add(ribbon);
                var name = Ui.Text(wing.DisplayName, 10, held > 0 ? Ui.InkColor : Ui.Ink3Color);
                name.style.whiteSpace = WhiteSpace.Normal;
                name.style.unityTextAlign = TextAnchor.UpperCenter;
                name.style.marginTop = 3;
                tile.Add(name);
                _tips.Add(tile, () => $"{wing.DisplayName}\n{wing.Description}\n{Loc.Count("UI/HeldBy", held)}");
                _ribbonGrid.Add(tile);
            }
        }

        // ───────────────────────────── Layout ─────────────────────────────

        /// <summary>
        /// Reports a drag or resize once the window has stayed put for a check, so the layout is saved without
        /// writing config on every pointer move.
        /// </summary>
        public void CheckLayout()
        {
            var position = _root.worldBound.position;
            if (float.IsNaN(position.x) || float.IsNaN(position.y)) return;
            var layout = CurrentLayout(position);
            if (_savedLayout == null)
            {
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

        /// <summary>The size comes from the inline style, which only the resize handle (or a saved size) sets.</summary>
        private Rect CurrentLayout(Vector2 position)
        {
            float width = _root.style.width.keyword == StyleKeyword.Undefined ? _root.style.width.value.value : -1;
            float height = _root.style.height.keyword == StyleKeyword.Undefined ? _root.style.height.value.value : -1;
            return new Rect(position.x, position.y, width, height);
        }

        private static bool Approximately(Rect a, Rect b) =>
            (a.position - b.position).sqrMagnitude < 1f && (a.size - b.size).sqrMagnitude < 1f;
    }
}
