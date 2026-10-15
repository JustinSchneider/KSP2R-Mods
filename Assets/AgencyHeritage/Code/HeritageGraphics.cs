using System;
using System.Collections.Generic;
using System.Linq;
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

        /// <summary>What a mission patch shows, worked out from the flight.</summary>
        public class PatchArt
        {
            public Color Base, Accent;
            public FlightReach Reach;
            public FlightFate Fate;
            public string Destination, HomeWorld;
            public bool Crewed, Docked;
            /// <summary>Part count at launch; 0 if unknown.</summary>
            public int Parts;
            public int Crew, CrewLost, Firsts;
            /// <summary>Every world away from home it went to, in order; two or more make a tour patch.</summary>
            public List<(string Body, FlightReach Reach)> Stops = new List<(string Body, FlightReach Reach)>();
        }

        private static readonly Color PatchField = new Color(0.05f, 0.07f, 0.12f);
        private static readonly Color Flame = new Color(1f, 0.7f, 0.28f);
        private static readonly Color Gold = new Color(1f, 0.8f, 0.25f);
        private static readonly Color Mourning = new Color(0.08f, 0.08f, 0.09f);
        private static readonly Color MourningEdge = new Color(0.86f, 0.42f, 0.4f);

        /// <summary>
        /// A round mission patch in the era's colors. The scene inside shows how far the flight got and where (a hop
        /// over the horizon, an orbit, a flyby, or a lander on another world), the craft is a probe or a crewed rocket
        /// sized by its part count, and the border carries the crew, the firsts the flight earned, and its fate.
        /// </summary>
        public static void Patch(Painter2D p, Rect r, PatchArt art)
        {
            var c = r.center;
            float s = Mathf.Min(r.width, r.height) / 116f;
            Vector2 P(float x, float y) => c + new Vector2(x * s, y * s);
            bool mourning = art.Fate == FlightFate.CrewLost;

            // A flight that lost its crew gets a black border, in mourning.
            Circle(p, c, 56 * s, mourning ? Mourning : art.Accent);
            Circle(p, c, 49 * s, PatchField);
            for (int i = 0; i < 36; i += 2) Ring(p, c, 49 * s, 3 * s, art.Base, i * 10, i * 10 + 10);

            var destination = BodyColor(art.Destination ?? art.HomeWorld);
            var home = BodyColor(art.HomeWorld);
            bool away = art.Destination != null && art.Destination != art.HomeWorld;
            int stages = art.Parts <= 0 ? 2 : art.Parts < 12 ? 1 : art.Parts < 35 ? 2 : art.Parts < 80 ? 3 : 4;
            bool lost = art.Fate == FlightFate.Destroyed;

            // A small home world in the corner and a dashed course from it, for a flight that went somewhere else.
            void FromHome(Vector2 to)
            {
                var from = P(-27, -22);
                Circle(p, from, 7 * s, home);
                DashedCurve(p, from + new Vector2(6 * s, 3 * s), Vector2.Lerp(from, to, 0.5f) + new Vector2(6 * s, -14 * s), to, Ink, 1.4f * s);
            }

            if (art.Stops.Count > 1) Tour(p, c, s, art, home, stages, lost);
            else switch (art.Reach)
            {
                case FlightReach.Suborbital:
                {
                    Horizon(p, c, s, 22, home);
                    DashedCurve(p, P(-26, 20), P(0, -46), P(26, 20), Ink, 1.6f * s);
                    var apex = P(0, -10);
                    if (lost) Burst(p, apex, 10 * s); else Craft(p, apex, 0.42f * s, art.Crewed, stages, art.Base, art.Accent, false);
                    break;
                }
                case FlightReach.Orbit:
                {
                    var center = away ? P(6, 10) : P(0, 8);
                    if (away) FromHome(center + new Vector2(-14 * s, -10 * s));
                    Circle(p, center, 19 * s, destination);
                    OrbitEllipse(p, center, 33 * s, 11 * s, new Color(1, 1, 1, 0.75f), 1.6f * s);
                    var at = center + new Vector2(30 * s, -5 * s);
                    if (lost) Burst(p, at, 8 * s); else Craft(p, at, 0.3f * s, art.Crewed, stages, art.Base, art.Accent, false);
                    break;
                }
                case FlightReach.Flyby:
                {
                    var center = P(10, 10);
                    Circle(p, center, 17 * s, destination);
                    var from = P(-27, -22);
                    Circle(p, from, 7 * s, home);
                    // In past the far side of the body and back out: a hyperbola's swing.
                    DashedCurve(p, from + new Vector2(6 * s, 4 * s), P(70, 6), P(-8, 34), Ink, 1.4f * s);
                    var at = P(-8, 34);
                    if (lost) Burst(p, at, 8 * s); else Craft(p, at, 0.26f * s, art.Crewed, stages, art.Base, art.Accent, false);
                    break;
                }
                case FlightReach.Landed:
                {
                    Horizon(p, c, s, 16, destination);
                    FromHome(P(-4, -2));
                    if (lost) Burst(p, P(4, 10), 11 * s);
                    else
                    {
                        Lander(p, P(6, 10.5f), 0.9f * s, art.Base, art.Accent);
                        // The agency's flag, planted beside it.
                        Line(p, P(-14, 11), P(-14, -2), Ink, 1.4f * s);
                        Polygon(p, art.Base, P(-14, -2), P(-3, -2), P(-3, 5), P(-14, 5));
                        Polygon(p, art.Accent, P(-14, 1), P(-3, 1), P(-3, 3), P(-14, 3));
                    }
                    break;
                }
                default:
                    // A launch the mod didn't follow: the agency's rocket lifting off its home world.
                    Circle(p, P(0, 14), 26 * s, new Color(0.18f, 0.42f, 0.7f));
                    Ellipse(p, P(-8, 10), 12 * s, 5 * s, new Color(0.37f, 0.68f, 0.35f));
                    Ellipse(p, P(12, 20), 8 * s, 4 * s, new Color(0.37f, 0.68f, 0.35f));
                    Craft(p, P(0, -18), 0.55f * s, art.Crewed || art.Crew > 0, stages, art.Base, art.Accent, true);
                    break;
            }

            if (art.Docked) DockingMark(p, P(26, -26), s);

            // Crew along the top: one dot each, dark and red-ringed for those who didn't come home.
            int shown = Mathf.Min(art.Crew, 7);
            for (int i = 0; i < shown; i++)
            {
                var dot = P((i - (shown - 1) / 2f) * 12, -44);
                bool gone = i >= shown - art.CrewLost;
                Circle(p, dot, 5 * s, gone ? MourningEdge : Color.white);
                Circle(p, dot, 4 * s, gone ? Mourning : new Color(0.61f, 0.83f, 0.42f));
            }

            // Firsts along the bottom: a gold star each.
            int stars = Mathf.Min(art.Firsts, 5);
            for (int i = 0; i < stars; i++)
            {
                float angle = (90 + (i - (stars - 1) / 2f) * 13) * Mathf.Deg2Rad;
                Star(p, c + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 52.5f * s, 6 * s, Gold, PatchField);
            }
        }

        /// <summary>Where a tour's worlds sit on the patch, and how big, for two, three or four of them.</summary>
        private static readonly (Vector2 At, float Radius)[][] TourSlots =
        {
            new[] { (new Vector2(-8, 4), 14f), (new Vector2(22, 22), 13f) },
            new[] { (new Vector2(-14, 6), 12f), (new Vector2(10, 26), 11f), (new Vector2(26, -2), 11f) },
            new[] { (new Vector2(-20, 6), 10f), (new Vector2(-2, 28), 9f), (new Vector2(22, 22), 9f), (new Vector2(24, -8), 9f) },
        };

        /// <summary>
        /// A flight that went to several worlds: a dashed course from home through each in turn, with an orbit ring
        /// round those it orbited and the agency's flag on those it landed on. The craft waits at the last.
        /// </summary>
        private static void Tour(Painter2D p, Vector2 c, float s, PatchArt art, Color home, int stages, bool lost)
        {
            var stops = art.Stops.Take(4).ToList();
            var slots = TourSlots[stops.Count - 2];
            var from = c + new Vector2(-27, -22) * s;
            Circle(p, from, 7 * s, home);
            float fromRadius = 7;
            for (int i = 0; i < stops.Count; i++)
            {
                var at = c + slots[i].At * s;
                var direction = (at - from).normalized;
                var a = from + direction * (fromRadius + 2) * s;
                var b = at - direction * (slots[i].Radius + 3) * s;
                var bend = new Vector2(direction.y, -direction.x) * 6 * s;
                DashedCurve(p, a, Vector2.Lerp(a, b, 0.5f) + bend, b, Ink, 1.3f * s, 3.5f);
                from = at;
                fromRadius = slots[i].Radius;
            }
            for (int i = 0; i < stops.Count; i++)
            {
                var at = c + slots[i].At * s;
                float radius = slots[i].Radius * s;
                Circle(p, at, radius, BodyColor(stops[i].Body));
                if (stops[i].Reach == FlightReach.Orbit)
                    OrbitEllipse(p, at, radius + 6 * s, radius * 0.38f + 2 * s, new Color(1, 1, 1, 0.75f), 1.3f * s);
                else if (stops[i].Reach == FlightReach.Landed)
                {
                    var foot = at + new Vector2(0, -radius + 1 * s);
                    Line(p, foot, foot + new Vector2(0, -9 * s), Ink, 1.2f * s);
                    Polygon(p, art.Base, foot + new Vector2(0, -9 * s), foot + new Vector2(7 * s, -9 * s), foot + new Vector2(7 * s, -4.5f * s), foot + new Vector2(0, -4.5f * s));
                }
            }
            var last = c + slots[stops.Count - 1].At * s + new Vector2(slots[stops.Count - 1].Radius + 5, -slots[stops.Count - 1].Radius) * s;
            if (lost) Burst(p, last, 7 * s);
            else Craft(p, last, 0.2f * s, art.Crewed, stages, art.Base, art.Accent, false);
        }

        /// <summary>The ground of a world filling the patch below a gently curved horizon at height y.</summary>
        private static void Horizon(Painter2D p, Vector2 c, float s, float y, Color fill)
        {
            const float disc = 47, bulge = 6;
            // Where the horizon meets the patch's edge, then the edge round the bottom and back.
            float half = Mathf.Sqrt(disc * disc - y * y);
            float edgeAngle = Mathf.Atan2(y, half) * Mathf.Rad2Deg;
            p.fillColor = fill;
            p.BeginPath();
            p.MoveTo(c + new Vector2(-half * s, y * s));
            p.QuadraticCurveTo(c + new Vector2(0, (y - 2 * bulge) * s), c + new Vector2(half * s, y * s));
            p.Arc(c, disc * s, edgeAngle, 180 - edgeAngle);
            p.ClosePath();
            p.Fill();
            // A lighter rim along the horizon.
            p.strokeColor = Color.Lerp(fill, Color.white, 0.35f);
            p.lineWidth = 1.5f * s;
            p.BeginPath();
            p.MoveTo(c + new Vector2(-half * s, y * s));
            p.QuadraticCurveTo(c + new Vector2(0, (y - 2 * bulge) * s), c + new Vector2(half * s, y * s));
            p.Stroke();
        }

        /// <summary>A quadratic curve drawn as dashes.</summary>
        public static void DashedCurve(Painter2D p, Vector2 a, Vector2 control, Vector2 b, Color color, float width, float dash = 4f)
        {
            Vector2 At(float t) => (1 - t) * (1 - t) * a + 2 * (1 - t) * t * control + t * t * b;
            float length = 0;
            var last = a;
            for (int i = 1; i <= 16; i++) { var next = At(i / 16f); length += Vector2.Distance(last, next); last = next; }
            int segments = Mathf.Max(2, Mathf.RoundToInt(length / (dash * 1.8f)));
            for (int i = 0; i < segments; i++)
            {
                float t0 = i / (float)segments;
                Line(p, At(t0), At(t0 + 0.55f / segments), color, width);
            }
        }

        private static void OrbitEllipse(Painter2D p, Vector2 c, float rx, float ry, Color color, float width)
        {
            const float k = 0.5523f;
            p.strokeColor = color;
            p.lineWidth = width;
            p.BeginPath();
            p.MoveTo(new Vector2(c.x + rx, c.y));
            p.BezierCurveTo(new Vector2(c.x + rx, c.y + ry * k), new Vector2(c.x + rx * k, c.y + ry), new Vector2(c.x, c.y + ry));
            p.BezierCurveTo(new Vector2(c.x - rx * k, c.y + ry), new Vector2(c.x - rx, c.y + ry * k), new Vector2(c.x - rx, c.y));
            p.BezierCurveTo(new Vector2(c.x - rx, c.y - ry * k), new Vector2(c.x - rx * k, c.y - ry), new Vector2(c.x, c.y - ry));
            p.BezierCurveTo(new Vector2(c.x + rx * k, c.y - ry), new Vector2(c.x + rx, c.y - ry * k), new Vector2(c.x + rx, c.y));
            p.Stroke();
        }

        /// <summary>
        /// The flight's craft, centered at c, in a box about 60×110 units: a crewed rocket with a capsule and one to four
        /// stages, or a probe core with antenna and solar panels.
        /// </summary>
        private static void Craft(Painter2D p, Vector2 c, float u, bool crewed, int stages, Color colorBase, Color colorAccent, bool flame)
        {
            Vector2 P(float x, float y) => c + new Vector2(x * u, y * u);
            var dark = new Color(0.13f, 0.13f, 0.14f);
            if (!crewed && stages <= 1)
            {
                // A bare probe: a ball with a dish and panels.
                Rect(p, new Rect(P(-30, -4).x, P(-30, -4).y, 18 * u, 8 * u), colorAccent);
                Rect(p, new Rect(P(12, -4).x, P(12, -4).y, 18 * u, 8 * u), colorAccent);
                Circle(p, c, 12 * u, colorBase);
                Line(p, P(0, -12), P(0, -26), Ink, 2 * u);
                Circle(p, P(0, -27), 3.5f * u, Ink);
                return;
            }

            float height = 20f * stages;
            float top = -height / 2 - 12;
            if (crewed) Polygon(p, colorAccent, P(0, top - 6), P(10, top + 12), P(-10, top + 12));   // capsule
            else
            {
                Circle(p, P(0, top + 4), 9 * u, colorAccent);                                            // probe core
                Line(p, P(0, top - 5), P(0, top - 16), Ink, 2 * u);
            }
            float y = top + 12;
            for (int i = 0; i < stages; i++)
            {
                float half = 9 + i * 2;
                Polygon(p, i % 2 == 0 ? colorBase : colorAccent, P(-half, y), P(half, y), P(half, y + 17), P(-half, y + 17));
                Polygon(p, dark, P(-half, y + 17), P(half, y + 17), P(half, y + 20), P(-half, y + 20));
                y += 20;
            }
            float w = 9 + (stages - 1) * 2;
            Polygon(p, colorAccent, P(-w, y - 18), P(-w - 9, y + 2), P(-w, y));
            Polygon(p, colorAccent, P(w, y - 18), P(w + 9, y + 2), P(w, y));
            if (stages >= 4)
            {
                // Heavy lift: a pair of boosters.
                Polygon(p, colorBase, P(-w - 14, y - 34), P(-w - 6, y - 40), P(-w - 6, y), P(-w - 14, y));
                Polygon(p, colorBase, P(w + 6, y - 40), P(w + 14, y - 34), P(w + 14, y), P(w + 6, y));
            }
            if (flame) Polygon(p, Flame, P(-6, y), P(6, y), P(0, y + 16));
        }

        /// <summary>A little lander on its legs, standing on the ground at its feet.</summary>
        private static void Lander(Painter2D p, Vector2 feet, float u, Color colorBase, Color colorAccent)
        {
            Vector2 P(float x, float y) => feet + new Vector2(x * u, y * u);
            Line(p, P(-6, -10), P(-13, 0), Ink, 1.6f * u);
            Line(p, P(6, -10), P(13, 0), Ink, 1.6f * u);
            Line(p, P(-16, 0), P(-10, 0), Ink, 1.6f * u);
            Line(p, P(10, 0), P(16, 0), Ink, 1.6f * u);
            Polygon(p, colorBase, P(-9, -18), P(9, -18), P(9, -8), P(-9, -8));
            Polygon(p, colorAccent, P(-6, -18), P(6, -18), P(3, -27), P(-3, -27));
        }

        /// <summary>The flight came apart: a burst of fire where the craft would be.</summary>
        private static void Burst(Painter2D p, Vector2 c, float radius)
        {
            Polygon(p, new Color(0.95f, 0.38f, 0.32f), StarPoints(c, radius, radius * 0.5f, 8, 11));
            Polygon(p, Flame, StarPoints(c, radius * 0.62f, radius * 0.3f, 8, -11));
            Circle(p, c, radius * 0.22f, new Color(1f, 0.95f, 0.7f));
        }

        /// <summary>Two craft meeting nose to nose: the flight docked.</summary>
        private static void DockingMark(Painter2D p, Vector2 c, float s)
        {
            Circle(p, c, 9 * s, PatchField);
            Rect(p, new Rect(c.x - 7 * s, c.y - 2.5f * s, 5 * s, 5 * s), Ink);
            Rect(p, new Rect(c.x + 2 * s, c.y - 2.5f * s, 5 * s, 5 * s), Ink);
            Line(p, c + new Vector2(-2 * s, 0), c + new Vector2(2 * s, 0), Ink, 1.2f * s);
        }

        private static void Star(Painter2D p, Vector2 c, float radius, Color fill, Color outline)
        {
            Polygon(p, outline, StarPoints(c, radius + 1.5f, (radius + 1.5f) * 0.45f, 5, -90));
            Polygon(p, fill, StarPoints(c, radius, radius * 0.45f, 5, -90));
        }

        private static Vector2[] StarPoints(Vector2 c, float outer, float inner, int points, float startDegrees)
        {
            var result = new Vector2[points * 2];
            for (int i = 0; i < result.Length; i++)
            {
                float angle = (startDegrees + i * 180f / points) * Mathf.Deg2Rad;
                float radius = i % 2 == 0 ? outer : inner;
                result[i] = c + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
            }
            return result;
        }

        /// <summary>
        /// A backdrop for a craft in flight: space fading to a glow of atmosphere over the curve of the home world,
        /// with a scatter of stars placed by the seed so each exhibit's sky is its own.
        /// </summary>
        public static void Sky(Painter2D p, Rect r, Color world, uint seed)
        {
            var space = new Color(0.02f, 0.03f, 0.07f);
            var glow = Color.Lerp(world, Color.white, 0.25f);
            const int bands = 40;
            float band = r.height / bands;
            for (int i = 0; i < bands; i++)
            {
                float t = i / (bands - 1f);
                var color = Color.Lerp(space, Color.Lerp(space, glow, 0.55f), t * t * t);
                Rect(p, new Rect(r.x, r.y + i * band - 0.5f, r.width, band + 1f), color);
            }
            for (int i = 0; i < 26; i++)
            {
                seed = seed * 1664525 + 1013904223;
                float x = r.x + (seed >> 8) % 1000 / 1000f * r.width;
                seed = seed * 1664525 + 1013904223;
                float y = r.y + (seed >> 8) % 1000 / 1000f * r.height * 0.6f;
                Circle(p, new Vector2(x, y), (seed >> 4) % 3 == 0 ? 1.1f : 0.7f, new Color(1, 1, 1, 0.35f + (seed >> 12) % 50 / 100f));
            }
            float radius = r.width * 1.8f;
            var center = new Vector2(r.center.x - r.width * 0.2f, r.yMax + radius - 34);
            Ring(p, center, radius + 4, 10, new Color(glow.r, glow.g, glow.b, 0.18f));
            Circle(p, center, radius, Color.Lerp(world, Color.black, 0.35f));
            Ring(p, center, radius - 1, 2, new Color(glow.r, glow.g, glow.b, 0.7f));
        }

        /// <summary>
        /// Light and shade over a flag picture so it reads as cloth: soft vertical folds that grow toward the fly end,
        /// a little shadow along the hoist where it's tied to the pole, and a lift of light along the top.
        /// </summary>
        public static void ClothFolds(Painter2D p, Rect r)
        {
            const int strips = 48;
            float w = r.width / strips;
            for (int i = 0; i < strips; i++)
            {
                float t = i / (strips - 1f);
                float wave = Mathf.Sin(t * Mathf.PI * 3.2f + 0.6f) * (0.35f + 0.65f * t);
                var tint = wave > 0 ? new Color(1, 1, 1, 0.16f * wave) : new Color(0, 0, 0, -0.28f * wave);
                Rect(p, new Rect(r.x + i * w - 0.3f, r.y, w + 0.6f, r.height), tint);
            }
            Rect(p, new Rect(r.x, r.y, 6, r.height), new Color(0, 0, 0, 0.25f));
            Rect(p, new Rect(r.x, r.y, r.width, 3), new Color(1, 1, 1, 0.12f));
            Rect(p, new Rect(r.x, r.yMax - 4, r.width, 4), new Color(0, 0, 0, 0.18f));
        }

        /// <summary>A rocket's exhaust, hanging down from the top centre of its box: a white core in yellow and orange.</summary>
        public static void Plume(Painter2D p, Rect r)
        {
            void Flame(float widthShare, float lengthShare, Color color)
            {
                float half = r.width * widthShare / 2, length = r.height * lengthShare;
                var top = new Vector2(r.center.x, r.y);
                p.fillColor = color;
                p.BeginPath();
                p.MoveTo(top + new Vector2(-half, 0));
                p.BezierCurveTo(top + new Vector2(-half * 1.3f, length * 0.35f), top + new Vector2(-half * 0.35f, length * 0.8f), top + new Vector2(0, length));
                p.BezierCurveTo(top + new Vector2(half * 0.35f, length * 0.8f), top + new Vector2(half * 1.3f, length * 0.35f), top + new Vector2(half, 0));
                p.ClosePath();
                p.Fill();
            }
            Flame(1f, 1f, new Color(1f, 0.45f, 0.15f, 0.45f));
            Flame(0.75f, 0.75f, new Color(1f, 0.7f, 0.28f, 0.8f));
            Flame(0.45f, 0.45f, new Color(1f, 0.93f, 0.7f, 0.95f));
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
