using System;
using System.Collections.Generic;
using Corum2.Core;
using UnityEngine;

namespace Corum2.Unity
{
    [Serializable]
    public sealed class ProjectileData
    {
        public float speed = 8f;
        public float maxDistance = 12f;
        public float width = 0.4f;
        public float height = 0.4f;
        public float spawnOffsetX = 0.6f;
        public float spawnOffsetY = 1f;

        public ProjectileDefinition ToCore() => new ProjectileDefinition
        {
            Speed = speed, MaxDistance = maxDistance, Width = width, Height = height, SpawnOffsetX = spawnOffsetX, SpawnOffsetY = spawnOffsetY,
        };
    }

    /// <summary>Inspector form of EnemyAIProfile (spec 11.2). All values are per-monster tuning (TEST-AI-01 in WP-04).</summary>
    [Serializable]
    public sealed class EnemyAIData
    {
        public AiType type = AiType.Fixture;
        public float moveSpeed = 2f;
        [Tooltip("Beyond this distance the enemy waits (EXP-AI-01, U-10).")] public float trackRange = 12f;
        [Header("Fleer (R-08-04)")] public float fleeSpeed = 3.5f;
        public float fleeTime = 1.2f;
        public bool reapproachAfterFlee = true;
        [Header("Jumper (R-08-05)")] public float jumpMinDistance = 2.5f;
        public float jumpMaxDistance = 5f;
        public float jumpSpeedX = 5f;
        public float jumpVelocityY = 9f;
        public float jumpCooldown = 1.5f;
        public bool hasAirAttack;
        public AttackData airAttack = new AttackData { kind = AttackKind.EnemyMelee };
        public float airAttackRange = 1.6f;
        [Header("Ranged (R-08-06)")] public bool hasProjectile;
        public ProjectileData projectile = new ProjectileData();
        public float retreatSpeed = 3f;
        public float retreatTime = 0.8f;

        public EnemyAIProfile ToCore() => new EnemyAIProfile
        {
            Type = type, MoveSpeed = moveSpeed, TrackRange = trackRange, FleeSpeed = fleeSpeed, FleeTime = fleeTime,
            ReapproachAfterFlee = reapproachAfterFlee, JumpMinDistance = jumpMinDistance, JumpMaxDistance = jumpMaxDistance,
            JumpSpeedX = jumpSpeedX, JumpVelocityY = jumpVelocityY, JumpCooldown = jumpCooldown,
            AirAttack = hasAirAttack ? airAttack.ToCore() : null, AirAttackRange = airAttackRange,
            Projectile = hasProjectile ? projectile.ToCore() : null, RetreatSpeed = retreatSpeed, RetreatTime = retreatTime,
        };
    }

    [Serializable]
    public sealed class BossPatternData
    {
        public string id = "";
        public AttackData attack = new AttackData { kind = AttackKind.EnemyMelee };
        public bool hasProjectile;
        public ProjectileData projectile = new ProjectileData();
        public float minDistance;
        public float maxDistance = 2f;
        public PlayerAirCondition playerState = PlayerAirCondition.Any;
        public int priority;
        public bool special;

        public BossPattern ToCore() => new BossPattern
        {
            Id = id, Attack = attack.ToCore(), Projectile = hasProjectile ? projectile.ToCore() : null, MinDistance = minDistance,
            MaxDistance = maxDistance, PlayerState = playerState, Priority = priority, Special = special,
        };
    }

    [Serializable]
    public sealed class PhaseTransitionData
    {
        public TransitionKind kind;
        [Tooltip("HpBelow: ratio of max HP (0..1).")] public float value;
        public string patternId = "";
        public int count = 1;
        public string eventName = "";
        public string targetPhase = "";

        public PhaseTransition ToCore() => new PhaseTransition
        {
            Kind = kind, Value = value, PatternId = patternId, Count = count, EventName = eventName, TargetPhase = targetPhase,
        };
    }

    [Serializable]
    public sealed class BossPhaseData
    {
        public string id = "";
        public List<BossPatternData> patterns = new List<BossPatternData>();
        public List<PhaseTransitionData> transitions = new List<PhaseTransitionData>();
        public float attackInterval = 1f;
        public float approachSpeed = 2f;
        public float retreatSpeed = 2f;
        public float preferredMinDistance = 1f;
        public float preferredMaxDistance = 3f;
        public float jumpInterval;
        public float jumpSpeedX = 5f;
        public float jumpVelocityY = 9f;
        public bool superArmor;
        [Range(0f, 1f)] public float staggerResistance;
        public LaunchResponse launch = LaunchResponse.Immune;
        public float attackSpeed = 1f;
        public float transitionTime = 0.8f;
        [Header("Presentation ids (logged; no assets in WP-04)")] public string transitionAnimation = "";
        public string transitionVfx = "";
        public string transitionCamera = "";

        public BossPhase ToCore()
        {
            var p = new BossPhase
            {
                Id = id, AttackInterval = attackInterval, ApproachSpeed = approachSpeed, RetreatSpeed = retreatSpeed,
                PreferredMinDistance = preferredMinDistance, PreferredMaxDistance = preferredMaxDistance, JumpInterval = jumpInterval,
                JumpSpeedX = jumpSpeedX, JumpVelocityY = jumpVelocityY, SuperArmor = superArmor, StaggerResistance = staggerResistance,
                Launch = launch, AttackSpeed = attackSpeed, TransitionTime = transitionTime, TransitionAnimation = transitionAnimation,
                TransitionVfx = transitionVfx, TransitionCamera = transitionCamera,
            };
            foreach (var x in patterns) p.Patterns.Add(x.ToCore());
            foreach (var t in transitions) p.Transitions.Add(t.ToCore());
            return p;
        }
    }

    [Serializable]
    public sealed class BossData
    {
        public string startPhase = "";
        public List<BossPhaseData> phases = new List<BossPhaseData>();

        public BossDefinition ToCore()
        {
            var b = new BossDefinition { StartPhase = startPhase };
            foreach (var p in phases) b.Phases.Add(p.ToCore());
            return b;
        }
    }
}
