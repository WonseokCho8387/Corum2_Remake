using System;
using System.Collections.Generic;
using Corum2.Core;
using NUnit.Framework;
using static Corum2.Tests.CombatFixtures;

namespace Corum2.Tests
{
    /// <summary>WP-02-R1 regressions for the GPT review findings WP02-F01..F03 (reproduction conditions from
    /// docs/GPT/evidence/WP-R1_WP02_GPT_v1.0/CombatBoundaryProbe.cs, extended per the review's re-check list).</summary>
    public class Wp02R1RegressionTests
    {
        // ------------------------------------------------------------------ F01 multi-Dodge

        static SimHarness DodgeSetup(string tc, (string, string, float)[] spawns, List<EnemyDefinition> defs)
        {
            var h = Harness(tc, World(spawns), defs);
            h.StepUntil(() => h.C.Find("e1").State == EnemyState.AttackStartup);
            h.Step(12); // same timing as SimultaneousHit_IsDodge_ForBoth
            return h;
        }

        /// <param name="order">0 = attackers first, 1 = residual targets first, 2 = interleaved.</param>
        [Test, Category("TC_06_15"), Category("TC_06_16"), Category("WP02_F01")]
        public void TwoAttackers_Dodge_ResidualTargets_NoException(
            [Values(0, 1, 3)] int residual, [Values(0, 1, 2)] int order, [Values(false, true)] bool armor)
        {
            var att = new List<(string, string, float)> { ("e1", "hitter", 11f), ("e2", "hitter", 11f) };
            var res = new List<(string, string, float)>();
            for (var i = 0; i < residual; i++) res.Add(("b" + i, "bag", 11f));
            var spawns = new List<(string, string, float)>();
            if (order == 0) { spawns.AddRange(att); spawns.AddRange(res); }
            else if (order == 1) { spawns.AddRange(res); spawns.AddRange(att); }
            else
            {
                spawns.Add(att[0]);
                spawns.AddRange(res);
                spawns.Add(att[1]);
            }
            var h = DodgeSetup("TC-06-15", spawns.ToArray(), Defs(Hitter("hitter", armor: armor), Bag()));

            Assert.DoesNotThrow(() => h.Press(LogicalAction.Attack).StepUntil(() => h.Count("dodge") > 0, 30).Step(2));

            Assert.AreEqual(2, h.Count("dodge"), "each simultaneous attacker gives one Dodge (CF-06 count order held)");
            Assert.AreEqual(h.C.Profile.PlayerMaxHp, h.C.PlayerHp, "no damage to the player");
            Assert.AreEqual(0, h.Count("hit.player"));
            Assert.AreEqual(MotorState.DodgeReact, h.P.State);
            Assert.IsNull(h.C.Attack, "attack cancelled by the Dodge");
            Assert.IsFalse(h.C.ChainAlive);
            Assert.AreEqual("Dodge", h.C.LastChainEndReason);
            foreach (var id in new[] { "e1", "e2" })
            {
                var e = h.C.Find(id);
                Assert.AreEqual(e.Def.MaxHp, e.Hp, id + " no damage");
                Assert.AreEqual(EnemyState.DodgeReact, e.State, id);
            }
            // EXP-DODGE-01: other targets overlapped on the same tick still take the (cancelled) attack's hit, once each.
            var dmg = h.C.Profile.GroundNormals[0].TestDamage;
            for (var i = 0; i < residual; i++)
            {
                var b = h.C.Find("b" + i);
                Assert.AreEqual(b.Def.MaxHp - dmg, b.Hp, 1e-3f, b.Id);
            }
            Assert.AreEqual(residual, h.Count("hit"));
            Assert.AreEqual(residual > 0 ? 1 : 0, h.Count("hitstop"), "one attacker stop for the residual hit event");
            Assert.AreEqual(0, h.C.ComboHits, "chain ended by the Dodge; residual hits are not counted into a new chain");
        }

        // ------------------------------------------------------------------ F02 hitstop per hit event

        static CombatProfile LongActive(int hitEvents = 1)
        {
            var p = Profile();
            p.GroundNormals[0].Active = 0.4f;
            p.GroundNormals[0].Knockback = 0f;
            p.GroundNormals[0].HitEvents = hitEvents;
            return p;
        }

        [Test, Category("TC_06_03"), Category("WP02_F02")]
        public void SameEvent_LateTargets_DoNotRestartAttackerHitstop([Values(1, 9)] int late)
        {
            var spawns = new List<(string, string, float)> { ("a", "bag", 11f) };
            for (var i = 0; i < late; i++) spawns.Add(("l" + i, "bag", 20f));
            var h = Harness("TC-06-03", World(spawns.ToArray()), Defs(Bag()), LongActive());
            h.Press(LogicalAction.Attack).StepUntil(() => h.Count("hitstop") > 0);
            var id = h.C.Attack.InstanceId;
            h.Step(5);
            for (var i = 0; i < late; i++) h.C.Find("l" + i).Position = new Vec2(11f, 0f);
            h.Step(1);
            foreach (var e in h.C.Enemies)
                if (e.Id != "a") Assert.Greater(e.Hitstop, 0f, e.Id + " gets its own reaction stop (EXP-HITSTOP-01)");
            h.Step(2);
            Assert.AreEqual(id, h.C.Attack.InstanceId, "same motion");
            Assert.AreEqual(1 + late, h.Count("hit"), "late targets are still hit");
            Assert.AreEqual(1, h.Count("hitstop"), "attacker stopped once for the single hit event");
            Assert.AreEqual(1, h.Count("hitstop.skip"));
            Assert.AreEqual(0f, Math.Max(0f, h.C.PlayerHitstop), 1e-4f, "no new attacker stop");
            h.StepUntil(() => h.C.Attack == null);
            Assert.AreEqual(1 + late, h.Count("hit"), "already-hit targets are not hit again by staying in the hitbox");
        }

        [Test, Category("TC_06_03"), Category("WP02_F02")]
        public void ExplicitMultiHitEvents_EachStopOnce()
        {
            var p = LongActive(2);
            var h = Harness("TC-06-03", World(("a", "bag", 11f), ("b", "bag", 11f)), Defs(Bag()), p);
            h.Hit();
            Assert.AreEqual(4, h.Count("hit"), "2 events x 2 targets");
            Assert.AreEqual(2, h.Count("hitstop"), "one attacker stop per hit event, not per target");
        }

        [Test, Category("TC_06_03"), Category("WP02_F02")]
        public void NextAttack_StopsAgain_AndFollowsData([Values(0.04f, 0.1f)] float stop)
        {
            var p = LongActive();
            p.GroundNormals[1].Hitstop = stop;
            var h = Harness("TC-06-03", World(("a", "bag", 11f)), Defs(Bag()), p);
            h.Hit();
            h.Press(LogicalAction.Attack).StepUntil(() => h.Count("hitstop") == 2, 60);
            Assert.AreEqual(stop, h.C.PlayerHitstop, 1e-4f, "a new attack is a new hit event");
        }

        // ------------------------------------------------------------------ F03 standard max 10

        static EnemyDefinition ImmuneBag()
        {
            var b = Bag();
            b.Launch = LaunchResponse.Immune;
            return b;
        }

        [Test, Category("TC_04_01"), Category("WP02_F03")]
        public void NonDash_RepeatedLauncher_CappedAtTen()
        {
            var h = Harness("TC-04-01", World(("e", "bag", 11f)), Defs(ImmuneBag()));
            for (var i = 0; i < 10; i++) h.Hit(LogicalAction.SpecialAttack);
            Assert.AreEqual(10, h.C.ChainSteps);
            Assert.AreEqual(10, h.C.ComboHits);
            h.Press(LogicalAction.SpecialAttack).Step();
            Assert.IsNotNull(h.LastWithReason("ChainMax:Total10"), "11th attack rejected (EXP-COMBO-04)");
            Assert.AreEqual(10, h.Count("attack.start"));
            Assert.AreEqual(10, h.C.ChainSteps);
            Assert.AreEqual(1, h.C.ChainId);
            Assert.IsFalse(h.P.Air.DashUsed);

            // after the window ends, the next attack opens a new chain at step 1
            h.StepUntil(() => !h.C.ChainAlive, 60);
            Assert.AreEqual("Timeout", h.C.LastChainEndReason);
            Assert.AreEqual(10, h.C.LastComboHits);
            h.Hit(LogicalAction.SpecialAttack);
            Assert.AreEqual(2, h.C.ChainId);
            Assert.AreEqual(1, h.C.ChainSteps);
        }

        [Test, Category("TC_04_01"), Category("WP02_F03")]
        public void NonDash_MixedPath_CappedAtTen()
        {
            var h = Harness("TC-04-01", World(("e", "bag", 11f)), Defs(ImmuneBag()));
            for (var i = 0; i < 4; i++) h.Hit();
            for (var i = 0; i < 6; i++) h.Hit(LogicalAction.SpecialAttack);
            Assert.AreEqual(10, h.C.ChainSteps);
            h.Press(LogicalAction.SpecialAttack).Step();
            Assert.IsNotNull(h.LastWithReason("ChainMax:Total10"));
            Assert.AreEqual(10, h.C.ComboHits);
        }

        [Test, Category("TC_04_01"), Category("WP02_F03")]
        public void NonDash_DiveAtCap_OpensNewChain()
        {
            var h = Harness("TC-04-01", World(("e", "bag", 11f)), Defs(ImmuneBag()));
            for (var i = 0; i < 10; i++) h.Hit(LogicalAction.SpecialAttack);
            h.Press(LogicalAction.Jump).Step(8).Dir(Dir8.Right).Press(LogicalAction.SpecialAttack).Step(2);
            Assert.AreEqual(MotorState.Dive, h.P.State);
            Assert.IsNotNull(h.LastWithReason("ChainMax:Total10(R-04-01"), "old chain ended at the cap");
            Assert.AreEqual(10, h.C.LastComboHits);
            Assert.AreEqual(2, h.C.ChainId);
            Assert.AreEqual(1, h.C.ChainSteps, "the dive is step 1 of a new chain");
        }

        [Test, Category("TC_04_01"), Category("WP02_F03")]
        public void DashBranch_Cf03Held_OnlyDiveAfterDashPassesTen()
        {
            var h = Harness("TC-INT-03", World(("e", "bag", 11f)), Defs(Bag()));
            for (var i = 0; i < 4; i++) h.Hit();
            h.Hit(LogicalAction.SpecialAttack);
            h.Press(LogicalAction.Jump).Step(3);
            for (var i = 0; i < 4; i++) h.Hit();
            Assert.AreEqual(9, h.C.ChainSteps);
            h.Dir(Dir8.Right).Press(LogicalAction.Attack).Step(2);
            Assert.AreEqual(1, h.Count("attack.start", "Attack:AirDash"), "dash joined the chain (it may already be stopped at the first target, R-04-06)");
            Assert.AreEqual(10, h.C.ChainSteps);
            Assert.IsTrue(h.C.ChainHasDash);
            h.StepUntil(() => h.P.State != MotorState.AirDash, 60);
            h.Dir(Dir8.Right).Press(LogicalAction.SpecialAttack).Step(2);
            Assert.AreEqual(MotorState.Dive, h.P.State);
            Assert.AreEqual(1, h.C.ChainId, "dash branch is not split by EXP-COMBO-04");
            Assert.AreEqual(11, h.C.ChainSteps, "CF-03 held: recorded, not approved");
            var dive = h.Events("attack.start").FindLast(e => e.Reason == "Attack:Dive");
            StringAssert.Contains("CF-03 held", (string)dive.Get("cf"));
        }

        [Test, Category("TC_04_01"), Category("WP02_F03")]
        public void HundredHits_TenTargetsTenSteps_NotCapped()
        {
            var spawns = new (string, string, float)[10];
            for (var i = 0; i < 10; i++) spawns[i] = ("e" + i, "boss", 11f);
            var boss = Bag("boss", 100000f);
            boss.Launch = LaunchResponse.Immune;
            var h = Harness("TC-04-04", World(spawns), Defs(boss));
            for (var i = 0; i < 4; i++) h.Hit();
            for (var i = 0; i < 6; i++) h.Hit(LogicalAction.SpecialAttack);
            Assert.AreEqual(10, h.C.ChainSteps);
            Assert.AreEqual(100, h.C.ComboHits, "the cap limits attack steps, not the hit count shown (R-04-04)");
        }
    }
}
