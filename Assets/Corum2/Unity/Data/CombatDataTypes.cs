using System;
using System.Collections.Generic;
using Corum2.Core;
using UnityEngine;

namespace Corum2.Unity
{
    /// <summary>Inspector form of AttackDefinition. Values are test/tuning values (EXP-ATK-00).</summary>
    [Serializable]
    public sealed class AttackData
    {
        public string id = "";
        public AttackKind kind;
        public DamageType damageType;
        [Header("Timing (s)")] public float startup = 0.06f;
        public float active = 0.08f;
        public float recovery = 0.12f;
        [Header("Hitbox (m, from feet, +X = facing)")] public float offsetX = 0.8f;
        public float offsetY = 0.9f;
        public float width = 1f;
        public float height = 1.2f;
        [Header("Result")] [Tooltip("D from the test DamageResolver TEST-DMG-01 (not a game formula, U-09)")]
        public float testDamage = 10f;
        public float hitstop = 0.04f;
        public float knockback = 1.5f;
        public float launchVelocity;
        public int hitEvents = 1;
        public bool unblockable;
        public bool guardBreak;
        public bool superArmor;
        public bool hitsDowned;

        public AttackDefinition ToCore() => new AttackDefinition
        {
            Id = id, Kind = kind, DamageType = damageType, Startup = startup, Active = active, Recovery = recovery,
            HitboxOffsetX = offsetX, HitboxOffsetY = offsetY, HitboxWidth = width, HitboxHeight = height,
            TestDamage = testDamage, Hitstop = hitstop, Knockback = knockback, LaunchVelocity = launchVelocity, HitEvents = hitEvents,
            Unblockable = unblockable, GuardBreak = guardBreak, SuperArmor = superArmor, HitsDowned = hitsDowned,
        };
    }

    [Serializable]
    public sealed class EnemySpawnData
    {
        public string instanceId = "";
        public string enemyId = "";
        public Vector2 position;
        public int facing = -1;
    }
}
