using System;
using System.Linq;
using Corum2.Core;
using NUnit.Framework;
using static Corum2.Tests.CombatFixtures;

namespace Corum2.Tests
{
    /// <summary>WP-11A-R1 TC-UNITY-07 (GPT WP-11A review section 3): candidates are logged with their tick and link to the hits;
    /// an external (late) hit signal is judged from the core's own state and never creates or repeats a hit.</summary>
    public class Wp11R1Tests
    {
        static SimHarness Bag1()
        {
            var h = Harness("WP11A-R1", World(("bag", "bag", 11f)), Defs(Bag()));
            h.Step().Teleport(10);
            return h;
        }

        static long StartAttack(SimHarness h)
        {
            h.Press(LogicalAction.Attack).Step();
            return h.C.Attack.InstanceId;
        }

        [Test]
        public void Candidates_AreLoggedOnTheHitTick_AndNameTheHit()
        {
            var h = Bag1();
            var id = StartAttack(h);
            h.StepUntil(() => h.C.Attack == null);
            var hit = h.Events("hit").Single();
            var cand = h.Events("hit.candidates").Where(e => Convert.ToInt64(e.Get("attackInstanceId")) == id).ToList();
            Assert.IsNotEmpty(cand);
            Assert.AreEqual(hit.SimulationTick, cand[0].SimulationTick, "collected on the tick the hit resolved");
            StringAssert.Contains(id + ":bag:0", (string)cand[0].Get("playerCandidates"));
        }

        [Test]
        public void SignalAfterTheAttackEnded_IsStale_AndAddsNoHit()
        {
            var h = Bag1();
            var id = StartAttack(h);
            h.StepUntil(() => h.C.Attack == null);
            Assert.AreEqual(id, h.C.LastEndedAttackInstanceId);
            var hits = h.Events("hit").Count;
            h.C.SubmitExternalHitSignal(id, "bag", "test");
            h.Step(2);
            var r = h.Events("hit.signal.resolved").Single();
            StringAssert.StartsWith("Rejected:StaleInstance:Ended", r.Reason);
            Assert.AreEqual(false, r.Get("appliedAsHit"));
            Assert.AreEqual(hits, h.Events("hit").Count);
        }

        [Test]
        public void SignalDuringStartup_IsNotActive()
        {
            var h = Bag1();
            var id = StartAttack(h);
            Assert.Less(h.C.Attack.Elapsed, h.C.Attack.Def.Startup);
            h.C.SubmitExternalHitSignal(id, "bag", "test");
            h.Step();
            StringAssert.StartsWith("Rejected:NotActive:Startup", h.Events("hit.signal.resolved").Single().Reason);
        }

        [Test]
        public void SignalOnTheCollectedTick_IsTheSameCandidate_AndTheTargetIsHitOnce()
        {
            var h = Bag1();
            var id = StartAttack(h);
            // the signal goes in just before the tick on which the hitbox first becomes active (the tick the core collects the bag)
            h.StepUntil(() => h.C.Attack.Elapsed + SimHarness.Dt >= h.C.Attack.Def.Startup - 1e-4f, 40);
            h.C.SubmitExternalHitSignal(id, "bag", "test");
            h.Step();
            Assert.AreEqual("Accepted:SameAsCollectedCandidate", h.Events("hit.signal.resolved").Single().Reason);
            Assert.AreEqual(false, h.Events("hit.signal.resolved").Single().Get("appliedAsHit"));
            // a repeat of it is judged once the attacker's hitstop is over: never a second hit
            h.C.SubmitExternalHitSignal(id, "bag", "test");
            h.StepUntil(() => h.Events("hit.signal.resolved").Count == 2, 40);
            var second = h.Events("hit.signal.resolved").Last();
            Assert.IsTrue(second.Reason == "Rejected:AlreadyHitThisEvent" || second.Reason.StartsWith("Rejected:NotActive") || second.Reason.StartsWith("Rejected:StaleInstance"),
                second.Reason);
            h.StepUntil(() => h.C.Attack == null);
            Assert.AreEqual(1, h.Events("hit").Count, "one hit per target per motion");
        }
    }
}
