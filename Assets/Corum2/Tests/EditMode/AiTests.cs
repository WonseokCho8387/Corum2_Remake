using System.Collections.Generic;
using System.Linq;
using Corum2.Core;
using NUnit.Framework;
using static Corum2.Tests.CombatFixtures;

namespace Corum2.Tests
{
    /// <summary>WP-04 enemy AI and boss phase test data (test values only).</summary>
    public static class AiFixtures
    {
        static DataMeta Meta(string id) => new DataMeta { DomainId = id, DataVersion = "test-4", Status = "experimental" };

        public static AttackDefinition Melee(string id, float d = 10f, float startup = 0.3f, bool armor = false) => new AttackDefinition
        {
            Id = id, Kind = AttackKind.EnemyMelee, TestDamage = d, Startup = startup, Active = 0.1f, Recovery = 0.3f,
            HitboxOffsetX = 0.8f, HitboxOffsetY = 0.9f, HitboxWidth = 1.2f, HitboxHeight = 1.2f, Knockback = 4f, SuperArmor = armor,
        };

        static EnemyDefinition Base(string id, AiType type, float interval = 1f)
        {
            var e = Bag(id);
            e.Attack = Melee(id + ".atk");
            e.AttackRange = 1.6f;
            e.AttackInterval = interval;
            e.FirstAttackDelay = 0f;
            e.Ai = new EnemyAIProfile { Type = type, MoveSpeed = 3f, TrackRange = 15f };
            return e;
        }

        public static EnemyDefinition Chaser(string id = "chaser", float interval = 1f) => Base(id, AiType.Chaser, interval);

        public static EnemyDefinition Fleer(string id = "fleer", bool reapproach = true)
        {
            var e = Base(id, AiType.Fleer);
            e.Ai.FleeSpeed = 4f;
            e.Ai.FleeTime = 0.8f;
            e.Ai.ReapproachAfterFlee = reapproach;
            return e;
        }

        public static EnemyDefinition Jumper(string id = "jumper")
        {
            var e = Base(id, AiType.Jumper);
            e.Ai.JumpMinDistance = 3f;
            e.Ai.JumpMaxDistance = 5f;
            e.Ai.JumpSpeedX = 5f;
            e.Ai.JumpVelocityY = 9f;
            e.Ai.JumpCooldown = 2f;
            e.Ai.AirAttack = Melee(id + ".air", 8f, 0.06f);
            e.Ai.AirAttackRange = 2.2f;
            return e;
        }

        public static EnemyDefinition Ranged(string id = "ranged")
        {
            var e = Base(id, AiType.Ranged, 1.5f);
            e.AttackRange = 6f;
            e.Attack = Melee(id + ".shot", 6f, 0.2f);
            e.Ai.Projectile = new ProjectileDefinition { Speed = 10f, MaxDistance = 9f };
            e.Ai.RetreatSpeed = 3f;
            e.Ai.RetreatTime = 0.6f;
            return e;
        }

        public static BossPattern Pattern(string id, float min, float max, PlayerAirCondition air = PlayerAirCondition.Any, int prio = 0,
            float d = 10f, bool armor = false, ProjectileDefinition proj = null) =>
            new BossPattern { Id = id, Attack = Melee(id, d, 0.3f, armor), MinDistance = min, MaxDistance = max, PlayerState = air, Priority = prio, Projectile = proj };

        public static BossPhase Phase(string id, params BossPattern[] patterns)
        {
            var p = new BossPhase { Id = id, AttackInterval = 0.8f, ApproachSpeed = 2f, RetreatSpeed = 2f, PreferredMinDistance = 0.8f, PreferredMaxDistance = 2.5f, TransitionTime = 0.5f };
            p.Patterns.AddRange(patterns);
            return p;
        }

        public static EnemyDefinition Boss(string id, string start, params BossPhase[] phases)
        {
            var e = Bag(id, 1000f);
            e.Attack = null;
            e.AttackRange = 1.6f;
            e.AttackInterval = 1f;
            e.FirstAttackDelay = 0f;
            e.Launch = LaunchResponse.Immune;
            e.Ai = new EnemyAIProfile { Type = AiType.Boss, TrackRange = 20f };
            e.Boss = new BossDefinition { StartPhase = start };
            e.Boss.Phases.AddRange(phases);
            return e;
        }

        public static PhaseTransition Hp(float ratio, string to) => new PhaseTransition { Kind = TransitionKind.HpBelow, Value = ratio, TargetPhase = to };
        public static PhaseTransition Used(string pattern, int n, string to) => new PhaseTransition { Kind = TransitionKind.PatternUsed, PatternId = pattern, Count = n, TargetPhase = to };
        public static PhaseTransition Ev(string name, string to) => new PhaseTransition { Kind = TransitionKind.Event, EventName = name, TargetPhase = to };

        public static AttackDefinition Damage(float d, DamageType t = DamageType.Physical) => new AttackDefinition
        {
            Id = "test.dmg." + d, Kind = t == DamageType.Magic ? AttackKind.TestMagic : AttackKind.GroundNormal, DamageType = t, TestDamage = d,
            Startup = 0f, Active = 0.1f, Recovery = 0f, Knockback = 2f,
        };

        public static SimHarness H(string tc, WorldData w, params EnemyDefinition[] defs) => Harness(tc, w, Defs(defs));
    }

    public class EnemyAiTests
    {
        static int StartsAt(SimHarness h, long tick) =>
            h.Events("enemy.state").Count(e => e.StateAfter == "AttackStartup" && e.SimulationTick == tick);

        [Test, Category("TC_08_01")]
        public void TenReadyChasers_AllStartAttack_SameTick_NoGlobalLimit()
        {
            var spawns = Enumerable.Range(0, 10).Select(i => ("c" + i, "chaser", 11f)).ToArray();
            var h = AiFixtures.H("TC-08-01", World(spawns), AiFixtures.Chaser());
            h.Step();
            Assert.AreEqual(10, StartsAt(h, h.Sim.Tick), "every ready enemy attacks at its first chance (R-08-01)");
            Assert.AreEqual(10, h.C.Enemies.Count(e => e.State == EnemyState.AttackStartup));
        }

        [Test, Category("TC_08_02")]
        public void InRange_ReadyAttacksAtOnce_BlockedStatesWaitThenAttack()
        {
            var h = AiFixtures.H("TC-08-02", World(("a", "chaser", 11f), ("s", "chaser", 11f), ("d", "chaser", 11f), ("k", "chaser", 11f)), AiFixtures.Chaser());
            // Before the first decision: stagger s, down d (launcher), kill k.
            h.C.Find("s").State = EnemyState.Stagger; h.C.Find("s").Timer = 0.3f;
            h.C.Find("d").State = EnemyState.Down; h.C.Find("d").Timer = 0.3f;
            h.C.TestApplyHit(AiFixtures.Damage(5000f), "k");
            h.Step();
            Assert.AreEqual(EnemyState.AttackStartup, h.C.Find("a").State, "ready: first chance");
            Assert.AreEqual(EnemyState.Stagger, h.C.Find("s").State, "stagger is not overridden (R-08-02)");
            Assert.AreEqual(EnemyState.Down, h.C.Find("d").State);
            h.StepUntil(() => h.C.Find("s").State != EnemyState.Stagger, 30).Step();
            Assert.AreEqual(EnemyState.AttackStartup, h.C.Find("s").State, "attacks at the first chance after the stagger");
            h.StepUntil(() => h.C.Find("a").State == EnemyState.Idle, 60).Step(2);
            Assert.AreEqual(EnemyState.Idle, h.C.Find("a").State, "re-attack interval blocks (R-08-07)");
            Assert.Greater(h.Count("ai.blocked", "Cooldown"), 0);
            Assert.AreEqual(0, h.Events("enemy.state").Count(e => e.ActorId == "k" && e.StateAfter == "AttackStartup"), "dead enemy never attacks");
        }

        [Test, Category("TC_08_03")]
        public void Chaser_ApproachesAttacks_ResumesChaseAfterStagger()
        {
            var h = AiFixtures.H("TC-08-03", World(("c", "chaser", 16f)), AiFixtures.Chaser());
            var c = h.C.Find("c");
            h.Step(5);
            Assert.Less(c.Position.X, 16f, "approaches from outside the range");
            Assert.AreEqual(1, h.Count("ai.decide", "OutOfRange:Approach"));
            h.StepUntil(() => c.State == EnemyState.AttackStartup, 200);
            Assert.LessOrEqual(System.Math.Abs(c.Position.X - h.P.Position.X), 1.6f + 1e-3);
            h.StepUntil(() => c.State == EnemyState.Idle, 60);
            h.Teleport(4f);
            h.C.TestApplyHit(AiFixtures.Damage(5f), "c");
            Assert.AreEqual(EnemyState.Stagger, c.State);
            var x = c.Position.X;
            h.StepUntil(() => c.State != EnemyState.Stagger, 60).Step(5);
            Assert.Less(c.Position.X, x, "chases again after the stagger (R-08-03)");
        }

        [Test, Category("TC_08_04")]
        public void Fleer_FleesAfterHit_ThenReapproachesPerSetting()
        {
            var h = AiFixtures.H("TC-08-04", World(("f", "fleer", 11f), ("g", "fleer2", 30f)), AiFixtures.Fleer(), AiFixtures.Fleer("fleer2", false));
            var f = h.C.Find("f");
            h.StepUntil(() => f.State == EnemyState.Idle && h.Count("enemy.state", "InRange") > 0, 60);
            h.C.TestApplyHit(AiFixtures.Damage(5f), "f");
            h.StepUntil(() => f.State == EnemyState.Idle, 60);
            Assert.AreEqual(1, h.Count("ai.flee", "AfterHitReaction"));
            var x0 = f.Position.X;
            h.Step(20);
            Assert.Greater(f.Position.X, x0 + 1f, "moves away from the player (R-08-04)");
            h.StepUntil(() => h.Count("ai.actionEnd", "FleeDone") > 0, 60).Step(10);
            Assert.AreEqual("Approach", f.LastDecision, "reapproach = true");

            var g = h.C.Find("g");
            h.Teleport(28.5f);
            h.StepUntil(() => g.InAttack, 60).StepUntil(() => g.State == EnemyState.Idle, 60);
            h.C.TestApplyHit(AiFixtures.Damage(5f), "g");
            h.StepUntil(() => h.Events("ai.actionEnd").Any(e => e.ActorId == "g"), 120);
            h.Teleport(20f).Step(20);
            Assert.AreEqual(AiAction.Hold, g.Action, "reapproach = false holds");
            Assert.AreEqual("Hold", g.LastDecision);
        }

        [Test, Category("TC_08_05")]
        public void Jumper_JumpsOnlyInsideTheWindow_AndAttacksFromTheAir()
        {
            var h = AiFixtures.H("TC-08-05", World(("j", "jumper", 14f)), AiFixtures.Jumper());
            var j = h.C.Find("j");
            h.Step();
            Assert.AreEqual(1, h.Count("ai.jump", "JumpApproach"), "distance 4 is inside [3,5]");
            Assert.IsFalse(j.Grounded);
            h.StepUntil(() => j.InAttack, 60);
            Assert.AreEqual("jumper.air", j.CurAttack.Id, "air attack when close (R-08-05)");
            Assert.IsFalse(j.Grounded);

            var far = AiFixtures.H("TC-08-05", World(("j", "jumper", 18f)), AiFixtures.Jumper());
            far.Step();
            Assert.AreEqual(0, far.Count("ai.jump"), "distance 8 is outside the window: walks");
            Assert.AreEqual("Approach", far.C.Find("j").LastDecision);
            far.StepUntil(() => far.Count("ai.jump") > 0, 120);
            Assert.That(System.Math.Abs(far.C.Find("j").Position.X - far.P.Position.X), Is.InRange(3f - 0.2f, 5f + 0.2f));

            var near = AiFixtures.H("TC-08-05", World(("j", "jumper", 12f)), AiFixtures.Jumper());
            near.Step(30);
            Assert.AreEqual(0, near.Count("ai.jump"), "distance 2 is below the window: no jump");
        }

        [Test, Category("TC_08_06")]
        public void Ranged_WaitsOutside_ShootsInside_RetreatsAfter()
        {
            var h = AiFixtures.H("TC-08-06", World(("r", "ranged", 20f)), AiFixtures.Ranged());
            var r = h.C.Find("r");
            h.Step(30);
            Assert.AreEqual(20f, r.Position.X, 1e-4, "out of range: waits in place (R-08-06)");
            Assert.AreEqual(0, h.Count("projectile.spawn"));
            h.Teleport(15f).StepUntil(() => h.Count("projectile.spawn") > 0, 60);
            h.StepUntil(() => h.Count("hit.player") > 0, 60);
            Assert.AreEqual("Projectile:1", h.Events("hit.player").Single().Get("via"));
            var x = r.Position.X;
            h.StepUntil(() => h.Count("ai.retreat") > 0, 60).Step(15);
            Assert.Greater(r.Position.X, x + 0.5f, "backs away after the attack");
        }

        [Test, Category("TC_08_06")]
        public void Projectile_FrontGuardBlocks_NoParry()
        {
            var h = AiFixtures.H("TC-08-06", World(("r", "ranged", 15f)), AiFixtures.Ranged());
            h.Press(LogicalAction.Guard).Step();
            h.StepUntil(() => h.Count("guard.block") > 0 || h.Count("hit.player") > 0, 120);
            Assert.AreEqual(1, h.Count("guard.block"));
            Assert.AreEqual(0, h.Count("guard.parry"), "no parry for projectiles (EXP-AI-05)");
            Assert.AreEqual(100f, h.C.PlayerHp, 1e-4, "D 6 x 0.1 < 1 -> 0");
        }

        [Test, Category("TC_08_07")]
        public void PerEnemyIntervals_AndNoNewAttackAfterPlayerDeath()
        {
            var fast = AiFixtures.Chaser("fast", 0.5f);
            var slow = AiFixtures.Chaser("slow", 2f);
            var h = AiFixtures.H("TC-08-07", World(("a", "fast", 11f), ("b", "slow", 9f)), fast, slow);
            h.C.PlayerHp = 1000f;
            h.Seconds(6);
            var na = h.Events("enemy.state").Count(e => e.ActorId == "a" && e.StateAfter == "AttackStartup");
            var nb = h.Events("enemy.state").Count(e => e.ActorId == "b" && e.StateAfter == "AttackStartup");
            Assert.Greater(na, nb + 1, "each enemy keeps its own interval");
            h.C.PlayerHp = 1f;
            h.StepUntil(() => h.P.State == MotorState.Dead, 200);
            var t = h.Sim.Tick;
            h.Seconds(3);
            Assert.AreEqual(0, h.Events("enemy.state").Count(e => e.SimulationTick > t + 30 && e.StateAfter == "AttackStartup"),
                "no new attack after the player died (R-08-07)");
            Assert.Greater(h.Count("ai.decide", "PlayerDead"), 0);
        }
    }

    public class BossTests
    {
        static EnemyDefinition SelectorBoss() => AiFixtures.Boss("sel", "p1",
            AiFixtures.Phase("p1",
                AiFixtures.Pattern("near", 0f, 2f, PlayerAirCondition.Grounded),
                AiFixtures.Pattern("far", 3f, 8f, PlayerAirCondition.Any, 0, 8f, false, new ProjectileDefinition { Speed = 10f, MaxDistance = 10f }),
                AiFixtures.Pattern("antiair", 0f, 3f, PlayerAirCondition.Airborne, 2)));

        [Test, Category("TC_08_08")]
        public void Pattern_ByDistanceAndPlayerAirState()
        {
            var h = AiFixtures.H("TC-08-08", World(("b", "sel", 11.5f)), SelectorBoss());
            h.Step();
            Assert.AreEqual("near", h.Events("boss.pattern").Last().StateAfter);
            var b = h.C.Find("b");
            // Teleport during the pattern interval; the next decision happens when the interval runs out.
            h.StepUntil(() => b.State == EnemyState.Idle, 200);
            h.Teleport(6f).StepUntil(() => h.Count("boss.pattern") > 1, 200);
            Assert.AreEqual("far", h.Events("boss.pattern").Last().StateAfter, "distance 5.5");
            h.StepUntil(() => b.State == EnemyState.Idle, 200).Teleport(10.5f).StepUntil(() => b.Cooldown <= 0.25f, 200);
            h.Press(LogicalAction.Jump).StepUntil(() => h.Count("boss.pattern") > 2, 60);
            var last = h.Events("boss.pattern").Last();
            Assert.AreEqual("antiair", last.StateAfter, "player airborne near");
            StringAssert.Contains("antiair", (string)last.Get("candidates"));
            StringAssert.DoesNotContain("near", (string)last.Get("candidates"), "near is grounded-only");
        }

        [Test, Category("TC_08_08")]
        public void NoPatternFits_MovesToPreferredBand()
        {
            var h = AiFixtures.H("TC-08-08", World(("b", "sel", 25f)), SelectorBoss());
            h.Step(5);
            Assert.AreEqual("Approach", h.C.Find("b").LastDecision, "distance 15: no candidate");
            Assert.AreEqual(0, h.Count("boss.pattern"));
        }

        [Test, Category("TC_08_08")]
        public void Boss_BeyondTrackRange_Waits()
        {
            var boss = SelectorBoss();
            boss.Ai.TrackRange = 12f;
            var h = AiFixtures.H("TC-08-08", World(("b", "sel", 40f)), boss);
            h.Step(30);
            Assert.AreEqual(40f, h.C.Find("b").Position.X, 1e-4, "distance 30 > track range 12 (EXP-AI-01)");
            Assert.AreEqual(1, h.Count("ai.decide", "BeyondTrackRange"));
        }

        static EnemyDefinition OnePhase() => AiFixtures.Boss("A", "only", AiFixtures.Phase("only", AiFixtures.Pattern("a1", 0f, 2f)));

        static EnemyDefinition TwoPhase()
        {
            var p1 = AiFixtures.Phase("b1", AiFixtures.Pattern("b1a", 0f, 2f));
            p1.Transitions.Add(AiFixtures.Hp(0.5f, "b2"));
            return AiFixtures.Boss("B", "b1", p1, AiFixtures.Phase("b2", AiFixtures.Pattern("b2a", 0f, 2f)));
        }

        static EnemyDefinition Variable()
        {
            var c1 = AiFixtures.Phase("c1", AiFixtures.Pattern("c1a", 0f, 2f));
            c1.Transitions.Add(AiFixtures.Used("c1a", 2, "c2"));
            var c2 = AiFixtures.Phase("c2", AiFixtures.Pattern("c2a", 0f, 2f));
            c2.Transitions.Add(AiFixtures.Ev("roar", "c3"));
            var c3 = AiFixtures.Phase("c3", AiFixtures.Pattern("c3a", 0f, 2f));
            c3.Transitions.Add(AiFixtures.Hp(0.2f, "c1"));
            return AiFixtures.Boss("C", "c1", c1, c2, c3);
        }

        static void Hit(SimHarness h, string id, float d) => h.C.TestApplyHit(AiFixtures.Damage(d), id);

        static void Free(SimHarness h, EnemyActor b) => h.StepUntil(() => b.State == EnemyState.Idle, 200).Step();

        [Test, Category("TC_08_09")]
        public void PhaseCountsPerBoss_NoSharedHardcode()
        {
            var h = AiFixtures.H("TC-08-09", World(("a", "A", 11f), ("b", "B", 30f), ("c", "C", 50f)), OnePhase(), TwoPhase(), Variable());
            h.Step();
            Assert.AreEqual("only", h.C.Find("a").PhaseId);
            Assert.AreEqual("b1", h.C.Find("b").PhaseId);
            Assert.AreEqual("c1", h.C.Find("c").PhaseId);
            Hit(h, "a", 900f);
            Free(h, h.C.Find("a"));
            Assert.AreEqual("only", h.C.Find("a").PhaseId, "one-phase boss never transitions at low HP");
            Hit(h, "b", 600f);
            Free(h, h.C.Find("b"));
            Assert.AreEqual("b2", h.C.Find("b").PhaseId);
            Assert.AreEqual("c1", h.C.Find("c").PhaseId, "other bosses unaffected");
        }

        [Test, Category("TC_08_10")]
        public void Transition_ByPatternUse_ByEvent_ByHp()
        {
            var h = AiFixtures.H("TC-08-10", World(("c", "C", 11f)), Variable());
            var c = h.C.Find("c");
            h.StepUntil(() => c.PhaseId == "c2", 400);
            Assert.AreEqual("c2", c.PhaseId);
            Assert.AreEqual("PatternUsed:c1ax2", h.Events("boss.phase").Last().Get("condition"));
            Assert.AreEqual(1000f, c.Hp, "no HP change needed");
            Free(h, c);
            h.Step(30);
            Assert.AreEqual("c2", c.PhaseId, "no event yet");
            h.C.RaiseBossEvent("c", "roar");
            h.StepUntil(() => c.PhaseId == "c3", 200);
            Assert.AreEqual("Event:roar", h.Events("boss.phase").Last().Get("condition"));
            Free(h, c);
            Hit(h, "c", 850f);
            Free(h, c);
            Assert.AreEqual("c1", c.PhaseId);
            Assert.AreEqual("HpBelow:0.2", h.Events("boss.phase").Last().Get("condition"));
        }

        [Test, Category("TC_08_10")]
        public void TwoThresholdsAtOnce_OnePerCheck_DeathWins()
        {
            var p1 = AiFixtures.Phase("p1", AiFixtures.Pattern("x", 0f, 2f));
            p1.Transitions.Add(AiFixtures.Hp(0.6f, "p2"));
            var p2 = AiFixtures.Phase("p2", AiFixtures.Pattern("y", 0f, 2f));
            p2.Transitions.Add(AiFixtures.Hp(0.3f, "p3"));
            var boss = AiFixtures.Boss("T", "p1", p1, p2, AiFixtures.Phase("p3", AiFixtures.Pattern("z", 0f, 2f)));
            var h = AiFixtures.H("TC-08-10", World(("t", "T", 30f), ("u", "T", 60f)), boss);
            var t = h.C.Find("t");
            Hit(h, "t", 800f);
            h.StepUntil(() => t.PhaseId == "p3", 400);
            var phases = h.Events("boss.phase").Where(e => e.ActorId == "t").Select(e => e.StateAfter).ToList();
            CollectionAssert.AreEqual(new[] { "p1", "p2", "p3" }, phases, "one transition per check (EXP-BOSS-04)");
            Hit(h, "u", 1000f);
            h.Step(30);
            Assert.AreEqual(EnemyState.Dead, h.C.Find("u").State == EnemyState.Gone ? EnemyState.Dead : h.C.Find("u").State);
            Assert.AreEqual(1, h.Events("boss.phase").Count(e => e.ActorId == "u"), "only the start phase: death wins (EXP-BOSS-02)");
        }

        [Test, Category("TC_08_11")]
        public void PhaseSettings_ApplyOnlyToThatPhase()
        {
            var p1 = AiFixtures.Phase("p1", AiFixtures.Pattern("slow", 0f, 2f));
            p1.Transitions.Add(AiFixtures.Ev("go", "p2"));
            var p2 = AiFixtures.Phase("p2", AiFixtures.Pattern("fast", 0f, 2f));
            p2.SuperArmor = true;
            p2.AttackSpeed = 2f;
            p2.StaggerResistance = 0.5f;
            p2.TransitionTime = 0.6f;
            p2.TransitionAnimation = "anim.roar";
            p2.TransitionVfx = "vfx.aura";
            p2.TransitionCamera = "cam.shake";
            var boss = AiFixtures.Boss("S", "p1", p1, p2);
            var other = AiFixtures.Boss("O", "o1", AiFixtures.Phase("o1", AiFixtures.Pattern("o", 0f, 2f)));
            var h = AiFixtures.H("TC-08-11", World(("s", "S", 11f), ("o", "O", 9f)), boss, other);
            var s = h.C.Find("s");
            h.Step();
            Assert.AreEqual(0.3f / 1f, s.Timer, 0.03f, "phase 1 startup 0.3");
            Hit(h, "s", 1f);
            Assert.AreEqual(EnemyState.Stagger, s.State, "phase 1: no armor, attack cancelled by the hit");
            Free(h, s);
            h.C.RaiseBossEvent("s", "go");
            h.StepUntil(() => s.State == EnemyState.PhaseTransition, 100);
            var ph = h.Events("boss.phase").Last(e => e.ActorId == "s");
            Assert.AreEqual("anim.roar", ph.Get("animation"));
            Assert.AreEqual("vfx.aura", ph.Get("vfx"));
            Assert.AreEqual("cam.shake", ph.Get("camera"));
            Assert.AreEqual(0.6f, s.TransitionTimer, 0.03f);
            h.StepUntil(() => s.State == EnemyState.AttackStartup, 100);
            Assert.AreEqual(0.15f, s.Timer, 0.03f, "phase 2 attack speed x2");
            Hit(h, "s", 1f);
            Assert.AreEqual(EnemyState.AttackStartup, s.State, "phase 2 super armor (R-06-11 per phase)");
            Assert.AreEqual(0f, h.C.Find("o").StaggerResistance);
            Assert.IsFalse(h.C.Find("o").PhaseSuperArmor, "other boss unchanged");
            Assert.AreEqual(1f, h.C.Find("o").AttackSpeed);
        }

        [Test, Category("TC_08_12")]
        public void Boss_UsesNormalHitRules_StaggerLaunchImmuneMagicArmorBreak()
        {
            var p1 = AiFixtures.Phase("p1", AiFixtures.Pattern("hit", 0f, 2f, PlayerAirCondition.Any, 0, 10f, true));
            p1.StaggerResistance = 0f;
            var boss = AiFixtures.Boss("N", "p1", p1);
            var h = AiFixtures.H("TC-08-12", World(("n", "N", 30f)), boss);
            var n = h.C.Find("n");
            h.Step();
            Hit(h, "n", 10f);
            Assert.AreEqual(EnemyState.Stagger, n.State, "idle boss: normal stagger");
            Free(h, n);
            h.C.TestApplyHit(new AttackDefinition { Id = "launch", Kind = AttackKind.Launcher, TestDamage = 5f, LaunchVelocity = 14f, Active = 0.1f }, "n");
            Assert.IsTrue(n.Grounded, "launch immune (per boss data)");
            StringAssert.StartsWith("LaunchImmune", h.Events("hit").Last().Reason);
            Free(h, n);
            h.Teleport(29f).StepUntil(() => n.State == EnemyState.AttackStartup, 100);
            Hit(h, "n", 5f);
            Assert.AreEqual(EnemyState.AttackStartup, n.State, "armored pattern keeps going on a physical hit");
            h.C.TestApplyHit(AiFixtures.Damage(5f, DamageType.Magic), "n");
            Assert.AreEqual(EnemyState.Stagger, n.State, "magic breaks armor and cancels (R-06-12) - no boss immunity");
        }

        [Test, Category("TC_08_12")]
        public void Boss_CanBeParried()
        {
            var boss = AiFixtures.Boss("P", "p1", AiFixtures.Phase("p1", AiFixtures.Pattern("hit", 0f, 2f)));
            var h = AiFixtures.H("TC-08-12", World(("p", "P", 11f)), boss);
            var b = h.C.Find("p");
            h.Step();
            Assert.AreEqual(EnemyState.AttackStartup, b.State);
            h.Step(12).Press(LogicalAction.Guard).StepUntil(() => h.Count("guard.parry") > 0 || h.Count("hit.player") > 0, 30);
            Assert.AreEqual(1, h.Count("guard.parry"), "parry applies to the boss like any enemy");
            Assert.AreEqual(EnemyState.Stagger, b.State);
        }
    }
}
