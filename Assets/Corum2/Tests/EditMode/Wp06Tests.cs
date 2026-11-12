using System.Linq;
using Corum2.Core;
using NUnit.Framework;
using static Corum2.Tests.CombatFixtures;

namespace Corum2.Tests
{
    /// <summary>WP-06: TC-04-03 enemy guard cancel (R-04-03, EXP-COMBO-05).</summary>
    public class Wp06Tests
    {
        static EnemyDefinition GuardBoss()
        {
            var p1 = AiFixtures.Phase("p1", AiFixtures.Pattern("far1", 6f, 9f));
            p1.PreferredMinDistance = 0f;
            p1.PreferredMaxDistance = 4f;
            p1.GuardFrequency = 1f;
            return AiFixtures.Boss("G", "p1", p1);
        }

        static SimHarness Guarding(out EnemyActor b, params (string id, string def, float x)[] extra)
        {
            var all = new[] { ("b", "G", 11f) }.Concat(extra).ToArray();
            var h = AiFixtures.H("TC-04-03", World(all), GuardBoss(), Bag());
            b = h.C.Find("b");
            h.Step();
            var bb = b;
            h.StepUntil(() => bb.State == EnemyState.Idle && bb.Hitstop <= 0f, 300).Step();
            return h;
        }

        [Test, Category("TC_04_03")]
        public void EnemyGuard_CancelsTheAttack_EndsTheChain_NoComboCount()
        {
            var h = Guarding(out var b);
            h.Teleport(10f).Press(LogicalAction.Attack).Step();
            Assert.AreEqual(EnemyState.Guard, b.State, "guard frequency 1 (EXP-BOSS-05)");
            h.StepUntil(() => h.Count("combo.guardCancel") > 0, 60);
            var hit = h.Events("hit").Single();
            Assert.IsTrue(hit.Reason.StartsWith("BossGuard"), hit.Reason);
            Assert.AreEqual(0, IntField(hit, "comboAfter"), "a guarded hit is not an effective hit (EXP-COMBO-05)");
            Assert.IsNull(h.C.Attack, "player attack cancelled, hitbox removed");
            Assert.AreEqual(1, h.Count("attack.end", "EnemyGuardCancel"));
            Assert.AreEqual(1, h.Count("combo.end", "EnemyGuardCancel"));
            Assert.IsFalse(h.C.ChainAlive);
            Assert.AreNotEqual(MotorState.GroundAttack, h.P.State, "player lock released");
            var chain = h.C.ChainId;
            h.Step(5).Press(LogicalAction.Attack).Step();
            Assert.Greater(h.C.ChainId, chain, "the next attack starts a new chain");
        }

        [Test, Category("TC_04_03")]
        public void EnemyGuard_MixedWithAnEffectiveHit_KeepsThatHitsCount_StillEndsTheChain()
        {
            // a bag at the same spot: same tick, one guarded hit and one effective hit (order is CF-06; experimental choice)
            var h = Guarding(out var b, ("bag", "bag", 11f));
            h.Teleport(10f).Press(LogicalAction.Attack).Step();
            h.StepUntil(() => h.Count("combo.guardCancel") > 0, 60);
            var hits = h.Events("hit");
            Assert.AreEqual(2, hits.Count);
            Assert.AreEqual(1, hits.Count(e => e.Reason.StartsWith("BossGuard")));
            var gc = h.Events("combo.guardCancel").Single();
            Assert.AreEqual(true, gc.Fields.First(f => f.Key == "mixedWithEffectiveHits").Value);
            var end = h.Events("combo.end").Single();
            Assert.IsTrue(end.Reason.StartsWith("EnemyGuardCancel"));
            Assert.AreEqual(1, IntField(end, "comboHits"), "only the bag hit counts");
        }

        [Test, Category("TC_04_03")]
        public void BackHit_IsNotGuarded_ChainContinues()
        {
            var h = Guarding(out var b);
            h.Teleport(12f, -1).Press(LogicalAction.Attack).Step();
            h.StepUntil(() => h.Count("hit") > 0, 60);
            Assert.AreEqual(0, h.Count("combo.guardCancel"), "back hits are not guarded (EXP-BOSS-06)");
            Assert.IsTrue(h.C.ChainAlive);
        }

        static int IntField(DiagEvent e, string key) => System.Convert.ToInt32(e.Fields.First(f => f.Key == key).Value);
    }
}
