using System.Linq;
using Corum2.Core;
using UnityEngine;

namespace Corum2.Unity
{
    [CreateAssetMenu(menuName = "Corum2/Combat Config", fileName = "CombatConfig")]
    public sealed class CombatConfig : ScriptableObject
    {
        public MetaFields meta = new MetaFields { domainId = "combat.player", policyIds = new[] { "EXP-ATK-00", "TEST-DMG-01" }, relatedRIds = new[] { "R-04-01", "R-05-02", "R-06-01", "R-07-02" } };
        public float playerMaxHp = 100f;
        [Header("Player death flow (R-06-19; EXP-DEATH-01/03, U-08)")] public float deathMotionTime = 1.2f;
        [Range(0.01f, 1f)] public float reviveHpRatio = 0.5f;
        public float reviveInvulnTime = 1.5f;
        public AttackData[] groundNormals = new AttackData[4];
        public AttackData launcher = new AttackData();
        public AttackData[] airNormals = new AttackData[4];
        public AttackData airDash = new AttackData();
        public AttackData dive = new AttackData();
        public AttackData downAttack = new AttackData();
        [Header("Chain (R-04-09)")] public float comboWindow = 0.5f;
        public float airAttackFallSpeed = 1.5f;
        [Header("Guard / parry (R-07-04, CF-04)")] public float parryWindow = 0.15f;
        public float parryStunTime = 0.8f;
        public float parryKnockback = 6f;
        [Header("Dodge (R-06-15)")] public float dodgeReactionTime = 0.35f;
        public float dodgeKnockback = 5f;
        [Header("Player reactions")] public float playerHitstun = 0.4f;
        public float knockbackDecel = 30f;
        public float rollInvulnStart = 0.05f;
        public float rollInvulnEnd = 0.30f;
        [Header("Upgrades / ranges")] public float diveAreaRadius = 2.5f;
        public float downAttackRange = 2.5f;

        public CombatProfile ToCore() => new CombatProfile
        {
            Meta = meta.ToCore(), PlayerMaxHp = playerMaxHp,
                DeathMotionTime = deathMotionTime, ReviveHpRatio = reviveHpRatio, ReviveInvulnTime = reviveInvulnTime,
            GroundNormals = groundNormals.Select(a => a.ToCore()).ToArray(), Launcher = launcher.ToCore(),
            AirNormals = airNormals.Select(a => a.ToCore()).ToArray(), AirDash = airDash.ToCore(), Dive = dive.ToCore(), DownAttack = downAttack.ToCore(),
            ComboWindow = comboWindow, AirAttackFallSpeed = airAttackFallSpeed, ParryWindow = parryWindow, ParryStunTime = parryStunTime,
            ParryKnockback = parryKnockback, DodgeReactionTime = dodgeReactionTime, DodgeKnockback = dodgeKnockback, PlayerHitstun = playerHitstun,
            KnockbackDecel = knockbackDecel, RollInvulnStart = rollInvulnStart, RollInvulnEnd = rollInvulnEnd, DiveAreaRadius = diveAreaRadius,
            DownAttackRange = downAttackRange,
        };
    }
}
