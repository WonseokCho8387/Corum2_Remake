using System.Collections.Generic;

namespace Corum2.Core
{
    public enum AttackKind
    {
        GroundNormal,
        Launcher,
        AirNormal,
        AirDash,
        Dive,
        DownAttack,
        EnemyMelee,
        /// <summary>Core-only test attack for magic rules (R-06-06/12). The magic system itself is P1.</summary>
        TestMagic,
    }

    public enum DamageType
    {
        Physical,
        Magic,
    }

    /// <summary>AttackDefinition (spec 11.2). Timings and sizes are tuning values (EXP-ATK-00), not approved.</summary>
    public sealed class AttackDefinition
    {
        public string Id = "";
        public AttackKind Kind;
        public DamageType DamageType;
        public float Startup = 0.08f;   // s before the hitbox is active
        public float Active = 0.10f;    // s hitbox active
        public float Recovery = 0.14f;  // s after the hitbox
        /// <summary>Hitbox centre relative to the attacker's feet, +X = facing direction.</summary>
        public float HitboxOffsetX = 0.8f;
        public float HitboxOffsetY = 0.9f;
        public float HitboxWidth = 1.0f;
        public float HitboxHeight = 1.0f;
        /// <summary>Non-guarded damage D supplied by the TEST damage resolver (TEST-DMG-01). Not a game formula (U-09).</summary>
        public float TestDamage = 10f;
        public float Hitstop = 0.04f;    // s, initial test values per spec 8.2
        /// <summary>Horizontal knockback speed (m/s). Physical: scaled per target, may be 0. Magic: must be &gt; 0.</summary>
        public float Knockback = 2f;
        public float LaunchVelocity;     // m/s, launcher only
        /// <summary>Explicit multi-hit events (R-06-02). Events split the active window evenly; 1 = normal attack.</summary>
        public int HitEvents = 1;
        public bool Unblockable;         // R-07-06
        public bool GuardBreak;          // R-07-06 "guard cancel"
        public bool SuperArmor;          // attacker keeps acting through physical hits (R-06-11)
        /// <summary>Designated attacks that may hit a downed target (R-06-09/10).</summary>
        public bool HitsDowned;

        public float Total => Startup + Active + Recovery;
    }

    public sealed class CombatProfile
    {
        public DataMeta Meta = new DataMeta();
        public float PlayerMaxHp = 100f;
        public AttackDefinition[] GroundNormals = new AttackDefinition[0];
        public AttackDefinition Launcher = new AttackDefinition();
        public AttackDefinition[] AirNormals = new AttackDefinition[0];
        public AttackDefinition AirDash = new AttackDefinition();
        public AttackDefinition Dive = new AttackDefinition();
        public AttackDefinition DownAttack = new AttackDefinition();
        /// <summary>Chain window after a motion ends (R-04-09, editable). Separate from the input buffer.</summary>
        public float ComboWindow = 0.5f;
        /// <summary>Max fall speed while an air normal attack plays (U-06; EXP-ATK-03).</summary>
        public float AirAttackFallSpeed = 1.5f;
        public float ParryWindow = 0.15f;        // R-07-04 range 0.10-0.20, meaning CF-04 (EXP-PARRY-01)
        public float ParryStunTime = 0.8f;       // enemy "무방비"
        public float ParryKnockback = 6f;
        public float DodgeReactionTime = 0.35f;  // R-06-15 reaction
        public float DodgeKnockback = 5f;
        public float PlayerHitstun = 0.4f;
        public float KnockbackDecel = 30f;       // m/s^2
        public float RollInvulnStart = 0.05f;    // s into the roll (R-06-13 "part of the roll")
        public float RollInvulnEnd = 0.30f;
        public float DiveAreaRadius = 2.5f;      // upgraded dive (R-04-08)
        public float DownAttackRange = 2.5f;
    }

    /// <summary>Separate test switches (spec 11.6): never one combined "upgraded" flag.</summary>
    public sealed class CombatUnlocks
    {
        public DataMeta Meta = new DataMeta();
        public bool DashPierce;
        public bool DiveArea;
    }

    public enum LaunchResponse
    {
        Launchable,
        Resist,
        Immune,
    }

    public sealed class EnemyDefinition
    {
        public DataMeta Meta = new DataMeta();
        public string Id = "";
        public float MaxHp = 100f;
        public float BodyWidth = 0.8f;
        public float BodyHeight = 1.6f;
        public float Gravity = 30f;
        public float StaggerTime = 0.4f;
        /// <summary>0 = full stagger each hit, 1 = never staggers (R-06-04 per-type setting).</summary>
        public float StaggerResistance;
        /// <summary>Physical knockback multiplier; 0 allowed for heavy enemies (R-06-05).</summary>
        public float PhysicalKnockbackScale = 1f;
        /// <summary>Magic knockback multiplier; must be &gt; 0 (R-06-06).</summary>
        public float MagicKnockbackScale = 1f;
        public LaunchResponse Launch = LaunchResponse.Launchable;
        public float LaunchResistScale = 0.5f;
        public float JuggleFallSpeed = 2f;      // R-06-07 slowed fall while being hit in the air
        public float JuggleSlowTime = 0.35f;
        public float DownTime = 1.0f;
        public float WakeupTime = 0.6f;         // fully invincible (R-06-10)
        public float DeathTime = 0.8f;          // death motion
        public float VanishTime = 0.4f;         // vanish effect, then removal (R-06-17)
        /// <summary>Test fixture attack (not AI, P0-08). Null = never attacks.</summary>
        public AttackDefinition Attack;
        public float AttackRange = 1.6f;
        public float AttackInterval = 1.5f;
        public float FirstAttackDelay = 1.0f;
    }

    public sealed class EnemySpawn
    {
        public string InstanceId = "";
        public string EnemyId = "";
        public Vec2 Position;
        public int Facing = -1;
    }

    /// <summary>Test damage resolver (spec 9 / 11.2 DamageResolver boundary). Returns D and the policy id.</summary>
    public static class TestDamageResolver
    {
        public const string PolicyId = "TEST-DMG-01 fixed D per attack (test input, not the remake formula, U-09)";
        public static float Resolve(AttackDefinition a) => a.TestDamage;
    }

    /// <summary>Guard rule R-07-02: g = D x 0.1; g &lt; 1 -> 0. g &gt;= 1 rounding is U-09 (EXP-GUARD-01 floor).</summary>
    public static class GuardRule
    {
        public const float Ratio = 0.1f;
        public const string RoundingPolicy = "EXP-GUARD-01 floor for g>=1 (U-09 undecided)";

        public static float Apply(float d)
        {
            var g = d * Ratio;
            if (g < 1f) return 0f;
            return (float)System.Math.Floor(g + 1e-4f);
        }
    }
}
