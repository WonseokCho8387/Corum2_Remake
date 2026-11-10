using System.Linq;
using Corum2.Core;
using NUnit.Framework;
using static Corum2.Tests.CombatFixtures;

namespace Corum2.Tests
{
    /// <summary>WP-04-R2 regressions for WP04-M02 (EXP-BOSS-05 eligibility): front of the boss's facing on the start tick,
    /// no turning before the check, judged only on the start tick (a busy boss never answers it later).</summary>
    public class AiR2Tests
    {
        static EnemyDefinition GuardBoss(float staggerResistance = 0f)
        {
            // guard frequency 1: every eligible start is guarded; patterns only far away (no attacks next to the player)
            var p1 = AiFixtures.Phase("p1", AiFixtures.Pattern("far", 6f, 9f));
            p1.PreferredMinDistance = 0f;
            p1.PreferredMaxDistance = 4f;
            p1.GuardFrequency = 1f;
            p1.StaggerResistance = staggerResistance;
            return AiFixtures.Boss("G", "p1", p1);
        }

        static void Free(SimHarness h, EnemyActor b) => h.StepUntil(() => b.State == EnemyState.Idle && b.Hitstop <= 0f, 300).Step();

        static int Guards(SimHarness h) => h.Events("boss.defense").Count(e => e.StateAfter == "Guard");

        [Test, Category("TC_08_11")]
        public void M02A_AttackStartBehindTheBoss_NotEligible_NoTurnBeforeCheck()
        {
            var h = AiFixtures.H("TC-08-11", World(("b", "G", 11f)), GuardBoss());
            var b = h.C.Find("b");
            h.Step();
            Free(h, b);
            Assert.AreEqual(-1, b.Facing, "boss faces the player on its left");
            h.Teleport(12f, -1).Press(LogicalAction.Attack).Step();
            Assert.AreNotEqual(EnemyState.Guard, b.State, "attack started behind the boss's facing: no guard (WP04-M02-A)");
            Assert.AreEqual(0, h.Count("boss.defense"));
            var skip = h.Events("boss.defenseSkip").Single();
            StringAssert.StartsWith("Behind", skip.Reason);
            Assert.AreEqual(0f, b.GuardAcc, "a skipped start does not count");
            h.StepUntil(() => h.C.Attack == null, 60);
            Assert.AreEqual(0, Guards(h), "no late guard for that attack");
        }

        [Test, Category("TC_08_11")]
        public void M02A_FrontControl_Guards()
        {
            var h = AiFixtures.H("TC-08-11", World(("b", "G", 11f)), GuardBoss());
            var b = h.C.Find("b");
            h.Step();
            Free(h, b);
            h.Teleport(10f, 1).Press(LogicalAction.Attack).Step();
            Assert.AreEqual(EnemyState.Guard, b.State, "in front: guarded on the start tick");
            Assert.AreEqual(1, Guards(h));
            Assert.AreEqual(0, h.Count("boss.defenseSkip"));
        }

        [Test, Category("TC_08_11")]
        public void M02A_BehindThenFront_OnlyTheFrontStartCounts()
        {
            var h = AiFixtures.H("TC-08-11", World(("b", "G", 11f)), GuardBoss());
            var b = h.C.Find("b");
            h.Step();
            Free(h, b);
            h.Teleport(12f, -1).Press(LogicalAction.Attack).Step();
            h.StepUntil(() => h.C.Attack == null, 60);
            Free(h, b);
            Assert.AreEqual(1, b.Facing, "turned toward the player afterwards (EXP-AI-08, normal idle facing)");
            // now the player at x12 is in front of the turned boss
            h.Press(LogicalAction.Attack).Step();
            Assert.AreEqual(EnemyState.Guard, b.State);
            Assert.AreEqual(1, Guards(h));
        }

        [Test, Category("TC_08_11")]
        public void M02B_StartDuringBossHitstop_NotAnsweredLater()
        {
            var h = AiFixtures.H("TC-08-11", World(("b", "G", 11f)), GuardBoss(1f));
            var b = h.C.Find("b");
            h.Step();
            Free(h, b);
            h.C.TestApplyHit(AiFixtures.Damage(5f), "b"); // StaggerResisted: stays Idle with hitstop 0.04
            Assert.AreEqual(EnemyState.Idle, b.State);
            Assert.Greater(b.Hitstop, 0f);
            h.Teleport(10f, 1).Press(LogicalAction.Attack).Step();
            StringAssert.StartsWith("Busy:Hitstop", h.Events("boss.defenseSkip").Single().Reason);
            h.Step(10);
            Assert.AreEqual(0, Guards(h), "the start fell in hitstop: no delayed guard after it (WP04-M02-B)");
            h.StepUntil(() => h.C.Attack == null, 60);
            Free(h, b);
            h.Teleport(10f, 1).Press(LogicalAction.Attack).Step();
            Assert.AreEqual(1, Guards(h), "the next start on a free boss is guarded; the skipped one did not count");
        }

        [Test, Category("TC_08_11")]
        public void M02B_StartDuringStagger_NotAnsweredAfterTheReaction()
        {
            var h = AiFixtures.H("TC-08-11", World(("b", "G", 11f)), GuardBoss());
            var b = h.C.Find("b");
            h.Step();
            Free(h, b);
            h.C.TestApplyHit(AiFixtures.Damage(5f), "b");
            h.Step(3); // past the hitstop, still staggered
            Assert.AreEqual(EnemyState.Stagger, b.State);
            h.Teleport(10f, 1).Press(LogicalAction.Attack).Step();
            StringAssert.StartsWith("Busy:Stagger", h.Events("boss.defenseSkip").Single().Reason);
            h.StepUntil(() => b.State == EnemyState.Idle, 60).Step(5);
            Assert.AreEqual(0, Guards(h), "no guard once the stagger ends for the same attack");
        }

        [Test, Category("TC_08_11")]
        public void M02_StartOutOfRangeOrNoSettings_NotLoggedAsSkip()
        {
            var h = AiFixtures.H("TC-08-11", World(("b", "G", 11f), ("o", "O", 40f)), GuardBoss(),
                AiFixtures.Boss("O", "q", AiFixtures.Phase("q", AiFixtures.Pattern("q", 6f, 9f))));
            var b = h.C.Find("b");
            h.Step();
            Free(h, b);
            h.Teleport(5f, 1).Press(LogicalAction.Attack).Step();
            Assert.AreEqual(0, h.Count("boss.defense") + h.Count("boss.defenseSkip"), "distance 6 > DefenseRange 3");
            h.StepUntil(() => h.C.Attack == null, 60);
            h.Teleport(39f, 1).Press(LogicalAction.Attack).Step();
            Assert.AreEqual(0, h.Events("boss.defenseSkip").Count(e => e.ActorId == "o"), "boss without guard/evade settings");
        }
    }
}
