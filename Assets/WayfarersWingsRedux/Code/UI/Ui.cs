using System;
using System.Collections.Generic;
using KSP.Game;
using KSP.Networking.MP.Utils;
using UnityEngine;
using UnityEngine.UIElements;
using WayfarersWings.Models.Wings;
using WayfarersWings.Utility;

namespace WayfarersWingsRedux.UI
{
    /// <summary>Colors and small building blocks shared by the mod's windows.</summary>
    internal static class Ui
    {
        public static readonly Color PanelColor = new Color(0.063f, 0.075f, 0.106f, 0.97f);
        public static readonly Color Panel2Color = new Color(0.09f, 0.106f, 0.15f);
        public static readonly Color DeepColor = new Color(0.047f, 0.059f, 0.086f);
        public static readonly Color LineColor = new Color(0.2f, 0.23f, 0.3f);
        public static readonly Color InkColor = new Color32(230, 235, 244, 255);
        public static readonly Color Ink2Color = new Color(0.64f, 0.66f, 0.72f);
        public static readonly Color Ink3Color = new Color32(127, 133, 152, 255);
        public static readonly Color SignalColor = new Color32(140, 146, 230, 255);
        public static readonly Color GoldColor = new Color32(231, 202, 118, 255);
        public static readonly Color GoodColor = new Color32(0, 255, 102, 255);
        public static readonly Color OnMissionColor = new Color32(255, 104, 92, 255);
        public static readonly Color ActiveColor = new Color32(105, 109, 255, 255);

        /// <summary>Full-size ribbon: the 2x art at its native size.</summary>
        public const float BigWidth = 120, BigHeight = 32;
        /// <summary>Small ribbon: the 1x art at its native size (or the 2x art on a HiDPI screen).</summary>
        public const float SmallWidth = 60, SmallHeight = 16;

        private static readonly Dictionary<string, Sprite> Sprites = new Dictionary<string, Sprite>();
        private static readonly Dictionary<string, List<VisualElement>> Waiting = new Dictionary<string, List<VisualElement>>();

        /// <summary>
        /// A ribbon: its image layers stacked bottom first. Small ribbons use the 1x pixel art unless the screen is
        /// HiDPI (or the player asked for big ribbons everywhere).
        /// </summary>
        public static VisualElement Ribbon(Wing wing, bool big, VisualElement context)
        {
            big |= Settings.ShowAlwaysBigRibbons.Value;
            float width = big ? BigWidth : SmallWidth, height = big ? BigHeight : SmallHeight;
            var ribbon = new VisualElement();
            ribbon.style.width = ribbon.style.minWidth = ribbon.style.maxWidth = width;
            ribbon.style.height = ribbon.style.minHeight = ribbon.style.maxHeight = height;
            ribbon.style.flexShrink = 0;
            bool hiDpi = big || IsHiDpi(context);
            foreach (var layer in wing.config.imageLayers)
            {
                var image = new VisualElement { pickingMode = PickingMode.Ignore };
                Fill(image);
                image.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
                ShowSprite(image, WingsApi.LayerAddress(layer, hiDpi));
                ribbon.Add(image);
            }
            return ribbon;
        }

        private static bool IsHiDpi(VisualElement context)
        {
            switch (Settings.RibbonHiDPIDisplayMode.Value)
            {
                case Settings.HiDPIDisplayMode.Normal: return false;
                case Settings.HiDPIDisplayMode.Retina: return true;
                default:
                    // The panel is drawn at stock UI scale; 1.5 or more screen pixels per point is HiDPI.
                    var tree = context?.panel?.visualTree;
                    float width = tree?.worldBound.width ?? 0;
                    return width > 0 && Screen.width / width > 1.5f;
            }
        }

        /// <summary>Shows an addressable sprite as the element's background, loading each key once.</summary>
        public static void ShowSprite(VisualElement element, string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            if (Sprites.TryGetValue(key, out var sprite))
            {
                if (sprite != null) element.style.backgroundImage = new StyleBackground(sprite);
                return;
            }
            if (Waiting.TryGetValue(key, out var waiting))
            {
                waiting.Add(element);
                return;
            }
            Waiting[key] = new List<VisualElement> { element };
            try
            {
                var loader = new SpriteLoader();
                loader.Init();
                loader.StartLoading(key, (l, success) => Deliver(key, success ? l.Sprite : null));
            }
            catch (Exception e)
            {
                SWLog.Warning($"Couldn't load ribbon layer {key}: {e.Message}");
                Deliver(key, null);
            }
        }

        private static void Deliver(string key, Sprite sprite)
        {
            if (sprite == null) SWLog.Warning($"Ribbon layer {key} is missing");
            Sprites[key] = sprite;
            if (!Waiting.TryGetValue(key, out var elements)) return;
            Waiting.Remove(key);
            if (sprite == null) return;
            foreach (var element in elements) element.style.backgroundImage = new StyleBackground(sprite);
        }

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

        /// <summary>Keeps a label on one line: a long text is cut off with "…".</summary>
        public static Label OneLine(Label label)
        {
            label.style.overflow = Overflow.Hidden;
            label.style.whiteSpace = WhiteSpace.NoWrap;
            label.style.textOverflow = TextOverflow.Ellipsis;
            return label;
        }

        public static Label Caption(string text)
        {
            var label = Text(text.ToUpperInvariant(), 10, SignalColor, true);
            label.style.marginTop = 14;
            label.style.marginBottom = 6;
            label.style.letterSpacing = 1.5f;
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

        public static VisualElement Column()
        {
            var column = new VisualElement();
            column.style.flexDirection = FlexDirection.Column;
            return column;
        }

        public static VisualElement Spacer()
        {
            var spacer = new VisualElement();
            spacer.style.flexGrow = 1;
            return spacer;
        }

        /// <summary>A kerbal's portrait, or an empty frame when the game has none.</summary>
        public static VisualElement Portrait(KerbalInfo kerbal, float size)
        {
            var portrait = new VisualElement();
            portrait.style.width = portrait.style.height = size;
            portrait.style.flexShrink = 0;
            portrait.style.backgroundColor = Panel2Color;
            portrait.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Cover);
            SetBorder(portrait, LineColor, 1);
            SetRadius(portrait, 3);
            var sprite = kerbal?.Portrait;
            if (sprite != null && sprite.texture != null) portrait.style.backgroundImage = new StyleBackground(sprite);
            return portrait;
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

        /// <summary>A small, quiet button for use inside a panel.</summary>
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

        public static void Fill(VisualElement e)
        {
            e.style.position = Position.Absolute;
            e.style.left = e.style.top = e.style.right = e.style.bottom = 0;
        }

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
    }

    /// <summary>
    /// Hover tips. UI Toolkit's own tooltip shows nothing in the game's panels, so each window floats one label
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
            target.pickingMode = PickingMode.Position;
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
                _tip.style.maxWidth = 280;
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
            // Below the element, kept inside the window; above it if there's no room below.
            var bounds = _root.WorldToLocal(worldBounds);
            float width = _root.layout.width, height = _root.layout.height;
            _tip.style.left = Mathf.Clamp(bounds.x, 4, Math.Max(4, width - 288));
            _tip.style.top = bounds.yMax + 6 < height - 60 ? bounds.yMax + 6 : bounds.y - 52;
        }

        public void Hide()
        {
            if (_tip != null) _tip.style.display = DisplayStyle.None;
        }
    }
}
