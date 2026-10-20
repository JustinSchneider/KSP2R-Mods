using System;
using System.Collections.Generic;
using System.Linq;
using KSP.Game;
using KSP.Sim.impl;
using UitkForKsp2.API;
using UitkForKsp2.API.Order;
using UitkForKsp2.Controls;
using UnityEngine;
using UnityEngine.UIElements;
using WayfarersWings.Models.Session;

namespace WayfarersWingsRedux.UI
{
    /// <summary>The ribbons a recovered vessel's crew earned on the mission, shown once it's back.</summary>
    internal sealed class MissionSummaryWindow
    {
        /// <summary>The player clicked a kerbal to see their whole record.</summary>
        public event Action<IGGuid> KerbalClicked;

        private const float Width = 560;
        private const float MaxBodyHeight = 460;

        private readonly AppShell _root;
        private readonly Tips _tips;
        private readonly PanelRenderer _document;
        private readonly ScrollView _list;

        public MissionSummaryWindow(Texture2D icon)
        {
            _root = new AppShell { name = "WayfarersWingsRedux_Mission", IconTexture = icon };
            _root.CloseClicked += () => Visible = false;
            _root.style.width = Width;
            _tips = new Tips(_root);

            var body = new InvertedCornerBox();
            body.AddToClassList("oab-window-body");
            _root.Add(body);

            _list = new ScrollView(ScrollViewMode.Vertical);
            _list.style.maxHeight = MaxBodyHeight;
            _list.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            Ui.SetPadding(_list.contentContainer, 10, 14);
            body.Add(_list);

            _document = Window.Create(new WindowOptions
            {
                WindowId = "WayfarersWingsRedux_Mission",
                Parent = null,
                IsHidingEnabled = true,
                UseStockScale = true,
                BringToFrontOnPointerDown = true,
                MoveOptions = new MoveOptions { IsMovingEnabled = true, CheckScreenBounds = true, HandleElementName = "header" },
            }, _root);
            _document.EnableUiSounds();

            _root.style.position = Position.Absolute;
            _root.SetDefaultPosition(size =>
            {
                var panel = _root.panel?.visualTree?.contentRect ?? new Rect(0, 0, 1920, 1080);
                return new Vector2((panel.width - size.x) / 2, Mathf.Max(40, (panel.height - size.y) / 3));
            });
            _root.Title = Loc.T("UI/MissionAchievements");
            Loc.Changed += () => _root.Title = Loc.T("UI/MissionAchievements");
            Visible = false;
        }

        public bool Visible
        {
            get => _root.style.display != DisplayStyle.None;
            set
            {
                _root.style.display = value ? DisplayStyle.Flex : DisplayStyle.None;
                if (!value) _tips.Hide();
            }
        }

        public bool Suspended
        {
            get => _root.style.visibility == Visibility.Hidden;
            set { if (value != Suspended) _root.style.visibility = value ? Visibility.Hidden : Visibility.Visible; }
        }

        /// <summary>Shows what each kerbal earned since their last launch. Does nothing if nobody earned anything.</summary>
        public void Show(IList<KerbalInfo> crew)
        {
            _list.Clear();
            int shown = 0;
            foreach (var kerbal in crew)
            {
                var profile = WingsSessionManager.Instance.GetKerbalProfile(kerbal.Id);
                var earned = profile.GetLastMissionEntries().Where(e => !e.isSuperseeded).ToList();
                if (earned.Count == 0) continue;
                shown++;

                var row = Ui.Row();
                row.style.alignItems = Align.FlexStart;
                row.style.marginBottom = 12;
                var portrait = Ui.Portrait(kerbal, 48);
                portrait.RegisterCallback<ClickEvent>(_ => KerbalClicked?.Invoke(kerbal.Id));
                row.Add(portrait);

                var column = Ui.Column();
                column.style.marginLeft = 10;
                column.style.flexGrow = 1;
                column.style.flexShrink = 1;
                var name = Ui.Text(kerbal.Attributes.GetFullName(), 14, Ui.InkColor, true);
                name.RegisterCallback<ClickEvent>(_ => KerbalClicked?.Invoke(kerbal.Id));
                column.Add(name);
                var ribbons = Ui.WrapRow();
                ribbons.style.marginTop = 6;
                foreach (var entry in earned)
                {
                    var ribbon = Ui.Ribbon(entry.Wing, true, _root);
                    ribbon.style.marginRight = 4;
                    ribbon.style.marginBottom = 4;
                    _tips.Add(ribbon, () => $"{entry.Wing.DisplayName}\n{entry.Wing.Description}");
                    ribbons.Add(ribbon);
                }
                column.Add(ribbons);
                row.Add(column);
                _list.Add(row);
            }
            Visible = shown > 0;
            if (shown > 0) BringToFront();
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
    }
}
