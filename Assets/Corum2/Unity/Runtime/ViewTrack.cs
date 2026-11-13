using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Corum2.Core;

namespace Corum2.Unity
{
    /// <summary>
    /// WP-11 TC-UNITY-02 diagnostics: per motion class, the rendered body and the judged body (sim AABB) are compared every frame.
    /// - penetration: how deep a box overlaps any map solid (0 when only touching; ground contact is a touch);
    /// - size: |rendered width - judged width| + |rendered height - judged height| (poses that deliberately change the drawn shape are not sized);
    /// - outside: how far the rendered feet lie outside the segment between the judged positions of the previous and current tick
    ///   (the view interpolates between the two ticks; 0 means it never shows a place the simulation was not in that tick).
    /// </summary>
    public sealed class ViewTrack
    {
        public sealed class Stat
        {
            public long Frames;
            public float MaxRenderPenetration, MaxSimPenetration, MaxSizeError, MaxOutside, MaxStep;
        }

        public readonly SortedDictionary<string, Stat> Classes = new SortedDictionary<string, Stat>(StringComparer.Ordinal);

        /// <summary>Overlap depth of two boxes: the smaller of the x / y overlaps when both exceed the AABB epsilon, else 0.</summary>
        public static float Penetration(Aabb a, Aabb b)
        {
            var ox = Math.Min(a.MaxX, b.MaxX) - Math.Max(a.MinX, b.MinX);
            var oy = Math.Min(a.MaxY, b.MaxY) - Math.Max(a.MinY, b.MinY);
            return ox > Aabb.Epsilon && oy > Aabb.Epsilon ? Math.Min(ox, oy) : 0f;
        }

        public static float MaxPenetration(MapData map, Aabb box)
        {
            var m = 0f;
            if (map == null) return m;
            foreach (var s in map.Solids) m = Math.Max(m, Penetration(box, s));
            return m;
        }

        /// <summary>Distance from point p to the segment a-b (2D).</summary>
        public static float OutsideSegment(Vec2 p, Vec2 a, Vec2 b)
        {
            var abx = b.X - a.X;
            var aby = b.Y - a.Y;
            var len2 = abx * abx + aby * aby;
            var t = len2 <= 1e-12f ? 0f : Math.Max(0f, Math.Min(1f, ((p.X - a.X) * abx + (p.Y - a.Y) * aby) / len2));
            var dx = p.X - (a.X + t * abx);
            var dy = p.Y - (a.Y + t * aby);
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        public void Add(string cls, float renderPen, float simPen, float sizeError, float outside, float step)
        {
            if (!Classes.TryGetValue(cls, out var s)) Classes[cls] = s = new Stat();
            s.Frames++;
            s.MaxRenderPenetration = Math.Max(s.MaxRenderPenetration, renderPen);
            s.MaxSimPenetration = Math.Max(s.MaxSimPenetration, simPen);
            s.MaxSizeError = Math.Max(s.MaxSizeError, sizeError);
            s.MaxOutside = Math.Max(s.MaxOutside, outside);
            s.MaxStep = Math.Max(s.MaxStep, step);
        }

        /// <summary>"cls:frames/renderPen/simPen/size/outside/step;..." with 4 decimals.</summary>
        public string Table()
        {
            var sb = new StringBuilder();
            foreach (var kv in Classes)
            {
                var s = kv.Value;
                if (sb.Length > 0) sb.Append(';');
                sb.Append(kv.Key).Append(':').Append(s.Frames).Append('/')
                    .Append(F(s.MaxRenderPenetration)).Append('/').Append(F(s.MaxSimPenetration)).Append('/')
                    .Append(F(s.MaxSizeError)).Append('/').Append(F(s.MaxOutside)).Append('/').Append(F(s.MaxStep));
            }
            return sb.ToString();
        }

        static string F(float v) => v.ToString("0.0000", CultureInfo.InvariantCulture);

        public static string PlayerClass(MotorState s, float velX)
        {
            switch (s)
            {
                case MotorState.Idle: return "still";
                case MotorState.Walk:
                case MotorState.Run: return "walkRun";
                case MotorState.Airborne: return "air";
                case MotorState.AirDash: return "airDash";
                case MotorState.Dive: return "dive";
                case MotorState.HitStun: return Math.Abs(velX) > 0.01f ? "knockback" : "hitStun";
                case MotorState.Slide:
                case MotorState.Roll: return "slideRoll";
                default: return "other";
            }
        }

        public static string EnemyClass(EnemyActor e)
        {
            if (!e.Grounded) return "air";
            if (Math.Abs(e.VelX) > 0.01f) return "knockback";
            if (Math.Abs(e.MoveVelX) > 0.01f) return "walk";
            return "still";
        }
    }
}
