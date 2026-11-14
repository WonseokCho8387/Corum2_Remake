namespace Corum2.Core
{
    /// <summary>
    /// Optional P1 hook into the P0 combat core. Null (the default, every P0 scene) = P0 behaviour unchanged. When set,
    /// damage goes through the P1 order (BAL-01~09), max HP comes from base + permanent growth, enemy HP losses are
    /// reported (EXP, lifesteal) and a lethal hit is held to the end of the tick so a level-up from the same event can
    /// come first (GRO-45).
    /// </summary>
    public interface ICombatExtension
    {
        float MaxHp { get; }

        /// <summary>HP damage a player attack deals to an enemy. d = the attack's base damage (P0 data value).</summary>
        float OutgoingDamage(PlayerAttack a, EnemyActor en, float d, bool enemyGuarded);

        /// <summary>HP damage an enemy attack deals to the player after guard, defence, rounding and shields.</summary>
        float IncomingDamage(AttackDefinition d, float dmg, bool guarded, string ownerId);

        void OnEnemyHpLost(PlayerAttack a, EnemyActor en, int hitEvent, float lost, bool killed);

        /// <summary>Extra launch height (world units) and hang time (s) from launcher growth (GRO-17).</summary>
        float LaunchHeightBonus { get; }
        float LaunchHangBonus { get; }

        /// <summary>Called at the end of each combat step: true = a lethal hit of this tick is held because a level-up of
        /// the same tick is pending (GRO-45); false = commit the death now.</summary>
        bool HoldDeath();

        void EndTick();
    }
}
