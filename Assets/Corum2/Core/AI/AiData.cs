using System.Collections.Generic;

namespace Corum2.Core
{
    /// <summary>P0-08 behaviour types (R-08-03..06, R-08-08). Fixture = the WP-02 timed training dummy (not P0-08 AI).</summary>
    public enum AiType
    {
        Fixture,
        /// <summary>R-08-03 reckless melee chaser.</summary>
        Chaser,
        /// <summary>R-08-04 flees after being hit.</summary>
        Fleer,
        /// <summary>R-08-05 jumps to close in or attacks from the air.</summary>
        Jumper,
        /// <summary>R-08-06 waits out of range, shoots in range, then backs off.</summary>
        Ranged,
        /// <summary>R-08-08..11 boss with phases and patterns.</summary>
        Boss,
    }

    /// <summary>Projectile fired by an attack (ranged enemies, boss patterns). Numbers are tuning values.</summary>
    public sealed class ProjectileDefinition
    {
        public float Speed = 8f;
        /// <summary>Distance after which the projectile disappears (U-10 range rules; EXP-AI-06).</summary>
        public float MaxDistance = 12f;
        public float Width = 0.4f;
        public float Height = 0.4f;
        /// <summary>Spawn point relative to the shooter's feet, +X = facing.</summary>
        public float SpawnOffsetX = 0.6f;
        public float SpawnOffsetY = 1.0f;
    }

    /// <summary>EnemyAIProfile (spec 11.2). All distances in metres, times in game seconds. All values are per-monster tuning.</summary>
    public sealed class EnemyAIProfile
    {
        public AiType Type = AiType.Fixture;
        public float MoveSpeed = 2f;
        /// <summary>Distance within which the enemy tracks the player at all (U-10 "추적 허용 범위"; EXP-AI-01).</summary>
        public float TrackRange = 12f;

        // Fleer (R-08-04)
        public float FleeSpeed = 3.5f;
        public float FleeTime = 1.2f;
        /// <summary>After fleeing: true = approach again, false = stay and only attack if the player comes into range.</summary>
        public bool ReapproachAfterFlee = true;

        // Jumper (R-08-05)
        public float JumpMinDistance = 2.5f;
        public float JumpMaxDistance = 5f;
        public float JumpSpeedX = 5f;
        public float JumpVelocityY = 9f;
        public float JumpCooldown = 1.5f;
        /// <summary>Attack used while airborne when the player is within AirAttackRange. Null = jump approach only.</summary>
        public AttackDefinition AirAttack;
        public float AirAttackRange = 1.6f;

        // Ranged (R-08-06)
        public ProjectileDefinition Projectile;
        public float RetreatSpeed = 3f;
        public float RetreatTime = 0.8f;
    }

    public enum PlayerAirCondition
    {
        Any,
        Grounded,
        Airborne,
    }

    /// <summary>One boss attack pattern and when it may be chosen (R-08-08).</summary>
    public sealed class BossPattern
    {
        public string Id = "";
        public AttackDefinition Attack;
        /// <summary>Optional projectile fired when the attack becomes active.</summary>
        public ProjectileDefinition Projectile;
        public float MinDistance;
        public float MaxDistance = 2f;
        public PlayerAirCondition PlayerState = PlayerAirCondition.Any;
        /// <summary>Higher wins; ties go to the least used, then list order (deterministic, EXP-BOSS-01).</summary>
        public int Priority;
        /// <summary>Phase special attack flag (R-08-11), logged.</summary>
        public bool Special;
    }

    public enum TransitionKind
    {
        /// <summary>HP at or below Value x MaxHp (R-08-10).</summary>
        HpBelow,
        /// <summary>Pattern PatternId used at least Count times in this phase (R-08-10 "특정 공격 사용").</summary>
        PatternUsed,
        /// <summary>Named event raised for this boss (R-08-10 "이벤트 발생").</summary>
        Event,
    }

    public sealed class PhaseTransition
    {
        public TransitionKind Kind;
        public float Value;
        public string PatternId = "";
        public int Count = 1;
        public string EventName = "";
        public string TargetPhase = "";
    }

    /// <summary>BossPhase (spec 11.2): everything a phase may change independently (R-08-11).</summary>
    public sealed class BossPhase
    {
        public string Id = "";
        public readonly List<BossPattern> Patterns = new List<BossPattern>();
        public readonly List<PhaseTransition> Transitions = new List<PhaseTransition>();
        /// <summary>Wait between the end of one pattern and the next decision.</summary>
        public float AttackInterval = 1f;
        public float ApproachSpeed = 2f;
        public float RetreatSpeed = 2f;
        /// <summary>Preferred distance band when no pattern fits: approach beyond Max, back off inside Min.</summary>
        public float PreferredMinDistance = 1f;
        public float PreferredMaxDistance = 3f;
        /// <summary>0 = never jumps. Otherwise jumps toward the player at most this often when beyond PreferredMax.</summary>
        public float JumpInterval;
        public float JumpSpeedX = 5f;
        public float JumpVelocityY = 9f;
        /// <summary>Every attack of this phase keeps acting through physical hits (R-06-11 per phase).</summary>
        public bool SuperArmor;
        public float StaggerResistance;
        public LaunchResponse Launch = LaunchResponse.Immune;
        /// <summary>Multiplies attack speed: 2 = twice as fast (startup/active/recovery halved).</summary>
        public float AttackSpeed = 1f;
        /// <summary>Length of the transition into this phase; no new attacks meanwhile (CF-09 details EXP-BOSS-03).</summary>
        public float TransitionTime = 0.8f;
        /// <summary>Presentation ids for the transition (animation / VFX / camera), logged; no real assets in WP-04.</summary>
        public string TransitionAnimation = "";
        public string TransitionVfx = "";
        public string TransitionCamera = "";
    }

    /// <summary>BossDefinition (spec 11.2): per-boss phase list and start phase (R-08-09).</summary>
    public sealed class BossDefinition
    {
        public string StartPhase = "";
        public readonly List<BossPhase> Phases = new List<BossPhase>();

        public BossPhase Find(string id)
        {
            foreach (var p in Phases) if (p.Id == id) return p;
            return null;
        }
    }
}
