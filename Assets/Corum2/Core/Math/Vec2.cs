using System;
using System.Globalization;

namespace Corum2.Core
{
    /// <summary>Horizontal (X) / height (Y) vector on the combat plane. Depth is not represented in Core (R-01-01).</summary>
    public readonly struct Vec2 : IEquatable<Vec2>
    {
        public readonly float X;
        public readonly float Y;

        public Vec2(float x, float y)
        {
            X = x;
            Y = y;
        }

        public static readonly Vec2 Zero = new Vec2(0f, 0f);

        public static Vec2 operator +(Vec2 a, Vec2 b) => new Vec2(a.X + b.X, a.Y + b.Y);
        public static Vec2 operator -(Vec2 a, Vec2 b) => new Vec2(a.X - b.X, a.Y - b.Y);
        public static Vec2 operator *(Vec2 a, float s) => new Vec2(a.X * s, a.Y * s);

        public Vec2 WithX(float x) => new Vec2(x, Y);
        public Vec2 WithY(float y) => new Vec2(X, y);

        public bool Equals(Vec2 other) => X.Equals(other.X) && Y.Equals(other.Y);
        public override bool Equals(object obj) => obj is Vec2 v && Equals(v);
        public override int GetHashCode() => (X.GetHashCode() * 397) ^ Y.GetHashCode();

        public override string ToString() =>
            "(" + X.ToString("0.###", CultureInfo.InvariantCulture) + ", " + Y.ToString("0.###", CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>Axis-aligned box on the combat plane.</summary>
    public readonly struct Aabb
    {
        public const float Epsilon = 1e-4f;

        public readonly float MinX;
        public readonly float MinY;
        public readonly float MaxX;
        public readonly float MaxY;

        public Aabb(float minX, float minY, float maxX, float maxY)
        {
            MinX = minX;
            MinY = minY;
            MaxX = maxX;
            MaxY = maxY;
        }

        public static Aabb FromCenterSize(float cx, float cy, float w, float h) =>
            new Aabb(cx - w * 0.5f, cy - h * 0.5f, cx + w * 0.5f, cy + h * 0.5f);

        /// <summary>Box whose bottom-centre is the given feet position.</summary>
        public static Aabb FromFeet(Vec2 feet, float width, float height) =>
            new Aabb(feet.X - width * 0.5f, feet.Y, feet.X + width * 0.5f, feet.Y + height);

        public float Width => MaxX - MinX;
        public float Height => MaxY - MinY;
        public float CenterX => (MinX + MaxX) * 0.5f;
        public float CenterY => (MinY + MaxY) * 0.5f;

        /// <summary>Strict overlap: touching edges do not count.</summary>
        public bool Overlaps(Aabb o) =>
            MinX < o.MaxX - Epsilon && MaxX > o.MinX + Epsilon &&
            MinY < o.MaxY - Epsilon && MaxY > o.MinY + Epsilon;

        public bool ContainsPoint(Vec2 p) => p.X >= MinX && p.X <= MaxX && p.Y >= MinY && p.Y <= MaxY;

        public override string ToString() =>
            string.Format(CultureInfo.InvariantCulture, "[{0:0.###},{1:0.###} .. {2:0.###},{3:0.###}]", MinX, MinY, MaxX, MaxY);
    }

    public static class MathUtil
    {
        public static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);

        public static float MoveTowards(float current, float target, float maxDelta)
        {
            if (Math.Abs(target - current) <= maxDelta) return target;
            return current + Math.Sign(target - current) * maxDelta;
        }

        public static int Sign(float v) => v > 0f ? 1 : (v < 0f ? -1 : 0);
    }
}
