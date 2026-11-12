using System.Linq;
using Corum2.Core;
using NUnit.Framework;
using static Corum2.Tests.CombatFixtures;

namespace Corum2.Tests
{
    /// <summary>R1 regressions for the GPT findings WP06-F01 (guard during a pierce dash), WP07-F01 / WP08-F01 (weak positive
    /// knockback must move) and WP08-F02 (lethal guard / parry damage is a death).</summary>
    public class Wp06to08R1Tests
    {
        static EnemyDefinition GuardBoss(float freq)
        {
            var p1 = AiFixtures.Phase("p1", AiFixtures.Pattern("far1", 6f, 9f));
            p1.PreferredMinDistance = 0f;
            p1.PreferredMaxDistance = 4f;
            p1.GuardFrequency = freq;
            return AiFixtures.Boss("G", "p1", p1);
        }

        static int IntField(DiagEvent e, string key) => System.Convert.ToInt32(e.Fields.First(f => f.Key == key).Value);

        // ------------------------------------------------------------ WP06-F01

        [Test, Category("TC_04_06"), Category("TC_04_03")]
        public void PierceDash_GuardedFirst_LaterEffectiveHitStillCounts([Values(1f, 0f)] float guardFreq)
        {
            var h = Harness("TC-04-06", World(("b", "G", 11.5f), ("bag", "bag", 13.5f)), Defs(GuardBoss(guardFreq), Bag()), cu: Unlocks(pierce: true));
            var b = h.C.Find("b");
            h.Step();
            h.StepUntil(() => b.State == EnemyState.Idle && b.Hitstop <= 0f, 300).Step();
            h.Teleport(10f).Press(LogicalAction.Jump).Step(4).Dir(Dir8.Right).Press(LogicalAction.Attack).Step();
            h.StepUntil(() => h.C.Find("bag").Hp < 1000f || h.P.State != MotorState.AirDash, 80);
            h.StepUntil(() => h.P.State != MotorState.AirDash, 80).Step(2);

            var bossHit = h.Events("hit").First(e => e.TargetId == "b");
            var bagHit = h.Events("hit").First(e => e.TargetId == "bag");
            Assert.Greater(bagHit.SimulationTick, bossHit.SimulationTick, "the bag is reached on a later tick");
            Assert.Less(h.C.Find("bag").Hp, 1000f, "effective damage on the bag");
            if (guardFreq > 0f)
            {
                Assert.IsTrue(bossHit.Reason.StartsWith("BossGuard"), bossHit.Reason);
                Assert.AreEqual(IntField(bossHit, "comboBefore"), IntField(bossHit, "comboAfter"), "guarded: no count");
                Assert.AreEqual(1, h.Count("combo.end", "EnemyGuardCancel"), "the guard ends the chain");
                Assert.AreEqual(1, h.Count("combo.reopen"), "the later effective hit opens a new chain");
                Assert.AreEqual(1, IntField(bagHit, "comboAfter"), "and counts");
            }
            else
            {
                Assert.AreEqual(1, IntField(bossHit, "comboAfter"));
                Assert.AreEqual(2, IntField(bagHit, "comboAfter"));
                Assert.AreEqual(0, h.Count("combo.reopen"));
            }
        }

        // ------------------------------------------------------------ WP07-F01 / WP08-F01

        [Test, Category("TC_06_05")]
        public void PhysicalKnockback_WeakPositiveMoves_ZeroStays([Values(1f, 0.5f, 0.2f, 0.1f, 0f)] float scale)
        {
            var e = Bag("k");
            e.PhysicalKnockbackScale = scale;
            var h = Harness("TC-06-05", World(("k", "k", 11f)), Defs(e));
            h.Hit().Step(30);
            var dx = h.C.Find("k").Position.X - 11f;
            if (scale > 0f) Assert.Greater(dx, 1e-4f, "positive knockback scale must move (kb " + 1.5f * scale + ")");
            else Assert.AreEqual(0f, dx, 1e-6f);
        }

        static AttackDefinition Magic(float kb) =>
            new AttackDefinition { Id = "testMagic", Kind = AttackKind.TestMagic, DamageType = DamageType.Magic, TestDamage = 5, Knockback = kb };

        [Test, Category("TC_06_06")]
        public void MagicKnockback_WeakPositiveScaleMoves([Values(1f, 0.1f, 0.05f)] float scale)
        {
            var e = Bag("m");
            e.PhysicalKnockbackScale = 0f;
            e.MagicKnockbackScale = scale;
            var h = Harness("TC-06-06", World(("m", "m", 11f)), Defs(e));
            h.C.TestApplyHit(Magic(5f), "m");
            h.Step(30);
            Assert.Greater(h.C.Find("m").Position.X - 11f, 1e-4f, "every effective magic hit knocks back (R-06-06)");
        }

        [Test, Category("TC_06_12")]
        public void MagicOnSuperArmor_WeakScale_StaggersCancelsAndMoves()
        {
            var e = Hitter("a", armor: true);
            e.MagicKnockbackScale = 0.1f;
            var h = Harness("TC-06-12", World(("e", "a", 11f)), Defs(e));
            var en = h.C.Find("e");
            h.StepUntil(() => en.State == EnemyState.AttackStartup);
            var x0 = en.Position.X;
            h.C.TestApplyHit(Magic(5f), "e");
            Assert.AreEqual(EnemyState.Stagger, en.State);
            Assert.IsNotNull(h.LastWithReason("MagicBreaksArmor"));
            h.Step(30);
            Assert.Greater(en.Position.X - x0, 1e-4f);
        }

        // ------------------------------------------------------------ WP08-F02

        static SimHarness LethalSetup(EnemyDefinition d, float hp)
        {
            var h = Harness("TC-07-02", World(("e", d.Id, 11f)), Defs(d));
            h.C.PlayerHp = hp;
            return h;
        }

        static void AssertDeadAndNoAttack(SimHarness h, string cause)
        {
            Assert.AreEqual(0f, h.C.PlayerHp);
            Assert.AreEqual(MotorState.Dead, h.P.State);
            Assert.AreEqual(1, h.Count("player.death"));
            StringAssert.Contains(cause, h.Events("player.death").Single().Get("cause").ToString());
            var starts = h.Count("attack.start");
            h.Press(LogicalAction.Attack).Step(3);
            Assert.AreEqual(starts, h.Count("attack.start"), "no attack after death");
        }

        [Test, Category("TC_07_02"), Category("TC_06_19")]
        public void LethalGuardDamage_KillsThePlayer([Values(1f, 2f)] float hp)
        {
            var h = LethalSetup(Hitter("g", 10f), hp);
            h.Press(LogicalAction.Guard).StepUntil(() => h.Count("guard.block") + h.Count("guard.parry") > 0);
            if (hp <= 1f) AssertDeadAndNoAttack(h, "DefendedHit:");
            else
            {
                Assert.AreEqual(1f, h.C.PlayerHp, 1e-4f);
                Assert.AreEqual(MotorState.Guard, h.P.State, "non-lethal guard damage keeps guarding");
                Assert.AreEqual(0, h.Count("player.death"));
            }
        }

        [Test, Category("TC_07_06"), Category("TC_06_19")]
        public void LethalGuardBreakDamage_KillsThePlayer()
        {
            var h = LethalSetup(Hitter("gb", 10f, guardBreak: true), 1f);
            h.StepUntil(() => h.C.Find("e").State == EnemyState.AttackStartup).Press(LogicalAction.Guard).StepUntil(() => h.Count("guard.block") > 0);
            AssertDeadAndNoAttack(h, "DefendedHit:GuardBreak");
        }

        [Test, Category("TC_07_05"), Category("TC_06_19")]
        public void LethalParryDamage_KillsThePlayer()
        {
            var h = LethalSetup(Hitter("p", 10f), 1f);
            var e = h.C.Find("e");
            h.StepUntil(() => e.State == EnemyState.AttackStartup).Step(12).Press(LogicalAction.Guard).StepUntil(() => h.Count("guard.parry") > 0, 20);
            AssertDeadAndNoAttack(h, "DefendedHit:Parry");
        }
    }
}
