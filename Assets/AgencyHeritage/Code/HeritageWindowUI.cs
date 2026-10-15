using System;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using KSP.Game;
using KSP.Networking.MP.Utils;
using Redux.UI.AgencyFlags;
using Redux.UI.Stickers;
using UitkForKsp2.API;
using UitkForKsp2.API.Manipulator;
using UitkForKsp2.API.Order;
using UitkForKsp2.Controls;
using UnityEngine;
using UnityEngine.UIElements;

namespace AgencyHeritage
{
    /// <summary>
    /// The heritage window. The Museum tab is a rail of brand eras over the selected era's exhibit (banner, stats,
    /// and one section at a time: timeline, missions, crew, collection). The Rebrand tab is the ceremony that begins
    /// a new era. It's built on the same <see cref="AppShell"/> template as the stock windows, so the frame, header,
    /// close button and resize handle come from the game's theme.
    /// </summary>
    public class HeritageWindowUI
    {
        public event Action CloseClicked;
        /// <summary>The player moved or resized the window: top-left corner in panel coordinates, and the size.</summary>
        public event Action<Rect> LayoutChanged;

        private const double SecondsPerDay = 21600;   // Kerbin: 6-hour days
        private const double DaysPerYear = 426;
        private const float DefaultWidth = 960;
        private const float DefaultHeight = 680;
        private const float MinWidth = 760;
        private const float MinHeight = 560;

        private static readonly Color PanelColor = new Color(0.063f, 0.075f, 0.106f, 0.97f);
        private static readonly Color Panel2Color = new Color(0.09f, 0.106f, 0.15f);
        private static readonly Color DeepColor = new Color(0.047f, 0.059f, 0.086f);
        private static readonly Color LineColor = new Color(0.2f, 0.23f, 0.3f);
        private static readonly Color InkColor = new Color32(230, 235, 244, 255);
        private static readonly Color Ink2Color = new Color(0.64f, 0.66f, 0.72f);
        private static readonly Color Ink3Color = new Color32(127, 133, 152, 255);
        private static readonly Color SignalColor = new Color32(140, 146, 230, 255);
        private static readonly Color GoodColor = new Color(0.45f, 0.88f, 0.42f);
        private static readonly Color BadColor = new Color(0.95f, 0.38f, 0.32f);
        private static readonly Color LostColor = new Color(0.86f, 0.42f, 0.4f);
        private static readonly Color PlacardColor = new Color(0.91f, 0.89f, 0.84f);
        private static readonly Color PlacardInk = new Color(0.11f, 0.1f, 0.09f);
        private static readonly Color WallColor = new Color(0.075f, 0.086f, 0.125f);

        private enum Section { Timeline, Missions, Crew, Collection }

        private readonly AgencyHeritagePlugin _plugin;
        private readonly AppShell _root;
        private readonly Button _museumTab, _rebrandTab;
        private readonly VisualElement _museumPage, _rebrandPage, _actions;
        private readonly List<Action> _fixedText = new List<Action>();
        private bool _onRebrandPage;
        private Rect? _savedLayout, _pendingLayout;

        // Museum
        private readonly ScrollView _rail;
        private readonly VisualElement _hero, _sectionTabs;
        private readonly ScrollView _sectionHost;
        private string _selectedEraId;
        private Section _section = Section.Timeline;
        private EraExhibit _exhibit;

        // Rebrand
        private readonly TextField _nameField, _noteField;
        private readonly VisualElement _flagPreview;
        private readonly Label _flagName, _formMessage;
        private readonly LiverySwatch _baseSwatch, _accentSwatch;
        private string _flagLocation;

        private readonly List<Texture2D> _thumbnails = new List<Texture2D>();
        private readonly Dictionary<string, Sprite> _sprites = new Dictionary<string, Sprite>();
        private readonly Dictionary<string, List<VisualElement>> _spriteWaiters = new Dictionary<string, List<VisualElement>>();

        /// <param name="savedLayout">Position and size from the config; negative values mean "use the default".</param>
        public HeritageWindowUI(Texture2D icon, AgencyHeritagePlugin plugin, Rect savedLayout)
        {
            _plugin = plugin;

            _root = new AppShell { name = "AgencyHeritage", IconTexture = icon };
            _root.CloseClicked += () => CloseClicked?.Invoke();
            _root.style.width = savedLayout.width > 0 ? Math.Max(MinWidth, savedLayout.width) : DefaultWidth;
            _root.style.height = savedLayout.height > 0 ? Math.Max(MinHeight, savedLayout.height) : DefaultHeight;

            // The page tabs sit in the stock header, between the title's dashes and the close button.
            var tabs = Row();
            tabs.style.flexShrink = 0;   // the dashes give way instead, or the last tab slides under the close button
            tabs.style.marginRight = 8;
            _museumTab = TabButton(() => ShowMuseum());
            _rebrandTab = TabButton(() => ShowRebrand());
            Fixed(() => _museumTab.text = Loc.T("Tab/History"));
            Fixed(() => _rebrandTab.text = Loc.T("Tab/Rebrand"));
            tabs.Add(_museumTab);
            tabs.Add(_rebrandTab);
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

            // Museum page
            _museumPage = new VisualElement();
            _museumPage.style.flexGrow = 1;
            body.Add(_museumPage);

            _rail = new ScrollView(ScrollViewMode.Horizontal);
            _rail.style.flexShrink = 0;
            _rail.style.height = 132;
            _rail.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            _rail.style.backgroundColor = DeepColor;
            _rail.style.borderBottomWidth = 1;
            _rail.style.borderBottomColor = LineColor;
            _rail.contentContainer.style.flexDirection = FlexDirection.Row;
            _rail.contentContainer.style.paddingLeft = 12;
            _rail.contentContainer.style.paddingTop = 10;
            _museumPage.Add(_rail);

            var hall = new VisualElement();
            hall.style.flexGrow = 1;
            hall.style.flexShrink = 1;
            SetPadding(hall, 10, 6);
            _museumPage.Add(hall);

            _hero = new VisualElement();
            _hero.style.flexShrink = 0;
            hall.Add(_hero);

            // The section tabs double as the era's stats: each shows its icon and count.
            _sectionTabs = Row();
            _sectionTabs.style.flexShrink = 0;
            _sectionTabs.style.marginTop = 10;
            _sectionTabs.style.marginRight = -8;
            hall.Add(_sectionTabs);

            _sectionHost = new ScrollView(ScrollViewMode.Vertical);
            _sectionHost.style.flexGrow = 1;
            _sectionHost.style.flexShrink = 1;
            _sectionHost.style.marginTop = 10;
            hall.Add(_sectionHost);

            // Rebrand page: identity on the left, livery and motto on the right, actions under the panel.
            var rebrand = new ScrollView(ScrollViewMode.Vertical);
            rebrand.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            rebrand.style.flexGrow = 1;
            rebrand.style.backgroundColor = Color.clear;
            SetPadding(rebrand.contentContainer, 10, 16);
            _rebrandPage = rebrand;
            _rebrandPage.style.display = DisplayStyle.None;
            body.Add(_rebrandPage);

            var intro = Text("", 12, Ink2Color);
            intro.style.whiteSpace = WhiteSpace.Normal;
            Fixed(() => intro.text = Loc.T("Rebrand/Intro"));
            _rebrandPage.Add(intro);

            var columns = Row();
            columns.style.alignItems = Align.FlexStart;
            _rebrandPage.Add(columns);

            var left = new VisualElement();
            left.style.flexGrow = 1;
            left.style.flexBasis = 0;
            left.style.marginRight = 28;
            columns.Add(left);

            left.Add(Caption("Caption/Name"));
            _nameField = new TextField { maxLength = 48 };
            _nameField.style.marginLeft = _nameField.style.marginRight = 0;
            _nameField.style.fontSize = 18;
            left.Add(_nameField);

            left.Add(Caption("Caption/Flag"));
            var flagRow = Row();
            flagRow.style.alignItems = Align.FlexEnd;
            _flagPreview = new VisualElement();
            _flagPreview.style.width = 200;
            _flagPreview.style.height = 126;
            _flagPreview.style.flexShrink = 0;
            _flagPreview.style.marginRight = 14;
            _flagPreview.style.backgroundColor = Color.black;
            SetBorder(_flagPreview, new Color(1, 1, 1, 0.8f), 3);
            flagRow.Add(_flagPreview);
            var flagInfo = new VisualElement();
            flagInfo.style.flexShrink = 1;
            _flagName = Text("", 14, InkColor, true);
            _flagName.style.whiteSpace = WhiteSpace.Normal;
            flagInfo.Add(_flagName);
            var chooseFlag = new Button(() => FlagTexturePickerWindow.OpenAgencyFlags(_flagLocation, SetFlag));
            Fixed(() => chooseFlag.text = Loc.T("Button/ChooseFlag"));
            chooseFlag.style.marginLeft = 0;
            chooseFlag.style.marginTop = 6;
            flagInfo.Add(chooseFlag);
            flagRow.Add(flagInfo);
            left.Add(flagRow);

            var right = new VisualElement();
            right.style.flexGrow = 1;
            right.style.flexBasis = 0;
            columns.Add(right);

            right.Add(Caption("Caption/Livery"));
            var swatches = Row();
            _baseSwatch = new LiverySwatch();
            _accentSwatch = new LiverySwatch();
            Fixed(() => _baseSwatch.SetText(Loc.T("Swatch/Base")));
            Fixed(() => _accentSwatch.SetText(Loc.T("Swatch/Accent")));
            Tip(_baseSwatch.Paint, () => Loc.T("Tooltip/ChangeColor"));
            Tip(_accentSwatch.Paint, () => Loc.T("Tooltip/ChangeColor"));
            var swap = new Button(() =>
            {
                var b = _baseSwatch.Value;
                _baseSwatch.Value = _accentSwatch.Value;
                _accentSwatch.Value = b;
            }) { text = "⇄" };
            Tip(swap, () => Loc.T("Tooltip/Swap"));
            swap.style.width = swap.style.height = 34;
            SetRadius(swap, 17);
            swap.style.marginLeft = swap.style.marginRight = 12;
            swatches.Add(_baseSwatch.Root);
            swatches.Add(swap);
            swatches.Add(_accentSwatch.Root);
            right.Add(swatches);

            right.Add(Caption("Caption/Motto"));
            _noteField = new TextField { multiline = true, maxLength = 200 };
            _noteField.style.minHeight = 72;
            _noteField.style.marginLeft = _noteField.style.marginRight = 0;
            _noteField.style.whiteSpace = WhiteSpace.Normal;
            Fixed(() => _noteField.textEdition.placeholder = Loc.T("Placeholder/Motto"));
            right.Add(_noteField);

            _formMessage = Text("", 12, BadColor);
            _formMessage.style.whiteSpace = WhiteSpace.Normal;
            _formMessage.style.marginTop = 14;
            _rebrandPage.Add(_formMessage);

            // Action row under the panel, as in the stock dialogs; shown on the rebrand page only.
            _actions = new VisualElement();
            _actions.AddToClassList("oab-window-actions");
            _actions.style.flexShrink = 0;
            _actions.style.justifyContent = Justify.FlexEnd;
            _actions.style.marginTop = 8;
            _actions.style.display = DisplayStyle.None;
            var cancel = new Button(() => ShowMuseum());
            cancel.AddToClassList("cancel");
            var confirm = new Button(ConfirmRebrand);
            Fixed(() => cancel.text = Loc.T("Button/Cancel"));
            Fixed(() => confirm.text = Loc.T("Button/NewEra"));
            foreach (var button in new[] { cancel, confirm })
            {
                button.AddToClassList("oab-window-action-button");
                button.style.flexGrow = 0;
                button.style.width = 200;
                // Shrink a long translation to fit, then cut it off with "…" rather than spilling out of the button.
                button.style.paddingLeft = button.style.paddingRight = 4;
                button.style.overflow = Overflow.Hidden;
                button.style.whiteSpace = WhiteSpace.NoWrap;
                button.style.textOverflow = TextOverflow.Ellipsis;
                button.style.unityTextAutoSize = new TextAutoSize(TextAutoSizeMode.BestFit, 10, 14);
                _actions.Add(button);
            }
            _root.Add(_actions);

            var document = Window.Create(new WindowOptions
            {
                WindowId = "AgencyHeritage_Main",
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
        }

        /// <summary>Registers text that's set once rather than on every rebuild; re-applied when the language changes.</summary>
        private void Fixed(Action apply) => _fixedText.Add(apply);

        private void ApplyFixedText()
        {
            _root.Title = Loc.T("Name");
            foreach (var apply in _fixedText) apply();
        }

        private void OnLanguageChanged()
        {
            ApplyFixedText();
            if (!Visible) return;   // the museum is rebuilt when the window opens
            if (_onRebrandPage) _formMessage.text = "";
            else ShowMuseum();
        }

        public bool Visible
        {
            get => _root.style.display != DisplayStyle.None;
            set
            {
                bool wasVisible = Visible;
                _root.style.display = value ? DisplayStyle.Flex : DisplayStyle.None;
                if (wasVisible && !value) ReleaseThumbnails();
            }
        }

        /// <summary>Hides the window for a while (behind the pause menu) without closing it or losing what's on it.</summary>
        public bool Suspended
        {
            get => _root.style.visibility == Visibility.Hidden;
            set { if (value != Suspended) _root.style.visibility = value ? Visibility.Hidden : Visibility.Visible; }
        }

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

        /// <summary>The size comes from the inline style, which only the resize handle (or a saved size) sets.</summary>
        private Rect CurrentLayout(Vector2 position)
        {
            float width = _root.style.width.keyword == StyleKeyword.Undefined ? _root.style.width.value.value : -1;
            float height = _root.style.height.keyword == StyleKeyword.Undefined ? _root.style.height.value.value : -1;
            return new Rect(position.x, position.y, width, height);
        }

        private static bool Approximately(Rect a, Rect b) =>
            (a.position - b.position).sqrMagnitude < 1f && (a.size - b.size).sqrMagnitude < 1f;

        /// <summary>Rebuilds the museum from the current history.</summary>
        public void Refresh() => ShowMuseum();

        // ───────────────────────────── Museum ─────────────────────────────

        private void ShowMuseum(string selectEraId = null)
        {
            _onRebrandPage = false;
            SetTabActive(_museumTab, true);
            SetTabActive(_rebrandTab, false);
            _rebrandPage.style.display = DisplayStyle.None;
            _actions.style.display = DisplayStyle.None;
            _museumPage.style.display = DisplayStyle.Flex;
            if (selectEraId != null) _selectedEraId = selectEraId;

            var data = _plugin.Data;
            _rail.Clear();
            if (data == null || data.Eras.Count == 0)
            {
                _hero.Clear();
                _sectionTabs.Clear();
                _sectionHost.Clear();
                _hero.Add(Text(Loc.T("Empty/NoCampaign"), 14, Ink2Color));
                return;
            }

            if (data.FindEra(_selectedEraId) == null) _selectedEraId = data.CurrentEra().Id;
            for (int i = 0; i < data.Eras.Count; i++) _rail.Add(BuildRailStop(data, data.Eras[i], i));
            _rail.Add(BuildNewEraStop());

            var era = data.FindEra(_selectedEraId);
            try
            {
                _plugin.ImportTravelLogLaunches();
                _exhibit = EraExhibit.Build(data, era, _plugin.TravelLogSnapshot(), _plugin.Roster(),
                    WingsBridge.AllAwards(), _plugin.HomeWorldName, _plugin.UniverseTime);
            }
            catch (Exception e)
            {
                SWLog.Error($"Couldn't build the exhibit for {era.Name}: {e}");
                _exhibit = new EraExhibit { Era = era, Number = data.Eras.IndexOf(era) + 1, StartUT = era.StartUT, EndUT = data.EndUT(era), IsCurrent = era == data.CurrentEra() };
            }
            try
            {
                _exhibit.Blueprints = BlueprintArchive.CardsForEra(data, era, _plugin.CampaignFolder, _plugin.WorkspaceFolder);
            }
            catch (Exception e)
            {
                SWLog.Error($"Couldn't read blueprints: {e}");
            }

            BuildHero(_exhibit);
            BuildSectionTabs();
            ShowSection(_section);
        }

        private VisualElement BuildRailStop(HeritageSaveData data, BrandEra era, int index)
        {
            bool selected = era.Id == _selectedEraId;
            bool current = era == data.CurrentEra();

            var stop = Column();
            stop.style.width = 150;
            stop.style.alignItems = Align.Center;
            stop.RegisterCallback<ClickEvent>(_ => ShowMuseum(era.Id));

            // The rail line runs behind the medallions; each stop draws its own piece.
            var medal = new VisualElement();
            medal.style.width = 150;
            medal.style.height = 58;
            medal.style.flexShrink = 0;
            var line = new Drawn((p, r) =>
            {
                float y = r.center.y;
                var dash = LineColor;
                for (float x = index == 0 ? r.center.x : 0; x < r.width; x += 12)
                    HeritageGraphics.Line(p, new Vector2(x, y), new Vector2(Mathf.Min(x + 7, r.width), y), dash, 2);
            });
            Fill(line);
            medal.Add(line);

            var ring = new Drawn((p, r) => HeritageGraphics.MedallionRing(p, r, era.Base, era.Accent, selected), 58, 58);
            ring.style.position = Position.Absolute;
            ring.style.left = 46;
            medal.Add(ring);

            var flag = new VisualElement();
            flag.style.position = Position.Absolute;
            flag.style.left = 46 + (selected ? 13 : 10);
            flag.style.top = selected ? 13 : 10;
            flag.style.width = flag.style.height = selected ? 32 : 38;
            SetRadius(flag, selected ? 16 : 19);
            flag.style.overflow = Overflow.Hidden;
            flag.style.backgroundColor = Color.black;
            ShowSprite(flag, era.FlagSpriteLocation, BackgroundSizeType.Cover);
            medal.Add(flag);

            if (current)
            {
                var now = Fit(Text(Loc.T("Value/Now"), 9, new Color(0.03f, 0.14f, 0.04f), true), 56, 7);
                now.style.position = Position.Absolute;
                now.style.left = 92;
                now.style.top = -2;
                now.style.backgroundColor = GoodColor;
                SetPadding(now, 0, 4);
                SetRadius(now, 2);
                medal.Add(now);
            }
            stop.Add(medal);

            // The name gets up to two lines; the era's dates are in the banner once it's selected.
            var name = Text(era.Name, 13, selected ? Color.white : Ink2Color, true);
            name.style.marginTop = 6;
            name.style.unityTextAlign = TextAnchor.UpperCenter;
            name.style.maxWidth = 140;
            name.style.maxHeight = 42;
            name.style.whiteSpace = WhiteSpace.Normal;
            name.style.overflow = Overflow.Hidden;
            name.style.textOverflow = TextOverflow.Ellipsis;
            stop.Add(name);
            return stop;
        }

        private VisualElement BuildNewEraStop()
        {
            var stop = Column();
            stop.style.width = 120;
            stop.style.alignItems = Align.Center;
            stop.RegisterCallback<ClickEvent>(_ => ShowRebrand());
            var circle = new Drawn((p, r) =>
            {
                var c = r.center;
                for (int a = 0; a < 360; a += 30) HeritageGraphics.Ring(p, c, 26, 2, Ink3Color, a, a + 18);
                HeritageGraphics.Line(p, c + new Vector2(-9, 0), c + new Vector2(9, 0), Ink2Color, 2.5f);
                HeritageGraphics.Line(p, c + new Vector2(0, -9), c + new Vector2(0, 9), Ink2Color, 2.5f);
            }, 58, 58);
            stop.Add(circle);
            var label = Fit(Text(Loc.T("Rail/Rebrand"), 13, Ink2Color, true), 116, 9);
            label.style.marginTop = 4;
            stop.Add(label);
            return stop;
        }

        private void BuildHero(EraExhibit exhibit)
        {
            var era = exhibit.Era;
            _hero.Clear();
            var hero = Row();
            hero.style.minHeight = 150;
            hero.style.overflow = Overflow.Hidden;
            SetRadius(hero, 4);
            SetPadding(hero, 14, 20);
            hero.style.borderBottomWidth = 4;
            hero.style.borderBottomColor = era.Accent;

            var background = new Drawn((p, r) => HeritageGraphics.HorizontalGradient(p, r,
                Color.Lerp(PanelColor, era.Base, 0.36f), Color.Lerp(Panel2Color, era.Base, 0.06f)));
            Fill(background);
            hero.Add(background);

            hero.Add(FlagOnPole(era.FlagSpriteLocation));

            var text = new VisualElement();
            text.style.flexGrow = 1;
            text.style.flexShrink = 1;
            text.style.marginLeft = 22;
            text.Add(Text($"{Loc.F("Hero/Era", Roman(exhibit.Number))}  ·  {Loc.T(exhibit.IsCurrent ? "Hero/Current" : "Hero/Past")}", 11, new Color(1, 1, 1, 0.72f), true));
            var name = Text(era.Name, 34, Color.white, true);
            name.style.whiteSpace = WhiteSpace.Normal;
            name.style.marginTop = 2;
            text.Add(name);
            string span = Loc.F("Hero/Span",
                Range(exhibit.StartUT, exhibit.IsCurrent ? (double?)null : exhibit.EndUT), FormatDays(exhibit.EndUT - exhibit.StartUT));
            // A rebrand also gets its real-world date; the founding era needs no label.
            if (era.StartRealTime > DateTime.MinValue)
                span += "   ·   " + Loc.F("Hero/Rebranded", era.StartRealTime.ToString("d", Loc.Culture));
            var line = Text(span, 12, new Color(1, 1, 1, 0.8f));
            line.style.whiteSpace = WhiteSpace.Normal;
            text.Add(line);
            if (!string.IsNullOrEmpty(era.Note) && era.Note != "Founded")
            {
                var quote = Text(Loc.F("Format/Quote", era.Note), 15, Color.white);
                quote.style.unityFontStyleAndWeight = FontStyle.Italic;
                quote.style.whiteSpace = WhiteSpace.Normal;
                quote.style.marginTop = 8;
                text.Add(quote);
            }
            hero.Add(text);

            var copy = ShareButton("Button/CopyColors", "Button/Copied", () =>
            {
                GUIUtility.systemCopyBuffer = $"#{ColorUtility.ToHtmlStringRGB(era.Base)}, #{ColorUtility.ToHtmlStringRGB(era.Accent)}";
                return true;
            });
            copy.style.backgroundColor = Color.clear;
            copy.style.color = new Color(1, 1, 1, 0.6f);
            SetBorder(copy, new Color(1, 1, 1, 0.22f), 1);
            // The era's two colors, as a pair of dots beside it.
            var colors = Row();
            colors.style.alignSelf = Align.FlexStart;
            colors.style.alignItems = Align.Center;
            colors.style.flexShrink = 0;
            colors.Add(new Drawn((p, r) =>
            {
                HeritageGraphics.Circle(p, new Vector2(r.x + 6, r.center.y), 5, era.Base);
                HeritageGraphics.Circle(p, new Vector2(r.x + 17, r.center.y), 5, era.Accent);
            }, 24, 14));
            colors.Add(copy);
            hero.Add(colors);

            _hero.Add(hero);
        }

        /// <summary>
        /// The era's flag flying from a pole: the flag picture with soft folds of light and shade across it, so it reads
        /// as cloth rather than a picture in a frame.
        /// </summary>
        private VisualElement FlagOnPole(string flagLocation)
        {
            const float poleX = 6, flagWidth = 170, flagHeight = 107;
            var mount = new VisualElement();
            mount.style.width = flagWidth + 16;
            mount.style.height = flagHeight + 30;
            mount.style.flexShrink = 0;
            mount.style.alignSelf = Align.Center;

            var pole = new Drawn((p, r) =>
            {
                HeritageGraphics.Line(p, new Vector2(poleX, 6), new Vector2(poleX, r.height), new Color(0.55f, 0.57f, 0.6f), 3.5f);
                HeritageGraphics.Line(p, new Vector2(poleX - 0.8f, 6), new Vector2(poleX - 0.8f, r.height), new Color(1, 1, 1, 0.35f), 1);
                HeritageGraphics.Circle(p, new Vector2(poleX, 5), 4.5f, new Color(0.85f, 0.75f, 0.4f));
            });
            Fill(pole);
            mount.Add(pole);

            var flag = new VisualElement();
            flag.style.position = Position.Absolute;
            flag.style.left = poleX + 2;
            flag.style.top = 10;
            flag.style.width = flagWidth;
            flag.style.height = flagHeight;
            flag.style.backgroundColor = Color.black;
            ShowSprite(flag, flagLocation, BackgroundSizeType.Cover);
            var folds = new Drawn((p, r) => HeritageGraphics.ClothFolds(p, r));
            Fill(folds);
            flag.Add(folds);
            mount.Add(flag);
            return mount;
        }

        /// <summary>
        /// The section tabs, as stat tiles: Timeline counts the era's firsts, Missions its launches, Crew its kerbals
        /// (with who flew, joined and was lost), Collection its blueprints.
        /// </summary>
        private void BuildSectionTabs()
        {
            _sectionTabs.Clear();
            var exhibit = _exhibit;
            var accent = Color.Lerp(exhibit.Era.Base, Color.white, 0.15f);

            string N(int n) => n.ToString(CultureInfo.InvariantCulture);
            var crew = new List<string>();
            if (exhibit.CrewFlownCount > 0) crew.Add($"{N(exhibit.CrewFlownCount)} {Loc.T("Role/Flew")}");
            if (exhibit.RecruitCount > 0) crew.Add($"{N(exhibit.RecruitCount)} {Loc.T("Stat/Recruits")}");
            if (exhibit.Losses.Count > 0) crew.Add($"{N(exhibit.Losses.Count)} {Loc.T("Stat/Lost")}");
            if (WingsBridge.IsInstalled && exhibit.Ribbons.Count > 0) crew.Add($"{N(exhibit.Ribbons.Count)} {Loc.T("Stat/Ribbons")}");

            AddSectionTile(Section.Timeline, HeritageGraphics.Glyph.Star, exhibit.Milestones.Count, Loc.T("Stat/Firsts"), accent);
            AddSectionTile(Section.Missions, HeritageGraphics.Glyph.Rocket, exhibit.Launches, Loc.T("Stat/Launches"), accent);
            AddSectionTile(Section.Crew, HeritageGraphics.Glyph.Helmet, exhibit.Crew.Count, string.Join(" · ", crew), accent);
            AddSectionTile(Section.Collection, HeritageGraphics.Glyph.Blueprint, exhibit.Blueprints.Count, Loc.T("Stat/Craft"), accent);
        }

        private void AddSectionTile(Section section, HeritageGraphics.Glyph glyph, int value, string detail, Color accent)
        {
            var tile = new Button(() => ShowSection(section));
            tile.userData = section;
            tile.style.flexDirection = FlexDirection.Row;
            tile.style.alignItems = Align.Center;
            tile.style.flexGrow = 1;
            tile.style.flexBasis = 0;
            tile.style.minWidth = 0;
            tile.style.height = 48;
            tile.style.marginLeft = 0;
            tile.style.marginTop = tile.style.marginBottom = 0;
            tile.style.marginRight = 8;
            SetRadius(tile, 4);
            SetPadding(tile, 0, 12);

            tile.Add(new Drawn((p, r) => HeritageGraphics.Icon(p, r, glyph, accent), 22, 22));
            var number = Text(value.ToString(CultureInfo.InvariantCulture), 22, InkColor, true);
            number.style.marginLeft = 10;
            number.style.flexShrink = 0;
            tile.Add(number);
            var text = new VisualElement();
            text.style.marginLeft = 10;
            text.style.flexShrink = 1;
            text.style.minWidth = 0;
            text.style.alignItems = Align.FlexStart;
            var name = Fit(Text(Loc.T("Section/" + section), 11, InkColor, true), 220, 8);
            name.style.unityTextAlign = TextAnchor.MiddleLeft;
            text.Add(name);
            if (!string.IsNullOrEmpty(detail))
            {
                var line = Fit(Text(detail, 9, Ink3Color, true), 220, 7);
                line.style.unityTextAlign = TextAnchor.MiddleLeft;
                text.Add(line);
            }
            tile.Add(text);
            _sectionTabs.Add(tile);
        }

        private void SetSectionTileActive(Button tile, bool active)
        {
            var accent = _exhibit == null ? LineColor : Color.Lerp(_exhibit.Era.Base, Color.white, 0.15f);
            tile.style.backgroundColor = active ? Panel2Color : new Color(Panel2Color.r, Panel2Color.g, Panel2Color.b, 0.45f);
            SetBorder(tile, active ? accent : LineColor, 1);
            tile.style.borderBottomWidth = active ? 3 : 1;
            tile.style.opacity = active ? 1f : 0.7f;
        }

        private void ShowSection(Section section)
        {
            _section = section;
            foreach (var child in _sectionTabs.Children())
                if (child is Button tile) SetSectionTileActive(tile, tile.userData is Section s && s == section);

            ReleaseThumbnails();
            _sectionHost.Clear();
            _sectionHost.scrollOffset = Vector2.zero;
            switch (section)
            {
                case Section.Timeline: BuildTimeline(); break;
                case Section.Missions: BuildMissions(); break;
                case Section.Crew: BuildCrew(); break;
                case Section.Collection: BuildCollection(); break;
            }
        }

        // ─── Timeline: milestones and launches along the era ───

        private static readonly float[] TimelineZooms = { 1, 2, 4, 8, 16 };
        private int _timelineZoom;
        /// <summary>The flight whose story is open under the timeline, or null.</summary>
        private FlightRecord _timelineFlight;

        private void BuildTimeline()
        {
            var exhibit = _exhibit;
            _timelinePlacements.Clear();
            _timelineTicks.Clear();
            if (exhibit.Milestones.Count == 0 && exhibit.LaunchTimes.Count == 0)
            {
                _sectionHost.Add(Empty(Loc.T("Empty/Timeline")));
                return;
            }
            if (_timelineFlight != null && !exhibit.Flights.Exists(f => f.Record == _timelineFlight)) _timelineFlight = null;

            // Firsts are labelled above the line; below it, a dated axis, then a label per flight and per loss. The line
            // sits only as low as the labels above it need, and the box is only as tall as the labels below it need.
            const float margin = 50, topReserve = 30, laneStep = 34, scrollerHeight = 14;
            float lineY = topReserve + 20;

            // The timeline scrolls sideways under fixed overlays: zoom at the top left, the legend at the top right.
            var frame = new VisualElement();
            frame.style.backgroundColor = Panel2Color;
            SetBorder(frame, LineColor, 1);
            SetRadius(frame, 4);
            _sectionHost.Add(frame);
            var scroll = new ScrollView(ScrollViewMode.Horizontal);
            scroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            frame.Add(scroll);
            var host = new VisualElement();
            scroll.Add(host);

            var zoomBar = Row();
            zoomBar.style.position = Position.Absolute;
            zoomBar.style.left = 8;
            zoomBar.style.top = 6;
            zoomBar.style.alignItems = Align.Center;
            var zoomOut = ZoomButton("−");
            var zoomLabel = Text("", 11, Ink2Color, true);
            zoomLabel.style.minWidth = 30;
            zoomLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            var zoomIn = ZoomButton("+");
            var hidden = Text("", 10, Ink3Color);
            hidden.style.marginLeft = 10;
            zoomBar.Add(zoomOut);
            zoomBar.Add(zoomLabel);
            zoomBar.Add(zoomIn);
            zoomBar.Add(hidden);
            frame.Add(zoomBar);

            var legend = TimelineLegend(exhibit);
            legend.style.position = Position.Absolute;
            legend.style.right = 10;
            legend.style.top = 8;
            frame.Add(legend);

            var story = new VisualElement();
            story.style.marginTop = 8;
            _sectionHost.Add(story);

            double start = exhibit.StartUT, end = Math.Max(exhibit.EndUT, start + 1);
            float X(Rect r, double ut) => margin + (float)((ut - start) / (end - start)) * (r.width - 2 * margin);
            var above = exhibit.Milestones.Select(m => new TimelineLabel
            {
                UT = m.UT,
                Title = MilestoneTitle(m),
                Detail = $"{MilestoneDetail(m)} · {FormatDate(m.UT)}",
            }).ToList();
            var below = FlightLabels(exhibit, start, end);

            var track = new Drawn((p, r) =>
            {
                float y = lineY;
                HeritageGraphics.Line(p, new Vector2(margin, y), new Vector2(r.width - margin, y), LineColor, 4);
                if (exhibit.IsCurrent)
                    for (float x = margin; x < r.width - margin; x += 12)
                        HeritageGraphics.Line(p, new Vector2(x, y), new Vector2(Mathf.Min(x + 6, r.width - margin), y), exhibit.Era.Base, 3);
                else
                    HeritageGraphics.Line(p, new Vector2(margin, y), new Vector2(r.width - margin, y), exhibit.Era.Base, 3);

                foreach (double ut in _timelineTicks)
                {
                    float x = X(r, ut);
                    HeritageGraphics.Line(p, new Vector2(x, y + 4), new Vector2(x, y + 9), Ink3Color, 1);
                }
                foreach (var placed in _timelinePlacements)
                {
                    float x = X(r, placed.UT);
                    var leader = placed.Flight != null && placed.Flight == _timelineFlight ? InkColor : new Color(0.35f, 0.38f, 0.47f);
                    HeritageGraphics.Line(p, new Vector2(x, y + (placed.Lane > 0 ? 10 : 0)), new Vector2(x, y + placed.Lane), leader, 1);
                }

                // The open flight is drawn last and brightest; the others fade back while it's open.
                foreach (var flight in exhibit.Flights.Where(f => f.Record != _timelineFlight))
                    DrawFlightPath(p, flight, ut => X(r, ut), y, start, end, exhibit.IsCurrent, _timelineFlight == null ? 0.8f : 0.25f, false);
                var open = exhibit.Flights.Find(f => f.Record != null && f.Record == _timelineFlight);
                if (open != null) DrawFlightPath(p, open, ut => X(r, ut), y, start, end, exhibit.IsCurrent, 1f, true);

                foreach (var loss in exhibit.Losses)
                {
                    float x = X(r, loss.UT);
                    HeritageGraphics.Line(p, new Vector2(x - 4, y + 6), new Vector2(x + 4, y + 14), LostColor, 2);
                    HeritageGraphics.Line(p, new Vector2(x + 4, y + 6), new Vector2(x - 4, y + 14), LostColor, 2);
                }
                foreach (var flight in exhibit.Flights)
                {
                    float x = X(r, flight.UT);
                    var ink = _timelineFlight == null || flight.Record == _timelineFlight ? InkColor : new Color(InkColor.r, InkColor.g, InkColor.b, 0.35f);
                    HeritageGraphics.Polygon(p, ink, new Vector2(x, y - 13), new Vector2(x + 4, y - 5), new Vector2(x - 4, y - 5));
                    HeritageGraphics.Line(p, new Vector2(x, y - 5), new Vector2(x, y + 4), ink, 2);
                }
                foreach (var milestone in exhibit.Milestones)
                {
                    float x = X(r, milestone.UT);
                    HeritageGraphics.Circle(p, new Vector2(x, y), 9, Color.white);
                    HeritageGraphics.Circle(p, new Vector2(x, y), 7, HeritageGraphics.BodyColor(milestone.Body));
                }
            });
            Fill(track);
            host.Add(track);

            var labels = new VisualElement();
            Fill(labels);
            labels.pickingMode = PickingMode.Ignore;
            host.Add(labels);

            void Open(FlightRecord flight)
            {
                _timelineFlight = _timelineFlight == flight ? null : flight;
                Layout();
                ShowStory(story, exhibit);
            }

            void Layout()
            {
                var r = host.contentRect;
                if (r.width <= 0) return;
                labels.Clear();
                _timelinePlacements.Clear();

                int left = PlaceLabels(above, new float[] { -44, -78, -112 }, ut => X(r, ut))
                    + PlaceLabels(below, new float[] { 34, 68, 102 }, ut => X(r, ut));
                hidden.text = left == 0 ? "" : Count("Timeline/Hidden", left, left.ToString(Loc.Culture));
                int lanesAbove = _timelinePlacements.Count(l => l.Lane < 0) == 0 ? 0 : (int)(-_timelinePlacements.Where(l => l.Lane < 0).Min(l => l.Lane) - 44) / (int)laneStep + 1;
                int lanesBelow = _timelinePlacements.Count(l => l.Lane > 0) == 0 ? 0 : (int)(_timelinePlacements.Where(l => l.Lane > 0).Max(l => l.Lane) - 34) / (int)laneStep + 1;
                lineY = topReserve + (lanesAbove == 0 ? 20 : 76 + laneStep * (lanesAbove - 1));
                float height = lineY + 30 + laneStep * lanesBelow + 4;
                if (Math.Abs(host.resolvedStyle.height - height) > 0.5f)
                {
                    host.style.height = height;
                    scroll.style.height = height + scrollerHeight;
                }

                // The axis: the era's first day, its last (or today), and round steps between.
                PlaceTick(labels, X(r, start), lineY, FormatDate(start), r.width);
                PlaceTick(labels, X(r, end), lineY,
                    exhibit.IsCurrent ? Loc.F("Timeline/Now", FormatDate(end)) : FormatDate(end), r.width);
                _timelineTicks.Clear();
                foreach (double ut in AxisTicks(start, end, r.width - 2 * margin))
                {
                    float x = X(r, ut);
                    if (x - X(r, start) < 75 || X(r, end) - x < 95) continue;
                    _timelineTicks.Add(ut);
                    PlaceTick(labels, x, lineY, FormatDate(ut), r.width);
                }

                foreach (var placed in _timelinePlacements)
                {
                    bool chosen = placed.Flight != null && placed.Flight == _timelineFlight;
                    var block = Column();
                    block.style.position = Position.Absolute;
                    block.style.width = 140;
                    block.style.alignItems = Align.Center;
                    block.style.left = Mathf.Clamp(X(r, placed.UT) - 70, 0, r.width - 140);
                    SetRadius(block, 3);
                    if (chosen) block.style.backgroundColor = new Color(1, 1, 1, 0.08f);
                    block.Add(Fit(Text(placed.Title, 12, placed.TitleColor ?? (chosen ? Color.white : InkColor), true), 140, 9));
                    block.Add(Fit(Text(placed.Detail, 10, Ink2Color), 140, 8));
                    if (placed.Lane < 0) block.style.bottom = r.height - (lineY + placed.Lane) + 2;
                    else block.style.top = lineY + placed.Lane + 2;
                    if (placed.Flight != null)
                    {
                        var flight = placed.Flight;
                        block.RegisterCallback<ClickEvent>(e =>
                        {
                            e.StopPropagation();
                            Open(flight);
                        });
                    }
                    labels.Add(block);
                }
                track.MarkDirtyRepaint();
            }

            // Every mark on the line, where it's drawn and what it is. Pointing at one names it (even when its label
            // didn't fit); clicking a flight's mark opens the flight.
            List<TimelineMark> Marks()
            {
                var r = host.contentRect;
                var marks = new List<TimelineMark>();
                bool InEra(double ut) => ut >= start && ut <= end;
                foreach (var flight in exhibit.Flights)
                {
                    string name = flight.VesselName ?? Loc.T("Flight/Unidentified");
                    string crew = flight.Crew.Count > 0 ? string.Join(", ", flight.Crew)
                        : Loc.T(flight.VesselName == null ? "Flight/CrewUnknown" : "Flight/Uncrewed");
                    if (InEra(flight.UT))
                        marks.Add(new TimelineMark(new Vector2(X(r, flight.UT), lineY - 8), flight.Record,
                            $"{name} · {Loc.T("Timeline/Launch")} · {FormatDate(flight.UT)}\n{crew}"));
                    if (flight.Record == null) continue;
                    foreach (var ev in flight.Record.Events.Where(ev => (IsTouchdown(ev) || ev.Kind == FlightEventKind.Destroyed) && InEra(ev.UT)))
                    {
                        string body = ev.Body == null ? Loc.T("Milestone/Unknown") : Loc.Body(ev.Body);
                        string what = ev.Kind == FlightEventKind.Destroyed ? Loc.T("Fate/Destroyed")
                            : Loc.F(ev.Kind == FlightEventKind.Splashed ? "Reach/Splashed" : "Reach/Landed", body);
                        marks.Add(new TimelineMark(new Vector2(X(r, ev.UT), lineY - (ev.Kind == FlightEventKind.Destroyed ? 10 : 6)), flight.Record,
                            $"{name} · {what}\n{FormatDate(ev.UT)} · {Loc.F("Tip/Elapsed", FormatSpan(ev.UT - flight.UT))}"));
                    }
                }
                foreach (var milestone in exhibit.Milestones)
                {
                    string text = $"{MilestoneTitle(milestone)} · {MilestoneDetail(milestone)} · {FormatDate(milestone.UT)}";
                    var earner = exhibit.Flights.Find(f => f.Firsts.Contains(milestone));
                    if (earner != null)
                        text += $"\n{earner.VesselName ?? Loc.T("Flight/Unidentified")}"
                            + (earner.Crew.Count > 0 ? ": " + string.Join(", ", earner.Crew) : "");
                    marks.Add(new TimelineMark(new Vector2(X(r, milestone.UT), lineY), earner?.Record, text));
                }
                foreach (var loss in exhibit.Losses)
                    marks.Add(new TimelineMark(new Vector2(X(r, loss.UT), lineY + 10), null,
                        $"{EraExhibit.ShortName(loss.Name)}\n" + (string.IsNullOrEmpty(loss.VesselName)
                            ? Loc.F("Crew/LostOn", FormatDate(loss.UT))
                            : Loc.F("Crew/LostAboard", loss.VesselName, FormatDate(loss.UT)))));
                return marks;
            }

            TimelineMark MarkAt(Vector2 world)
            {
                var at = host.WorldToLocal(world);
                TimelineMark nearest = null;
                float best = 11;
                foreach (var mark in Marks())
                {
                    float distance = Vector2.Distance(mark.At, at);
                    if (distance < best) { best = distance; nearest = mark; }
                }
                return nearest;
            }

            host.RegisterCallback<ClickEvent>(e =>
            {
                var mark = MarkAt(e.position);
                if (mark?.Flight != null) Open(mark.Flight);
            });
            TimelineMark hovered = null;
            host.RegisterCallback<PointerMoveEvent>(e =>
            {
                var mark = MarkAt(e.position);
                if (mark?.Text == hovered?.Text) return;
                hovered = mark;
                if (mark == null) HideTip();
                else
                {
                    var world = host.LocalToWorld(mark.At);
                    ShowTipAt(new Rect(world.x - 6, world.y - 6, 12, 12), mark.Text);
                }
            });
            host.RegisterCallback<PointerLeaveEvent>(_ => { hovered = null; HideTip(); });

            void Zoom(int step)
            {
                _timelineZoom = Mathf.Clamp(_timelineZoom + step, 0, TimelineZooms.Length - 1);
                zoomLabel.text = $"{TimelineZooms[_timelineZoom]:0}×";
                zoomOut.SetEnabled(_timelineZoom > 0);
                zoomIn.SetEnabled(_timelineZoom < TimelineZooms.Length - 1);
                float viewport = scroll.contentViewport.layout.width;
                if (viewport > 0) host.style.width = viewport * TimelineZooms[_timelineZoom];
            }
            zoomOut.clicked += () => Zoom(-1);
            zoomIn.clicked += () => Zoom(1);
            scroll.contentViewport.RegisterCallback<GeometryChangedEvent>(_ => Zoom(0));
            host.RegisterCallback<GeometryChangedEvent>(_ => Layout());
            host.style.height = 200;
            scroll.style.height = 200 + scrollerHeight;
            Zoom(0);
            ShowStory(story, exhibit);
        }

        private VisualElement TimelineLegend(EraExhibit exhibit)
        {
            var legend = Row();
            legend.style.alignItems = Align.Center;
            legend.pickingMode = PickingMode.Ignore;
            void Entry(Action<Painter2D, Rect> draw, string key)
            {
                var mark = new Drawn(draw, 14, 14);
                mark.style.marginLeft = legend.childCount == 0 ? 0 : 10;
                legend.Add(mark);
                var label = Text(Loc.T(key), 10, Ink3Color);
                label.pickingMode = PickingMode.Ignore;
                legend.Add(label);
            }
            Entry((p, r) =>
            {
                var c = r.center;
                HeritageGraphics.Polygon(p, InkColor, c + new Vector2(0, -7), c + new Vector2(4, 1), c + new Vector2(-4, 1));
            }, "Legend/Launch");
            Entry((p, r) => HeritageGraphics.Circle(p, r.center, 5, HeritageGraphics.BodyColor("Kerbin")), "Legend/First");
            if (exhibit.Flights.Any(f => f.Record != null && f.Record.Events.Exists(IsTouchdown)))
                Entry((p, r) => LandingMark(p, r.center + new Vector2(0, 4), HeritageGraphics.BodyColor("Mun")), "Legend/Landing");
            if (exhibit.Flights.Any(f => f.Record != null && f.Record.Has(FlightEventKind.Destroyed)))
                Entry((p, r) => WreckMark(p, r.center, 1f), "Legend/Wreck");
            if (exhibit.Losses.Count > 0)
                Entry((p, r) =>
                {
                    var c = r.center;
                    HeritageGraphics.Line(p, c + new Vector2(-4, -4), c + new Vector2(4, 4), LostColor, 2);
                    HeritageGraphics.Line(p, c + new Vector2(4, -4), c + new Vector2(-4, 4), LostColor, 2);
                }, "Legend/Lost");
            return legend;
        }

        /// <summary>A mark on the timeline: where it's drawn, the flight it belongs to (if any), and its hover text.</summary>
        private class TimelineMark
        {
            public readonly Vector2 At;
            public readonly FlightRecord Flight;
            public readonly string Text;

            public TimelineMark(Vector2 at, FlightRecord flight, string text)
            {
                At = at;
                Flight = flight;
                Text = text;
            }
        }

        private static Button ZoomButton(string text)
        {
            var button = new Button { text = text };
            button.style.width = 26;
            button.style.height = 22;
            button.style.fontSize = 14;
            button.style.unityFontStyleAndWeight = FontStyle.Bold;
            button.style.marginLeft = button.style.marginRight = 0;
            SetPadding(button, 0, 0);
            return button;
        }

        /// <summary>The open flight's whole story, in order and dated, under the timeline.</summary>
        private void ShowStory(VisualElement story, EraExhibit exhibit)
        {
            story.Clear();
            var flight = exhibit.Flights.Find(f => f.Record != null && f.Record == _timelineFlight);
            if (flight == null) return;

            var box = new VisualElement();
            box.style.backgroundColor = Panel2Color;
            SetBorder(box, LineColor, 1);
            SetRadius(box, 4);
            SetPadding(box, 8, 12);
            story.Add(box);

            var heading = Row();
            heading.style.alignItems = Align.Center;
            heading.Add(Text((flight.VesselName ?? Loc.T("Flight/Unidentified")).ToUpper(Loc.Culture), 12, InkColor, true));
            string summary = FlightStory(flight);
            if (summary != null)
            {
                var fate = Text("   " + summary, 11, FateColor(flight.Fate));
                heading.Add(fate);
            }
            var close = ZoomButton("×");
            close.style.marginLeft = StyleKeyword.Auto;
            close.clicked += () => { _timelineFlight = null; ShowSection(Section.Timeline); };
            heading.Add(close);
            box.Add(heading);

            var steps = WrapRow();
            steps.style.marginTop = 6;
            // A date is shown once; the steps after it on the same day leave it blank.
            string lastDate = null;
            void Step(string what, double ut, Color color)
            {
                var chip = Column();
                chip.style.marginRight = 18;
                chip.style.marginBottom = 4;
                chip.Add(Text(what, 11, color, true));
                string date = FormatDate(ut);
                chip.Add(Text(date == lastDate ? " " : date, 10, Ink3Color));
                lastDate = date;
                steps.Add(chip);
            }
            Step(Loc.T("Timeline/Launch"), flight.UT, InkColor);
            foreach (var e in flight.Record.Events.OrderBy(e => e.UT))
            {
                string body = e.Body == null ? Loc.T("Milestone/Unknown") : Loc.Body(e.Body);
                switch (e.Kind)
                {
                    case FlightEventKind.Orbit: Step(Loc.F("Reach/Orbit", body), e.UT, InkColor); break;
                    case FlightEventKind.EnteredSoi: Step(Loc.F("Story/Entered", body), e.UT, InkColor); break;
                    case FlightEventKind.Landed: Step(Loc.F("Reach/Landed", body), e.UT, InkColor); break;
                    case FlightEventKind.Splashed: Step(Loc.F("Reach/Splashed", body), e.UT, InkColor); break;
                    case FlightEventKind.Docked: Step(Loc.T("Reach/Docked"), e.UT, InkColor); break;
                    case FlightEventKind.Recovered: Step(Loc.T("Fate/Recovered"), e.UT, GoodColor); break;
                    case FlightEventKind.Destroyed: Step(Loc.T("Fate/Destroyed"), e.UT, LostColor); break;
                }
            }
            foreach (var loss in exhibit.Losses.Where(l => flight.Record.LostCrew.Contains(l.Name)))
                Step(Loc.F("Story/Lost", EraExhibit.ShortName(loss.Name)), loss.UT, LostColor);
            box.Add(steps);
        }

        /// <summary>A label per flight (its name and dates, at its launch) and per lost kerbal, oldest first.</summary>
        private static List<TimelineLabel> FlightLabels(EraExhibit exhibit, double start, double end)
        {
            var result = new List<TimelineLabel>();
            foreach (var flight in exhibit.Flights.Where(f => f.UT >= start && f.UT <= end))
            {
                // The flight runs from launch to its last landing, recovery or loss, or is still out.
                double? last = flight.Record?.Events
                    .Where(e => IsTouchdown(e) || e.Kind == FlightEventKind.Recovered || e.Kind == FlightEventKind.Destroyed)
                    .Select(e => (double?)e.UT).DefaultIfEmpty(null).Max();
                string dates = flight.Fate == FlightFate.InFlight ? Range(flight.UT, null)
                    : last.HasValue && FormatDate(last.Value) != FormatDate(flight.UT) ? Range(flight.UT, last.Value)
                    : FormatDate(flight.UT);
                result.Add(new TimelineLabel
                {
                    UT = flight.UT,
                    Title = flight.VesselName ?? Loc.T("Flight/Unidentified"),
                    Detail = dates,
                    Flight = flight.Record,
                });
            }
            foreach (var loss in exhibit.Losses)
                result.Add(new TimelineLabel
                {
                    UT = loss.UT,
                    Title = EraExhibit.ShortName(loss.Name),
                    Detail = $"{Loc.T("Timeline/Lost")} · {FormatDate(loss.UT)}",
                    TitleColor = LostColor,
                });
            return result.OrderBy(l => l.UT).ToList();
        }

        /// <summary>Round dates between two times, about one per 110 pixels: every day, few days, few dozen days, or years.</summary>
        private static IEnumerable<double> AxisTicks(double start, double end, float width)
        {
            double spanDays = (end - start) / SecondsPerDay;
            if (spanDays <= 0 || width <= 0) yield break;
            double pixelsPerDay = width / spanDays;
            var steps = new double[] { 1, 2, 5, 10, 20, 50, 100, 200 }
                .Concat(new double[] { 1, 2, 5, 10, 20, 50, 100 }.Select(y => y * DaysPerYear));
            double step = steps.FirstOrDefault(d => d * pixelsPerDay >= 110);
            if (step <= 0) yield break;
            for (double day = Math.Ceiling(start / SecondsPerDay / step) * step; day * SecondsPerDay < end; day += step)
                if (day * SecondsPerDay > start) yield return day * SecondsPerDay;
        }

        /// <summary>A date under the line, centered on its tick and kept inside the box.</summary>
        private static void PlaceTick(VisualElement labels, float x, float lineY, string text, float width)
        {
            var label = Text(text, 10, Ink3Color);
            label.style.position = Position.Absolute;
            label.style.width = 120;
            label.style.unityTextAlign = TextAnchor.UpperCenter;
            label.style.left = Mathf.Clamp(x - 60, 0, width - 120);
            label.style.top = lineY + 11;
            labels.Add(label);
        }

        private static bool IsTouchdown(FlightEvent e) => e.Kind == FlightEventKind.Landed || e.Kind == FlightEventKind.Splashed;

        /// <summary>
        /// A flight on the timeline: a dashed hop above the line from its launch to each landing (so you can see a
        /// mission go up and come back), a marker in the body's color where it touched down, a grey cross if the
        /// craft was lost, and a hop that stays up to the end of the line while it's still out there.
        /// </summary>
        private static void DrawFlightPath(Painter2D p, ExhibitEntry flight, Func<double, float> x, float y,
            double start, double end, bool openEnded, float alpha, bool highlight)
        {
            var record = flight.Record;
            if (record == null || !record.IsFollowed()) return;
            var stops = record.Events.Where(e => IsTouchdown(e) || e.Kind == FlightEventKind.Destroyed)
                .Where(e => e.UT >= start && e.UT <= end).OrderBy(e => e.UT).ToList();
            var ink = highlight ? Color.white : Ink2Color;
            var color = new Color(ink.r, ink.g, ink.b, alpha);
            float width = highlight ? 2.2f : 1.5f;

            float Height(float from, float to) => Mathf.Clamp((to - from) * 0.35f, 7, 26);
            float lastX = x(record.UT);
            foreach (var stop in stops)
            {
                float stopX = x(stop.UT);
                if (stopX - lastX >= 3)
                {
                    float h = Height(lastX, stopX);
                    HeritageGraphics.DashedCurve(p, new Vector2(lastX, y - 4), new Vector2((lastX + stopX) / 2, y - 4 - 2 * h),
                        new Vector2(stopX, y - 4), color, width, 3);
                }
                lastX = stopX;
            }
            // Still flying: the hop climbs and carries on to the end of the line.
            if (flight.Fate == FlightFate.InFlight && openEnded)
            {
                float endX = x(end);
                if (endX - lastX >= 3)
                {
                    float h = Height(lastX, endX);
                    HeritageGraphics.DashedCurve(p, new Vector2(lastX, y - 4), new Vector2(lastX + Mathf.Min(h, (endX - lastX) / 2), y - 4 - h),
                        new Vector2(endX, y - 4 - h), color, width, 3);
                }
            }
            foreach (var stop in stops)
            {
                var at = new Vector2(x(stop.UT), y);
                if (stop.Kind == FlightEventKind.Destroyed) WreckMark(p, at + new Vector2(0, -10), alpha);
                else LandingMark(p, at, HeritageGraphics.BodyColor(stop.Body), alpha);
            }
        }

        /// <summary>A downward triangle touching the line: a landing or splashdown.</summary>
        private static void LandingMark(Painter2D p, Vector2 at, Color body, float alpha = 1f)
        {
            body.a = alpha;
            HeritageGraphics.Polygon(p, new Color(1, 1, 1, alpha), at + new Vector2(-6, -12), at + new Vector2(6, -12), at + new Vector2(0, -1));
            HeritageGraphics.Polygon(p, body, at + new Vector2(-4.2f, -10.8f), at + new Vector2(4.2f, -10.8f), at + new Vector2(0, -3.2f));
        }

        /// <summary>A small grey cross: a craft lost.</summary>
        private static void WreckMark(Painter2D p, Vector2 at, float alpha)
        {
            var grey = new Color(0.55f, 0.57f, 0.63f, alpha);
            HeritageGraphics.Line(p, at + new Vector2(-3.5f, -3.5f), at + new Vector2(3.5f, 3.5f), grey, 2);
            HeritageGraphics.Line(p, at + new Vector2(3.5f, -3.5f), at + new Vector2(-3.5f, 3.5f), grey, 2);
        }

        /// <summary>A dated label on the timeline.</summary>
        private class TimelineLabel
        {
            public double UT;
            public string Title, Detail;
            public Color? TitleColor;
            /// <summary>The flight this label opens, if it's a flight's.</summary>
            public FlightRecord Flight;
            /// <summary>Signed distance of the label from the line: negative above, positive below.</summary>
            public float Lane;
        }

        private readonly List<TimelineLabel> _timelinePlacements = new List<TimelineLabel>();
        private readonly List<double> _timelineTicks = new List<double>();

        /// <summary>
        /// Gives each label the nearest lane where it clears the label before it. One that fits nowhere is left out;
        /// its mark on the line still shows the moment. Returns how many were left out.
        /// </summary>
        private int PlaceLabels(List<TimelineLabel> items, float[] lanes, Func<double, float> x)
        {
            int left = 0;
            var lastX = new float[lanes.Length];
            for (int i = 0; i < lastX.Length; i++) lastX[i] = float.NegativeInfinity;
            foreach (var item in items)
            {
                float px = x(item.UT);
                int lane = Array.FindIndex(lastX, last => px - last >= 145);
                if (lane < 0) { left++; continue; }
                lastX[lane] = px;
                item.Lane = lanes[lane];
                _timelinePlacements.Add(item);
            }
            return left;
        }

        // ─── Missions: a patch for each recorded flight ───

        private void BuildMissions()
        {
            var exhibit = _exhibit;
            if (exhibit.Flights.Count > 0) _sectionHost.Add(MissionToolbar(exhibit));
            var view = MissionView(exhibit);
            var wall = WrapRow();
            foreach (var flight in view)
            {
                var card = Column();
                card.style.width = 150;
                card.style.alignItems = Align.Center;
                card.style.marginRight = 12;
                card.style.marginBottom = 14;
                var art = new HeritageGraphics.PatchArt
                {
                    Base = exhibit.Era.Base,
                    Accent = exhibit.Era.Accent,
                    Reach = flight.Reach,
                    Fate = flight.Fate,
                    Destination = flight.Destination,
                    HomeWorld = _plugin.HomeWorldName,
                    Crewed = flight.CrewCount > 0,
                    Docked = flight.Docked,
                    Parts = flight.Record?.Parts ?? 0,
                    Crew = flight.CrewCount,
                    CrewLost = flight.LostCrew.Count,
                    Firsts = flight.Firsts.Count,
                    Stops = flight.Stops.Select(st => (st.Body, st.Reach)).ToList(),
                };
                var patch = new Drawn((p, r) => HeritageGraphics.Patch(p, r, art), 110, 110);
                var tipFlight = flight;
                Tip(patch, () => PatchTip(tipFlight));
                card.Add(patch);
                var record = flight.Record;
                card.RegisterCallback<ClickEvent>(_ =>
                {
                    _timelineFlight = record;
                    ShowSection(Section.Timeline);
                });
                var name = Text((flight.VesselName ?? Loc.T("Flight/Unidentified")).ToUpper(Loc.Culture), 12, InkColor, true);
                name.style.marginTop = 6;
                name.style.unityTextAlign = TextAnchor.MiddleCenter;
                name.style.whiteSpace = WhiteSpace.Normal;
                card.Add(name);
                card.Add(Text(FormatDate(flight.UT), 10, Ink2Color));
                string story = FlightStory(flight);
                if (story != null)
                {
                    var storyLabel = Text(story, 10, FateColor(flight.Fate));
                    storyLabel.style.whiteSpace = WhiteSpace.Normal;
                    storyLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
                    storyLabel.style.marginTop = 2;
                    card.Add(storyLabel);
                }
                foreach (var first in flight.Firsts)
                {
                    string where = first.Body == null ? "" : " · " + Loc.Body(first.Body);
                    var firstLabel = Text("★ " + MilestoneTitle(first) + where, 10, new Color(1f, 0.8f, 0.25f), true);
                    firstLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
                    card.Add(firstLabel);
                }
                // Crew lost on the flight are named in red.
                string lostColor = "#" + ColorUtility.ToHtmlStringRGB(LostColor);
                string crewText = flight.Crew.Count > 0
                    ? string.Join(", ", flight.Crew.Select(n => flight.LostCrew.Contains(n) ? $"<color={lostColor}>{n}</color>" : n))
                    : Loc.T(flight.VesselName == null ? "Flight/CrewUnknown" : "Flight/Uncrewed");
                var who = Text(crewText, 10, Ink3Color);
                who.enableRichText = true;
                who.style.whiteSpace = WhiteSpace.Normal;
                who.style.unityTextAlign = TextAnchor.MiddleCenter;
                card.Add(who);
                wall.Add(card);
            }
            if (view.Count > 0) _sectionHost.Add(wall);
            else _sectionHost.Add(Empty(Loc.T(exhibit.Flights.Count == 0 ? "Empty/Missions" : "Empty/NoMatch")));
        }

        /// <summary>
        /// What a patch's caption leaves out: how long the flight lasted and when it ended, and the craft's size at
        /// launch. Empty for a flight the mod didn't follow.
        /// </summary>
        private string PatchTip(ExhibitEntry flight)
        {
            var record = flight.Record;
            if (record == null || !record.IsFollowed()) return null;
            var lines = new List<string>();
            double duration = Duration(record);
            double? end = EraExhibit.EndOf(record);
            string dates = flight.Fate == FlightFate.InFlight ? Range(record.UT, null)
                : end.HasValue ? Range(record.UT, end.Value) : FormatDate(record.UT);
            lines.Add(duration > 0 ? $"{dates} · {FormatSpan(duration)}" : dates);
            if (record.Parts > 0)
                lines.Add($"{Count("Placard/Parts", record.Parts, record.Parts.ToString(Loc.Culture))} · " +
                    Loc.F("Spec/Tonnes", record.Mass.ToString(record.Mass < 10 ? "N2" : "N1", Loc.Culture)));
            return string.Join("\n", lines);
        }

        /// <summary>"Landed on Mun · Recovered": how far the flight got and how it ended; null for one the mod didn't follow.</summary>
        private static string FlightStory(ExhibitEntry flight)
        {
            var parts = new List<string>();
            string Bodies(FlightReach reach) =>
                string.Join(Loc.T("Format/ListSeparator"), flight.Stops.Where(st => st.Reach == reach).Select(st => Loc.Body(st.Body)));
            if (flight.Stops.Count > 1)
            {
                // A tour: the worlds grouped by how far it got at each, furthest first.
                if (flight.Stops.Any(st => st.Reach == FlightReach.Landed)) parts.Add(Loc.F("Reach/Landed", Bodies(FlightReach.Landed)));
                if (flight.Stops.Any(st => st.Reach == FlightReach.Orbit)) parts.Add(Loc.F("Reach/Orbit", Bodies(FlightReach.Orbit)));
                if (flight.Stops.Any(st => st.Reach == FlightReach.Flyby)) parts.Add(Loc.F("Reach/Flyby", Bodies(FlightReach.Flyby)));
            }
            else
            {
                string body = flight.Destination == null ? null : Loc.Body(flight.Destination);
                switch (flight.Reach)
                {
                    case FlightReach.Suborbital: parts.Add(Loc.T("Reach/Suborbital")); break;
                    case FlightReach.Orbit: parts.Add(Loc.F("Reach/Orbit", body)); break;
                    case FlightReach.Flyby: parts.Add(Loc.F("Reach/Flyby", body)); break;
                    case FlightReach.Landed:
                        var touchdown = flight.Record?.Events.LastOrDefault(e => IsTouchdown(e) && e.Body == flight.Destination);
                        parts.Add(Loc.F(touchdown?.Kind == FlightEventKind.Splashed ? "Reach/Splashed" : "Reach/Landed", body));
                        break;
                }
            }
            if (flight.Docked) parts.Add(Loc.T("Reach/Docked"));
            if (flight.Fate != FlightFate.Unknown) parts.Add(Loc.T("Fate/" + flight.Fate));
            return parts.Count == 0 ? null : string.Join(" · ", parts);
        }

        private static Color FateColor(FlightFate fate)
        {
            switch (fate)
            {
                case FlightFate.Recovered: return GoodColor;
                case FlightFate.InFlight: return SignalColor;
                case FlightFate.Destroyed:
                case FlightFate.CrewLost: return LostColor;
                default: return Ink2Color;
            }
        }

        // ─── Crew roll ───

        // ─── Sorting and filtering chips, shared by Missions and Crew ───

        /// <summary>A labelled row of small toggle chips; the chosen one is lit, and choosing another rebuilds the section.</summary>
        private VisualElement Chips<T>(string labelKey, IEnumerable<(T Value, string Text)> options, T chosen, Action<T> choose)
        {
            var bar = Row();
            bar.style.alignItems = Align.Center;
            bar.style.marginRight = 18;
            bar.style.marginBottom = 4;
            var label = Text(Loc.T(labelKey), 9, Ink3Color, true);
            label.style.marginRight = 6;
            bar.Add(label);
            foreach (var option in options)
            {
                bool on = EqualityComparer<T>.Default.Equals(option.Value, chosen);
                var value = option.Value;
                var chip = new Button(() => { choose(value); ShowSection(_section); }) { text = option.Text };
                chip.style.height = 22;
                chip.style.fontSize = 10;
                chip.style.marginLeft = 0;
                chip.style.marginRight = 4;
                SetPadding(chip, 0, 8);
                SetRadius(chip, 11);
                chip.style.backgroundColor = on ? Panel2Color : Color.clear;
                SetBorder(chip, on ? Color.Lerp(_exhibit.Era.Base, Color.white, 0.15f) : LineColor, 1);
                chip.style.color = on ? InkColor : Ink3Color;
                bar.Add(chip);
            }
            return bar;
        }

        private static VisualElement Toolbar(params VisualElement[] bars)
        {
            var toolbar = WrapRow();
            toolbar.style.marginBottom = 10;
            foreach (var bar in bars) toolbar.Add(bar);
            return toolbar;
        }

        // ─── Missions: sort and filter ───

        private enum MissionSort { Oldest, Newest, Longest, Furthest }
        private enum MissionFilter { All, Crewed, Uncrewed, InFlight, Recovered, Lost }
        private MissionSort _missionSort = MissionSort.Oldest;
        private MissionFilter _missionFilter = MissionFilter.All;

        private static bool Matches(ExhibitEntry flight, MissionFilter filter)
        {
            switch (filter)
            {
                case MissionFilter.Crewed: return flight.CrewCount > 0;
                case MissionFilter.Uncrewed: return flight.CrewCount == 0;
                case MissionFilter.InFlight: return flight.Fate == FlightFate.InFlight;
                case MissionFilter.Recovered: return flight.Fate == FlightFate.Recovered;
                case MissionFilter.Lost: return flight.Fate == FlightFate.Destroyed || flight.Fate == FlightFate.CrewLost;
                default: return true;
            }
        }

        /// <summary>How long a flight lasted, or has lasted so far; 0 if the mod didn't follow it.</summary>
        private double Duration(FlightRecord flight)
        {
            if (flight == null || !flight.IsFollowed()) return 0;
            double end = EraExhibit.EndOf(flight) ?? (EraExhibit.FateOf(flight) == FlightFate.InFlight ? _plugin.UniverseTime : flight.UT);
            return Math.Max(0, end - flight.UT);
        }

        /// <summary>How far a flight got, for sorting: other worlds beat home, landings beat orbits beat flybys.</summary>
        private static int Distance(ExhibitEntry flight) =>
            flight.Stops.Count * 10 + flight.Stops.Select(st => EraExhibit.Rank(st.Reach)).DefaultIfEmpty(0).Max()
            + (flight.Reach == FlightReach.Orbit ? 1 : 0);

        private List<ExhibitEntry> MissionView(EraExhibit exhibit)
        {
            var flights = exhibit.Flights.Where(f => Matches(f, _missionFilter));
            switch (_missionSort)
            {
                case MissionSort.Newest: return flights.OrderByDescending(f => f.UT).ToList();
                case MissionSort.Longest: return flights.OrderByDescending(f => Duration(f.Record)).ThenBy(f => f.UT).ToList();
                case MissionSort.Furthest: return flights.OrderByDescending(Distance).ThenBy(f => f.UT).ToList();
                default: return flights.OrderBy(f => f.UT).ToList();
            }
        }

        private VisualElement MissionToolbar(EraExhibit exhibit)
        {
            var sorts = new[]
            {
                (MissionSort.Oldest, Loc.T("Sort/Oldest")), (MissionSort.Newest, Loc.T("Sort/Newest")),
                (MissionSort.Longest, Loc.T("Sort/Longest")), (MissionSort.Furthest, Loc.T("Sort/Furthest")),
            };
            // Filters show their counts, and only the ones with something in them.
            var filters = new List<(MissionFilter, string)>();
            foreach (MissionFilter filter in Enum.GetValues(typeof(MissionFilter)))
            {
                int n = exhibit.Flights.Count(f => Matches(f, filter));
                if (filter != MissionFilter.All && n == 0 && filter != _missionFilter) continue;
                string name = filter == MissionFilter.InFlight ? Loc.T("Fate/InFlight")
                    : filter == MissionFilter.Recovered ? Loc.T("Fate/Recovered")
                    : Loc.T("Filter/" + filter);
                filters.Add((filter, $"{name} {n.ToString(Loc.Culture)}"));
            }
            return Toolbar(
                Chips("Sort/Label", sorts, _missionSort, v => _missionSort = v),
                Chips("Filter/Label", filters, _missionFilter, v => _missionFilter = v));
        }

        // ─── Crew: service records ───

        private enum CrewSort { Flights, TimeAloft, Name, Joined }
        private enum CrewFilter { All, Flew, Lost }
        private CrewSort _crewSort = CrewSort.Flights;
        private CrewFilter _crewFilter = CrewFilter.All;
        /// <summary>The kerbal whose record is open at the top of the roll, or null.</summary>
        private string _crewOpen;

        /// <summary>A kerbal's service from a set of flights: the shown era's, or their whole career.</summary>
        private class ServiceRecord
        {
            public CrewMember Member;
            public List<FlightRecord> Flights = new List<FlightRecord>();
            public double TimeAloft;
            public List<FlightStop> Worlds = new List<FlightStop>();
            /// <summary>This era's firsts they flew on.</summary>
            public List<ExhibitEntry> Firsts = new List<ExhibitEntry>();
            public bool Lost => Member.Lost != null && !Member.Returned;
        }

        private ServiceRecord RecordOf(CrewMember member) =>
            RecordOf(member, _exhibit.Flights.Select(f => f.Record).Where(f => f != null));

        private ServiceRecord RecordOf(CrewMember member, IEnumerable<FlightRecord> flights)
        {
            var record = new ServiceRecord { Member = member };
            string home = _plugin.HomeWorldName;
            foreach (var flight in flights.Where(f => f.Crew.Contains(member.Name)).OrderBy(f => f.UT))
            {
                record.Flights.Add(flight);
                double aloft = Duration(flight);
                // The lost were only aboard until they were lost.
                if (flight.LostCrew.Contains(member.Name))
                {
                    var loss = _plugin.Data?.Losses.Find(l => l.Name == member.Name && l.UT >= flight.UT);
                    if (loss != null) aloft = Math.Max(0, loss.UT - flight.UT);
                }
                record.TimeAloft += aloft;
                foreach (var stop in EraExhibit.StopsOf(flight, home, true))
                {
                    var known = record.Worlds.Find(w => w.Body == stop.Body);
                    if (known == null) record.Worlds.Add(new FlightStop { Body = stop.Body, Reach = stop.Reach });
                    else if (EraExhibit.Rank(stop.Reach) > EraExhibit.Rank(known.Reach)) known.Reach = stop.Reach;
                }
            }
            record.Firsts = _exhibit.Flights.Where(f => f.Record != null && record.Flights.Contains(f.Record)).SelectMany(f => f.Firsts).ToList();
            return record;
        }

        private void BuildCrew()
        {
            var exhibit = _exhibit;
            if (exhibit.Crew.Count == 0)
            {
                _sectionHost.Add(Empty(Loc.T("Empty/Crew")));
                return;
            }

            var records = exhibit.Crew.Select(RecordOf).ToList();
            var sorts = new[]
            {
                (CrewSort.Flights, Loc.T("Sort/Flights")), (CrewSort.TimeAloft, Loc.T("Sort/TimeAloft")),
                (CrewSort.Name, Loc.T("Sort/Name")), (CrewSort.Joined, Loc.T("Sort/Joined")),
            };
            var filters = new List<(CrewFilter, string)> { (CrewFilter.All, $"{Loc.T("Filter/All")} {records.Count.ToString(Loc.Culture)}") };
            int flew = records.Count(r => r.Flights.Count > 0), lost = records.Count(r => r.Lost);
            if (flew > 0) filters.Add((CrewFilter.Flew, $"{Loc.T("Filter/Flew")} {flew.ToString(Loc.Culture)}"));
            if (lost > 0) filters.Add((CrewFilter.Lost, $"{Loc.T("Filter/Lost")} {lost.ToString(Loc.Culture)}"));
            _sectionHost.Add(Toolbar(
                Chips("Sort/Label", sorts, _crewSort, v => _crewSort = v),
                Chips("Filter/Label", filters, _crewFilter, v => _crewFilter = v)));

            var open = records.Find(r => r.Member.Name == _crewOpen);
            if (open != null) _sectionHost.Add(RecordPanel(open));
            else _crewOpen = null;

            IEnumerable<ServiceRecord> view = records.Where(r =>
                _crewFilter == CrewFilter.All || (_crewFilter == CrewFilter.Flew ? r.Flights.Count > 0 : r.Lost));
            switch (_crewSort)
            {
                case CrewSort.TimeAloft: view = view.OrderByDescending(r => r.TimeAloft); break;
                case CrewSort.Name: view = view.OrderBy(r => EraExhibit.ShortName(r.Member.Name), StringComparer.CurrentCulture); break;
                case CrewSort.Joined: view = view.OrderBy(r => r.Member.Joined ?? double.MaxValue); break;
                default: view = view.OrderByDescending(r => r.Flights.Count).ThenByDescending(r => r.TimeAloft); break;
            }

            var wall = WrapRow();
            foreach (var record in view) wall.Add(CrewCard(record));
            _sectionHost.Add(wall);
        }

        private VisualElement CrewCard(ServiceRecord record)
        {
            var member = record.Member;
            bool open = member.Name == _crewOpen;
            var card = Row();
            card.style.width = 248;
            card.style.marginRight = 8;
            card.style.marginBottom = 8;
            card.style.backgroundColor = Panel2Color;
            SetBorder(card, open ? Color.Lerp(_exhibit.Era.Base, Color.white, 0.15f) : LineColor, 1);
            SetRadius(card, 4);
            SetPadding(card, 8, 10);
            card.RegisterCallback<ClickEvent>(_ =>
            {
                _crewOpen = open ? null : member.Name;
                ShowSection(Section.Crew);
            });
            card.Add(Portrait(member));

            var text = new VisualElement();
            text.style.marginLeft = 10;
            text.style.flexShrink = 1;
            text.style.minWidth = 0;
            text.Add(Fit(Text(EraExhibit.ShortName(member.Name), 13, InkColor, true), 160, 9));
            if (member.Returned) text.Add(Fit(Text(Loc.T("Role/Returned"), 9, SignalColor, true), 160, 7));
            else if (record.Lost)
            {
                var status = Fit(Text($"{Loc.T("Role/Lost")} · {FormatDate(member.Lost.UT)}", 9, LostColor, true), 160, 7);
                var loss = member.Lost;
                Tip(status, () => string.IsNullOrEmpty(loss.VesselName)
                    ? Loc.F("Crew/LostOn", FormatDate(loss.UT))
                    : Loc.F("Crew/LostAboard", loss.VesselName, FormatDate(loss.UT)));
                text.Add(status);
            }
            if (member.Joined.HasValue) text.Add(Fit(Text(Loc.F("Crew/Joined", FormatDate(member.Joined.Value)), 9, Ink3Color), 160, 7));

            if (record.Flights.Count > 0)
            {
                string flights = Count("Crew/Flights", record.Flights.Count, record.Flights.Count.ToString(Loc.Culture));
                string aloft = record.TimeAloft > 0 ? " · " + Loc.F("Crew/Aloft", FormatSpan(record.TimeAloft)) : "";
                var line = Fit(Text(flights + aloft, 10, GoodColor), 160, 8);
                line.style.marginTop = 4;
                text.Add(line);
            }
            if (record.Worlds.Count > 0 || record.Firsts.Count > 0 || member.Recruited)
            {
                var strip = Row();
                strip.style.alignItems = Align.Center;
                strip.style.marginTop = 4;
                if (member.Recruited)
                {
                    var badge = RecruitBadge();
                    double? joined = member.Joined;
                    Tip(badge, () => joined.HasValue ? Loc.F("Tip/Recruited", FormatDate(joined.Value)) : Loc.T("Tip/RecruitedEra"));
                    strip.Add(badge);
                }
                foreach (var world in record.Worlds.Take(9))
                {
                    var dot = WorldDot(world);
                    var reached = world;
                    Tip(dot, () => Loc.F(reached.Reach == FlightReach.Landed ? "Reach/Landed"
                        : reached.Reach == FlightReach.Flyby ? "Reach/Flyby" : "Reach/Orbit", Loc.Body(reached.Body)));
                    strip.Add(dot);
                }
                if (record.Firsts.Count > 0)
                {
                    var stars = Text("★ " + record.Firsts.Count.ToString(Loc.Culture), 10, new Color(1f, 0.8f, 0.25f), true);
                    stars.style.marginLeft = 4;
                    var firsts = record.Firsts;
                    Tip(stars, () => string.Join("\n", firsts.Select(m =>
                        m.Body == null ? MilestoneTitle(m) : $"{MilestoneTitle(m)} · {Loc.Body(m.Body)}")));
                    strip.Add(stars);
                }
                text.Add(strip);
            }
            foreach (var ribbon in member.Ribbons.Take(2))
            {
                var element = Ribbon(ribbon);
                element.style.marginTop = 4;
                text.Add(element);
            }
            card.Add(text);
            return card;
        }

        // ─── Hover tips ───
        // UI Toolkit's own tooltip property shows nothing in the game's panels, so the window draws its own: one label,
        // floated beside whatever the pointer is over.

        private Label _tip;

        /// <summary>Shows text beside an element while the pointer is over it. The text is read when shown, so it follows the language.</summary>
        private void Tip(VisualElement target, Func<string> text)
        {
            target.pickingMode = PickingMode.Position;
            target.RegisterCallback<PointerEnterEvent>(_ => ShowTip(target, text()));
            target.RegisterCallback<PointerLeaveEvent>(_ => HideTip());
            target.RegisterCallback<DetachFromPanelEvent>(_ => HideTip());
        }

        private void ShowTip(VisualElement target, string text) => ShowTipAt(target.worldBound, text);

        /// <summary>Shows text beside a spot in the window (in world coordinates).</summary>
        private void ShowTipAt(Rect worldBounds, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (_tip == null)
            {
                _tip = Text("", 11, InkColor);
                _tip.pickingMode = PickingMode.Ignore;
                _tip.style.position = Position.Absolute;
                _tip.style.maxWidth = 260;
                _tip.style.whiteSpace = WhiteSpace.Normal;
                _tip.style.backgroundColor = new Color(0.04f, 0.05f, 0.08f, 0.96f);
                SetBorder(_tip, LineColor, 1);
                SetRadius(_tip, 3);
                SetPadding(_tip, 4, 8);
            }
            if (_tip.parent != _root) _root.Add(_tip);
            _tip.text = text;
            _tip.style.display = DisplayStyle.Flex;
            _tip.BringToFront();
            // Below the element, kept inside the window; above it if there's no room below.
            var bounds = _root.WorldToLocal(worldBounds);
            float width = _root.layout.width, height = _root.layout.height;
            _tip.style.left = Mathf.Clamp(bounds.x, 4, Math.Max(4, width - 268));
            _tip.style.top = bounds.yMax + 6 < height - 40 ? bounds.yMax + 6 : bounds.y - 34;
        }

        private void HideTip()
        {
            if (_tip != null) _tip.style.display = DisplayStyle.None;
        }

        /// <summary>A small grey "R": the kerbal joined during this era.</summary>
        private static VisualElement RecruitBadge()
        {
            var badge = new VisualElement();
            badge.style.width = badge.style.height = 15;
            badge.style.marginRight = 4;
            badge.style.alignItems = Align.Center;
            badge.style.justifyContent = Justify.Center;
            SetRadius(badge, 8);
            badge.style.backgroundColor = new Color(0.32f, 0.35f, 0.42f);
            var letter = Text(Loc.T("Crew/RecruitBadge"), 9, InkColor, true);
            letter.style.unityTextAlign = TextAnchor.MiddleCenter;
            badge.Add(letter);
            return badge;
        }

        /// <summary>A world on a service record: a ring for a flyby, a ringed globe for an orbit, a flag for a landing.</summary>
        private static VisualElement WorldDot(FlightStop world)
        {
            var dot = new Drawn((p, r) =>
            {
                var c = r.center + new Vector2(0, 1);
                var color = HeritageGraphics.BodyColor(world.Body);
                if (world.Reach == FlightReach.Flyby) HeritageGraphics.Ring(p, c, 4.5f, 1.6f, color);
                else HeritageGraphics.Circle(p, c, 5, color);
                if (world.Reach == FlightReach.Orbit) HeritageGraphics.Ring(p, c, 7, 1, new Color(1, 1, 1, 0.6f));
                if (world.Reach == FlightReach.Landed)
                {
                    HeritageGraphics.Line(p, c + new Vector2(0, -4), c + new Vector2(0, -10), Color.white, 1.2f);
                    HeritageGraphics.Polygon(p, Color.white, c + new Vector2(0, -10), c + new Vector2(5, -8.5f), c + new Vector2(0, -7));
                }
            }, 18, 22);
            dot.style.marginRight = 2;
            return dot;
        }

        /// <summary>
        /// A kerbal's record, open above the roll: the flights they were on this era, each opening its story, and
        /// their whole career beside them.
        /// </summary>
        private VisualElement RecordPanel(ServiceRecord record)
        {
            var member = record.Member;
            var box = new VisualElement();
            box.style.marginBottom = 10;
            box.style.flexDirection = FlexDirection.Row;
            box.style.alignItems = Align.FlexStart;
            box.style.backgroundColor = Panel2Color;
            SetBorder(box, LineColor, 1);
            SetRadius(box, 4);
            SetPadding(box, 8, 12);

            var era = new VisualElement();
            era.style.flexGrow = 1;
            era.style.flexShrink = 1;
            var heading = Row();
            heading.Add(Text(EraExhibit.ShortName(member.Name).ToUpper(Loc.Culture), 12, InkColor, true));
            heading.Add(Text("   " + _exhibit.Era.Name, 11, Ink3Color));
            era.Add(heading);

            var list = WrapRow();
            list.style.marginTop = 6;
            string home = _plugin.HomeWorldName;
            foreach (var flight in record.Flights)
            {
                var entry = new ExhibitEntry { Record = flight, VesselName = flight.VesselName, Fate = EraExhibit.FateOf(flight), Stops = EraExhibit.StopsOf(flight, home, false) };
                entry.Reach = entry.Stops.Count > 0 ? entry.Stops.OrderByDescending(st => EraExhibit.Rank(st.Reach)).First().Reach
                    : flight.Has(FlightEventKind.Orbit) ? FlightReach.Orbit : flight.Events.Count > 0 ? FlightReach.Suborbital : FlightReach.Unknown;
                entry.Destination = entry.Stops.LastOrDefault()?.Body ?? home;
                var chip = Column();
                chip.style.marginRight = 18;
                chip.style.marginBottom = 6;
                chip.Add(Text(flight.VesselName ?? Loc.T("Flight/Unidentified"), 11, InkColor, true));
                double? end = EraExhibit.EndOf(flight);
                string dates = entry.Fate == FlightFate.InFlight ? Range(flight.UT, null)
                    : end.HasValue && FormatDate(end.Value) != FormatDate(flight.UT) ? Range(flight.UT, end.Value) : FormatDate(flight.UT);
                chip.Add(Text(dates, 10, Ink3Color));
                string story = FlightStory(entry);
                // A safe return is the usual ending; only losses and flights still out stand out.
                if (story != null) chip.Add(Text(story, 10, entry.Fate == FlightFate.Recovered ? Ink2Color : FateColor(entry.Fate)));
                var record2 = flight;
                chip.RegisterCallback<ClickEvent>(_ => OpenFlight(record2));
                list.Add(chip);
            }
            era.Add(list);
            box.Add(era);
            box.Add(CareerColumn(member));

            var close = ZoomButton("×");
            close.style.marginLeft = 10;
            close.style.flexShrink = 0;
            close.clicked += () => { _crewOpen = null; ShowSection(Section.Crew); };
            box.Add(close);
            return box;
        }

        /// <summary>Everything the kerbal has done for the agency, across every era.</summary>
        private VisualElement CareerColumn(CrewMember member)
        {
            var data = _plugin.Data;
            var career = RecordOf(member, data?.Flights ?? new List<FlightRecord>());
            var column = new VisualElement();
            column.style.width = 250;
            column.style.flexShrink = 0;
            column.style.paddingLeft = 12;
            column.style.borderLeftWidth = 1;
            column.style.borderLeftColor = LineColor;

            var caption = Text(Loc.T("Crew/Career"), 10, Ink3Color, true);
            caption.style.letterSpacing = 1.5f;
            caption.style.marginBottom = 4;
            column.Add(caption);
            Label Line(string text, Color color)
            {
                var line = Text(text, 11, color);
                line.style.whiteSpace = WhiteSpace.Normal;
                line.style.marginBottom = 2;
                column.Add(line);
                return line;
            }

            if (member.Joined.HasValue) Line(Loc.F("Crew/Joined", FormatDate(member.Joined.Value)), Ink2Color);
            var totals = new List<string>();
            if (career.Flights.Count > 0) totals.Add(Count("Crew/Flights", career.Flights.Count, career.Flights.Count.ToString(Loc.Culture)));
            if (career.TimeAloft > 0) totals.Add(Loc.F("Crew/Aloft", FormatSpan(career.TimeAloft)));
            if (totals.Count > 0) Line(string.Join(" · ", totals), InkColor);
            if (career.Worlds.Count > 0)
                Line(string.Join(Loc.T("Format/ListSeparator"), career.Worlds.Select(w => Loc.Body(w.Body))), Ink2Color);

            // Flights per era, once there's more than one era to split them between.
            if (data != null && data.Eras.Count > 1 && career.Flights.Count > 0)
            {
                foreach (var brand in data.Eras)
                {
                    int flown = career.Flights.Count(f => f.UT >= brand.StartUT && f.UT < data.EndUT(brand));
                    if (flown == 0) continue;
                    var color = brand == _exhibit.Era ? InkColor : Ink3Color;
                    var row = Row();
                    row.style.marginTop = 2;
                    var name = Fit(Text(brand.Name, 10, color), 200, 8);
                    name.style.flexShrink = 1;
                    row.Add(name);
                    var count = Text(flown.ToString(Loc.Culture), 10, color, true);
                    count.style.marginLeft = StyleKeyword.Auto;
                    row.Add(count);
                    column.Add(row);
                }
            }

            foreach (var loss in data?.Losses.Where(l => l?.Name == member.Name) ?? Enumerable.Empty<LossRecord>())
            {
                var line = Line(string.IsNullOrEmpty(loss.VesselName)
                    ? Loc.F("Crew/LostOn", FormatDate(loss.UT))
                    : Loc.F("Crew/LostAboard", loss.VesselName, FormatDate(loss.UT)), LostColor);
                line.style.marginTop = 4;
            }
            return column;
        }

        /// <summary>"4d 3h", "2h 10m" or "45m", in the game's six-hour days.</summary>
        private static string FormatSpan(double seconds)
        {
            int minutes = (int)(seconds / 60);
            int days = (int)(seconds / SecondsPerDay), hours = (int)(seconds % SecondsPerDay / 3600);
            if (days > 0) return Loc.F("Crew/Days", days, hours);
            if (minutes >= 60) return Loc.F("Crew/Hours", minutes / 60, minutes % 60);
            return Loc.F("Crew/Minutes", Math.Max(1, minutes));
        }

        /// <summary>
        /// The kerbal's portrait: the copy saved when they were lost, the game's own, or a drawn stand-in when the
        /// game hasn't made one. The lost are shown dimmed, in a red frame.
        /// </summary>
        private VisualElement Portrait(CrewMember member)
        {
            bool lost = member.Lost != null && !member.Returned;
            var frame = new VisualElement();
            frame.style.width = frame.style.height = 58;
            SetRadius(frame, 29);
            frame.style.overflow = Overflow.Hidden;
            var ring = lost ? LostColor : member.Role == CrewRole.Flew ? GoodColor : new Color(0.9f, 0.92f, 0.96f);
            SetBorder(frame, ring, 2);
            frame.style.backgroundColor = new Color(0.11f, 0.14f, 0.2f);
            if (lost) frame.style.opacity = 0.6f;

            var portrait = member.Portrait;
            // The roster has no portrait of the lost, in their final era or the ones before it.
            string file = lost ? member.Lost.PortraitFile : portrait == null ? member.SavedPortrait : null;
            var saved = file != null && _plugin.PortraitFolder != null
                ? LoadThumbnail(Path.Combine(_plugin.PortraitFolder, file))
                : null;
            if (saved != null)
            {
                frame.style.backgroundImage = new StyleBackground(saved);
                frame.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Cover);
            }
            else if (portrait != null && portrait.texture != null)
            {
                frame.style.backgroundImage = new StyleBackground(portrait);
                frame.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Cover);
            }
            else
            {
                var head = new Drawn((p, r) => HeritageGraphics.KerbalHead(p, r, member.Name));
                Fill(head);
                frame.Add(head);
            }
            return frame;
        }

        /// <summary>A Wayfarer's Wings ribbon, built from its image layers like that mod does.</summary>
        private VisualElement Ribbon(WingAward award)
        {
            var ribbon = new VisualElement { tooltip = award.WingName };
            ribbon.style.width = 90;
            ribbon.style.height = 24;
            foreach (var key in award.ImageLayers)
            {
                var layer = new VisualElement();
                Fill(layer);
                ShowAsset(layer, key);
                ribbon.Add(layer);
            }
            if (award.IsFirst)
            {
                var first = Text(Loc.T("Ribbon/First"), 8, new Color(1f, 0.85f, 0.4f), true);
                first.style.position = Position.Absolute;
                first.style.right = -2;
                first.style.top = -8;
                ribbon.Add(first);
            }
            return ribbon;
        }

        // ─── Collection: blueprints framed on a wall ───

        private void BuildCollection()
        {
            var cards = _exhibit.Blueprints;
            if (cards.Count == 0)
            {
                _sectionHost.Add(Empty(Loc.T("Empty/Collection")));
                return;
            }
            foreach (var card in cards) _sectionHost.Add(BlueprintExhibit(card));
        }

        /// <summary>One blueprint: the craft in flight on the left, its spec sheet on the right.</summary>
        private VisualElement BlueprintExhibit(BlueprintCard card)
        {
            var exhibit = Row();
            exhibit.style.marginBottom = 12;
            exhibit.style.backgroundColor = WallColor;
            SetBorder(exhibit, LineColor, 1);
            SetRadius(exhibit, 4);
            exhibit.style.overflow = Overflow.Hidden;
            exhibit.Add(CraftScene(card));

            var sheet = new VisualElement();
            sheet.style.flexGrow = 1;
            sheet.style.flexShrink = 1;
            sheet.style.minWidth = 0;
            SetPadding(sheet, 14, 18);
            var title = Text(string.IsNullOrEmpty(card.VehicleName) ? card.Name : card.VehicleName, 18, InkColor, true);
            title.style.whiteSpace = WhiteSpace.Normal;
            title.style.marginRight = 250;   // clear of the share buttons in the corner
            sheet.Add(title);
            sheet.Add(Text(card.SaveTime.ToString("d", Loc.Culture), 10, Ink3Color));
            if (card.Description != null)
            {
                var blurb = Text(card.Description, 11, Ink2Color);
                blurb.style.whiteSpace = WhiteSpace.Normal;
                blurb.style.unityFontStyleAndWeight = FontStyle.Italic;
                blurb.style.marginTop = 6;
                sheet.Add(blurb);
            }

            var specs = new VisualElement();
            specs.style.marginTop = 10;
            void Spec(string key, string value)
            {
                if (string.IsNullOrEmpty(value)) return;
                var row = Row();
                row.style.marginBottom = 3;
                var label = Text(Loc.T(key), 9, Ink3Color, true);
                label.style.width = 96;
                label.style.flexShrink = 0;
                label.style.paddingTop = 2;
                row.Add(label);
                var text = Text(value, 12, InkColor);
                text.style.whiteSpace = WhiteSpace.Normal;
                text.style.flexShrink = 1;
                row.Add(text);
                specs.Add(row);
            }
            string N(double v, string format = "N0") => v.ToString(format, Loc.Culture);

            if (card.DeltaVVac > 0)
                Spec("Spec/DeltaV", Loc.F("Spec/DeltaVValue", N(card.DeltaVVac), N(card.DeltaVAsl)));
            if (card.LaunchTwr > 0) Spec("Spec/Twr", N(card.LaunchTwr, "N2"));
            if (card.BurnTime > 0) Spec("Spec/BurnTime", FormatDuration(card.BurnTime));
            if (card.Mass > 0) Spec("Spec/Mass", Loc.F("Spec/Tonnes", N(card.Mass, card.Mass < 10 ? "N2" : "N1")));
            if (card.Height > 0) Spec("Spec/Size", Loc.F("Spec/SizeValue", N(card.Width, "N1"), N(card.Height, "N1")));
            var build = new List<string> { Count("Placard/Parts", card.Parts, N(card.Parts)) };
            if (card.Stages > 0) build.Add(Count("Spec/Stages", card.Stages, N(card.Stages)));
            Spec("Spec/Build", string.Join(" · ", build));
            Spec("Spec/Crew", card.CrewSeats > 0 ? Count("Spec/Seats", card.CrewSeats, N(card.CrewSeats)) : Loc.T("Flight/Uncrewed"));
            Spec("Spec/Engines", string.Join(Loc.T("Format/ListSeparator"), card.Engines.Select(e =>
            {
                string name = PartTitle(e.Name);
                return e.Amount > 1 ? Loc.F("Spec/Times", N(e.Amount), name) : name;
            })));
            Spec("Spec/Propellant", string.Join(" · ", card.Propellant.Select(p =>
                $"{ResourceName(p.Name)} {Loc.F("Spec/Tonnes", N(p.Amount, p.Amount < 10 ? "N1" : "N0"))}")));
            var power = new List<string>();
            if (card.Battery > 0) power.Add(Loc.F("Spec/Battery", N(card.Battery)));
            if (card.SolarPanels > 0) power.Add(Count("Spec/Solar", card.SolarPanels, N(card.SolarPanels)));
            if (card.Generators > 0) power.Add(Count("Spec/Generators", card.Generators, N(card.Generators)));
            if (card.Alternators > 0) power.Add(Count("Spec/Alternators", card.Alternators, N(card.Alternators)));
            Spec("Spec/Power", string.Join(" · ", power));
            var systems = new List<string>();
            if (card.Parachutes > 0) systems.Add(Count("Spec/Parachutes", card.Parachutes, N(card.Parachutes)));
            if (card.Experiments > 0) systems.Add(Count("Spec/Experiments", card.Experiments, N(card.Experiments)));
            if (card.Antennas > 0) systems.Add(Count("Spec/Antennas", card.Antennas, N(card.Antennas)));
            if (card.RcsThrusters > 0) systems.Add(Count("Spec/Rcs", card.RcsThrusters, N(card.RcsThrusters)));
            Spec("Spec/Systems", string.Join(" · ", systems));

            // Every flight launched from this blueprint, in any era; each opens its story on the timeline.
            var flights = FlightsOf(card);
            if (flights.Count > 0)
            {
                var row = Row();
                var label = Text(Loc.T("Spec/Flights"), 9, Ink3Color, true);
                label.style.width = 96;
                label.style.flexShrink = 0;
                label.style.paddingTop = 2;
                row.Add(label);
                var list = WrapRow();
                list.style.flexShrink = 1;
                foreach (var flight in flights)
                {
                    var fate = EraExhibit.FateOf(flight);
                    var chip = Text(flight.VesselName, 12, fate == FlightFate.Unknown ? InkColor : FateColor(fate));
                    chip.style.marginRight = 12;
                    chip.style.borderBottomWidth = 1;
                    chip.style.borderBottomColor = LineColor;
                    var record = flight;
                    chip.RegisterCallback<ClickEvent>(_ => OpenFlight(record));
                    list.Add(chip);
                }
                row.Add(list);
                specs.Add(row);
            }
            sheet.Add(specs);
            exhibit.Add(sheet);

            // Sharing, in the corner: the craft itself for anyone's VAB, the spec sheet, or both to disk.
            var actions = Row();
            actions.style.position = Position.Absolute;
            actions.style.top = 10;
            actions.style.right = 12;
            if (card.WorkspacePath != null)
                actions.Add(ShareButton("Button/CopyCraft", "Button/Copied", () => CopyCraft(card)));
            actions.Add(ShareButton("Button/CopySpecs", "Button/Copied", () =>
            {
                GUIUtility.systemCopyBuffer = SpecJson(card).ToString(Newtonsoft.Json.Formatting.Indented);
                return true;
            }));
            actions.Add(ShareButton("Button/Export", "Button/Exported", () => ExportBlueprint(card)));
            exhibit.Add(actions);
            return exhibit;
        }

        /// <summary>The flights launched from a blueprint, oldest first.</summary>
        private List<FlightRecord> FlightsOf(BlueprintCard card)
        {
            var data = _plugin.Data;
            string vehicle = string.IsNullOrEmpty(card.VehicleName) ? card.Name : card.VehicleName;
            if (data == null) return new List<FlightRecord>();
            return data.Flights.Where(f => EraExhibit.IsFlightOf(f.VesselName, vehicle)).OrderBy(f => f.UT).ToList();
        }

        /// <summary>Opens a flight's story on the timeline of the era it launched in.</summary>
        private void OpenFlight(FlightRecord flight)
        {
            var data = _plugin.Data;
            var era = data?.Eras.LastOrDefault(e => e.StartUT <= flight.UT) ?? data?.CurrentEra();
            if (era == null) return;
            _timelineFlight = flight;
            _section = Section.Timeline;
            ShowMuseum(era.Id);
        }

        /// <summary>A small button that briefly says it worked (or shows nothing new if it didn't).</summary>
        private static Button ShareButton(string key, string doneKey, Func<bool> act)
        {
            var button = new Button { text = Loc.T(key) };
            button.style.height = 22;
            button.style.fontSize = 10;
            button.style.marginLeft = 6;
            button.style.marginRight = 0;
            SetPadding(button, 0, 8);
            button.clicked += () =>
            {
                if (!act()) return;
                button.text = Loc.T(doneKey);
                button.schedule.Execute(() => button.text = Loc.T(key)).ExecuteLater(1800);
            };
            return button;
        }

        /// <summary>
        /// Puts the blueprint's craft on the clipboard in the form the VAB's own copy uses, so it can be pasted into
        /// any VAB with Ctrl+V, or sent to another player to paste into theirs.
        /// </summary>
        private static bool CopyCraft(BlueprintCard card)
        {
            try
            {
                var assembly = BlueprintArchive.MainAssembly(card.WorkspacePath);
                if (assembly == null) return false;
                GUIUtility.systemCopyBuffer = assembly.ToString(Newtonsoft.Json.Formatting.None);
                return true;
            }
            catch (Exception e)
            {
                SWLog.Warning($"Couldn't copy {card.Name}: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// Writes the blueprint's files (the craft, its thumbnail and details, ready to drop into any campaign's
        /// Workspaces folder) and its spec sheet to a folder, then shows the folder.
        /// </summary>
        private bool ExportBlueprint(BlueprintCard card)
        {
            try
            {
                string vehicle = string.IsNullOrEmpty(card.VehicleName) ? card.Name : card.VehicleName;
                string folder = Path.Combine(Application.persistentDataPath, "AgencyHeritage", "Exports", SafeFileName(vehicle));
                Directory.CreateDirectory(folder);
                if (card.WorkspacePath != null)
                    foreach (string extension in new[] { ".json", ".meta", ".png" })
                    {
                        string source = Path.ChangeExtension(card.WorkspacePath, extension);
                        if (File.Exists(source)) File.Copy(source, Path.Combine(folder, Path.GetFileName(source)), true);
                    }
                else if (card.ThumbnailPath != null)
                    File.Copy(card.ThumbnailPath, Path.Combine(folder, SafeFileName(vehicle) + ".png"), true);
                File.WriteAllText(Path.Combine(folder, "spec.json"), SpecJson(card).ToString(Newtonsoft.Json.Formatting.Indented));
                Application.OpenURL("file:///" + folder.Replace('\\', '/'));
                return true;
            }
            catch (Exception e)
            {
                SWLog.Warning($"Couldn't export {card.Name}: {e.Message}");
                return false;
            }
        }

        private static string SafeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name;
        }

        /// <summary>The spec sheet as plain data, in English keys and SI units, for sharing outside the game.</summary>
        private JObject SpecJson(BlueprintCard card)
        {
            var era = _plugin.Data?.EraAtRealTime(card.SaveTime);
            var spec = new JObject
            {
                ["vehicle"] = string.IsNullOrEmpty(card.VehicleName) ? card.Name : card.VehicleName,
                ["workspace"] = card.Name,
                ["saved"] = card.SaveTime.ToString("s", CultureInfo.InvariantCulture),
                ["agency"] = era?.Name,
                ["description"] = card.Description,
                ["massTonnes"] = Math.Round(card.Mass, 3),
                ["widthMetres"] = Math.Round(card.Width, 2),
                ["heightMetres"] = Math.Round(card.Height, 2),
                ["parts"] = card.Parts,
                ["stages"] = card.Stages,
                ["crewSeats"] = card.CrewSeats,
                ["engines"] = new JArray(card.Engines.Select(e => new JObject
                {
                    ["part"] = e.Name, ["name"] = PartTitle(e.Name), ["count"] = (int)e.Amount,
                })),
                ["propellantTonnes"] = new JObject(card.Propellant.Select(pr => new JProperty(pr.Name, Math.Round(pr.Amount, 3)))),
                ["batteryEC"] = Math.Round(card.Battery, 1),
                ["solarPanels"] = card.SolarPanels,
                ["generators"] = card.Generators,
                ["alternators"] = card.Alternators,
                ["parachutes"] = card.Parachutes,
                ["experiments"] = card.Experiments,
                ["antennas"] = card.Antennas,
                ["rcsThrusters"] = card.RcsThrusters,
            };
            if (card.DeltaVVac > 0)
            {
                spec["deltaVVacuum"] = Math.Round(card.DeltaVVac);
                spec["deltaVSeaLevel"] = Math.Round(card.DeltaVAsl);
                spec["launchTwr"] = Math.Round(card.LaunchTwr, 2);
                spec["burnTimeSeconds"] = Math.Round(card.BurnTime);
            }
            spec["flights"] = new JArray(FlightsOf(card).Select(f => new JObject
            {
                ["name"] = f.VesselName,
                ["launched"] = FormatDate(f.UT),
                ["crew"] = new JArray(f.Crew),
                ["fate"] = EraExhibit.FateOf(f).ToString(),
            }));
            return spec;
        }

        /// <summary>A part's name as the game shows it, e.g. "engine_2v_methalox_mainsail" → "Mainsail".</summary>
        private static string PartTitle(string partName) => Loc.Game("Parts/Title/" + partName, partName);

        /// <summary>A resource's name as the game shows it; some of the game's keys have spaces ("Solid Fuel").</summary>
        private static string ResourceName(string resource) =>
            Loc.Game("Resource/DisplayName/" + resource,
                Loc.Game("Resource/DisplayName/" + EraExhibit.SplitWords(resource), EraExhibit.SplitWords(resource)));

        /// <summary>"4m 12s", or "1h 3m" for long burns.</summary>
        private static string FormatDuration(double seconds)
        {
            var span = TimeSpan.FromSeconds(Math.Max(0, seconds));
            return span.TotalHours >= 1
                ? Loc.F("Spec/HoursMinutes", (int)span.TotalHours, span.Minutes)
                : Loc.F("Spec/MinutesSeconds", span.Minutes, span.Seconds);
        }

        private const float SceneWidth = 220, SceneHeight = 270;

        /// <summary>
        /// The craft in flight: its blueprint picture, cropped to the craft and tilted into a climb, riding its exhaust
        /// over the curve of the home world. The picture keeps the look the blueprint had when it was saved.
        /// </summary>
        private VisualElement CraftScene(BlueprintCard card)
        {
            var scene = new VisualElement();
            scene.style.width = SceneWidth;
            scene.style.height = SceneHeight;
            scene.style.flexShrink = 0;
            scene.style.overflow = Overflow.Hidden;
            uint seed = 2166136261;
            foreach (char ch in card.Name ?? "") seed = (seed ^ ch) * 16777619;
            scene.Add(new Drawn((p, r) => HeritageGraphics.Sky(p, r, HeritageGraphics.BodyColor(_plugin.HomeWorldName), seed), SceneWidth, SceneHeight));

            var texture = LoadThumbnail(card.ThumbnailPath);
            var crop = texture == null ? (RectInt?)null : CraftBounds(texture);
            if (crop == null) return scene;
            var sprite = Sprite.Create(texture, new Rect(crop.Value.x, crop.Value.y, crop.Value.width, crop.Value.height), new Vector2(0.5f, 0.5f));
            _craftSprites.Add(sprite);

            // Rockets climb nose-up, tilted into their gravity turn; anything wider than tall is a plane, flown level.
            float aspect = crop.Value.width / (float)crop.Value.height;
            bool plane = aspect > 1.3f;
            float maxWidth = plane ? 180 : 120, maxHeight = plane ? 110 : 180;
            float height = Mathf.Min(maxHeight, maxWidth / aspect), width = height * aspect;

            var craft = new VisualElement();
            craft.style.position = Position.Absolute;
            craft.style.width = width;
            craft.style.height = height;
            craft.style.left = (SceneWidth - width) / 2 + (plane ? 0 : 8);
            craft.style.top = plane ? (SceneHeight - height) / 2 - 20 : 22;
            craft.style.backgroundImage = new StyleBackground(sprite);
            craft.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
            craft.style.rotate = new UnityEngine.UIElements.Rotate(new Angle(plane ? -6 : 14, AngleUnit.Degree));
            craft.style.overflow = Overflow.Visible;
            if (!plane)
            {
                // The plume hangs from the craft's base and turns with it.
                float plumeWidth = Mathf.Clamp(width * 0.55f, 14, 60), plumeLength = Mathf.Clamp(height * 0.6f, 40, 110);
                var plume = new Drawn((p, r) => HeritageGraphics.Plume(p, r), plumeWidth, plumeLength);
                plume.style.position = Position.Absolute;
                plume.style.left = (width - plumeWidth) / 2;
                plume.style.top = height - 2;
                craft.Add(plume);
            }
            scene.Add(craft);
            return scene;
        }

        private readonly List<Sprite> _craftSprites = new List<Sprite>();
        private readonly Dictionary<Texture2D, RectInt?> _craftBounds = new Dictionary<Texture2D, RectInt?>();

        /// <summary>The part of a blueprint picture the craft fills (its opaque pixels), in texture coordinates.</summary>
        private RectInt? CraftBounds(Texture2D texture)
        {
            if (_craftBounds.TryGetValue(texture, out var known)) return known;
            RectInt? bounds = null;
            try
            {
                var pixels = texture.GetPixels32();
                int w = texture.width, h = texture.height, minX = w, minY = h, maxX = -1, maxY = -1;
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                        if (pixels[y * w + x].a > 16)
                        {
                            if (x < minX) minX = x;
                            if (x > maxX) maxX = x;
                            if (y < minY) minY = y;
                            if (y > maxY) maxY = y;
                        }
                if (maxX >= minX && maxY >= minY) bounds = new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1);
            }
            catch (Exception)
            {
                // Not readable: show the whole picture.
                bounds = new RectInt(0, 0, texture.width, texture.height);
            }
            _craftBounds[texture] = bounds;
            return bounds;
        }

        // ───────────────────────────── Rebrand ─────────────────────────────

        /// <summary>Shows the Rebrand page; non-null arguments replace the agency's current values in the form.</summary>
        public void ShowRebrand(string flagSpriteLocation = null, Color? colorBase = null, Color? colorAccent = null)
        {
            var session = _plugin.Session;
            if (session == null) return;

            _onRebrandPage = true;
            SetTabActive(_museumTab, false);
            SetTabActive(_rebrandTab, true);
            _museumPage.style.display = DisplayStyle.None;
            _rebrandPage.style.display = DisplayStyle.Flex;
            _actions.style.display = DisplayStyle.Flex;
            _formMessage.text = "";

            _nameField.SetValueWithoutNotify(session.GetMyAgencyName());
            _noteField.SetValueWithoutNotify("");
            _baseSwatch.Value = colorBase ?? session.GetMyAgencyColorBase();
            _accentSwatch.Value = colorAccent ?? session.GetMyAgencyColorAccent();

            SetFlag(flagSpriteLocation ?? session.GetMyAgencyFlagSpriteLocation());
        }

        private void SetFlag(string location)
        {
            _flagLocation = location;
            _flagName.text = string.IsNullOrEmpty(location) ? "" : FlagTextureCatalog.GetDisplayName(location);
            ShowSprite(_flagPreview, location, BackgroundSizeType.Cover);
        }

        private void ConfirmRebrand()
        {
            var session = _plugin.Session;
            if (session != null && _nameField.value.Trim() == session.GetMyAgencyName()
                && _flagLocation == session.GetMyAgencyFlagSpriteLocation()
                && _baseSwatch.Value == session.GetMyAgencyColorBase()
                && _accentSwatch.Value == session.GetMyAgencyColorAccent())
            {
                _formMessage.text = Loc.T("Error/NoChange");
                return;
            }

            string error = _plugin.Rebrand(_nameField.value, _flagLocation, _baseSwatch.Value, _accentSwatch.Value, _noteField.value);
            if (error != null)
            {
                _formMessage.text = error;
                return;
            }
            _section = Section.Timeline;
            ShowMuseum(_plugin.Data.CurrentEra().Id);
        }

        /// <summary>A big livery swatch that opens Redux's color picker.</summary>
        private class LiverySwatch
        {
            public readonly VisualElement Root;
            private readonly VisualElement _paint;
            private readonly Label _label, _hex;
            private Color _value = Color.white;

            public LiverySwatch()
            {
                Root = Column();
                Root.style.alignItems = Align.Center;
                _label = Fit(Text("", 10, Ink2Color, true), 96, 8);
                Root.Add(_label);
                _paint = new VisualElement();
                _paint.style.width = 96;
                _paint.style.height = 58;
                _paint.style.marginTop = 4;
                _paint.style.marginBottom = 4;
                SetBorder(_paint, new Color(1, 1, 1, 0.3f), 1);
                SetRadius(_paint, 3);
                _paint.RegisterCallback<ClickEvent>(_ =>
                {
                    StickerColorPickerWindow.Open(_value, color => Value = color);
                    BringColorPickerToFront();
                });
                Root.Add(_paint);
                _hex = Text("", 11, InkColor);
                Root.Add(_hex);
            }

            /// <summary>
            /// Redux gives the picker a fixed sort order, while this window rises above it each time it's clicked, so
            /// the picker would open out of sight behind it.
            /// </summary>
            private static void BringColorPickerToFront()
            {
                try
                {
                    var type = typeof(StickerColorPickerWindow);
                    var picker = type.GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null);
                    var document = type.GetField("_window", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(picker) as UIDocument;
                    if (document == null || document.panelSettings == null) return;
                    OrderManager.Register(document.panelSettings);   // no-op if it's already known
                    OrderManager.BringToFront(document.panelSettings);
                }
                catch (Exception e)
                {
                    SWLog.Warning($"Couldn't bring the color picker to the front: {e.Message}");
                }
            }

            public void SetText(string label) => _label.text = label;

            /// <summary>The colored square, which opens the picker.</summary>
            public VisualElement Paint => _paint;

            public Color Value
            {
                get => _value;
                set
                {
                    _value = new Color(value.r, value.g, value.b, 1f);
                    _paint.style.backgroundColor = _value;
                    _hex.text = "#" + ColorUtility.ToHtmlStringRGB(_value);
                }
            }
        }

        // ───────────────────────────── Images ─────────────────────────────

        private Texture2D LoadThumbnail(string path)
        {
            if (path == null) return null;
            try
            {
                var texture = new Texture2D(2, 2);
                if (!texture.LoadImage(File.ReadAllBytes(path)))
                {
                    UnityEngine.Object.Destroy(texture);
                    return null;
                }
                _thumbnails.Add(texture);
                return texture;
            }
            catch (Exception e)
            {
                SWLog.Warning($"Couldn't load thumbnail {path}: {e.Message}");
                return null;
            }
        }

        private void ReleaseThumbnails()
        {
            foreach (var sprite in _craftSprites) UnityEngine.Object.Destroy(sprite);
            _craftSprites.Clear();
            _craftBounds.Clear();
            foreach (var texture in _thumbnails) UnityEngine.Object.Destroy(texture);
            _thumbnails.Clear();
        }

        /// <summary>Shows an agency flag, loaded once per location the way the game loads them.</summary>
        private void ShowSprite(VisualElement element, string location, BackgroundSizeType size)
        {
            element.style.backgroundImage = StyleKeyword.None;
            element.style.backgroundSize = new BackgroundSize(size);
            if (string.IsNullOrEmpty(location)) return;
            if (Cached(element, location)) return;

            var loader = new SpriteLoader();
            loader.Init();
            loader.StartLoading(location, (l, success) => Deliver(location, success ? l.Sprite : null));
        }

        /// <summary>Shows an addressable sprite, such as a ribbon layer. Flags are addressable sprites too.</summary>
        private void ShowAsset(VisualElement element, string key) => ShowSprite(element, key, BackgroundSizeType.Contain);

        /// <summary>True if the sprite is known (and applied) or already loading; otherwise registers the element.</summary>
        private bool Cached(VisualElement element, string key)
        {
            if (_sprites.TryGetValue(key, out var sprite))
            {
                if (sprite != null) element.style.backgroundImage = new StyleBackground(sprite);
                return true;
            }
            if (_spriteWaiters.TryGetValue(key, out var waiting))
            {
                waiting.Add(element);
                return true;
            }
            _spriteWaiters[key] = new List<VisualElement> { element };
            return false;
        }

        private void Deliver(string key, Sprite sprite)
        {
            _sprites[key] = sprite;
            if (!_spriteWaiters.TryGetValue(key, out var elements)) return;
            _spriteWaiters.Remove(key);
            if (sprite == null) return;
            foreach (var element in elements) element.style.backgroundImage = new StyleBackground(sprite);
        }

        // ───────────────────────────── Building blocks ─────────────────────────────

        private static Button TabButton(Action clicked)
        {
            var tab = new Button(clicked);
            tab.style.fontSize = 11;
            tab.style.unityFontStyleAndWeight = FontStyle.Bold;
            tab.style.height = 24;
            tab.style.marginLeft = 4;
            tab.style.marginRight = 0;
            tab.style.backgroundColor = Color.clear;
            SetBorder(tab, Color.clear, 1);
            SetRadius(tab, 3);
            return tab;
        }

        private static void SetTabActive(Button tab, bool active)
        {
            tab.style.color = active ? InkColor : Ink3Color;
            tab.style.backgroundColor = active ? Panel2Color : Color.clear;
            SetBorder(tab, active ? LineColor : Color.clear, 1);
        }

        private static Label Text(string text, float size, Color color, bool bold = false)
        {
            var label = new Label(text);
            label.style.fontSize = size;
            label.style.color = color;
            label.style.marginLeft = label.style.marginRight = label.style.marginTop = label.style.marginBottom = 0;
            label.style.paddingLeft = label.style.paddingRight = label.style.paddingTop = label.style.paddingBottom = 0;
            if (bold) label.style.unityFontStyleAndWeight = FontStyle.Bold;
            return label;
        }

        private Label Caption(string key)
        {
            var label = Text("", 10, SignalColor, true);
            Fixed(() => label.text = Loc.T(key));
            label.style.marginTop = 14;
            label.style.marginBottom = 6;
            label.style.letterSpacing = 1.5f;
            return label;
        }

        /// <summary>
        /// Keeps a label on one line in a fixed-width spot: a long translation is shrunk to fit, then cut off with "…".
        /// </summary>
        private static Label Fit(Label label, float maxWidth, float minFontSize)
        {
            label.style.maxWidth = maxWidth;
            label.style.overflow = Overflow.Hidden;
            label.style.whiteSpace = WhiteSpace.NoWrap;
            label.style.textOverflow = TextOverflow.Ellipsis;
            label.style.unityTextAutoSize = new TextAutoSize(TextAutoSizeMode.BestFit, minFontSize, label.style.fontSize.value.value);
            return label;
        }

        private static Label Empty(string text)
        {
            var label = Text(text, 12, Ink2Color);
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.marginTop = 6;
            label.style.maxWidth = 640;
            return label;
        }

        private static VisualElement Row()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            return row;
        }

        private static VisualElement Column(params VisualElement[] children)
        {
            var column = new VisualElement();
            column.style.flexDirection = FlexDirection.Column;
            foreach (var child in children) column.Add(child);
            if (children.Length > 0) column.style.alignItems = Align.Center;
            return column;
        }

        private static VisualElement WrapRow()
        {
            var wrap = new VisualElement();
            wrap.style.flexDirection = FlexDirection.Row;
            wrap.style.flexWrap = Wrap.Wrap;
            wrap.style.alignItems = Align.FlexStart;
            return wrap;
        }

        /// <summary>Stretches an element over its parent, behind the parent's other children.</summary>
        private static void Fill(VisualElement e)
        {
            e.style.position = Position.Absolute;
            e.style.left = e.style.top = e.style.right = e.style.bottom = 0;
        }

        private static string Roman(int n)
        {
            if (n <= 0 || n > 39) return n.ToString(CultureInfo.InvariantCulture);
            string[] tens = { "", "X", "XX", "XXX" };
            string[] ones = { "", "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX" };
            return tens[n / 10] + ones[n % 10];
        }

        /// <summary>Year and day on the Kerbin calendar, counted from 1 as the game shows them.</summary>
        private static string FormatDate(double ut)
        {
            double days = Math.Floor(Math.Max(0, ut) / SecondsPerDay);
            int year = (int)Math.Floor(days / DaysPerYear) + 1;
            int day = (int)(days % DaysPerYear) + 1;
            return Loc.F("Time/Date", year, day);
        }

        /// <summary>"Y1 D1 – Y2 D40", or "Y1 D1 – present" when the end is null.</summary>
        private static string Range(double start, double? end) =>
            Loc.F("Format/Range", FormatDate(start), end.HasValue ? FormatDate(end.Value) : Loc.T("Value/Present"));

        /// <summary>A count with its noun: the key's ".One" form for exactly one, ".Many" otherwise.</summary>
        private static string Count(string key, double n, string formatted) =>
            Loc.F(key + (n == 1 ? ".One" : ".Many"), formatted);

        private static string MilestoneTitle(ExhibitEntry milestone) => Loc.T("Milestone/" + milestone.Kind);

        private static string MilestoneDetail(ExhibitEntry milestone)
        {
            string body = string.IsNullOrEmpty(milestone.Body) ? Loc.T("Milestone/Unknown") : Loc.Body(milestone.Body);
            switch (milestone.Kind)
            {
                case MilestoneKind.Launch: return Loc.F("Milestone/From", body);
                case MilestoneKind.Visit: return body;
                case MilestoneKind.Discovered:
                    if (string.IsNullOrEmpty(milestone.Site)) return Loc.T("Milestone/Unknown");
                    // The game names most sites as science regions ("KerbinKSC" is "Kerbal Space Center").
                    string site = Loc.Game("Science/Regions/" + milestone.Site,
                        EraExhibit.SplitWords(EraExhibit.SiteWithoutBody(milestone.Site, milestone.Body)));
                    return string.IsNullOrEmpty(milestone.Body) ? site : Loc.F("Milestone/SiteOn", site, body);
                default: return Loc.F("Milestone/On", body);
            }
        }

        private static string FormatDays(double seconds)
        {
            double days = Math.Max(0, seconds) / SecondsPerDay;
            if (days < 1) return Loc.T("Time/UnderADay");
            if (days < DaysPerYear) return Count("Time/Days", Math.Floor(days), Math.Floor(days).ToString("0", CultureInfo.InvariantCulture));
            string years = (days / DaysPerYear).ToString("0.#", CultureInfo.InvariantCulture);
            return Count("Time/Years", years == "1" ? 1 : 2, years);
        }

        private static void SetPadding(VisualElement e, float vertical, float horizontal)
        {
            e.style.paddingTop = e.style.paddingBottom = vertical;
            e.style.paddingLeft = e.style.paddingRight = horizontal;
        }

        private static void SetBorder(VisualElement e, Color color, float width)
        {
            e.style.borderTopColor = e.style.borderBottomColor = e.style.borderLeftColor = e.style.borderRightColor = color;
            e.style.borderTopWidth = e.style.borderBottomWidth = e.style.borderLeftWidth = e.style.borderRightWidth = width;
        }

        private static void SetRadius(VisualElement e, float radius)
        {
            e.style.borderTopLeftRadius = e.style.borderTopRightRadius = radius;
            e.style.borderBottomLeftRadius = e.style.borderBottomRightRadius = radius;
        }
    }
}
