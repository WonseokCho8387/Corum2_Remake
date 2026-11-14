using System;
using System.Collections.Generic;

namespace Corum2.Core.P1
{
    public sealed class P1Projectile
    {
        public long Id;
        public long CastId;
        public string SpellId = "";
        public SpellKind Kind;
        public Vec2 Pos;
        /// <summary>Flight direction in degrees (0 = +X, 90 = up).</summary>
        public float Angle;
        public float Speed, Life, Radius, Damage;
        public bool Physical;
        // homing (MAG-18~26)
        public float HomeTurn;
        public string TargetId = "";
        public bool TargetLost;
        // light orb chain (MAG-59 epic) / dark orb pierce + shock (MAG-60)
        public int ChainLeft;
        public float ChainRadius;
        public int PierceLeft;
        public float Shock, ShockRadius;
        // fire ball (MAG-61)
        public float ExplodeDamage, ExplodeRadius, Burn, BurnTick, BurnInterval, Delay, DelayedRatio;
        public int Delayed;
        public readonly HashSet<string> Hit = new HashSet<string>();
        public bool Dead;
    }

    public sealed class P1Explosion
    {
        public long CastId;
        public string SpellId = "";
        public Vec2 Pos;
        public float Radius, Damage, Timer, Burn, BurnTick, BurnInterval;
        public string Label = "";
    }

    public sealed class P1Wall
    {
        public long CastId;
        public float X, Y, Width, Height, Tick, Interval, Remaining;
        public readonly Dictionary<string, float> NextTick = new Dictionary<string, float>();
    }

    public sealed class P1Dot
    {
        public string EnemyId = "";
        public string SpellId = "";
        public float Tick, Interval, Remaining, Timer;
    }

    /// <summary>Active summon (MAG-80~91): one at a time, no HP, never targeted by enemies.</summary>
    public sealed class P1Summon
    {
        public string SpellId = "";
        public SpellKind Kind;
        public float Remaining;
        public Vec2 Pos;
        public float AttackTimer, HealTimer;
        public string TargetId = "";

        public MiniJson.Obj ToJson() => new MiniJson.Obj().Set("spellId", SpellId).Set("kind", Kind.ToString()).Set("remaining", (double)Remaining)
            .Set("x", (double)Pos.X).Set("y", (double)Pos.Y).Set("attackTimer", (double)AttackTimer).Set("healTimer", (double)HealTimer);

        public static P1Summon FromJson(MiniJson.Obj o) => o == null ? null : new P1Summon
        {
            SpellId = o.Str("spellId"), Kind = (SpellKind)Enum.Parse(typeof(SpellKind), o.Str("kind")), Remaining = o.Flt("remaining"),
            Pos = new Vec2(o.Flt("x"), o.Flt("y")), AttackTimer = o.Flt("attackTimer"), HealTimer = o.Flt("healTimer"),
        };
    }
}
