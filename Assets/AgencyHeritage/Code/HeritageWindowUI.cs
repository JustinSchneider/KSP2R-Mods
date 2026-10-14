using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using KSP.Game;
using KSP.Networking.MP.Utils;
using Redux.UI.AgencyFlags;
using Redux.UI.Stickers;
using UitkForKsp2.API;
using UnityEngine;
using UnityEngine.UIElements;

namespace AgencyHeritage
{
    /// <summary>
    /// The heritage window. The Museum tab is a rail of brand eras over the selected era's exhibit (banner, stats,
    /// and one section at a time: timeline, missions, crew, collection). The Rebrand tab is the ceremony that begins
    /// a new era.
    /// </summary>
    public class HeritageWindowUI
    {
        public event Action CloseClicked;

        private const double SecondsPerDay = 21600;   // Kerbin: 6-hour days
        private const double DaysPerYear = 426;
        private const float WindowWidth = 960;
        private const float WindowHeight = 680;

        private static readonly Color PanelColor = new Color(0.063f, 0.075f, 0.106f, 0.97f);
        private static readonly Color Panel2Color = new Color(0.09f, 0.106f, 0.15f);
        private static readonly Color DeepColor = new Color(0.047f, 0.059f, 0.086f);
        private static readonly Color HeaderColor = new Color(0.12f, 0.14f, 0.2f);
        private static readonly Color LineColor = new Color(0.2f, 0.23f, 0.3f);
        private static readonly Color InkColor = new Color(0.93f, 0.94f, 0.96f);
        private static readonly Color Ink2Color = new Color(0.64f, 0.66f, 0.72f);
        private static readonly Color Ink3Color = new Color(0.42f, 0.45f, 0.53f);
        private static readonly Color SignalColor = new Color(0.48f, 0.55f, 0.95f);
        private static readonly Color GoodColor = new Color(0.45f, 0.88f, 0.42f);
        private static readonly Color BadColor = new Color(0.95f, 0.38f, 0.32f);
        private static readonly Color LostColor = new Color(0.86f, 0.42f, 0.4f);
        private static readonly Color PlacardColor = new Color(0.91f, 0.89f, 0.84f);
        private static readonly Color PlacardInk = new Color(0.11f, 0.1f, 0.09f);
        private static readonly Color WallColor = new Color(0.075f, 0.086f, 0.125f);

        private enum Section { Timeline, Missions, Crew, Collection }

        private readonly AgencyHeritagePlugin _plugin;
        private readonly VisualElement _root;
        private readonly Label _title;
        private readonly Button _museumTab, _rebrandTab;
        private readonly VisualElement _museumPage, _rebrandPage;

        // Museum
        private readonly ScrollView _rail;
        private readonly VisualElement _hero, _stats, _sectionTabs;
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

        public HeritageWindowUI(Texture2D icon, AgencyHeritagePlugin plugin)
        {
            _plugin = plugin;

            _root = new VisualElement { name = "agency-heritage" };
            _root.style.width = WindowWidth;
            _root.style.height = WindowHeight;
            _root.style.backgroundColor = PanelColor;
            SetBorder(_root, LineColor, 1);
            SetRadius(_root, 5);
            _root.style.overflow = Overflow.Hidden;

            // Header with the two tabs
            var header = Row();
            header.name = "header";
            header.style.height = 34;
            header.style.flexShrink = 0;
            header.style.backgroundColor = HeaderColor;
            header.style.borderBottomWidth = 1;
            header.style.borderBottomColor = LineColor;
            SetPadding(header, 0, 10);
            var iconElement = new VisualElement();
            iconElement.style.width = iconElement.style.height = 18;
            iconElement.style.marginRight = 8;
            iconElement.style.backgroundImage = new StyleBackground(icon);
            header.Add(iconElement);
            _title = Text("Agency Heritage", 13, InkColor, true);
            _title.style.flexGrow = 1;
            header.Add(_title);
            _museumTab = TabButton("MUSEUM", () => ShowMuseum());
            _rebrandTab = TabButton("REBRAND", OpenRebrand);
            header.Add(_museumTab);
            header.Add(_rebrandTab);
            var close = new Button(() => CloseClicked?.Invoke()) { text = "✕" };
            close.style.width = close.style.height = 22;
            close.style.marginLeft = 10;
            close.style.marginRight = 0;
            header.Add(close);
            _root.Add(header);

            // Museum page
            _museumPage = new VisualElement();
            _museumPage.style.flexGrow = 1;
            _root.Add(_museumPage);

            _rail = new ScrollView(ScrollViewMode.Horizontal);
            _rail.style.flexShrink = 0;
            _rail.style.height = 112;
            _rail.style.backgroundColor = DeepColor;
            _rail.style.borderBottomWidth = 1;
            _rail.style.borderBottomColor = LineColor;
            _rail.contentContainer.style.flexDirection = FlexDirection.Row;
            _rail.contentContainer.style.paddingLeft = 12;
            _rail.contentContainer.style.paddingTop = 10;
            _museumPage.Add(_rail);

            var hall = new VisualElement();
            hall.style.flexGrow = 1;
            SetPadding(hall, 12, 14);
            _museumPage.Add(hall);

            _hero = new VisualElement();
            _hero.style.flexShrink = 0;
            hall.Add(_hero);

            _stats = Row();
            _stats.style.flexShrink = 0;
            _stats.style.marginTop = 10;
            hall.Add(_stats);

            _sectionTabs = Row();
            _sectionTabs.style.flexShrink = 0;
            _sectionTabs.style.marginTop = 12;
            _sectionTabs.style.borderBottomWidth = 1;
            _sectionTabs.style.borderBottomColor = LineColor;
            hall.Add(_sectionTabs);

            _sectionHost = new ScrollView(ScrollViewMode.Vertical);
            _sectionHost.style.flexGrow = 1;
            _sectionHost.style.marginTop = 10;
            hall.Add(_sectionHost);

            // Rebrand page: identity on the left, livery and statement on the right, actions along the bottom.
            _rebrandPage = new VisualElement();
            _rebrandPage.style.flexGrow = 1;
            _rebrandPage.style.display = DisplayStyle.None;
            SetPadding(_rebrandPage, 18, 24);
            _root.Add(_rebrandPage);

            var intro = Text("A rebrand begins a new era in the museum. Vessels and blueprints you already have keep " +
                             "their colors; new parts in the VAB start in the new livery.", 12, Ink2Color);
            intro.style.whiteSpace = WhiteSpace.Normal;
            _rebrandPage.Add(intro);

            var columns = Row();
            columns.style.alignItems = Align.FlexStart;
            columns.style.flexGrow = 1;
            _rebrandPage.Add(columns);

            var left = new VisualElement();
            left.style.flexGrow = 1;
            left.style.flexBasis = 0;
            left.style.marginRight = 28;
            columns.Add(left);

            left.Add(Caption("NEW NAME"));
            _nameField = new TextField { maxLength = 48 };
            _nameField.style.marginLeft = _nameField.style.marginRight = 0;
            _nameField.style.fontSize = 18;
            left.Add(_nameField);

            left.Add(Caption("FLAG"));
            var flagRow = Row();
            flagRow.style.alignItems = Align.FlexEnd;
            _flagPreview = new VisualElement();
            _flagPreview.style.width = 200;
            _flagPreview.style.height = 126;
            _flagPreview.style.marginRight = 14;
            _flagPreview.style.backgroundColor = Color.black;
            SetBorder(_flagPreview, new Color(1, 1, 1, 0.8f), 3);
            flagRow.Add(_flagPreview);
            var flagInfo = new VisualElement();
            _flagName = Text("", 14, InkColor, true);
            flagInfo.Add(_flagName);
            var chooseFlag = new Button(() => FlagTexturePickerWindow.OpenAgencyFlags(_flagLocation, SetFlag)) { text = "Choose flag…" };
            chooseFlag.style.marginLeft = 0;
            chooseFlag.style.marginTop = 6;
            flagInfo.Add(chooseFlag);
            flagRow.Add(flagInfo);
            left.Add(flagRow);

            var right = new VisualElement();
            right.style.flexGrow = 1;
            right.style.flexBasis = 0;
            columns.Add(right);

            right.Add(Caption("LIVERY"));
            var swatches = Row();
            _baseSwatch = new LiverySwatch("BASE");
            _accentSwatch = new LiverySwatch("ACCENT");
            var swap = new Button(() =>
            {
                var b = _baseSwatch.Value;
                _baseSwatch.Value = _accentSwatch.Value;
                _accentSwatch.Value = b;
            }) { text = "⇄", tooltip = "Swap base and accent" };
            swap.style.width = swap.style.height = 34;
            SetRadius(swap, 17);
            swap.style.marginLeft = swap.style.marginRight = 12;
            swatches.Add(_baseSwatch.Root);
            swatches.Add(swap);
            swatches.Add(_accentSwatch.Root);
            right.Add(swatches);
            right.Add(Hint("Click a swatch to choose its color."));

            right.Add(Caption("STATEMENT (OPTIONAL)"));
            _noteField = new TextField { multiline = true, maxLength = 200 };
            _noteField.style.minHeight = 72;
            _noteField.style.marginLeft = _noteField.style.marginRight = 0;
            _noteField.style.whiteSpace = WhiteSpace.Normal;
            right.Add(_noteField);
            right.Add(Hint("Shown as a quote on the era's banner, e.g. \"Merged with Rockomax\"."));

            var actions = Row();
            actions.style.marginTop = 16;
            actions.style.paddingTop = 14;
            actions.style.borderTopWidth = 1;
            actions.style.borderTopColor = LineColor;
            _formMessage = Text("", 12, BadColor);
            _formMessage.style.whiteSpace = WhiteSpace.Normal;
            _formMessage.style.flexGrow = 1;
            _formMessage.style.flexShrink = 1;
            actions.Add(_formMessage);
            var cancel = new Button(() => ShowMuseum()) { text = "Cancel" };
            var confirm = new Button(ConfirmRebrand) { text = "Begin a new era" };
            confirm.style.backgroundColor = InkColor;
            confirm.style.color = new Color(0.04f, 0.05f, 0.07f);
            confirm.style.unityFontStyleAndWeight = FontStyle.Bold;
            foreach (var button in new[] { cancel, confirm })
            {
                button.style.width = 180;
                button.style.height = 38;
                button.style.marginLeft = 10;
                button.style.marginRight = 0;
                actions.Add(button);
            }
            _rebrandPage.Add(actions);

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

            _root.style.position = Position.Absolute;
            _root.SetDefaultPosition(size =>
            {
                var panel = _root.panel?.visualTree?.contentRect ?? new Rect(0, 0, 1920, 1080);
                return new Vector2((panel.width - size.x) / 2, Mathf.Max(40, (panel.height - size.y) / 2));
            });
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

        /// <summary>Rebuilds the museum from the current history.</summary>
        public void Refresh() => ShowMuseum();

        // ───────────────────────────── Museum ─────────────────────────────

        private void ShowMuseum(string selectEraId = null)
        {
            _title.text = "Agency Heritage";
            SetTabActive(_museumTab, true);
            SetTabActive(_rebrandTab, false);
            _rebrandPage.style.display = DisplayStyle.None;
            _museumPage.style.display = DisplayStyle.Flex;
            if (selectEraId != null) _selectedEraId = selectEraId;

            var data = _plugin.Data;
            _rail.Clear();
            if (data == null || data.Eras.Count == 0)
            {
                _hero.Clear();
                _stats.Clear();
                _sectionTabs.Clear();
                _sectionHost.Clear();
                _hero.Add(Text("Load a campaign to open its museum.", 14, Ink2Color));
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
            BuildStats(_exhibit);
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
                var now = Text("NOW", 9, new Color(0.03f, 0.14f, 0.04f), true);
                now.style.position = Position.Absolute;
                now.style.left = 92;
                now.style.top = -2;
                now.style.backgroundColor = GoodColor;
                SetPadding(now, 0, 4);
                SetRadius(now, 2);
                medal.Add(now);
            }
            stop.Add(medal);

            var name = Text(era.Name, 13, selected ? Color.white : Ink2Color, true);
            name.style.marginTop = 4;
            name.style.unityTextAlign = TextAnchor.MiddleCenter;
            name.style.maxWidth = 140;
            name.style.overflow = Overflow.Hidden;
            name.style.textOverflow = TextOverflow.Ellipsis;
            stop.Add(name);
            stop.Add(Text($"{FormatDate(era.StartUT)} – {(current ? "present" : FormatDate(data.EndUT(era)))}", 10, Ink3Color));
            return stop;
        }

        private VisualElement BuildNewEraStop()
        {
            var stop = Column();
            stop.style.width = 120;
            stop.style.alignItems = Align.Center;
            stop.RegisterCallback<ClickEvent>(_ => OpenRebrand());
            var circle = new Drawn((p, r) =>
            {
                var c = r.center;
                for (int a = 0; a < 360; a += 30) HeritageGraphics.Ring(p, c, 26, 2, Ink3Color, a, a + 18);
                HeritageGraphics.Line(p, c + new Vector2(-9, 0), c + new Vector2(9, 0), Ink2Color, 2.5f);
                HeritageGraphics.Line(p, c + new Vector2(0, -9), c + new Vector2(0, 9), Ink2Color, 2.5f);
            }, 58, 58);
            stop.Add(circle);
            var label = Text("New era", 13, Ink2Color, true);
            label.style.marginTop = 4;
            stop.Add(label);
            stop.Add(Text("Rebrand…", 10, Ink3Color));
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

            var flag = new VisualElement();
            flag.style.width = 170;
            flag.style.height = 107;
            flag.style.flexShrink = 0;
            flag.style.backgroundColor = Color.black;
            SetBorder(flag, new Color(1, 1, 1, 0.82f), 3);
            flag.transform.rotation = Quaternion.Euler(0, 0, -1.5f);
            ShowSprite(flag, era.FlagSpriteLocation, BackgroundSizeType.Cover);
            hero.Add(flag);

            var text = new VisualElement();
            text.style.flexGrow = 1;
            text.style.flexShrink = 1;
            text.style.marginLeft = 22;
            text.Add(Text($"ERA {Roman(exhibit.Number)}  ·  {(exhibit.IsCurrent ? "CURRENT IDENTITY" : "PAST IDENTITY")}", 11, new Color(1, 1, 1, 0.72f), true));
            var name = Text(era.Name, 34, Color.white, true);
            name.style.whiteSpace = WhiteSpace.Normal;
            name.style.marginTop = 2;
            text.Add(name);
            string span = $"{FormatDate(exhibit.StartUT)} – {(exhibit.IsCurrent ? "present" : FormatDate(exhibit.EndUT))}  ·  {FormatDays(exhibit.EndUT - exhibit.StartUT)}{(exhibit.IsCurrent ? " so far" : "")}";
            string origin = era.StartRealTime > DateTime.MinValue
                ? $"Rebranded {era.StartRealTime.ToString("d", CultureInfo.CurrentCulture)}"
                : "Founding identity";
            text.Add(Text($"{span}   ·   {origin}", 12, new Color(1, 1, 1, 0.8f)));
            if (!string.IsNullOrEmpty(era.Note) && era.Note != "Founded")
            {
                var quote = Text($"“{era.Note}”", 15, Color.white);
                quote.style.unityFontStyleAndWeight = FontStyle.Italic;
                quote.style.whiteSpace = WhiteSpace.Normal;
                quote.style.marginTop = 8;
                text.Add(quote);
            }
            hero.Add(text);

            var livery = Row();
            livery.style.flexShrink = 0;
            livery.Add(PaintChip("BASE", era.Base));
            livery.Add(PaintChip("ACCENT", era.Accent));
            hero.Add(livery);

            _hero.Add(hero);
        }

        private static VisualElement PaintChip(string label, Color color)
        {
            var chip = Column();
            chip.style.width = 62;
            chip.style.marginLeft = 10;
            chip.style.alignItems = Align.Center;
            chip.Add(Text(label, 9, new Color(1, 1, 1, 0.7f), true));
            var paint = new VisualElement();
            paint.style.width = 58;
            paint.style.height = 78;
            paint.style.marginTop = 3;
            paint.style.marginBottom = 3;
            paint.style.backgroundColor = color;
            SetBorder(paint, new Color(1, 1, 1, 0.25f), 1);
            SetRadius(paint, 2);
            chip.Add(paint);
            chip.Add(Text("#" + ColorUtility.ToHtmlStringRGB(color), 10, Color.white));
            return chip;
        }

        private void BuildStats(EraExhibit exhibit)
        {
            _stats.Clear();
            var accent = Color.Lerp(exhibit.Era.Base, Color.white, 0.15f);
            AddStat(HeritageGraphics.Glyph.Rocket, exhibit.Launches, "LAUNCHES", accent);
            AddStat(HeritageGraphics.Glyph.Helmet, exhibit.CrewFlownCount, "CREW FLOWN", accent);
            AddStat(HeritageGraphics.Glyph.Person, exhibit.RecruitCount, exhibit.Number == 1 ? "FOUNDERS" : "RECRUITS", accent);
            AddStat(HeritageGraphics.Glyph.Star, exhibit.Milestones.Count, "MILESTONES", accent);
            if (WingsBridge.IsInstalled) AddStat(HeritageGraphics.Glyph.Ribbon, exhibit.Ribbons.Count, "RIBBONS", accent);
            AddStat(HeritageGraphics.Glyph.Blueprint, exhibit.Blueprints.Count, "BLUEPRINTS", accent);
            // Only eras that lost someone get the tile; a row of "0 lost" would read oddly.
            if (exhibit.Losses.Count > 0) AddStat(HeritageGraphics.Glyph.Wreath, exhibit.Losses.Count, "LOST", LostColor);
        }

        private void AddStat(HeritageGraphics.Glyph glyph, int value, string label, Color accent)
        {
            var tile = Row();
            tile.style.flexGrow = 1;
            tile.style.flexBasis = 0;
            tile.style.height = 56;
            tile.style.backgroundColor = Panel2Color;
            SetBorder(tile, LineColor, 1);
            SetRadius(tile, 4);
            SetPadding(tile, 0, 12);
            if (_stats.childCount > 0) tile.style.marginLeft = 8;

            tile.Add(new Drawn((p, r) => HeritageGraphics.Icon(p, r, glyph, accent), 26, 26));
            var text = new VisualElement();
            text.style.marginLeft = 10;
            text.Add(Text(value.ToString(CultureInfo.CurrentCulture), 24, InkColor, true));
            text.Add(Text(label, 9, Ink2Color, true));
            tile.Add(text);
            _stats.Add(tile);
        }

        private void BuildSectionTabs()
        {
            _sectionTabs.Clear();
            foreach (Section section in Enum.GetValues(typeof(Section)))
            {
                string label = section.ToString().ToUpperInvariant();
                int count = section switch
                {
                    Section.Missions => _exhibit.Launches,
                    Section.Crew => _exhibit.Crew.Count,
                    Section.Collection => _exhibit.Blueprints.Count,
                    _ => -1,
                };
                var tab = TabButton(count >= 0 ? $"{label}  {count}" : label, () => ShowSection(section));
                tab.userData = section;
                tab.style.marginLeft = 0;
                tab.style.marginRight = 4;
                _sectionTabs.Add(tab);
            }
        }

        private void ShowSection(Section section)
        {
            _section = section;
            foreach (var child in _sectionTabs.Children())
                if (child is Button tab) SetTabActive(tab, tab.userData is Section s && s == section);

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

        private void BuildTimeline()
        {
            var exhibit = _exhibit;
            _timelinePlacements.Clear();
            if (exhibit.Milestones.Count == 0 && exhibit.LaunchTimes.Count == 0)
            {
                _sectionHost.Add(Empty("Nothing happened in this era yet. Launches and firsts will appear here."));
                return;
            }

            var host = new VisualElement();
            host.style.height = 200;
            host.style.backgroundColor = Panel2Color;
            SetBorder(host, LineColor, 1);
            SetRadius(host, 4);
            _sectionHost.Add(host);

            const float margin = 50;
            double start = exhibit.StartUT, end = Math.Max(exhibit.EndUT, start + 1);
            float X(Rect r, double ut) => margin + (float)((ut - start) / (end - start)) * (r.width - 2 * margin);

            var track = new Drawn((p, r) =>
            {
                float y = r.height / 2;
                HeritageGraphics.Line(p, new Vector2(margin, y), new Vector2(r.width - margin, y), LineColor, 4);
                if (exhibit.IsCurrent)
                    for (float x = margin; x < r.width - margin; x += 12)
                        HeritageGraphics.Line(p, new Vector2(x, y), new Vector2(Mathf.Min(x + 6, r.width - margin), y), exhibit.Era.Base, 3);
                else
                    HeritageGraphics.Line(p, new Vector2(margin, y), new Vector2(r.width - margin, y), exhibit.Era.Base, 3);

                foreach (var loss in exhibit.Losses)
                {
                    float x = X(r, loss.UT);
                    HeritageGraphics.Line(p, new Vector2(x - 4, y + 6), new Vector2(x + 4, y + 14), LostColor, 2);
                    HeritageGraphics.Line(p, new Vector2(x + 4, y + 6), new Vector2(x - 4, y + 14), LostColor, 2);
                }
                foreach (double ut in exhibit.LaunchTimes)
                {
                    float x = X(r, ut);
                    HeritageGraphics.Polygon(p, InkColor, new Vector2(x, y - 13), new Vector2(x + 4, y - 5), new Vector2(x - 4, y - 5));
                    HeritageGraphics.Line(p, new Vector2(x, y - 5), new Vector2(x, y + 4), InkColor, 2);
                }
                foreach (var placed in _timelinePlacements)
                {
                    float x = X(r, placed.Entry.UT);
                    HeritageGraphics.Line(p, new Vector2(x, y), new Vector2(x, y + placed.Lane * 1f), new Color(0.35f, 0.38f, 0.47f), 1);
                }
                foreach (var placed in _timelinePlacements)
                {
                    float x = X(r, placed.Entry.UT);
                    HeritageGraphics.Circle(p, new Vector2(x, y), 9, Color.white);
                    HeritageGraphics.Circle(p, new Vector2(x, y), 7, HeritageGraphics.BodyColor(placed.Entry.Body));
                }
            });
            Fill(track);
            host.Add(track);

            var labels = new VisualElement();
            Fill(labels);
            labels.pickingMode = PickingMode.Ignore;
            host.Add(labels);
            var startLabel = Text(FormatDate(start), 11, Ink3Color);
            var endLabel = Text(exhibit.IsCurrent ? "NOW" : FormatDate(end), 11, Ink3Color);
            labels.Add(startLabel);
            labels.Add(endLabel);

            // Milestones alternate above and below the line, moving out a lane when they'd overlap a neighbour.
            host.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                var r = host.contentRect;
                if (r.width <= 0) return;
                float y = r.height / 2;
                startLabel.style.position = endLabel.style.position = Position.Absolute;
                startLabel.style.left = margin - 20;
                startLabel.style.top = y + 10;
                endLabel.style.right = margin - 20;
                endLabel.style.top = y + 10;

                PlaceMilestones(exhibit.Milestones, x => X(r, x));
                foreach (var label in labels.Children().Where(c => c.userData is TimelinePlacement).ToList()) labels.Remove(label);
                foreach (var placed in _timelinePlacements)
                {
                    var block = Column();
                    block.userData = placed;
                    block.style.position = Position.Absolute;
                    block.style.width = 140;
                    block.style.alignItems = Align.Center;
                    block.style.left = Mathf.Clamp(X(r, placed.Entry.UT) - 70, 0, r.width - 140);
                    block.Add(Text(placed.Entry.Title, 12, InkColor, true));
                    block.Add(Text($"{placed.Entry.Detail} · {FormatDate(placed.Entry.UT)}", 10, Ink2Color));
                    if (placed.Lane < 0) block.style.bottom = r.height - (y + placed.Lane) + 2;
                    else block.style.top = y + placed.Lane + 2;
                    labels.Add(block);
                }
                track.MarkDirtyRepaint();
            });

            var legend = Row();
            legend.style.marginTop = 8;
            legend.Add(new Drawn((p, r) =>
            {
                var c = r.center;
                HeritageGraphics.Polygon(p, InkColor, c + new Vector2(0, -7), c + new Vector2(4, 1), c + new Vector2(-4, 1));
            }, 14, 14));
            legend.Add(Text("launch", 11, Ink2Color));
            var dot = new Drawn((p, r) => HeritageGraphics.Circle(p, r.center, 5, HeritageGraphics.BodyColor("Kerbin")), 14, 14);
            dot.style.marginLeft = 14;
            legend.Add(dot);
            legend.Add(Text("first, colored by the body it happened at", 11, Ink2Color));
            if (exhibit.Losses.Count > 0)
            {
                var cross = new Drawn((p, r) =>
                {
                    var c = r.center;
                    HeritageGraphics.Line(p, c + new Vector2(-4, -4), c + new Vector2(4, 4), LostColor, 2);
                    HeritageGraphics.Line(p, c + new Vector2(4, -4), c + new Vector2(-4, 4), LostColor, 2);
                }, 14, 14);
                cross.style.marginLeft = 14;
                legend.Add(cross);
                legend.Add(Text("kerbal lost", 11, Ink2Color));
            }
            _sectionHost.Add(legend);
        }

        private class TimelinePlacement
        {
            public ExhibitEntry Entry;
            /// <summary>Signed distance of the label from the line: negative above, positive below.</summary>
            public float Lane;
        }

        private readonly List<TimelinePlacement> _timelinePlacements = new List<TimelinePlacement>();

        private void PlaceMilestones(List<ExhibitEntry> milestones, Func<double, float> x)
        {
            float[] lanes = { -30, 30, -62, 62 };
            var lastX = new float[lanes.Length];
            for (int i = 0; i < lastX.Length; i++) lastX[i] = float.NegativeInfinity;

            _timelinePlacements.Clear();
            int preferred = 0;
            foreach (var milestone in milestones)
            {
                float px = x(milestone.UT);
                int lane = -1;
                for (int k = 0; k < lanes.Length; k++)
                {
                    int candidate = (preferred + k) % lanes.Length;
                    if (px - lastX[candidate] >= 145) { lane = candidate; break; }
                }
                if (lane < 0) continue;   // too crowded to label; the dot still shows the moment
                lastX[lane] = px;
                preferred = (lane + 1) % 2;
                _timelinePlacements.Add(new TimelinePlacement { Entry = milestone, Lane = lanes[lane] });
            }
        }

        // ─── Missions: a patch for each recorded flight ───

        private void BuildMissions()
        {
            var exhibit = _exhibit;
            var wall = WrapRow();
            foreach (var flight in exhibit.Flights)
            {
                var card = Column();
                card.style.width = 150;
                card.style.alignItems = Align.Center;
                card.style.marginRight = 12;
                card.style.marginBottom = 14;
                int crew = flight.CrewCount;
                card.Add(new Drawn((p, r) => HeritageGraphics.Patch(p, r, exhibit.Era.Base, exhibit.Era.Accent, crew), 110, 110));
                var name = Text(flight.Title.ToUpperInvariant(), 12, InkColor, true);
                name.style.marginTop = 6;
                name.style.unityTextAlign = TextAnchor.MiddleCenter;
                name.style.whiteSpace = WhiteSpace.Normal;
                card.Add(name);
                card.Add(Text(FormatDate(flight.UT), 10, Ink2Color));
                var who = Text(flight.Detail, 10, Ink3Color);
                who.style.whiteSpace = WhiteSpace.Normal;
                who.style.unityTextAlign = TextAnchor.MiddleCenter;
                card.Add(who);
                wall.Add(card);
            }
            if (exhibit.Flights.Count > 0) _sectionHost.Add(wall);

            if (exhibit.Flights.Count == 0)
                _sectionHost.Add(Empty("No launches in this era yet. Each launch gets a mission patch here."));
        }

        // ─── Crew roll ───

        private void BuildCrew()
        {
            var exhibit = _exhibit;
            if (exhibit.Crew.Count == 0)
            {
                _sectionHost.Add(Empty("No kerbals flew or joined during this era yet."));
                return;
            }

            var wall = WrapRow();
            foreach (var member in exhibit.Crew)
            {
                var card = Column();
                card.style.width = 104;
                card.style.alignItems = Align.Center;
                card.style.marginRight = 4;
                card.style.marginBottom = 10;
                card.Add(Portrait(member));
                var name = Text(member.Name.EndsWith(" Kerman") ? member.Name.Substring(0, member.Name.Length - 7) : member.Name, 12, InkColor, true);
                name.style.marginTop = 4;
                name.style.maxWidth = 100;
                name.style.overflow = Overflow.Hidden;
                name.style.textOverflow = TextOverflow.Ellipsis;
                card.Add(name);
                bool lost = member.Lost != null && !member.Returned;
                string role = member.Returned ? "RETURNED"
                    : lost ? $"LOST · {FormatDate(member.Lost.UT)}"
                    : member.Role == CrewRole.Flew ? "FLEW" : member.Role == CrewRole.Founder ? "FOUNDER" : "RECRUIT";
                var roleColor = member.Returned ? SignalColor : lost ? LostColor : member.Role == CrewRole.Flew ? GoodColor : Ink3Color;
                card.Add(Text(role, 9, roleColor, true));
                if (member.Lost != null)
                    card.tooltip = string.IsNullOrEmpty(member.Lost.VesselName)
                        ? $"Lost on {FormatDate(member.Lost.UT)}"
                        : $"Lost aboard {member.Lost.VesselName}, {FormatDate(member.Lost.UT)}";

                foreach (var ribbon in member.Ribbons.Take(2))
                {
                    var element = Ribbon(ribbon);
                    element.style.marginTop = 4;
                    card.Add(element);
                }
                if (member.Ribbons.Count > 2) card.Add(Text($"+{member.Ribbons.Count - 2} more", 9, Ink3Color));
                wall.Add(card);
            }
            _sectionHost.Add(wall);
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

            var saved = lost && member.Lost.PortraitFile != null && _plugin.PortraitFolder != null
                ? LoadThumbnail(Path.Combine(_plugin.PortraitFolder, member.Lost.PortraitFile))
                : null;
            var portrait = member.Portrait;
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
                var first = Text("FIRST", 8, new Color(1f, 0.85f, 0.4f), true);
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
                _sectionHost.Add(Empty("No blueprints were saved in this era."));
                return;
            }

            var wall = WrapRow();
            wall.style.backgroundColor = WallColor;
            SetRadius(wall, 4);
            SetPadding(wall, 16, 16);
            foreach (var card in cards)
            {
                var exhibit = Column();
                exhibit.style.width = 160;
                exhibit.style.marginRight = 18;
                exhibit.style.marginBottom = 16;
                exhibit.style.alignItems = Align.Center;

                var frame = new VisualElement();
                frame.style.width = frame.style.height = 150;
                frame.style.backgroundColor = new Color(0.14f, 0.16f, 0.22f);
                SetBorder(frame, new Color(0.17f, 0.18f, 0.24f), 6);
                var texture = LoadThumbnail(card.ThumbnailPath);
                if (texture != null)
                {
                    frame.style.backgroundImage = new StyleBackground(texture);
                    frame.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
                }
                exhibit.Add(frame);

                var placard = Column();
                placard.style.width = 136;
                placard.style.marginTop = 10;
                placard.style.backgroundColor = PlacardColor;
                SetPadding(placard, 6, 8);
                SetRadius(placard, 1);
                var title = Text(string.IsNullOrEmpty(card.VehicleName) ? card.Name : card.VehicleName, 13, PlacardInk, true);
                title.style.whiteSpace = WhiteSpace.Normal;
                placard.Add(title);
                placard.Add(Text($"{card.Parts} parts · {card.SaveTime.ToString("d", CultureInfo.CurrentCulture)}", 10, new Color(0.34f, 0.32f, 0.29f)));
                exhibit.Add(placard);
                wall.Add(exhibit);
            }
            _sectionHost.Add(wall);
        }

        // ───────────────────────────── Rebrand ─────────────────────────────

        private void OpenRebrand()
        {
            var session = _plugin.Session;
            if (session == null) return;

            _title.text = "Rebrand agency";
            SetTabActive(_museumTab, false);
            SetTabActive(_rebrandTab, true);
            _museumPage.style.display = DisplayStyle.None;
            _rebrandPage.style.display = DisplayStyle.Flex;
            _formMessage.text = "";

            _nameField.SetValueWithoutNotify(session.GetMyAgencyName());
            _noteField.SetValueWithoutNotify("");
            _baseSwatch.Value = session.GetMyAgencyColorBase();
            _accentSwatch.Value = session.GetMyAgencyColorAccent();

            SetFlag(session.GetMyAgencyFlagSpriteLocation());
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
                _formMessage.text = "Change the name, flag or livery to begin a new era.";
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
            private readonly Label _hex;
            private Color _value = Color.white;

            public LiverySwatch(string label)
            {
                Root = Column();
                Root.style.alignItems = Align.Center;
                Root.Add(Text(label, 10, Ink2Color, true));
                _paint = new VisualElement();
                _paint.style.width = 96;
                _paint.style.height = 58;
                _paint.style.marginTop = 4;
                _paint.style.marginBottom = 4;
                SetBorder(_paint, new Color(1, 1, 1, 0.3f), 1);
                SetRadius(_paint, 3);
                _paint.RegisterCallback<ClickEvent>(_ => StickerColorPickerWindow.Open(_value, color =>
                {
                    Value = color;
                }));
                Root.Add(_paint);
                _hex = Text("", 11, InkColor);
                Root.Add(_hex);
            }

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

        private static Button TabButton(string text, Action clicked)
        {
            var tab = new Button(clicked) { text = text };
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

        private static Label Caption(string text)
        {
            var label = Text(text, 10, SignalColor, true);
            label.style.marginTop = 14;
            label.style.marginBottom = 6;
            label.style.letterSpacing = 1.5f;
            return label;
        }

        private static Label Hint(string text)
        {
            var label = Text(text, 11, Ink3Color);
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.marginTop = 6;
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
            return $"Y{year} D{day}";
        }

        private static string FormatDays(double seconds)
        {
            double days = Math.Max(0, seconds) / SecondsPerDay;
            if (days < 1) return "under a day";
            if (days < DaysPerYear) return $"{Math.Floor(days):0} day{(Math.Floor(days) == 1 ? "" : "s")}";
            double years = days / DaysPerYear;
            return $"{years:0.#} years";
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
