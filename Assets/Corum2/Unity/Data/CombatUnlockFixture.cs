using Corum2.Core;
using UnityEngine;

namespace Corum2.Unity
{
    [CreateAssetMenu(menuName = "Corum2/Combat Unlock Fixture", fileName = "CombatUnlocks")]
    public sealed class CombatUnlockFixture : ScriptableObject
    {
        public MetaFields meta = new MetaFields { domainId = "combat.unlocks", relatedRIds = new[] { "R-04-06", "R-04-08" } };
        [Tooltip("Test switch (spec 11.6), not a level rule.")] public bool dashPierce;
        public bool diveArea;

        public CombatUnlocks ToCore() => new CombatUnlocks { Meta = meta.ToCore(), DashPierce = dashPierce, DiveArea = diveArea };
    }
}
