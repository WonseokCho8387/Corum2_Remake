using System;
using Corum2.Core;
using NUnit.Framework;
using static Corum2.Tests.CombatFixtures;

namespace Corum2.Tests
{
    /// <summary>WP-02 Core checks for P0-04~07 (automated evidence A; V/L in play are separate).</summary>
    public class ChainComboTests
    {
        [Test, Category("TC_04_01")]
        public void StandardChain_4_1_4_1_IsTenAttacks_JumpNotCounted_NoAutoChase()
        {
            var h = Harness("TC-04-01", World(("e1", "bag", 11f)), Defs(Bag()));
            for (var i = 0; i < 4; i++) h.Hit();
            h.Hit(LogicalAction.SpecialAttack);
            Assert.IsTrue(h.P.Grounded, "no automatic chase after the launcher (R-04-02)");
            Assert.AreEqual(EnemyState.Launched, h.C.Find("e1").State);
            h.Press(LogicalAction.Jump).Step(3);
            for (var i = 0; i < 4; i++) h.Hit();
            h.Dir(Dir8.Right).Press(LogicalAction.SpecialAttack).Step();
            h.StepUntil(() => h.P.Grounded && h.C.Attack == null);
            Assert.AreEqual(10, h.Count("attack.start"), "ten attack events, the jump is not one");
            Assert.AreEqual(10, h.C.ChainAlive ? h.C.ComboHits : h.C.LastComboHits, "ten valid hits");
            Assert.AreEqual(10, h.C.ChainSteps);
        }

        [Test, Category("TC_04_02")]
        public void AfterLauncher_StayOnGround_AndFightAnotherEnemy()
        {
            var h = Harness("TC-04-02", World(("e1", "bag", 11f), ("e2", "bag", 9f)), Defs(Bag()));
            h.Hit(LogicalAction.SpecialAttack);
            h.Step(5);
            Assert.IsTrue(h.P.Grounded);
            Assert.AreEqual(MotorState.Idle, h.P.State);
            var hp = h.C.Find("e2").Hp;
            h.Dir(Dir8.Left).Hit();
            Assert.Less(h.C.Find("e2").Hp, hp, "ground attack on the other enemy");
        }

        [Test, Category("TC_04_03")]
        public void ChainEnds_Miss_Timeout_GuardCancel()
        {
            var miss = Harness("TC-04-03", World(), Defs(Bag()));
            miss.Hit();
            Assert.AreEqual("Miss", miss.C.LastChainEndReason);

            var timeout = Harness("TC-04-03", World(("e1", "bag", 11f)), Defs(Bag()));
            timeout.Hit().Step(30);
            Assert.AreEqual("Timeout", timeout.C.LastChainEndReason);
            timeout.Hit();
            Assert.AreEqual(2, timeout.Count("combo.begin"), "next attack starts a new chain");

            var cancel = Harness("TC-04-03", World(("e1", "bag", 11f)), Defs(Bag()));
            cancel.Press(LogicalAction.Attack).Step(2).Press(LogicalAction.Guard).Step();
            Assert.AreEqual("GuardCancel", cancel.C.LastChainEndReason);
            Assert.AreEqual(MotorState.Guard, cancel.P.State);
        }

        [Test, Category("TC_04_04")]
        public void OneAttack_CountsEveryTargetOnce([Values(1, 5, 10)] int n)
        {
            var spawns = new (string, string, float)[n];
            for (var i = 0; i < n; i++) spawns[i] = ("e" + i, "bag", 11f);
            var h = Harness("TC-04-04", World(spawns), Defs(Bag()));
            h.Hit();
            Assert.AreEqual(n, h.C.ComboHits);
            Assert.AreEqual(1, h.C.ChainSteps, "one attack step, n hits (R-04-04/05)");
        }

        [Test, Category("TC_04_05")]
        public void LaunchImmune_TakesDamageAndCount_DoesNotRise()
        {
            var boss = Bag("boss");
            boss.Launch = LaunchResponse.Immune;
            var h = Harness("TC-04-05", World(("b", "boss", 11f)), Defs(boss));
            h.Hit(LogicalAction.SpecialAttack);
            var b = h.C.Find("b");
            Assert.IsTrue(b.Grounded);
            Assert.Less(b.Hp, b.Def.MaxHp);
            Assert.AreEqual(1, h.C.ComboHits);
        }

        [Test, Category("TC_04_06")]
        public void Dash_InitialStopsAtFirst_PierceHitsAll([Values(false, true)] bool pierce)
        {
            var h = Harness("TC-04-06", World(("e1", "bag", 11.4f), ("e2", "bag", 12.2f), ("e3", "bag", 13.0f)), Defs(Bag()), cu: Unlocks(pierce: pierce));
            h.Press(LogicalAction.Jump).Step(4).Dir(Dir8.Right).Press(LogicalAction.Attack).Step();
            h.StepUntil(() => h.P.State != MotorState.AirDash, 40).Step(2);
            Assert.AreEqual(pierce ? 3 : 1, h.C.ChainAlive ? h.C.ComboHits : h.C.LastComboHits);
            if (!pierce) Assert.IsNotNull(h.LastWithReason("DashStoppedAtFirstTarget"));
        }

        [Test, Category("TC_04_07")]
        public void AirAttack_Dash_Attack_ExecutesOnLandingAsGroundAttack()
        {
            var h = Harness("TC-04-07", World(("e1", "bag", 14f)), Defs(Bag()));
            h.Press(LogicalAction.Jump).Step(4).Hit();
            h.Dir(Dir8.Right).Press(LogicalAction.Attack).Step(2);
            Assert.AreEqual(MotorState.AirDash, h.P.State);
            h.Dir(Dir8.None).Press(LogicalAction.Attack).Step();
            Assert.IsNotNull(h.LastWithReason("AfterDash->GroundAttackOnLanding"));
            h.StepUntil(() => h.P.Grounded, 120).Step(2);
            Assert.AreEqual(1, h.Count("attack.reservationExecuted"));
            Assert.AreEqual(MotorState.GroundAttack, h.P.State);
            Assert.AreEqual(1, h.Count("attack.start", "Attack:AirNormal"), "no air normal after the dash");
        }

        [Test, Category("TC_04_08")]
        public void Dive_InitialSingleTarget_AreaHitsCluster([Values(false, true)] bool area)
        {
            var h = Harness("TC-04-08", World(("e1", "bag", 12.0f), ("e2", "bag", 12.6f), ("e3", "bag", 13.2f)), Defs(Bag()), cu: Unlocks(area: area));
            h.Press(LogicalAction.Jump).Step(12).Dir(Dir8.Right).Press(LogicalAction.SpecialAttack).Step();
            h.StepUntil(() => h.P.Grounded && h.C.Attack == null, 120);
            var hits = h.Count("hit");
            if (area) Assert.AreEqual(3, hits);
            else Assert.AreEqual(1, hits);
        }

        [Test, Category("TC_04_09")]
        public void ComboWindow_IsData_BoundaryRespected([Values(0.2f, 0.6f)] float window)
        {
            var h = Harness("TC-04-09", World(("e1", "bag", 11f)), Defs(Bag()), Profile(window));
            h.Hit().Step(20); // 0.4 s after the motion
            h.Hit();
            Assert.AreEqual(window < 0.4f ? 2 : 1, h.Count("combo.begin"));
        }

        [Test, Category("TC_04_10"), Category("TC_06_18")]
        public void DeadTarget_EndsOnlyItsLink()
        {
            var h = Harness("TC-04-10", World(("a", "weak", 11f), ("b", "bag", 11f), ("c", "bag", 11f)), Defs(Bag(), Bag("weak", 15f)));
            h.Hit().Hit();
            Assert.AreEqual(EnemyState.Dead, h.C.Find("a").State);
            Assert.IsTrue(h.C.ChainAlive, "whole combo not reset by one death");
            var before = h.C.ComboHits;
            h.Hit();
            Assert.AreEqual(before + 2, h.C.ComboHits, "only B and C count after A died");
            Assert.AreEqual(1, h.Count("combo.linkEnd"));
        }
    }

    public class StateCancelTests
    {
        [Test, Category("TC_05_01")]
        public void MoveAndJump_LockedUntilMotionEnds()
        {
            var h = Harness("TC-05-01", World(), Defs(Bag()));
            var x0 = h.P.Position.X;
            h.Press(LogicalAction.Attack).Step().Dir(Dir8.Right).Press(LogicalAction.Jump).Step(3);
            Assert.AreEqual(x0, h.P.Position.X, 1e-4f);
            Assert.IsNotNull(h.LastWithReason("ActionLock:GroundAttack"));
            h.StepUntil(() => h.C.Attack == null).Step(2);
            Assert.Greater(h.P.Position.X, x0, "free after the motion");
        }

        [Test, Category("TC_05_02")]
        public void RbCancels_GroundNormal_Launcher_AirNormal([Values(0, 1, 2)] int which)
        {
            var h = Harness("TC-05-02", World(("e1", "bag", 30f)), Defs(Bag()));
            if (which == 2) h.Press(LogicalAction.Jump).Step(4);
            h.Press(which == 1 ? LogicalAction.SpecialAttack : LogicalAction.Attack).Step(2);
            h.Press(LogicalAction.Guard).Step();
            Assert.IsNull(h.C.Attack, "hitbox removed");
            Assert.AreEqual(MotorState.Guard, h.P.State);
            Assert.AreEqual("GuardCancel", h.C.LastChainEndReason);
        }

        [Test, Category("TC_05_03")]
        public void Rb_RefusedDuringDashAndDive([Values(LogicalAction.Attack, LogicalAction.SpecialAttack)] LogicalAction a)
        {
            var h = Harness("TC-05-03", World(), Defs(Bag()));
            h.Press(LogicalAction.Jump).Step(8).Dir(Dir8.Right).Press(a).Step().Press(LogicalAction.Guard).Step();
            Assert.AreNotEqual(MotorState.Guard, h.P.State);
            Assert.IsNotNull(h.LastWithReason("ActionLock:" + (a == LogicalAction.Attack ? "AirDash" : "Dive")));
        }

        [Test, Category("TC_05_04")]
        public void HitStun_LocksAllCombatActions_ThenReleases()
        {
            var h = Harness("TC-05-04", World(("e1", "hitter", 11f)), Defs(Hitter()));
            h.StepUntil(() => h.P.State == MotorState.HitStun);
            var x = h.P.Position.X;
            h.Press(LogicalAction.Jump).Press(LogicalAction.Attack).Press(LogicalAction.Guard).Step();
            Assert.AreEqual(MotorState.HitStun, h.P.State);
            Assert.AreEqual(3, h.Count("input.reject", "ActionLock:HitStun"));
            h.Release(LogicalAction.Guard).StepUntil(() => h.P.State != MotorState.HitStun);
            Assert.AreEqual(MotorState.Idle, h.P.State);
        }
    }

    public class HitReactionTests
    {
        [Test, Category("TC_06_01")]
        public void OneMotion_OneHitPerTarget_NextMotionHitsAgain()
        {
            var h = Harness("TC-06-01", World(("e1", "bag", 11f)), Defs(Bag()));
            h.Hit();
            Assert.AreEqual(1, h.Count("hit"), "active window spans several ticks, still one hit");
            h.Hit();
            Assert.AreEqual(2, h.Count("hit"));
        }

        [Test, Category("TC_06_02")]
        public void MultiHitEvents_EachEventOncePerTarget()
        {
            var p = Profile();
            p.GroundNormals[0].HitEvents = 3;
            p.GroundNormals[0].Active = 0.12f;
            var h = Harness("TC-06-02", World(("e1", "bag", 11f), ("e2", "bag", 11f), ("e3", "bag", 11f)), Defs(Bag()), p);
            h.Hit();
            Assert.AreEqual(9, h.Count("hit"), "3 events x 3 targets");
        }

        [Test, Category("TC_06_03")]
        public void Hitstop_DoesNotStackWithTargets_AndFollowsData([Values(1, 10)] int n, [Values(0.04f, 0.1f)] float stop)
        {
            var p = Profile();
            p.GroundNormals[0].Hitstop = stop;
            var spawns = new (string, string, float)[n];
            for (var i = 0; i < n; i++) spawns[i] = ("e" + i, "bag", 11f);
            var h = Harness("TC-06-03", World(spawns), Defs(Bag()), p);
            h.Press(LogicalAction.Attack).StepUntil(() => h.Count("hit") > 0);
            Assert.AreEqual(stop, h.C.PlayerHitstop, 1e-4f);
        }

        [Test, Category("TC_06_04")]
        public void Stagger_RefreshedEachHit_ResistantDoesNot()
        {
            var tough = Bag("tough");
            tough.StaggerResistance = 1f;
            var h = Harness("TC-06-04", World(("n", "bag", 11f), ("t", "tough", 11f)), Defs(Bag(), tough));
            h.Hit();
            Assert.AreEqual(EnemyState.Stagger, h.C.Find("n").State);
            h.Hit();
            Assert.AreEqual(EnemyState.Stagger, h.C.Find("n").State, "refreshed");
            Assert.AreEqual(EnemyState.Idle, h.C.Find("t").State);
        }

        [Test, Category("TC_06_05")]
        public void Knockback_SmallForLight_ZeroWhenScaleZero()
        {
            var heavy = Bag("heavy");
            heavy.PhysicalKnockbackScale = 0f;
            var h = Harness("TC-06-05", World(("l", "bag", 11f), ("h", "heavy", 11f)), Defs(Bag(), heavy));
            for (var i = 0; i < 4; i++) h.Hit();
            h.Step(10);
            Assert.AreEqual(11f, h.C.Find("h").Position.X, 1e-3f);
            var dx = h.C.Find("l").Position.X - 11f;
            Assert.Greater(dx, 0f);
            Assert.Less(dx, 1f, "short knockback, no large 4th-hit push");
        }

        [Test, Category("TC_06_06")]
        public void Magic_AlwaysKnocksBack_EvenPhysicalImmune_AndZeroIsInvalid()
        {
            var heavy = Bag("heavy");
            heavy.PhysicalKnockbackScale = 0f;
            var h = Harness("TC-06-06", World(("h", "heavy", 11f)), Defs(heavy));
            var magic = new AttackDefinition { Id = "testMagic", Kind = AttackKind.TestMagic, DamageType = DamageType.Magic, TestDamage = 5, Knockback = 3f };
            h.C.TestApplyHit(magic, "h");
            h.Step(5);
            Assert.Greater(h.C.Find("h").Position.X, 11.01f);

            var bad = Profile();
            bad.Launcher.DamageType = DamageType.Magic;
            bad.Launcher.Knockback = 0f;
            var badEnemy = Bag("bad");
            badEnemy.MagicKnockbackScale = 0f;
            var issues = DataValidator.ValidateCombat(bad, Unlocks(), Defs(badEnemy), World());
            Assert.IsTrue(issues.Exists(i => i.Message.Contains("Magic attack knockback")));
            Assert.IsTrue(issues.Exists(i => i.Message.Contains("MagicKnockbackScale")));
        }

        [Test, Category("TC_06_07")]
        public void AirHits_SlowTheFall_NormalAfterStopping()
        {
            var h = Harness("TC-06-07", World(("e1", "bag", 11f)), Defs(Bag()));
            h.Hit(LogicalAction.SpecialAttack);
            var e = h.C.Find("e1");
            h.StepUntil(() => e.VelY < -3f, 60);
            e.JuggleTimer = 0.35f; // as after an air hit
            h.Step();
            Assert.GreaterOrEqual(e.VelY, -e.Def.JuggleFallSpeed - 1e-3f, "slowed but still falling");
            h.Step(20);
            Assert.Less(e.VelY, -e.Def.JuggleFallSpeed, "normal fall once hits stop");
        }

        [Test, Category("TC_06_08")]
        public void LaunchResponses_Differ([Values(LaunchResponse.Launchable, LaunchResponse.Resist, LaunchResponse.Immune)] LaunchResponse r)
        {
            var d = Bag("x");
            d.Launch = r;
            var h = Harness("TC-06-08", World(("e", "x", 11f)), Defs(d));
            h.Press(LogicalAction.SpecialAttack).StepUntil(() => h.Count("hit") > 0);
            var vy = h.C.Find("e").VelY;
            if (r == LaunchResponse.Launchable) Assert.AreEqual(14f, vy, 1e-3f);
            if (r == LaunchResponse.Resist) Assert.AreEqual(7f, vy, 1e-3f);
            if (r == LaunchResponse.Immune) Assert.AreEqual(0f, vy, 1e-3f);
            Assert.AreEqual(1, h.C.ComboHits, "damage and count in every case");
        }

        static SimHarness Downed(string tc, float x = 11f)
        {
            var h = Harness(tc, World(("e", "bag", x)), Defs(Bag()));
            h.Hit(LogicalAction.SpecialAttack);
            h.StepUntil(() => h.C.Find("e").State == EnemyState.Down, 120);
            return h;
        }

        [Test, Category("TC_06_09")]
        public void DownAttack_OnDownedEnemy_BothSides([Values(1, -1)] int side)
        {
            var h = Downed("TC-06-09");
            var e = h.C.Find("e");
            h.Teleport(e.Position.X - side * 1.2f, side).Step();
            h.Dir(side > 0 ? Dir8.DownRight : Dir8.DownLeft).Step().Press(LogicalAction.SpecialAttack).Step();
            Assert.AreEqual(1, h.Count("attack.start", "Attack:DownAttack"), "down attack, not a launcher");
            h.StepUntil(() => h.C.Attack == null);
            Assert.AreEqual(1, h.Count("hit", "DownHit"));
        }

        [Test, Category("TC_06_10")]
        public void Down_OnlyDesignatedHits_WakeupInvincible_ThenHittable()
        {
            var h = Downed("TC-06-10", 11f);
            var e = h.C.Find("e");
            h.Teleport(e.Position.X - 1f, 1).Step();
            h.Hit();
            Assert.IsNotNull(h.LastWithReason("DownedNotHittableByThisAttack"));
            h.StepUntil(() => e.State == EnemyState.Wakeup, 120);
            h.Hit();
            Assert.IsNotNull(h.LastWithReason("TargetInvincible:Wakeup"));
            h.StepUntil(() => e.State == EnemyState.Idle, 120);
            var hp = e.Hp;
            h.Hit();
            Assert.Less(e.Hp, hp, "hittable the moment wakeup ends");
        }

        [Test, Category("TC_06_11"), Category("TC_06_12")]
        public void SuperArmor_PhysicalDamagesOnly_MagicBreaksThisAction()
        {
            var h = Harness("TC-06-11", World(("e", "armor", 11.2f)), Defs(Hitter("armor", armor: true)));
            var e = h.C.Find("e");
            h.StepUntil(() => e.State == EnemyState.AttackStartup);
            h.C.TestApplyHit(Profile().GroundNormals[0], "e");
            Assert.AreEqual(EnemyState.AttackStartup, e.State, "attack continues");
            Assert.IsNotNull(h.LastWithReason("SuperArmor:DamageOnly"));
            var magic = new AttackDefinition { Id = "testMagic", Kind = AttackKind.TestMagic, DamageType = DamageType.Magic, TestDamage = 5, Knockback = 3f };
            h.C.TestApplyHit(magic, "e");
            Assert.AreEqual(EnemyState.Stagger, e.State);
            Assert.IsNotNull(h.LastWithReason("MagicBreaksArmor"));
            h.StepUntil(() => e.State == EnemyState.AttackStartup, 200);
            h.C.TestApplyHit(Profile().GroundNormals[0], "e");
            Assert.AreEqual(EnemyState.AttackStartup, e.State, "next armored attack has armor again");
        }

        [Test, Category("TC_06_13")]
        public void RollInvulnerability_OnlyInsideWindow_NoPostHitInvulnerability()
        {
            var h = Harness("TC-06-13", World(("e", "hitter", 11f)), Defs(Hitter()));
            var e = h.C.Find("e");
            h.StepUntil(() => e.State == EnemyState.AttackStartup);
            h.Step(11); // 4 ticks before active; roll covers it with the window
            h.Dir(Dir8.DownLeft).Step().Press(LogicalAction.Jump).Step(4);
            Assert.AreEqual(1, h.Count("hit.skip", "PlayerInvincible"));
            Assert.AreEqual(0, h.Count("hit.player"));

            var g = Harness("TC-06-13", World(("e", "hitter", 11f)), Defs(Hitter()));
            g.StepUntil(() => g.P.State == MotorState.HitStun).StepUntil(() => g.P.State != MotorState.HitStun);
            g.StepUntil(() => g.Count("hit.player") == 2, 200);
            Assert.AreEqual(2, g.Count("hit.player"), "no automatic invulnerability after a hit");
        }

        [Test, Category("TC_06_14")]
        public void ContinuousHits_ObservedLockOnly()
        {
            var h = Harness("TC-06-14", World(("a", "hitter", 11f), ("b", "hitter", 9f)), Defs(Hitter()));
            h.Step(250);
            Assert.Greater(h.Count("hit.player"), 1);
            Assert.AreEqual(0, h.Count("protection"), "no protection policy added (R-06-14)");
        }

        [Test, Category("TC_06_15"), Category("TC_06_16")]
        public void SimultaneousHit_IsDodge_ForBoth([Values(false, true)] bool armor)
        {
            var h = Harness("TC-06-15", World(("e", "hitter", 11f)), Defs(Hitter("hitter", armor: armor)));
            var e = h.C.Find("e");
            h.StepUntil(() => e.State == EnemyState.AttackStartup);
            h.Step(12); // enemy active 15 ticks after startup; player active 2 ticks after the press tick
            h.Press(LogicalAction.Attack).StepUntil(() => h.Count("dodge") > 0 || h.Count("hit") > 0 || h.Count("hit.player") > 0, 30);
            Assert.AreEqual(1, h.Count("dodge"));
            Assert.AreEqual(e.Def.MaxHp, e.Hp);
            Assert.AreEqual(h.C.Profile.PlayerMaxHp, h.C.PlayerHp);
            Assert.AreEqual(MotorState.DodgeReact, h.P.State);
            Assert.AreEqual(EnemyState.DodgeReact, e.State);
            Assert.AreEqual("Dodge", h.C.LastChainEndReason);
            h.Press(LogicalAction.Attack).Step();
            Assert.IsNotNull(h.LastWithReason("ActionLock:DodgeReact"), "no action before the reaction ends");
        }

        [Test, Category("TC_06_17")]
        public void Death_ExcludesTarget_ThenVanishAndRemoval()
        {
            var h = Harness("TC-06-17", World(("a", "weak", 11f)), Defs(Bag("weak", 15f)));
            h.Hit().Hit();
            var a = h.C.Find("a");
            Assert.AreEqual(EnemyState.Dead, a.State);
            var hits = h.Count("hit");
            h.Hit();
            Assert.AreEqual(hits, h.Count("hit"), "no hits after death");
            h.StepUntil(() => a.State == EnemyState.Gone, 200);
            Assert.AreEqual(1, h.Count("enemy.vanish"));
            Assert.AreEqual(1, h.Count("enemy.removed"));
        }
    }

    public class GuardParryTests
    {
        static SimHarness HitterAt(string tc, float x, EnemyDefinition d = null) =>
            Harness(tc, World(("e", (d ?? Hitter()).Id, x)), Defs(d ?? Hitter()));

        [Test, Category("TC_07_01")]
        public void Guard_FrontOnly_TurnToFace()
        {
            var back = HitterAt("TC-07-01", 9f); // enemy behind (player faces right)
            back.Press(LogicalAction.Guard).StepUntil(() => back.Count("hit.player") + back.Count("guard.block") > 0);
            Assert.AreEqual(1, back.Count("hit.player", "HitFromBehind"));

            var turn = HitterAt("TC-07-01", 9f);
            turn.Press(LogicalAction.Guard).Step().Dir(Dir8.Left).Step(2);
            Assert.AreEqual(-1, turn.P.Facing);
            turn.StepUntil(() => turn.Count("hit.player") + turn.Count("guard.block") + turn.Count("guard.parry") > 0);
            Assert.AreEqual(0, turn.Count("hit.player"));
        }

        [Test, Category("TC_07_02"), Category("TC_OPS_02")]
        public void GuardDamage_9_10_50()
        {
            Assert.AreEqual(0f, GuardRule.Apply(9));
            Assert.AreEqual(1f, GuardRule.Apply(10));
            Assert.AreEqual(5f, GuardRule.Apply(50));
            var h = HitterAt("TC-07-02", 11f, Hitter("hitter", 50f));
            h.Press(LogicalAction.Guard).StepUntil(() => h.Count("guard.block") > 0);
            Assert.AreEqual(h.C.Profile.PlayerMaxHp - 5f, h.C.PlayerHp, 1e-4f);
            var e = h.LastWithReason("FrontGuard");
            StringAssert.Contains("TEST-DMG-01", e.Get("damagePolicy").ToString());
        }

        [Test, Category("TC_07_03"), Category("TC_03_06")]
        public void ContinuousGuard_AndAirGuard()
        {
            var h = HitterAt("TC-07-03", 11f);
            h.Press(LogicalAction.Guard).StepUntil(() => h.Count("guard.block") >= 2, 200);
            Assert.AreEqual(2, h.Count("guard.block"));

            var air = HitterAt("TC-07-03", 11f);
            air.StepUntil(() => air.C.Find("e").State == EnemyState.AttackStartup);
            air.Step(10).Press(LogicalAction.Jump).Step(2).Press(LogicalAction.Guard).Step();
            Assert.AreEqual(MotorState.Guard, air.P.State);
            Assert.IsFalse(air.P.Grounded, "guard in the air");
        }

        [Test, Category("TC_07_04")]
        public void ParryWindow_Boundary([Values(0.10f, 0.15f, 0.20f)] float w, [Values(-1, 1)] int side)
        {
            var p = Profile();
            p.ParryWindow = w;
            var h = Harness("TC-07-04", World(("e", "hitter", 11f)), Defs(Hitter()), p);
            var e = h.C.Find("e");
            h.StepUntil(() => e.State == EnemyState.AttackStartup);
            // enemy becomes active 15 ticks later; press RB w seconds before (inside) or w+0.04 s before (outside)
            var before = (int)Math.Floor((side > 0 ? w : w + 0.04f) / SimHarness.Dt + 1e-4); // tick resolution: 0.15 s -> 7 ticks (0.14 s)
            h.Step(15 - before - 1).Press(LogicalAction.Guard).StepUntil(() => h.Count("guard.parry") + h.Count("guard.block") > 0, 40);
            Assert.AreEqual(side > 0 ? 1 : 0, h.Count("guard.parry"));
        }

        [Test, Category("TC_07_05")]
        public void Parry_StopsEnemy_PlayerAttacksAtOnce()
        {
            var h = HitterAt("TC-07-05", 11f);
            var e = h.C.Find("e");
            h.StepUntil(() => e.State == EnemyState.AttackStartup).Step(12).Press(LogicalAction.Guard).StepUntil(() => h.Count("guard.parry") > 0, 20);
            Assert.AreEqual(EnemyState.Stagger, e.State);
            h.Press(LogicalAction.Attack).Step();
            Assert.AreEqual(MotorState.GroundAttack, h.P.State, "attack right after the parry");
        }

        [Test, Category("TC_07_06")]
        public void Unblockable_And_GuardBreak()
        {
            var u = HitterAt("TC-07-06", 11f, Hitter("u", 20f, unblockable: true));
            u.Press(LogicalAction.Guard).StepUntil(() => u.Count("hit.player") > 0);
            Assert.AreEqual(u.C.Profile.PlayerMaxHp - 20f, u.C.PlayerHp, 1e-4f);

            var b = HitterAt("TC-07-06", 11f, Hitter("b", 20f, guardBreak: true));
            b.StepUntil(() => b.C.Find("e").State == EnemyState.AttackStartup).Press(LogicalAction.Guard).StepUntil(() => b.Count("guard.block") > 0);
            Assert.AreNotEqual(MotorState.Guard, b.P.State, "guard released");
            Assert.IsNotNull(b.LastWithReason("GuardBreak"));
        }

        [Test, Category("TC_03_09")]
        public void AfterLanding_AttackAndGuardImmediately()
        {
            var h = Harness("TC-03-09", World(("e1", "bag", 11f)), Defs(Bag()));
            h.Press(LogicalAction.Jump).Step().StepUntil(() => h.P.Grounded && h.P.State == MotorState.Idle);
            h.Press(LogicalAction.Attack).Step();
            Assert.AreEqual(MotorState.GroundAttack, h.P.State);
            h.StepUntil(() => h.C.Attack == null).Press(LogicalAction.Jump).Step().StepUntil(() => h.P.Grounded && h.P.State == MotorState.Idle).Press(LogicalAction.Guard).Step();
            Assert.AreEqual(MotorState.Guard, h.P.State);
        }

        [Test, Category("TC_UNITY_03")]
        public void CombatFixture_IsValid()
        {
            var issues = DataValidator.ValidateCombat(Profile(), Unlocks(), Defs(Bag(), Hitter()), World(("e", "bag", 11f)));
            Assert.IsEmpty(issues, string.Join("\n", issues));
        }
    }
}
