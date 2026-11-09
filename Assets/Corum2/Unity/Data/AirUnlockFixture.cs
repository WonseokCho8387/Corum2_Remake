using System;
using System.Collections.Generic;
using Corum2.Core;
using UnityEngine;

namespace Corum2.Unity
{
    [CreateAssetMenu(menuName = "Corum2/Air Unlock Fixture", fileName = "AirUnlocks")]
    public sealed class AirUnlockFixture : ScriptableObject
    {
        public MetaFields meta = new MetaFields { domainId = "unlock.fixture", relatedRIds = new[] { "R-03-03" } };
        [Tooltip("Test switch, not a level rule (spec 11.6).")] public bool extraJump;
        public bool airDash;
        public bool dive;

        public AirUnlocks ToCore() => new AirUnlocks { Meta = meta.ToCore(), ExtraJump = extraJump, AirDash = airDash, Dive = dive };
    }
}
