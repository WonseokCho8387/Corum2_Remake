using System.Linq;
using Corum2.Core;
using NUnit.Framework;
using static Corum2.Tests.CombatFixtures;

namespace Corum2.Tests
{
    /// <summary>WP-05-R1 regressions: WP05-F01 (no in-between diagonal from one stick sample), WP05-F02 (ring boundary keeps
    /// the routed button's meaning), WP05-F03 (refused scroll not consumed; slide/roll wait), WP05-M01 (history cap).</summary>
    public class Wp05R1Tests
    {
        static SimHarness Bag() => Wp05Fixtures.H("TC-02-02", Wp05Fixtures.Profile(), ("b", "bag", 11.4f));

        static int Casts(SimHarness h) => h.Count("magic.cast");
        static int Attacks(SimHarness h) => h.Count("attack.start");

        [Test, Category("TC_02_02")]
        public void F01_StickDownThenRight_OneSampleEach_NoPhantomDiagonal()
        {
            var h = Bag();
            h.Frame((0f, -1f)).Step(2).Frame((1f, 0f)).Step(2);
            h.Press(LogicalAction.Attack).Step().Release(LogicalAction.Attack).Step(20);
            Assert.AreEqual(0, Casts(h), "down then right with no diagonal sample is not the fireball");
            Assert.AreEqual(1, Attacks(h), "normal attack");
            StringAssert.DoesNotContain("DownRight", (string)h.Events("magic.commandMiss").Single().Get("history"));
        }

        [Test, Category("TC_02_02")]
        public void F01_Control_RealDiagonalSample_Casts()
        {
            var h = Bag();
            h.Frame((0f, -1f)).Step(2).Frame((0.71f, -0.71f)).Step(2).Frame((1f, 0f)).Step(2);
            h.Press(LogicalAction.Attack).Step().Release(LogicalAction.Attack).Step(20);
            Assert.AreEqual(1, Casts(h));
            Assert.AreEqual(0, Attacks(h));
        }

        [Test, Category("TC_02_02")]
        public void F01_KeyboardHeldDiagonal_StillRecorded()
        {
            // Two keys held at different times really are a diagonal (down held, right pressed later, down released later).
            var h = Bag();
            h.Press(LogicalAction.MoveDown).Step(2).Press(LogicalAction.MoveRight, 0.005).Step(2).Release(LogicalAction.MoveDown, 0.003).Step(2);
            h.Press(LogicalAction.Attack).Step().Release(LogicalAction.Attack).Step(20);
            Assert.AreEqual(1, Casts(h), "keyboard ↓, ↓+→, → is ↓↘→");
        }

        static SimHarness WithFireHistory()
        {
            var h = Bag();
            h.Frame((0f, -1f)).Step(2).Frame((0.71f, -0.71f)).Step(2).Frame((1f, 0f)).Step(2);
            return h;
        }

        [Test, Category("TC_02_02")]
        public void F02_XThenLB_SameFrame_And_WithATickBetween_SameMeaning()
        {
            var a = WithFireHistory();
            a.Frame(LogicalAction.Attack, LogicalAction.RingToggle).Step(20);
            var b = WithFireHistory();
            b.Frame(LogicalAction.Attack).Step().Frame(LogicalAction.RingToggle).Step(20);
            Assert.AreEqual(1, Casts(a), "X was given to the game before LB: it keeps its meaning");
            Assert.AreEqual(0, Attacks(a));
            Assert.AreEqual(Casts(a), Casts(b), "tick placement does not change the result");
            Assert.AreEqual(Attacks(a), Attacks(b));
            Assert.IsTrue(a.Sim.Ring.IsOpen && b.Sim.Ring.IsOpen, "the ring still opened");
        }

        [Test, Category("TC_02_02")]
        public void F02_LBThenX_RingOwnsX_NoCastNoAttack()
        {
            var h = WithFireHistory();
            h.Frame(LogicalAction.RingToggle, LogicalAction.Attack).Step(20);
            Assert.AreEqual(0, Casts(h));
            Assert.AreEqual(0, Attacks(h));
            Assert.Greater(h.Events("input.route").Count(e => e.Get("action").ToString() == "Attack" && (string)e.Get("consumer") == "ring"), 0,
                "X consumed by the ring only (no double use)");
        }

        [TestCase(Dir8.Down, MotorState.Slide)]
        [TestCase(Dir8.DownRight, MotorState.Roll)]
        public void F03_ScrollDuringSlideOrRoll_WaitsThenCastsOnce(Dir8 dir, MotorState state)
        {
            var h = Wp05Fixtures.H("TC-02-10", Wp05Fixtures.Profile(), ("b", "bag", 40f));
            h.Dir(dir).Step(1).Press(LogicalAction.Jump).Step(2);
            Assert.AreEqual(state, h.P.State);
            h.Ui(LogicalAction.QuickDown).Step();
            Assert.AreEqual(1, h.Inv().Count("scroll"), "not consumed while waiting (fixture owns 1)");
            StringAssert.Contains("EXP-ITEM-07", h.Events("item.request").Single().Reason);
            h.Release(LogicalAction.Jump).Dir(Dir8.None).StepUntil(() => h.Count("magic.cast") > 0, 120);
            Assert.AreEqual(1, Casts(h), "cast after the motion");
            Assert.AreEqual(0, h.Inv().Count("scroll"), "consumed once, when the cast started");
            Assert.AreEqual(0, h.Count("magic.reject"));
        }

        [Test, Category("TC_02_10")]
        public void F03_CastStartedThenGuardCancel_ScrollStaysConsumed_CooldownStarted()
        {
            var h = Wp05Fixtures.H("TC-02-10", Wp05Fixtures.Profile(), ("b", "bag", 40f));
            h.Ui(LogicalAction.QuickDown).Step();
            Assert.AreEqual(1, Casts(h));
            h.Press(LogicalAction.Guard).Step(20);
            Assert.AreEqual(false, h.Events("magic.cancel").Single().Get("effectApplied"));
            Assert.AreEqual(0, h.Inv().Count("scroll"), "a started cast consumed the scroll (EXP-MAGIC-03 / EXP-ITEM-04)");
            Assert.Greater(h.Sim.Items.CooldownLeft, 0.0);
        }

        [Test, Category("DATA")]
        public void M01_CommandLengthMatchesHistoryCap()
        {
            var p = Wp05Fixtures.Profile();
            var ok = new MagicCommand { Id = "long32", MagicId = "fire", Button = LogicalAction.SpecialAttack, Window = 5f };
            for (var i = 0; i < MagicCommandRecognizer.MaxHistory; i++) ok.Directions.Add(i % 2 == 0 ? Dir8.Left : Dir8.Right);
            p.Commands.Add(ok);
            Assert.IsEmpty(RingValidator.Validate(p), "32 directions accepted");
            var r = new MagicCommandRecognizer(p);
            for (var i = 0; i < 32; i++) r.Observe(ok.Directions[i], i * 0.1, i);
            Assert.AreEqual("long32", r.Match(LogicalAction.SpecialAttack, 3.2)?.Id, "a 32-direction command can match");
            var bad = Wp05Fixtures.Profile();
            var c33 = new MagicCommand { Id = "long33", MagicId = "fire", Button = LogicalAction.SpecialAttack, Window = 5f };
            for (var i = 0; i < 33; i++) c33.Directions.Add(i % 2 == 0 ? Dir8.Left : Dir8.Right);
            bad.Commands.Add(c33);
            Assert.IsTrue(RingValidator.Validate(bad).Any(i => i.ToString().Contains("at most 32")));
        }

        [Test, Category("TC_06_19a")]
        public void ReviveInvulnerability_LoggedWithItsOwnReason()
        {
            var h = Wp05Fixtures.H("TC-06-19a", Wp05Fixtures.Profile(1), ("e", "hitter", 11f));
            h.C.PlayerHp = 1f;
            h.StepUntil(() => h.P.State == MotorState.Dead, 200).Seconds(1.3).Tap(LogicalAction.Jump).Step();
            Assert.IsTrue(h.C.PlayerInvincible);
            h.StepUntil(() => h.Count("hit.skip") > 0 || !h.C.PlayerInvincible, 200);
            Assert.Greater(h.Count("hit.skip"), 0, "the hitter swung during the revive invulnerability");
            Assert.AreEqual("PlayerInvincible:Revive(EXP-DEATH-03)", h.Events("hit.skip").First().Reason);
        }
    }
}
