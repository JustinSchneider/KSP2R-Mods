using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace AgencyHeritage
{
    /// <summary>A VisualElement that paints itself with vector drawing; call MarkDirtyRepaint after changing what it draws.</summary>
    public class Drawn : VisualElement
    {
        public Action<Painter2D, Rect> Draw;

        public Drawn(Action<Painter2D, Rect> draw, float width = float.NaN, float height = float.NaN)
        {
            Draw = draw;
            pickingMode = PickingMode.Ignore;
            if (!float.IsNaN(width)) style.width = width;
            if (!float.IsNaN(height)) style.height = height;
            generateVisualContent += context =>
            {
                var rect = contentRect;
                if (rect.width <= 0 || rect.height <= 0) return;
                Draw?.Invoke(context.painter2D, rect);
            };
        }
    }

    /// <summary>The museum's drawn artwork: medallion rings, rockets in a livery, kerbal portraits, mission patches.</summary>
    public static class HeritageGraphics
    {
        public static readonly Color Ink = new Color(0.93f, 0.94f, 0.96f);

        public static void Circle(Painter2D p, Vector2 center, float radius, Color fill)
        {
            p.fillColor = fill;
            p.BeginPath();
            p.Arc(center, radius, 0f, 360f);
            p.Fill();
        }

        public static void Ring(Painter2D p, Vector2 center, float radius, float width, Color color, float from = 0, float to = 360)
        {
            p.strokeColor = color;
            p.lineWidth = width;
            p.lineCap = LineCap.Butt;
            p.BeginPath();
            p.Arc(center, radius, from, to);
            p.Stroke();
        }

        public static void Ellipse(Painter2D p, Vector2 c, float rx, float ry, Color fill)
        {
            const float k = 0.5523f;
            p.fillColor = fill;
            p.BeginPath();
            p.MoveTo(new Vector2(c.x + rx, c.y));
            p.BezierCurveTo(new Vector2(c.x + rx, c.y + ry * k), new Vector2(c.x + rx * k, c.y + ry), new Vector2(c.x, c.y + ry));
            p.BezierCurveTo(new Vector2(c.x - rx * k, c.y + ry), new Vector2(c.x - rx, c.y + ry * k), new Vector2(c.x - rx, c.y));
            p.BezierCurveTo(new Vector2(c.x - rx, c.y - ry * k), new Vector2(c.x - rx * k, c.y - ry), new Vector2(c.x, c.y - ry));
            p.BezierCurveTo(new Vector2(c.x + rx * k, c.y - ry), new Vector2(c.x + rx, c.y - ry * k), new Vector2(c.x + rx, c.y));
            p.ClosePath();
            p.Fill();
        }

        public static void Polygon(Painter2D p, Color fill, params Vector2[] points)
        {
            p.fillColor = fill;
            p.BeginPath();
            p.MoveTo(points[0]);
            for (int i = 1; i < points.Length; i++) p.LineTo(points[i]);
            p.ClosePath();
            p.Fill();
        }

        public static void Rect(Painter2D p, Rect r, Color fill) =>
            Polygon(p, fill, new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, r.yMin), new Vector2(r.xMax, r.yMax), new Vector2(r.xMin, r.yMax));

        public static void Line(Painter2D p, Vector2 a, Vector2 b, Color color, float width)
        {
            p.strokeColor = color;
            p.lineWidth = width;
            p.lineCap = LineCap.Round;
            p.BeginPath();
            p.MoveTo(a);
            p.LineTo(b);
            p.Stroke();
        }

        /// <summary>A left-to-right fade, drawn as thin bands.</summary>
        public static void HorizontalGradient(Painter2D p, Rect r, Color from, Color to)
        {
            const int bands = 32;
            float w = r.width / bands;
            for (int i = 0; i < bands; i++)
                Rect(p, new Rect(r.x + i * w - 0.5f, r.y, w + 1f, r.height), Color.Lerp(from, to, i / (bands - 1f)));
        }

        /// <summary>The medallion frame: a ring half in the base color and half in the accent.</summary>
        public static void MedallionRing(Painter2D p, Rect r, Color colorBase, Color colorAccent, bool selected)
        {
            var c = r.center;
            float radius = Mathf.Min(r.width, r.height) / 2f;
            if (selected) Ring(p, c, radius - 1.5f, 3f, Ink);
            float band = radius - (selected ? 7f : 4f);
            Ring(p, c, band, 6f, colorBase, 90, 270);
            Ring(p, c, band, 6f, colorAccent, 270, 450);
        }

        /// <summary>A stacked rocket painted in two colors, in the spirit of a blueprint thumbnail.</summary>
        public static void Rocket(Painter2D p, Rect r, Color colorBase, Color colorAccent, int stages = 3)
        {
            float unit = Mathf.Min(r.width / 60f, r.height / 200f);
            float cx = r.center.x;
            float top = r.center.y - 100 * unit;
            Vector2 P(float x, float y) => new Vector2(cx + x * unit, top + y * unit);
            var dark = new Color(0.13f, 0.13f, 0.14f);

            Polygon(p, colorAccent, P(0, 10), P(10, 32), P(-10, 32));
            float y = 32, height = 140f / stages;
            for (int i = 0; i < stages; i++)
            {
                float half = i == stages - 1 ? 14 : 10 + i * 1.5f;
                Polygon(p, i % 2 == 0 ? colorBase : colorAccent, P(-half, y), P(half, y), P(half, y + height - 4), P(-half, y + height - 4));
                Polygon(p, dark, P(-half, y + height - 4), P(half, y + height - 4), P(half, y + height), P(-half, y + height));
                y += height;
            }
            Polygon(p, colorAccent, P(-14, y - 30), P(-24, y + 4), P(-14, y));
            Polygon(p, colorAccent, P(14, y - 30), P(24, y + 4), P(14, y));
            Polygon(p, new Color(0.23f, 0.23f, 0.24f), P(-6, y), P(6, y), P(9, y + 10), P(-9, y + 10));
        }

        /// <summary>
        /// A kerbal in a helmet, a stand-in for when the game has no portrait. The name picks the skin tone, the
        /// glance, the brows and the expression, so a crew roll doesn't look like a row of clones.
        /// </summary>
        public static void KerbalHead(Painter2D p, Rect r, string name)
        {
            var c = r.center;
            float s = Mathf.Min(r.width, r.height) / 64f;
            uint hash = 2166136261;
            foreach (char ch in name ?? "") hash = (hash ^ ch) * 16777619;
            int Pick(int n, int shift) => (int)((hash >> shift) % (uint)n);

            var skins = new[]
            {
                new Color(0.61f, 0.83f, 0.42f), new Color(0.5f, 0.76f, 0.34f),
                new Color(0.7f, 0.87f, 0.5f), new Color(0.56f, 0.8f, 0.46f),
            };
            var skin = skins[Pick(skins.Length, 0)];
            var ink = new Color(0.07f, 0.07f, 0.07f);
            var line = new Color(0.18f, 0.29f, 0.1f);
            float glance = (Pick(3, 4) - 1) * 1.6f * s;
            float eyeSize = 5f + Pick(3, 8) * 0.6f;

            Circle(p, c, 30 * s, new Color(0.9f, 0.92f, 0.96f));
            Circle(p, c, 26 * s, new Color(0.11f, 0.14f, 0.2f));
            Ellipse(p, c + new Vector2(0, 4 * s), (16 + Pick(3, 12)) * s, 15 * s, skin);
            Ellipse(p, c + new Vector2(-7 * s, 1 * s), eyeSize * s, (eyeSize + 0.5f) * s, Color.white);
            Ellipse(p, c + new Vector2(7 * s, 1 * s), eyeSize * s, (eyeSize + 0.5f) * s, Color.white);
            Circle(p, c + new Vector2(-6.5f * s + glance, 2 * s), 2 * s, ink);
            Circle(p, c + new Vector2(7.5f * s + glance, 2 * s), 2 * s, ink);

            // Brows: level, raised, or determined
            int brows = Pick(3, 16);
            float tilt = brows == 2 ? 2.5f : 0;
            float lift = brows == 1 ? -2f : 0;
            Line(p, c + new Vector2(-12 * s, (-7 + lift - tilt) * s), c + new Vector2(-3 * s, (-7 + lift + tilt) * s), line, 1.6f * s);
            Line(p, c + new Vector2(3 * s, (-7 + lift + tilt) * s), c + new Vector2(12 * s, (-7 + lift - tilt) * s), line, 1.6f * s);

            // Mouth: a smile, a big grin, or a nervous line
            switch (Pick(3, 20))
            {
                case 0:
                    p.strokeColor = line;
                    p.lineWidth = 2 * s;
                    p.lineCap = LineCap.Round;
                    p.BeginPath();
                    p.MoveTo(c + new Vector2(-7 * s, 11 * s));
                    p.QuadraticCurveTo(c + new Vector2(0, 17 * s), c + new Vector2(7 * s, 11 * s));
                    p.Stroke();
                    break;
                case 1:
                    Polygon(p, new Color(0.25f, 0.12f, 0.1f), c + new Vector2(-9 * s, 10 * s), c + new Vector2(9 * s, 10 * s), c + new Vector2(0, 18 * s));
                    Polygon(p, Color.white, c + new Vector2(-8 * s, 10 * s), c + new Vector2(8 * s, 10 * s), c + new Vector2(0, 12.5f * s));
                    break;
                default:
                    Line(p, c + new Vector2(-6 * s, 13 * s), c + new Vector2(-2 * s, 12 * s), line, 2 * s);
                    Line(p, c + new Vector2(-2 * s, 12 * s), c + new Vector2(2 * s, 13.5f * s), line, 2 * s);
                    Line(p, c + new Vector2(2 * s, 13.5f * s), c + new Vector2(6 * s, 12.5f * s), line, 2 * s);
                    break;
            }
            Ring(p, c, 26 * s, 3 * s, new Color(1, 1, 1, 0.18f), 200, 340);
        }

        /// <summary>A round mission patch in the era's colors: planet, rocket, and a dot per crew member.</summary>
        public static void Patch(Painter2D p, Rect r, Color colorBase, Color colorAccent, int crew)
        {
            var c = r.center;
            float s = Mathf.Min(r.width, r.height) / 116f;
            Circle(p, c, 56 * s, colorAccent);
            Circle(p, c, 49 * s, new Color(0.05f, 0.07f, 0.12f));
            for (int i = 0; i < 36; i += 2) Ring(p, c, 49 * s, 3 * s, colorBase, i * 10, i * 10 + 10);
            Circle(p, c + new Vector2(0, 14 * s), 26 * s, new Color(0.18f, 0.42f, 0.7f));
            Ellipse(p, c + new Vector2(-8 * s, 10 * s), 12 * s, 5 * s, new Color(0.37f, 0.68f, 0.35f));
            Ellipse(p, c + new Vector2(12 * s, 20 * s), 8 * s, 4 * s, new Color(0.37f, 0.68f, 0.35f));
            Polygon(p, colorBase, c + new Vector2(0, -36 * s), c + new Vector2(6 * s, -22 * s), c + new Vector2(6 * s, -4 * s),
                c + new Vector2(-6 * s, -4 * s), c + new Vector2(-6 * s, -22 * s));
            Polygon(p, new Color(1f, 0.7f, 0.28f), c + new Vector2(-4 * s, -4 * s), c + new Vector2(4 * s, -4 * s), c + new Vector2(0, 6 * s));
            for (int i = 0; i < crew && i < 7; i++)
            {
                var dot = c + new Vector2((i - (Mathf.Min(crew, 7) - 1) / 2f) * 12 * s, -44 * s);
                Circle(p, dot, 5 * s, Color.white);
                Circle(p, dot, 4 * s, new Color(0.61f, 0.83f, 0.42f));
            }
        }

        public enum Glyph { Rocket, Helmet, Person, Star, Blueprint, Ribbon, Wreath }

        /// <summary>Small icons for the stat tiles, drawn in a 24×24 box.</summary>
        public static void Icon(Painter2D p, Rect r, Glyph glyph, Color color)
        {
            float s = Mathf.Min(r.width, r.height) / 24f;
            Vector2 P(float x, float y) => new Vector2(r.x + x * s, r.y + y * s);
            switch (glyph)
            {
                case Glyph.Rocket:
                    Polygon(p, color, P(12, 2), P(16, 9), P(16, 16), P(8, 16), P(8, 9));
                    Polygon(p, color, P(8, 12), P(4, 18), P(8, 17));
                    Polygon(p, color, P(16, 12), P(20, 18), P(16, 17));
                    Polygon(p, color, P(10, 17), P(14, 17), P(12, 22));
                    break;
                case Glyph.Helmet:
                    Circle(p, P(12, 12), 9 * s, color);
                    Ellipse(p, P(12, 12), 6 * s, 4 * s, new Color(0.09f, 0.11f, 0.15f));
                    break;
                case Glyph.Person:
                    Circle(p, P(12, 8), 4 * s, color);
                    Ellipse(p, P(12, 20), 8 * s, 5 * s, color);
                    break;
                case Glyph.Star:
                    var points = new Vector2[10];
                    for (int i = 0; i < 10; i++)
                    {
                        float angle = (-90 + i * 36) * Mathf.Deg2Rad;
                        float radius = (i % 2 == 0 ? 10 : 4.2f) * s;
                        points[i] = P(12, 13) + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                    }
                    Polygon(p, color, points);
                    break;
                case Glyph.Blueprint:
                    p.strokeColor = color;
                    p.lineWidth = 2 * s;
                    p.BeginPath();
                    p.MoveTo(P(3, 3)); p.LineTo(P(21, 3)); p.LineTo(P(21, 21)); p.LineTo(P(3, 21)); p.ClosePath();
                    p.MoveTo(P(3, 9)); p.LineTo(P(21, 9));
                    p.MoveTo(P(9, 9)); p.LineTo(P(9, 21));
                    p.Stroke();
                    break;
                case Glyph.Wreath:
                    // A memorial wreath: an open ring with a ribbon tied at the bottom.
                    Ring(p, P(12, 11), 7.5f * s, 3f * s, color, 120, 420);
                    Polygon(p, color, P(12, 18), P(8, 23), P(10, 23), P(12, 20));
                    Polygon(p, color, P(12, 18), P(16, 23), P(14, 23), P(12, 20));
                    break;
                case Glyph.Ribbon:
                    Rect(p, new Rect(P(2, 7).x, P(2, 7).y, 20 * s, 10 * s), color);
                    Rect(p, new Rect(P(9, 7).x, P(9, 7).y, 6 * s, 10 * s), new Color(0.09f, 0.11f, 0.15f, 0.5f));
                    break;
            }
        }

        /// <summary>The color a celestial body gets on the timeline.</summary>
        public static Color BodyColor(string body)
        {
            switch (body)
            {
                case "Kerbin": return new Color(0.25f, 0.56f, 0.84f);
                case "Mun": return new Color(0.6f, 0.6f, 0.6f);
                case "Minmus": return new Color(0.62f, 0.86f, 0.78f);
                case "Duna": return new Color(0.78f, 0.36f, 0.2f);
                case "Ike": return new Color(0.45f, 0.43f, 0.42f);
                case "Eve": return new Color(0.55f, 0.3f, 0.7f);
                case "Gilly": return new Color(0.6f, 0.5f, 0.45f);
                case "Moho": return new Color(0.55f, 0.42f, 0.33f);
                case "Dres": return new Color(0.65f, 0.62f, 0.58f);
                case "Jool": return new Color(0.38f, 0.66f, 0.22f);
                case "Laythe": return new Color(0.3f, 0.45f, 0.75f);
                case "Vall": return new Color(0.7f, 0.78f, 0.85f);
                case "Tylo": return new Color(0.8f, 0.76f, 0.7f);
                case "Bop": return new Color(0.5f, 0.42f, 0.35f);
                case "Pol": return new Color(0.82f, 0.78f, 0.5f);
                case "Eeloo": return new Color(0.88f, 0.9f, 0.92f);
                case "Kerbol": return new Color(1f, 0.82f, 0.3f);
                default: return new Color(0.75f, 0.65f, 0.9f);
            }
        }
    }
}
