using System;

namespace Corum2.Core
{
    /// <summary>Result of one kinematic move on the combat plane.</summary>
    public struct MoveResult
    {
        public Vec2 Position;
        public bool HitLeft;       // blocked by a solid while moving left
        public bool HitRight;
        public bool HitLeftBound;  // blocked by the map's left edge while moving left
        public bool HitRightBound;
        public bool HitCeiling;
        public bool Landed;        // blocked from below while moving down
    }

    /// <summary>
    /// Axis-separated AABB movement against a map's solids and its left/right range.
    /// Implementation choice IMPL-PHYS-01 (appendix section 2: physics API not fixed by the template).
    /// Pure C#, so the same tick produces the same result in EditMode tests and in play.
    /// </summary>
    public static class Collision
    {
        const float Skin = 1e-4f;

        public static MoveResult Move(MapData map, Vec2 feet, float width, float height, float dx, float dy)
        {
            var r = new MoveResult { Position = feet };
            var pos = feet;

            if (dx != 0f)
            {
                var nx = pos.X + dx;
                var box = Aabb.FromFeet(new Vec2(nx, pos.Y), width, height);
                foreach (var s in map.Solids)
                {
                    if (!box.Overlaps(s)) continue;
                    if (dx > 0f)
                    {
                        nx = Math.Min(nx, s.MinX - width * 0.5f - Skin);
                        r.HitRight = true;
                    }
                    else
                    {
                        nx = Math.Max(nx, s.MaxX + width * 0.5f + Skin);
                        r.HitLeft = true;
                    }
                    box = Aabb.FromFeet(new Vec2(nx, pos.Y), width, height);
                }
                var minX = map.MinX + width * 0.5f;
                var maxX = map.MaxX - width * 0.5f;
                if (dx < 0f && nx <= minX)
                {
                    nx = minX;
                    r.HitLeftBound = true;
                }
                if (dx > 0f && nx >= maxX)
                {
                    nx = maxX;
                    r.HitRightBound = true;
                }
                pos = new Vec2(nx, pos.Y);
            }

            if (dy != 0f)
            {
                var ny = pos.Y + dy;
                var box = Aabb.FromFeet(new Vec2(pos.X, ny), width, height);
                foreach (var s in map.Solids)
                {
                    if (!box.Overlaps(s)) continue;
                    if (dy > 0f)
                    {
                        ny = Math.Min(ny, s.MinY - height - Skin);
                        r.HitCeiling = true;
                    }
                    else
                    {
                        ny = Math.Max(ny, s.MaxY);
                        r.Landed = true;
                    }
                    box = Aabb.FromFeet(new Vec2(pos.X, ny), width, height);
                }
                pos = new Vec2(pos.X, ny);
            }

            r.Position = pos;
            return r;
        }

        /// <summary>True when a solid lies directly under the feet (within a small probe distance).</summary>
        public static bool HasGround(MapData map, Vec2 feet, float width, float probe = 0.02f)
        {
            var box = new Aabb(feet.X - width * 0.5f, feet.Y - probe, feet.X + width * 0.5f, feet.Y + Skin);
            foreach (var s in map.Solids)
                if (box.Overlaps(s)) return true;
            return false;
        }

        public static bool Fits(MapData map, Vec2 feet, float width, float height)
        {
            var box = Aabb.FromFeet(feet, width, height);
            foreach (var s in map.Solids)
                if (box.Overlaps(s)) return false;
            return true;
        }

        /// <summary>Drops a point straight down onto the first solid below (used for spawns).</summary>
        public static Vec2 SnapToGround(MapData map, Vec2 feet, float width, float maxDrop = 50f)
        {
            var best = float.NegativeInfinity;
            foreach (var s in map.Solids)
            {
                if (s.MaxX <= feet.X - width * 0.5f || s.MinX >= feet.X + width * 0.5f) continue;
                if (s.MaxY <= feet.Y + Skin && s.MaxY >= feet.Y - maxDrop && s.MaxY > best) best = s.MaxY;
            }
            return float.IsNegativeInfinity(best) ? feet : new Vec2(feet.X, best);
        }
    }
}
