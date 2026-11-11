using System.Collections.Generic;
using System.Linq;
using Corum2.Core;
using NUnit.Framework;
using static Corum2.Tests.CombatFixtures;

namespace Corum2.Tests
{
    /// <summary>WP-05 test data: magic, temporary commands (TEST-MAGIC-01) and a revive item.</summary>
    public static class Wp05Fixtures
    {
        public static RingProfile Profile(int revives = 1, bool commandTakesButton = true)
        {
            var p = RingFixtures.Profile(false);
            p.Magics.Clear();
            p.Magics.Add(new MagicDefinition { Id = "fire", Effect = MagicEffect.Attack, Power = 30f, CastTime = 0.2f, Recovery = 0.2f, Knockback = 4f });
            p.Magics.Add(new MagicDefinition { Id = "cure", Effect = MagicEffect.Heal, Power = 25f, CastTime = 0.2f });
            p.Magics.Add(new MagicDefinition { Id = "might", Effect = MagicEffect.Buff, Power = 1.5f, CastTime = 0.1f, BuffDuration = 3f });
            p.Commands.Add(Cmd("fireball", "fire", LogicalAction.Attack, Dir8.Down, Dir8.DownRight, Dir8.Right));
            p.Commands.Add(Cmd("cure", "cure", LogicalAction.Guard, Dir8.Down, Dir8.DownLeft, Dir8.Left));
            p.Commands.Add(Cmd("might", "might", LogicalAction.Jump, Dir8.Down, Dir8.DownLeft, Dir8.Left));
            p.CommandTakesButton = commandTakesButton;
            p.Items.Add(new ItemDefinition { Id = "feather", Name = "Phoenix Feather", Kind = ItemKind.Consumable, Effect = ItemEffectType.Revive, Cooldown = 0f });
            if (revives > 0) p.StartInventory.Add(new ItemStack { ItemId = "feather", Count = revives });
            p.StartQuickslots = new[] { "", "", "scroll", "" }; // D-pad down = fire scroll
            if (revives > 0) p.StartQuickslots[3] = "feather";
            return p;
        }

        public static MagicCommand Cmd(string id, string magic, LogicalAction button, params Dir8[] dirs)
        {
            var c = new MagicCommand { Id = id, MagicId = magic, Button = button, Window = 0.6f };
            c.Directions.AddRange(dirs);
            return c;
        }

        public static SimHarness H(string tc, RingProfile ring, params (string id, string def, float x)[] enemies)
        {
            var defs = Defs(Bag("bag", 1000f), Hitter("hitter", 10f));
            return RingFixtures.Ring(tc, World(enemies), defs, ring);
        }

        /// <summary>Holds each direction for two ticks, then presses the button (directions stay held as the last one).</summary>
        public static SimHarness Command(this SimHarness h, LogicalAction button, params Dir8[] dirs)
        {
            foreach (var d in dirs) h.Dir(d).Step(2);
            return h.Press(button).Step().Release(button).Dir(Dir8.None).Step();
        }
    }

    public class MagicCommandTests
    {
        static SimHarness Bag(RingProfile p = null) => Wp05Fixtures.H("TC-02-02", p ?? Wp05Fixtures.Profile(), ("b", "bag", 11.4f));

        [Test, Category("TC_02_02")]
        public void AttackCommand_CastsMagic_InsteadOfTheNormalAttack()
        {
            var h = Bag();
            h.Command(LogicalAction.Attack, Dir8.Down, Dir8.DownRight, Dir8.Right).Step(30);
            var m = h.Events("magic.command").Single();
            Assert.AreEqual("fireball", m.Get("commandId"));
            StringAssert.Contains("Attack+Right", (string)m.Get("competingNormalAction"), "the competing normal action is logged (CF-01)");
            Assert.AreEqual(1, h.Count("magic.cast"));
            Assert.AreEqual(0, h.Count("attack.start"), "the button press was taken by the command (EXP-CMD-02)");
            var hit = h.Events("hit").Single();
            Assert.AreEqual("Magic", hit.Get("damageType").ToString());
            Assert.AreEqual("fire", hit.Get("magicId"));
            StringAssert.StartsWith("Magic", hit.Reason, "magic hit rules (R-06-06)");
        }

        [Test, Category("TC_02_02")]
        public void IncompleteOrLateCommand_RunsTheNormalAttack()
        {
            var h = Bag();
            h.Command(LogicalAction.Attack, Dir8.Down, Dir8.None, Dir8.Right).Step(30); // neutral in between: no diagonal recorded
            Assert.AreEqual(0, h.Count("magic.cast"), "two directions: no command");
            Assert.AreEqual(1, h.Count("magic.commandMiss"));
            Assert.AreEqual(1, h.Count("attack.start"));

            var late = Bag();
            foreach (var d in new[] { Dir8.Down, Dir8.DownRight, Dir8.Right }) late.Dir(d).Step(2);
            late.Step(40); // 0.8 s > window 0.6 s
            late.Press(LogicalAction.Attack).Step().Release(LogicalAction.Attack).Step(30);
            Assert.AreEqual(0, late.Count("magic.cast"), "outside the window");
            Assert.AreEqual(1, late.Count("attack.start"));
        }

        [Test, Category("TC_02_02")]
        public void HealCommand_OnGuardButton_And_FailedCommandGuards()
        {
            var h = Bag();
            h.C.PlayerHp = 50f;
            h.Command(LogicalAction.Guard, Dir8.Down, Dir8.DownLeft, Dir8.Left).Step(30);
            Assert.AreEqual(75f, h.C.PlayerHp, 1e-3, "cure +25");
            Assert.AreEqual(0, h.Events("motor.state").Count(e => e.StateAfter == "Guard"), "the guard press was taken by the command");
            var f = Bag();
            f.Dir(Dir8.Left).Step(2).Press(LogicalAction.Guard).Step();
            Assert.AreEqual(MotorState.Guard, f.P.State, "no command: RB guards as usual");
        }

        [Test, Category("TC_02_02")]
        public void BuffCommand_OnJump_MultipliesPhysicalDamage()
        {
            var h = Bag();
            h.Command(LogicalAction.Jump, Dir8.Down, Dir8.DownLeft, Dir8.Left).Step(20);
            Assert.AreEqual(0, h.Events("motor.state").Count(e => e.StateAfter == "Airborne" && e.Reason.Contains("Jump")), "no jump");
            Assert.AreEqual(1.5f, h.C.BuffMultiplier, 1e-4);
            h.Teleport(10f, 1).Hit();
            var hit = h.Events("hit").Last();
            Assert.AreEqual(1.5f, System.Convert.ToSingle(hit.Get("buffMultiplier")), 1e-4);
            Assert.AreEqual(15f, System.Convert.ToSingle(hit.Get("damageD")), 1e-3, "10 x 1.5");
            h.Seconds(3.5);
            Assert.AreEqual(1, h.Count("magic.buffEnd"));
            Assert.AreEqual(1f, h.C.BuffMultiplier);
        }

        [Test, Category("TC_02_02")]
        public void CommandTakesButtonFalse_NormalActionRuns_MatchLogged()
        {
            var h = Bag(Wp05Fixtures.Profile(1, false));
            h.Command(LogicalAction.Attack, Dir8.Down, Dir8.DownRight, Dir8.Right).Step(30);
            Assert.AreEqual(1, h.Count("magic.command"));
            Assert.AreEqual(1, h.Count("magic.commandLoggedOnly"));
            Assert.AreEqual(0, h.Count("magic.cast"));
            Assert.AreEqual(1, h.Count("attack.start"));
        }

        [Test, Category("TC_02_02")]
        public void CastRefusedInHitStun_FallsBackToTheNormalAction()
        {
            var h = Wp05Fixtures.H("TC-02-02", Wp05Fixtures.Profile(), ("e", "hitter", 11f));
            h.C.PlayerHp = 1000f;
            h.StepUntil(() => h.P.State == MotorState.HitStun, 100);
            h.Command(LogicalAction.Attack, Dir8.Down, Dir8.DownRight, Dir8.Right); // 6 ticks of directions, then X, still in hit stun
            Assert.AreEqual(1, h.Count("magic.command"));
            Assert.AreEqual(1, h.Count("magic.commandFallback"), "refused cast -> normal action (EXP-CMD-03)");
            StringAssert.StartsWith("ActionLock", (string)h.Events("magic.commandFallback").Single().Get("reject"));
            Assert.Greater(h.Events("input.reject").Count(e => e.Reason.StartsWith("ActionLock:HitStun")), 0, "the normal attack is then refused by the hit reaction");
            Assert.AreEqual(0, h.Count("magic.cast"));
        }

        [Test, Category("TC_02_02")]
        public void GuardCancelsTheCast_NoEffect()
        {
            var h = Bag();
            h.C.PlayerHp = 50f;
            h.Command(LogicalAction.Guard, Dir8.Down, Dir8.DownLeft, Dir8.Left);
            Assert.AreEqual(1, h.Count("magic.cast"));
            h.Press(LogicalAction.Guard).Step(20);
            var c = h.Events("magic.cancel").Single();
            Assert.AreEqual(false, c.Get("effectApplied"));
            Assert.AreEqual(50f, h.C.PlayerHp, 1e-3, "cancelled before the cure");
        }

        [Test, Category("TC_02_02")]
        public void LongestCommandWins_ThenListOrder()
        {
            var p = Wp05Fixtures.Profile();
            p.Commands.Add(Wp05Fixtures.Cmd("bigfire", "fire", LogicalAction.Attack, Dir8.Left, Dir8.Down, Dir8.DownRight, Dir8.Right));
            var h = Bag(p);
            h.Command(LogicalAction.Attack, Dir8.Left, Dir8.Down, Dir8.DownRight, Dir8.Right).Step(5);
            Assert.AreEqual("bigfire", h.Events("magic.command").Single().Get("commandId"), "EXP-CMD-04");
        }
    }

    public class MagicScrollEquivalenceTests
    {
        [Test, Category("TC_02_10")]
        public void CommandAndScroll_SameMagic_SamePowerAndCastTime()
        {
            var h = Wp05Fixtures.H("TC-02-10", Wp05Fixtures.Profile(), ("b", "bag", 11.4f));
            h.Command(LogicalAction.Attack, Dir8.Down, Dir8.DownRight, Dir8.Right).Step(40);
            h.Teleport(10f, 1).Step(2);
            h.Ui(LogicalAction.QuickDown).Step(40); // D-pad down = fire scroll
            var casts = h.Events("magic.cast").ToList();
            Assert.AreEqual(2, casts.Count);
            Assert.AreEqual("command:fireball", casts[0].Get("source"));
            StringAssert.StartsWith("scroll:", (string)casts[1].Get("source"));
            Assert.AreEqual(casts[0].Get("power"), casts[1].Get("power"), "same power (R-02-10)");
            Assert.AreEqual(casts[0].Get("castTime"), casts[1].Get("castTime"), "same cast time");
            var effects = h.Events("magic.effect").ToList();
            Assert.AreEqual(2, effects.Count);
            // the cast tick is the first 0.02 s step of the motion (same as attacks), so the effect lands 9 ticks later
            Assert.AreEqual(effects[0].SimulationTick - casts[0].SimulationTick, effects[1].SimulationTick - casts[1].SimulationTick, "same cast delay");
            Assert.AreEqual(9, effects[0].SimulationTick - casts[0].SimulationTick);
            var hits = h.Events("hit").Where(e => (string)e.Get("magicId") == "fire").ToList();
            Assert.AreEqual(2, hits.Count);
            Assert.AreEqual(System.Convert.ToSingle(hits[0].Get("damageD")), System.Convert.ToSingle(hits[1].Get("damageD")));
            Assert.AreEqual(0, h.Inv().Count("scroll"), "scroll consumed");
        }
    }

    public class DeathFlowTests
    {
        static SimHarness Dying(int revives)
        {
            var h = Wp05Fixtures.H("TC-06-19a", Wp05Fixtures.Profile(revives), ("e", "hitter", 11f));
            h.C.PlayerHp = 1f;
            h.StepUntil(() => h.P.State == MotorState.Dead, 200);
            return h;
        }

        [Test, Category("TC_06_19a")]
        public void ReviveOwned_MotionThenPrompt_UseRevives()
        {
            var h = Dying(1);
            Assert.AreEqual(DeathStage.Motion, h.Sim.Death.Stage);
            h.Tap(LogicalAction.Jump).Step(5);
            Assert.AreEqual(DeathStage.Motion, h.Sim.Death.Stage, "input ignored during the death motion");
            h.Seconds(1.3);
            Assert.AreEqual(DeathStage.RevivePrompt, h.Sim.Death.Stage);
            h.Tap(LogicalAction.Jump).Step();
            Assert.AreEqual(DeathStage.None, h.Sim.Death.Stage);
            Assert.AreNotEqual(MotorState.Dead, h.P.State);
            Assert.AreEqual(50f, h.C.PlayerHp, 1e-3, "EXP-DEATH-03: half of max");
            Assert.AreEqual(0, h.Inv().Count("feather"));
            Assert.IsTrue(h.C.PlayerInvincible, "invulnerable right after the revive");
            Assert.AreEqual(1, h.Count("player.revive"));
        }

        [Test, Category("TC_06_19a")]
        public void ReviveDeclined_GameOver_MenuSelectionLoggedNotPerformed()
        {
            var h = Dying(1);
            h.Seconds(1.3);
            h.Tap(LogicalAction.MoveDown).Step();
            Assert.AreEqual(1, h.Sim.Death.Cursor, "cursor on Decline");
            h.Tap(LogicalAction.Jump).Step();
            Assert.AreEqual(DeathStage.GameOver, h.Sim.Death.Stage);
            Assert.AreEqual(1, h.Inv().Count("feather"), "declining keeps the item");
            h.Tap(LogicalAction.MoveDown).Step().Tap(LogicalAction.MoveDown).Step().Tap(LogicalAction.MoveDown).Step();
            Assert.AreEqual(2, h.Sim.Death.Cursor, "no wrap past Lobby");
            h.Tap(LogicalAction.Jump).Step();
            var sel = h.Events("death.menuSelect").Single();
            Assert.AreEqual("Lobby", sel.Get("option"));
            Assert.AreEqual(false, sel.Get("returnPerformed"));
            StringAssert.Contains("BLOCKED_SPEC", (string)sel.Get("tc19b"));
            Assert.AreEqual(MotorState.Dead, h.P.State, "nothing is restored (U-08)");
        }

        [Test, Category("TC_06_19a")]
        public void ButtonB_DeclinesTheRevive()
        {
            var h = Dying(1);
            h.Seconds(1.3).Tap(LogicalAction.ButtonB).Step();
            Assert.AreEqual(DeathStage.GameOver, h.Sim.Death.Stage);
            StringAssert.Contains("ButtonB", h.Events("death.flow").Last().Reason);
        }

        [Test, Category("TC_06_19a")]
        public void NoReviveItem_GameOverDirectly_ThreeOptions()
        {
            var h = Dying(0);
            h.Seconds(1.3);
            Assert.AreEqual(DeathStage.GameOver, h.Sim.Death.Stage);
            var f = h.Events("death.flow").Last();
            Assert.AreEqual("CheckpointRestart,LoadSave,Lobby", f.Get("options"));
            Assert.AreEqual(0, h.Count("death.revive"));
            var t = h.Events("player.death").Single().SimulationTick;
            h.Seconds(2);
            Assert.AreEqual(0, h.Events("enemy.state").Count(e => e.SimulationTick > t + 30 && e.StateAfter == "AttackStartup"), "R-08-07");
        }

        [Test, Category("TC_06_19a")]
        public void DpadDuringDeath_GoesToTheDeathMenu_NotQuickslots()
        {
            var h = Dying(1);
            h.Seconds(1.3);
            h.Ui(LogicalAction.QuickDown).Step();
            Assert.AreEqual(0, h.Events("item.request").Count(), "no quickslot use while dead");
            Assert.Greater(h.Events("input.route").Count(e => e.Reason.StartsWith("Forward:DeathFlow")), 0);
        }

        [Test, Category("DATA")]
        public void ReviveItem_NotUsableFromQuickslot()
        {
            var h = Wp05Fixtures.H("TC-06-19a", Wp05Fixtures.Profile(1), ("b", "bag", 30f));
            h.Ui(LogicalAction.QuickLeft).Step();
            StringAssert.Contains("ReviveOnlyOnDeath", h.Events("item.request").Single().Reason);
            Assert.AreEqual(1, h.Inv().Count("feather"));
        }

        [Test, Category("DATA")]
        public void Validator_CommandsMagicAndDeathValues()
        {
            Assert.IsEmpty(RingValidator.Validate(Wp05Fixtures.Profile()));
            var cases = new (string expect, System.Action<RingProfile> f)[]
            {
                ("at least 3 directions", p => p.Commands[0].Directions.RemoveAt(0)),
                ("None is not allowed", p => p.Commands[0].Directions[1] = Dir8.None),
                ("twice in a row", p => p.Commands[0].Directions[1] = Dir8.Down),
                ("button must be", p => p.Commands[0].Button = LogicalAction.RingToggle),
                ("Window", p => p.Commands[0].Window = float.NaN),
                ("not defined", p => p.Commands[0].MagicId = "nope"),
                ("same directions and button", p => p.Commands.Add(Wp05Fixtures.Cmd("dup", "fire", LogicalAction.Attack, Dir8.Down, Dir8.DownRight, Dir8.Right))),
                ("Knockback", p => p.Magics[0].Knockback = 0f),
                ("BuffDuration", p => p.Magics[2].BuffDuration = 0f),
                ("heal magic needs Power", p => p.Magics[1].Power = 0f),
            };
            foreach (var (expect, f) in cases)
            {
                var p = Wp05Fixtures.Profile();
                f(p);
                var issues = RingValidator.Validate(p);
                Assert.IsTrue(issues.Any(i => i.ToString().Contains(expect)), expect + ": " + string.Join(" | ", issues));
            }
            var c = CombatFixtures.Profile();
            c.ReviveHpRatio = 2f;
            Assert.IsTrue(DataValidator.ValidateCombat(c, CombatFixtures.Unlocks(), new List<EnemyDefinition>(), null).Any(i => i.ToString().Contains("ReviveHpRatio")));
        }
    }
}
