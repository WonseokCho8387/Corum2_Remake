using Corum2.Core;
using UnityEngine;

namespace Corum2.Unity
{
    [CreateAssetMenu(menuName = "Corum2/Enemy Definition", fileName = "Enemy")]
    public sealed class EnemyDefinitionAsset : ScriptableObject
    {
        public MetaFields meta = new MetaFields { domainId = "enemy", relatedRIds = new[] { "R-06-04", "R-06-05", "R-06-06", "R-06-08" } };
        public string enemyId = "";
        public float maxHp = 100f;
        public float bodyWidth = 0.8f;
        public float bodyHeight = 1.6f;
        public float gravity = 30f;
        [Header("Reactions")] public float staggerTime = 0.4f;
        [Range(0f, 1f)] public float staggerResistance;
        public float physicalKnockbackScale = 1f;
        [Tooltip("Must be > 0 (R-06-06).")] public float magicKnockbackScale = 1f;
        public LaunchResponse launch = LaunchResponse.Launchable;
        public float launchResistScale = 0.5f;
        public float juggleFallSpeed = 2f;
        public float juggleSlowTime = 0.35f;
        public float downTime = 1f;
        public float wakeupTime = 0.6f;
        public float deathTime = 0.8f;
        public float vanishTime = 0.4f;
        [Header("Default attack (fixture timer, or AI when in range)")] public bool hasAttack;
        public AttackData attack = new AttackData { kind = AttackKind.EnemyMelee };
        public float attackRange = 1.6f;
        public float attackInterval = 1.5f;
        public float firstAttackDelay = 1f;
        [Header("P0-08 AI (WP-04). Type Fixture = WP-02 timed dummy")] public EnemyAIData ai = new EnemyAIData();
        [Tooltip("Used when ai.type is Boss (R-08-09).")] public BossData boss = new BossData();
        [Header("Presentation only")] public Color color = new Color(0.8f, 0.35f, 0.3f);

        public EnemyDefinition ToCore() => new EnemyDefinition
        {
            Meta = meta.ToCore(), Id = enemyId, MaxHp = maxHp, BodyWidth = bodyWidth, BodyHeight = bodyHeight, Gravity = gravity,
            StaggerTime = staggerTime, StaggerResistance = staggerResistance, PhysicalKnockbackScale = physicalKnockbackScale,
            MagicKnockbackScale = magicKnockbackScale, Launch = launch, LaunchResistScale = launchResistScale, JuggleFallSpeed = juggleFallSpeed,
            JuggleSlowTime = juggleSlowTime, DownTime = downTime, WakeupTime = wakeupTime, DeathTime = deathTime, VanishTime = vanishTime,
            Attack = hasAttack ? attack.ToCore() : null, AttackRange = attackRange, AttackInterval = attackInterval, FirstAttackDelay = firstAttackDelay,
            Ai = ai != null && ai.type != AiType.Fixture ? ai.ToCore() : null,
            Boss = ai != null && ai.type == AiType.Boss ? boss.ToCore() : null,
        };
    }
}
