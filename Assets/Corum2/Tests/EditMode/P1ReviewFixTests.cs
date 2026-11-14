using System.Linq;
using Corum2.Core;
using Corum2.Core.P1;
using NUnit.Framework;
using static Corum2.Tests.CombatFixtures;

namespace Corum2.Tests
{
    /// <summary>Fixes from the GPT P1 probes (docs/GPT/evidence/P1_GPT_v1.0/probe_results.json, partial review).</summary>
    public class P1ReviewFixTests
    {
        static readonly Dir8[] QCF = { Dir8.Down, Dir8.DownRight, Dir8.Right };

        [Test]
        public void T167_WakeupInvincibleToP1Hits()
        {
            var m = new P1MagicTests.M(null, ("e", Bag("e", 100), 12f)).Learn("MagicMissile");
            m.E("e").State = EnemyState.Wakeup;
            m.E("e").Timer = 10f;
            m.Cast(LogicalAction.Attack, Dir8.Right, Dir8.Up, Dir8.Left).Settle(40);
            Assert.AreEqual(100f, m.E("e").Hp, "R-06-10 wake-up invincibility");
        }

        [Test]
        public void T143_SecondPressOfSameTickPairDoesNotFallBack()
        {
            foreach (var order in new[] { new[] { LogicalAction.Attack, LogicalAction.Jump }, new[] { LogicalAction.Jump, LogicalAction.Attack } })
            {
                var m = new P1MagicTests.M(null).Learn("spell.light_orb", "spell.light_sword");
                m.Motion(QCF);
                m.H.Press(order[0]).Press(order[1]).Step();
                m.Settle(10);
                Assert.AreEqual(0, m.H.Count("attack.start"), "no X fallback");
                Assert.IsTrue(m.H.P.Grounded, "no A fallback");
                Assert.AreEqual(0, m.H.Count("p1.magic.commit"));
                Assert.AreEqual(5f, m.G.CurMp);
            }
        }

        [Test]
        public void T167_ParryIsZeroDamageInP1()
        {
            var m = new P1MagicTests.M(null, ("h", Hitter("h", 20), 11f));
            m.H.StepUntil(() => m.E("h").InAttack, 200).Step(11); // startup 0.3 s: press RB about 0.08 s before the hit
            m.H.Press(LogicalAction.Guard).StepUntil(() => m.H.Count("guard.parry") + m.H.Count("guard.block") > 0, 100);
            Assert.AreEqual(1, m.H.Count("guard.parry"));
            Assert.AreEqual(10f, m.H.C.PlayerHp, "parry 0 (BAL-05)");
        }

        [Test]
        public void T134_RingOpenedAndClosedBetweenTicksDropsBuffer()
        {
            var m = new P1MagicTests.M(null).Learn("MagicMissile");
            m.Motion(Dir8.Right, Dir8.Up);
            m.H.Ui(LogicalAction.RingToggle).Ui(LogicalAction.RingToggle);
            Assert.IsFalse(m.H.Sim.Ring.IsOpen);
            m.Motion(Dir8.Left).Button(LogicalAction.Attack).Settle(20);
            Assert.AreEqual(0, m.H.Count("p1.magic.commit"), "no completion from the old partial motion");
        }

        [Test]
        public void T133_T140_PlainBUsesSelectedItemFailedQcfBDoesNot()
        {
            var m = new P1MagicTests.M(null);
            m.H.C.SetPlayerHp(1, "test");
            m.H.Ui(LogicalAction.RingToggle).Cursor(0f, 1f).Ui(LogicalAction.RingToggle); // slot 0 (up) = hp potion
            Assert.AreEqual("p1test.potion.hp", m.H.Sim.Ring.LastSelectedItem);
            m.Button(LogicalAction.ButtonB).Settle(5);
            Assert.AreEqual(6f, m.H.C.PlayerHp, "plain B used the selected potion");
            Assert.AreEqual(2, m.H.Sim.Items.Inventory.Count("p1test.potion.hp"));
            var f = new P1MagicTests.M(null).Mp(0).Learn("spell.heal");
            f.H.Ui(LogicalAction.RingToggle).Cursor(0f, 1f).Ui(LogicalAction.RingToggle);
            f.Cast(LogicalAction.ButtonB, QCF).Settle(20);
            Assert.AreEqual(3, f.H.Sim.Items.Inventory.Count("p1test.potion.hp"), "completed QCF+B failure: no item use (MAG-103)");
        }
    }
}
