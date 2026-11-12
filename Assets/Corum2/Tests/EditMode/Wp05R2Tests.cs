using System.Collections.Generic;
using System.Linq;
using Corum2.Core;
using NUnit.Framework;

namespace Corum2.Tests
{
    /// <summary>
    /// WP-05-R2 regressions (WP-05-R1 review): WP05-F01 residual (separate stick samples with the same device time are
    /// each recorded; only the edges of one sample are merged) and the invulnerability reason at the roll-window edge.
    /// </summary>
    public class Wp05R2Tests
    {
        static SimHarness Bag() => Wp05Fixtures.H("TC-02-02", Wp05Fixtures.Profile(), ("b", "bag", 11.4f));

        static string History(SimHarness h) =>
            (string)h.Events("magic.command").Concat(h.Events("magic.commandMiss")).Single().Get("history");

        /// <summary>The review's reproduction: ↓ (t=.001, seq 1), ↘ (.004, seq 3), → (.004, seq 5), X, all in one routed frame.</summary>
        static SimHarness ReviewTie(double lastDelta)
        {
            var h = Bag();
            var tracker = new DirectionEdgeTracker();
            long seq = 0;
            var samples = new List<StickSample>();
            var edges = new List<InputEvent>();
            void Sample(float x, float y, double t)
            {
                var s = ++seq;
                samples.Add(new StickSample(t, x, y, s));
                tracker.Update(Dir8Util.FromStick(x, y, .35f), t, "pad", "stick", () => ++seq, edges, s);
            }
            Sample(0f, -1f, .001);
            Sample(.71f, -.71f, .004);
            Sample(1f, 0f, .004 + lastDelta);
            edges.Add(new InputEvent(++seq, LogicalAction.Attack, InputEdge.Press, .009, "pad", "X"));
            var routed = new List<InputEvent>();
            h.Sim.RouteFrame(edges, samples, routed, .02);
            var tick = new TickInput { Device = "pad", Dir = Dir8.Right, RealTime = .02 };
            tick.Events.AddRange(routed);
            h.Sim.Step(tick, .02f);
            return h;
        }

        [TestCase(0.0)]
        [TestCase(0.000001)]
        [Category("TC_02_02")]
        public void F01_SeparateSamplesWithTheSameTime_EachRecorded(double delta)
        {
            var h = ReviewTie(delta);
            Assert.AreEqual(1, h.Count("magic.cast"), "↓↘→ X casts whether or not the last two samples share a time");
            Assert.AreEqual(0, h.Count("attack.start"));
        }

        [Test, Category("TC_02_02")]
        public void F01_HarnessFrame_ThreeSamplesSameTime_Casts()
        {
            var h = Bag();
            h.Frame((0f, -1f), (0.71f, -0.71f), (1f, 0f)).Step(2);
            h.Press(LogicalAction.Attack).Step().Release(LogicalAction.Attack).Step(20);
            Assert.AreEqual(1, h.Count("magic.cast"));
            Assert.AreEqual(0, h.Count("attack.start"));
        }

        [Test, Category("TC_02_02")]
        public void F01_TwoSamplesSameFrame_DownThenRight_StillNoPhantomDiagonal()
        {
            // The second sample yields Right press + Down release (one group): the in-between DownRight is not recorded.
            var h = Bag();
            h.Frame((0f, -1f), (1f, 0f)).Step(2);
            h.Press(LogicalAction.Attack).Step().Release(LogicalAction.Attack).Step(20);
            Assert.AreEqual(0, h.Count("magic.cast"));
            Assert.AreEqual(1, h.Count("attack.start"));
            StringAssert.DoesNotContain("DownRight", History(h));
        }

        [Test]
        public void EdgesOfOneSample_ShareItsSeq_KeyboardEventsHaveNone()
        {
            var tr = new DirectionEdgeTracker();
            var outp = new List<InputEvent>();
            long id = 10;
            tr.Update(Dir8.Down, 0, "pad", "stick", () => ++id, outp, 7);
            tr.Update(Dir8.Right, 0, "pad", "stick", () => ++id, outp, 9);
            Assert.AreEqual(3, outp.Count, "Down press, then Right press + Down release");
            Assert.AreEqual(7, outp[0].SampleGroup);
            Assert.IsTrue(outp.Skip(1).All(e => e.SampleGroup == 9));
            Assert.AreEqual(0, new InputEvent(1, LogicalAction.MoveDown, InputEdge.Press, 0, "kb", "s").SampleGroup);
        }

        static SimHarness Revived()
        {
            var h = Wp05Fixtures.H("TC-06-19a", Wp05Fixtures.Profile(1), ("e", "hitter", 11f));
            h.C.PlayerHp = 1f;
            h.StepUntil(() => h.P.State == MotorState.Dead, 200).Seconds(1.3).Tap(LogicalAction.Jump).Step(2);
            Assert.AreNotEqual(MotorState.Dead, h.P.State, "revived");
            Assert.IsTrue(h.C.ReviveInvulnerable);
            return h;
        }

        [Test, Category("TC_06_19a")]
        public void Reason_RollBeforeItsWindow_DuringRevive_IsRevive()
        {
            var h = Revived();
            h.Dir(Dir8.DownRight).Press(LogicalAction.Jump).Step(1);
            Assert.AreEqual(MotorState.Roll, h.P.State);
            Assert.Less(h.P.RollElapsed, h.C.Profile.RollInvulnStart, "still before the roll window");
            Assert.IsFalse(h.C.InRollWindow);
            Assert.AreEqual("PlayerInvincible:Revive(EXP-DEATH-03)", h.C.InvincibleReason);
            h.StepUntil(() => h.C.InRollWindow, 20);
            Assert.AreEqual("PlayerInvincible:RollWindow(R-06-13)+Revive(EXP-DEATH-03)", h.C.InvincibleReason);
        }
    }
}
