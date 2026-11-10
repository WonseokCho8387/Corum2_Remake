using System.Linq;
using Corum2.Core;
using NUnit.Framework;
using static Corum2.Tests.CombatFixtures;

namespace Corum2.Tests
{
    /// <summary>WP-04-R1 regressions: WP04-F01 (AI propulsion in hit reactions), WP04-F02 (air attack re-attack interval),
    /// WP04-F03 (transition timer vs hit reactions), WP04-M01 (per-phase guard / evade frequency, R-08-11).</summary>
    public class AiR1Tests
    {
        // ------------------------------------------------------------ F01

        static AttackDefinition Magic(float d = 5f) => AiFixtures.Damage(d, DamageType.Magic);

        static float Fv(DiagEvent e, string k) => System.Convert.ToSingle(e.Get(k), System.Globalization.CultureInfo.InvariantCulture);

        [Test, Category("TC_08_05")]
        public void F01_Jumper_MagicHitMidJump_NoAiPropulsion_KnockbackAway()
        {
            var h = AiFixtures.H("TC-08-05", World(("j", "jumper", 14f)), AiFixtures.Jumper());
            var j = h.C.Find("j");
            h.Step();
            Assert.AreEqual(AiAction.Jump, j.Action);
            Assert.Less(j.MoveVelX, 0f, "jumping toward the player");
            h.C.TestApplyHit(Magic(), "j");
            Assert.AreEqual(EnemyState.Stagger, j.State);
            Assert.AreEqual(0f, j.MoveVelX, "propulsion cut on the hit (EXP-AI-09)");
            Assert.AreNotEqual(AiAction.Jump, j.Action, "the jump action ends");
            Assert.Greater(j.VelX, 0f, "knockback away from the player stays");
            var x = j.Position.X;
            var prev = x;
            for (var i = 0; i < 200 && j.State == EnemyState.Stagger; i++)
            {
                h.Step();
                Assert.AreEqual(0f, j.MoveVelX, "no AI propulsion during the reaction");
                Assert.GreaterOrEqual(j.Position.X, prev - 1e-5f, "never moves toward the player while staggered");
                prev = j.Position.X;
            }
            Assert.Greater(j.Position.X, x, "net movement away from the player");
            Assert.IsTrue(j.Grounded, "gravity still applies; stagger ends on the ground");
            Assert.AreEqual(1, h.Count("ai.propulsionCut"));
            Assert.AreEqual(0, h.Events("enemy.state").Count(e => e.ActorId == "j" && e.StateAfter == "AttackStartup" && e.SimulationTick < h.Sim.Tick - 1),
                "no air attack from the cut jump");
        }

        [Test, Category("TC_08_05")]
        public void F01_Jumper_PhysicalAirHitMidJump_StopsHorizontalPropulsion_NoAirAttack()
        {
            var h = AiFixtures.H("TC-08-05", World(("j", "jumper", 14f)), AiFixtures.Jumper());
            var j = h.C.Find("j");
            h.Step();
            h.C.TestApplyHit(AiFixtures.Damage(5f), "j");
            StringAssert.StartsWith("AirHit", h.Events("hit").Last().Reason);
            var x = j.Position.X;
            h.StepUntil(() => j.Grounded, 200);
            Assert.AreEqual(x, j.Position.X, 1e-4, "air hit: no horizontal AI movement while falling");
            Assert.AreEqual(0, h.Events("enemy.state").Count(e => e.ActorId == "j" && e.StateAfter == "AttackStartup"), "no air attack after the hit");
        }

        [Test, Category("TC_08_11")]
        public void F01_BossPhaseJump_MagicHit_NoPropulsion()
        {
            var p1 = AiFixtures.Phase("p1", AiFixtures.Pattern("near", 0f, 2f));
            p1.JumpInterval = 1f;
            p1.JumpSpeedX = 6f;
            var h = AiFixtures.H("TC-08-11", World(("b", "J", 16f)), AiFixtures.Boss("J", "p1", p1));
            var b = h.C.Find("b");
            h.StepUntil(() => h.Count("ai.jump") > 0, 150);
            Assert.AreEqual(1, h.Count("ai.jump", "BossPhaseJump"));
            Assert.Less(b.MoveVelX, 0f);
            h.C.TestApplyHit(Magic(), "b");
            Assert.AreEqual(0f, b.MoveVelX);
            var prev = b.Position.X;
            while (b.State == EnemyState.Stagger)
            {
                h.Step();
                Assert.AreEqual(0f, b.MoveVelX);
                Assert.GreaterOrEqual(b.Position.X, prev - 1e-5f, "boss does not keep closing in while staggered");
                prev = b.Position.X;
                if (h.Sim.Tick > 400) break;
            }
        }

        [Test, Category("TC_08_05")]
        public void F01_ReactionStates_NeverKeepAiPropulsion()
        {
            var states = new[]
            {
                EnemyState.Stagger, EnemyState.DodgeReact, EnemyState.Launched, EnemyState.Down, EnemyState.Wakeup, EnemyState.PhaseTransition,
                EnemyState.Guard,
            };
            foreach (var st in states)
            {
                var h = AiFixtures.H("TC-08-05", World(("j", "jumper", 14f)), AiFixtures.Jumper());
                var j = h.C.Find("j");
                h.Step();
                j.State = st;
                j.Timer = 1f;
                j.VelX = 0f;
                var x = j.Position.X;
                h.Step();
                Assert.AreEqual(0f, j.MoveVelX, st + ": AI propulsion off");
                Assert.AreEqual(x, j.Position.X, 1e-5, st + ": no horizontal movement without knockback");
            }
        }

        [Test, Category("TC_08_12")]
        public void F01_ParryDuringJumperAirAttack_CutsJumpAndKnocksBack()
        {
            var jd = AiFixtures.Jumper();
            jd.Ai.AirAttack.Startup = 0.12f;
            jd.Ai.AirAttack.HitboxWidth = 3f; // reaches the player from the air attack range (fixture only)
            jd.Ai.AirAttack.HitboxHeight = 4f;
            var h = AiFixtures.H("TC-08-12", World(("j", "jumper", 14f)), jd);
            var j = h.C.Find("j");
            h.StepUntil(() => j.InAttack, 60);
            Assert.IsFalse(j.Grounded);
            h.Press(LogicalAction.Guard).StepUntil(() => h.Count("guard.parry") > 0 || h.Count("hit.player") > 0 || j.Grounded, 60);
            Assert.AreEqual(1, h.Count("guard.parry"), "air attack parried");
            Assert.AreEqual(EnemyState.Stagger, j.State);
            Assert.AreEqual(0f, j.MoveVelX);
            Assert.AreNotEqual(AiAction.Jump, j.Action);
            var prev = j.Position.X;
            for (var i = 0; i < 20; i++)
            {
                h.Step();
                Assert.GreaterOrEqual(j.Position.X, prev - 1e-5f, "parry knockback away, no propulsion toward the player");
                prev = j.Position.X;
            }
        }

        // ------------------------------------------------------------ F02

        static EnemyDefinition SlowJumper(float interval)
        {
            var e = AiFixtures.Jumper();
            e.AttackInterval = interval;
            e.Ai.JumpCooldown = 0f;
            return e;
        }

        static int AirStarts(SimHarness h) =>
            h.Events("enemy.state").Count(e => e.ActorId == "j" && e.StateAfter == "AttackStartup" && e.Reason.StartsWith("AirAttack"));

        [Test, Category("TC_08_07")]
        public void F02_AirAttack_WaitsForReattackInterval_EvenWithJumpCooldownZero()
        {
            var h = AiFixtures.H("TC-08-07", World(("j", "jumper", 14f)), SlowJumper(100f));
            var j = h.C.Find("j");
            h.C.PlayerHp = 10000f;
            h.StepUntil(() => j.InAttack, 60);
            Assert.AreEqual(1, AirStarts(h));
            h.StepUntil(() => !j.InAttack && j.Grounded && j.State == EnemyState.Idle, 200);
            Assert.Greater(j.Cooldown, 90f);
            h.Teleport(j.Position.X - 4f).Seconds(3);
            Assert.Greater(h.Count("ai.jump"), 1, "jumping itself is not forbidden");
            Assert.AreEqual(1, AirStarts(h), "no second air attack while the 100 s interval runs (WP04-F02)");
            Assert.Greater(h.Count("ai.blocked", "Cooldown:ReattackInterval:Air"), 0);
        }

        [Test, Category("TC_08_07")]
        public void F02_AirAttack_Boundary_StartsOnlyOnceTheIntervalIsOver()
        {
            var h = AiFixtures.H("TC-08-07", World(("j", "jumper", 14f)), SlowJumper(1.0f));
            var j = h.C.Find("j");
            h.C.PlayerHp = 10000f;
            h.StepUntil(() => j.InAttack, 60);
            h.StepUntil(() => !j.InAttack && j.Grounded && j.State == EnemyState.Idle, 200);
            var lastCooldown = j.Cooldown;
            for (var i = 0; i < 400 && AirStarts(h) < 2; i++)
            {
                // keep the player at jump distance whenever the jumper is back on the ground
                if (j.Grounded && j.State == EnemyState.Idle && System.Math.Abs(j.Position.X - h.P.Position.X) < 3.5f)
                    h.Teleport(j.Position.X + (j.Position.X < 30f ? 4f : -4f));
                lastCooldown = j.Cooldown;
                h.Step();
            }
            Assert.AreEqual(2, AirStarts(h), "second air attack after the interval");
            Assert.LessOrEqual(j.Cooldown, 0f, "started with the interval over");
            Assert.LessOrEqual(lastCooldown, 0.02f + 1e-4f, "the tick before, at most one tick of interval was left");
            Assert.Greater(h.Count("ai.blocked", "Cooldown:ReattackInterval:Air"), 0, "blocked while the interval was running");
        }

        [Test, Category("TC_08_05")]
        public void F02_JumpWithPlayerOutOfAirRange_NoAirAttack_LandsThenGround()
        {
            var h = AiFixtures.H("TC-08-05", World(("j", "jumper", 14f)), AiFixtures.Jumper());
            var j = h.C.Find("j");
            h.Step();
            h.Teleport(4f);
            h.StepUntil(() => j.Grounded, 200);
            Assert.AreEqual(0, AirStarts(h), "player out of air range");
            Assert.AreEqual(1, h.Count("ai.land"));
        }

        // ------------------------------------------------------------ F03

        static EnemyDefinition TransitionBoss(float transition = 2f)
        {
            var p1 = AiFixtures.Phase("p1", AiFixtures.Pattern("a", 0f, 2f));
            p1.Transitions.Add(AiFixtures.Ev("go", "p2"));
            var p2 = AiFixtures.Phase("p2", AiFixtures.Pattern("b", 0f, 2f));
            p2.TransitionTime = transition;
            return AiFixtures.Boss("T", "p1", p1, p2);
        }

        /// <summary>Boss b at distance 1, event raised while free; returns the tick the transition started.</summary>
        static long StartTransition(SimHarness h, EnemyActor b)
        {
            h.C.PlayerHp = 100000f;
            h.StepUntil(() => b.State == EnemyState.Idle && b.Cooldown > 0f, 200);
            h.C.RaiseBossEvent("b", "go");
            h.StepUntil(() => b.State == EnemyState.PhaseTransition, 200);
            return h.Events("boss.phase").Last(e => e.StateAfter == "p2").SimulationTick;
        }

        static long FirstP2Attack(SimHarness h, long after) =>
            h.Events("boss.pattern").Where(e => e.StateAfter == "b" && e.SimulationTick >= after).Select(e => e.SimulationTick).DefaultIfEmpty(-1).First();

        [Test, Category("TC_08_10")]
        public void F03_NoHit_TransitionRunsFullTime()
        {
            var h = AiFixtures.H("TC-08-10", World(("b", "T", 11f)), TransitionBoss());
            var b = h.C.Find("b");
            var t0 = StartTransition(h, b);
            h.Seconds(3);
            var end = h.Events("boss.transitionEnd").Single();
            Assert.AreEqual(t0 + 100, end.SimulationTick, 1, "2 s = 100 ticks");
            Assert.GreaterOrEqual(FirstP2Attack(h, t0), end.SimulationTick);
        }

        [TestCase(false, TestName = "F03_PhysicalHitDuringTransition_KeepsTransitionTime")]
        [TestCase(true, TestName = "F03_MagicHitDuringTransition_KeepsTransitionTime")]
        public void F03_HitDuringTransition_KeepsTransitionTime(bool magic)
        {
            var h = AiFixtures.H("TC-08-10", World(("b", "T", 11f)), TransitionBoss());
            var b = h.C.Find("b");
            var t0 = StartTransition(h, b);
            h.Step(5);
            h.C.TestApplyHit(magic ? Magic() : AiFixtures.Damage(5f), "b");
            Assert.AreEqual(EnemyState.Stagger, b.State, "hits react normally (EXP-BOSS-03)");
            Assert.Greater(b.TransitionTimer, 1.8f, "transition time not overwritten by the reaction");
            h.StepUntil(() => b.State != EnemyState.Stagger, 100);
            Assert.AreEqual(EnemyState.PhaseTransition, b.State, "back to the transition after the reaction");
            StringAssert.Contains("TransitionResumes", h.Events("enemy.state").Last(e => e.ActorId == "b").Reason);
            h.Seconds(3);
            var end = h.Events("boss.transitionEnd").Single();
            Assert.AreEqual(t0 + 100 + 2, end.SimulationTick, 1, "ends on time (2 s plus the 2-tick hitstop of the one hit)");
            var first = FirstP2Attack(h, t0);
            Assert.Greater(first, 0);
            Assert.GreaterOrEqual(first, end.SimulationTick, "no new attack before the transition ends");
        }

        [Test, Category("TC_08_10")]
        public void F03_RepeatedHits_TransitionEndsDuringReaction_ThenFree()
        {
            var h = AiFixtures.H("TC-08-10", World(("b", "T", 11f)), TransitionBoss(1f));
            var b = h.C.Find("b");
            var t0 = StartTransition(h, b);
            for (var i = 0; i < 6; i++)
            {
                h.C.TestApplyHit(AiFixtures.Damage(1f), "b");
                h.Seconds(0.3);
                Assert.IsFalse(b.InAttack, "no attack while being hit through the transition");
            }
            var end = h.Events("boss.transitionEnd").Single();
            // the transition timer runs in game time and pauses only for the boss's hitstop (0.04 s = 2 ticks per hit, EXP-BOSS-03)
            var hitsBefore = h.Events("hit").Count(e => e.SimulationTick < end.SimulationTick);
            Assert.AreEqual(t0 + 50 + 2 * hitsBefore, end.SimulationTick, 1, "1 s plus hitstop");
            Assert.AreEqual("TransitionDoneDuringReaction(EXP-BOSS-03)", end.Reason);
            h.StepUntil(() => b.State != EnemyState.Stagger, 100);
            Assert.AreEqual(EnemyState.Idle, b.State, "transition over: free after the reaction");
            Assert.AreEqual(0, h.Events("boss.pattern").Count(e => e.StateAfter == "b" && e.SimulationTick < end.SimulationTick));
        }

        [Test, Category("TC_08_10")]
        public void F03_DeathDuringTransition_CancelsIt()
        {
            var h = AiFixtures.H("TC-08-10", World(("b", "T", 11f)), TransitionBoss());
            var b = h.C.Find("b");
            StartTransition(h, b);
            h.Step(10);
            h.C.TestApplyHit(AiFixtures.Damage(5000f), "b");
            Assert.AreEqual(EnemyState.Dead, b.State, "death wins (EXP-BOSS-02)");
            Assert.AreEqual(1, h.Count("boss.transitionCancel", "Death"));
            h.Seconds(3);
            Assert.AreEqual(0, h.Count("boss.transitionEnd"));
            Assert.AreEqual(0, h.Events("boss.pattern").Count(e => e.StateAfter == "b"));
        }

        // ------------------------------------------------------------ M01 (R-08-11 guard / evade frequency)

        static EnemyDefinition DefenseBoss(float guard1, float evade1, float guard2, float evade2)
        {
            // Patterns only far away, so the boss never attacks the player standing next to it.
            var p1 = AiFixtures.Phase("p1", AiFixtures.Pattern("far1", 6f, 9f));
            p1.PreferredMinDistance = 0f;
            p1.PreferredMaxDistance = 4f;
            p1.GuardFrequency = guard1;
            p1.EvadeFrequency = evade1;
            p1.Transitions.Add(AiFixtures.Ev("go", "p2"));
            var p2 = AiFixtures.Phase("p2", AiFixtures.Pattern("far2", 6f, 9f));
            p2.PreferredMinDistance = 0f;
            p2.PreferredMaxDistance = 4f;
            p2.GuardFrequency = guard2;
            p2.EvadeFrequency = evade2;
            p2.EvadeSpeed = 8f;
            p2.EvadeTime = 0.3f;
            return AiFixtures.Boss("D", "p1", p1, p2);
        }

        static void Free(SimHarness h, EnemyActor b) => h.StepUntil(() => b.State == EnemyState.Idle && b.Hitstop <= 0f, 300).Step();

        static void Attack(SimHarness h, EnemyActor b)
        {
            h.Teleport(b.Position.X - 1.0f);
            h.Hit();
        }

        [Test, Category("TC_08_11")]
        public void M01_GuardFrequencyHalf_EveryOtherAttackGuarded_GuardRuleDamage()
        {
            var h = AiFixtures.H("TC-08-11", World(("b", "D", 11f)), DefenseBoss(0.5f, 0f, 0f, 0f));
            var b = h.C.Find("b");
            h.Step();
            for (var i = 0; i < 4; i++)
            {
                Free(h, b);
                Attack(h, b);
            }
            var picks = h.Events("boss.defense").Select(e => e.StateAfter).ToList();
            CollectionAssert.AreEqual(new[] { "None", "Guard", "None", "Guard" }, picks, "frequency 0.5 = every other eligible attack (EXP-BOSS-05)");
            var guarded = h.Events("hit").Where(e => e.Reason.StartsWith("BossGuard")).ToList();
            Assert.AreEqual(2, guarded.Count, "guarded hits");
            foreach (var g in guarded)
            {
                Assert.AreEqual(1f, Fv(g, "damageTaken"), 1e-4, "D 10 x 0.1 = 1 (guard rule)");
                Assert.AreEqual("Guard", g.StateAfter, "no stagger");
                Assert.AreEqual(0f, Fv(g, "knockbackVel"), 1e-4);
            }
            Assert.AreEqual(2, h.Events("hit").Count(e => e.Reason.StartsWith("Stagger")), "unguarded hits stagger");
            Assert.AreEqual(1000f - 10f - 1f - 10f - 1f, b.Hp, 1e-3);
        }

        [Test, Category("TC_08_11")]
        public void M01_Guard_MagicAndBackHitsAreNotGuarded()
        {
            var h = AiFixtures.H("TC-08-11", World(("b", "D", 11f)), DefenseBoss(1f, 0f, 0f, 0f));
            var b = h.C.Find("b");
            h.Step();
            Free(h, b);
            h.Teleport(10f).Press(LogicalAction.Attack).Step();
            Assert.AreEqual(EnemyState.Guard, b.State);
            h.C.TestApplyHit(Magic(), "b");
            Assert.AreEqual(EnemyState.Stagger, b.State, "magic is not guarded (R-08-12, EXP-BOSS-06)");
            h.StepUntil(() => h.C.Attack == null, 60);
            Free(h, b);
            h.Teleport(10f).Press(LogicalAction.Attack).Step();
            Assert.AreEqual(EnemyState.Guard, b.State);
            h.Teleport(12f, -1);
            h.C.TestApplyHit(AiFixtures.Damage(10f), "b");
            Assert.AreEqual(EnemyState.Stagger, b.State, "hit from behind is not guarded");
            Assert.AreEqual(10f, Fv(h.Events("hit").Last(), "damageTaken"), 1e-4);
        }

        [Test, Category("TC_08_11")]
        public void M01_EvadePerPhase_BackStepAway_OtherPhaseAndBossUnchanged()
        {
            var h = AiFixtures.H("TC-08-11", World(("b", "D", 11f), ("o", "D2", 40f)), DefenseBoss(0f, 0f, 0f, 1f),
                AiFixtures.Boss("D2", "q", AiFixtures.Phase("q", AiFixtures.Pattern("q", 6f, 9f))));
            var b = h.C.Find("b");
            h.C.PlayerHp = 100000f;
            h.Step();
            Free(h, b);
            Attack(h, b);
            Assert.AreEqual(0, h.Count("boss.defense"), "phase 1: guard and evade frequency 0");
            Free(h, b);
            h.C.RaiseBossEvent("b", "go");
            h.StepUntil(() => b.PhaseId == "p2", 100);
            h.StepUntil(() => b.State == EnemyState.Idle, 200).Step();
            h.Teleport(b.Position.X - 2.0f); // eligible (DefenseRange 3) and the step takes it out of the hitbox
            var x = b.Position.X;
            h.Press(LogicalAction.Attack).Step();
            Assert.AreEqual(EnemyState.Evade, b.State, "phase 2: evade frequency 1");
            Assert.AreEqual("Evade", h.Events("boss.defense").Last().StateAfter);
            h.StepUntil(() => b.State != EnemyState.Evade, 60);
            Assert.AreEqual(x + 8f * 0.3f, b.Position.X, 0.2f, "back step EvadeSpeed x EvadeTime away from the player");
            Assert.AreEqual(0, h.Events("boss.defense").Count(e => e.ActorId == "o"), "other boss has no defense settings");
        }

        [Test, Category("TC_08_10")]
        public void M01_NoGuardOrEvadeDuringTransition()
        {
            var boss = DefenseBoss(1f, 0f, 1f, 0f);
            boss.Boss.Phases[1].TransitionTime = 1f;
            var h = AiFixtures.H("TC-08-10", World(("b", "D", 11f)), boss);
            var b = h.C.Find("b");
            h.Step();
            Free(h, b);
            h.C.RaiseBossEvent("b", "go");
            h.StepUntil(() => b.State == EnemyState.PhaseTransition, 100);
            var n = h.Count("boss.defense");
            h.Teleport(10f).Press(LogicalAction.Attack).Step();
            Assert.AreEqual(n, h.Count("boss.defense"), "an attack start during the transition is not answered (EXP-BOSS-03)");
            Assert.AreNotEqual(EnemyState.Guard, b.State);
        }

        [Test, Category("DATA")]
        public void M01_Validator_GuardEvadeSettings()
        {
            System.Func<EnemyDefinition, System.Collections.Generic.List<ValidationIssue>> v = e =>
                DataValidator.ValidateCombat(Profile(), Unlocks(), new System.Collections.Generic.List<EnemyDefinition> { e }, null);
            Assert.IsEmpty(v(DefenseBoss(0.5f, 0.25f, 0f, 1f)));
            var cases = new (string field, System.Action<BossPhase> f)[]
            {
                ("GuardFrequency", p => p.GuardFrequency = 1.5f),
                ("EvadeFrequency", p => p.EvadeFrequency = float.NaN),
                ("GuardTime", p => { p.GuardFrequency = 0.5f; p.GuardTime = 0f; }),
                ("EvadeSpeed", p => { p.EvadeFrequency = 0.5f; p.EvadeSpeed = -1f; }),
                ("DefenseRange", p => { p.GuardFrequency = 0.5f; p.DefenseRange = 0f; }),
            };
            foreach (var (field, f) in cases)
            {
                var e = DefenseBoss(0f, 0f, 0f, 0f);
                f(e.Boss.Phases[0]);
                var issues = v(e);
                Assert.IsTrue(issues.Any(i => i.ToString().Contains(field)), field + ": " + string.Join(" | ", issues));
            }
        }
    }
}
