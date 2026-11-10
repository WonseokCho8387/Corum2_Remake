using System.Collections.Generic;
using System.Linq;
using Corum2.Core;
using NUnit.Framework;
using static Corum2.Tests.CombatFixtures;

namespace Corum2.Tests
{
    /// <summary>WP-03 ring inventory and quickslots (P0-02 R-02-03..11, R-05-05). Test data only (TEST-ITEM-01).</summary>
    public static class RingFixtures
    {
        static DataMeta Meta(string id) => new DataMeta { DomainId = id, DataVersion = "test-3", Status = "experimental" };

        public static RingProfile Profile(bool twoPages = true)
        {
            var p = new RingProfile { Meta = Meta("ring.test") };
            p.Magics.Add(new MagicDefinition { Id = "fire", Power = 30f, CastTime = 0.2f });
            p.Items.Add(new ItemDefinition { Id = "potion", Name = "Potion", Kind = ItemKind.Consumable, Effect = ItemEffectType.Heal, Amount = 20f, Cooldown = 1f });
            p.Items.Add(new ItemDefinition { Id = "hipotion", Name = "Hi-Potion", Kind = ItemKind.Consumable, Effect = ItemEffectType.Heal, Amount = 50f, Cooldown = 1f });
            p.Items.Add(new ItemDefinition { Id = "herb", Name = "Herb", Kind = ItemKind.Consumable, Effect = ItemEffectType.Heal, Amount = 5f, Cooldown = 0.5f });
            p.Items.Add(new ItemDefinition { Id = "scroll", Name = "Fire Scroll", Kind = ItemKind.MagicScroll, Effect = ItemEffectType.Magic, MagicId = "fire", Cooldown = 1f });
            p.Items.Add(new ItemDefinition { Id = "key", Name = "Old Key", Kind = ItemKind.Quest, Effect = ItemEffectType.None });
            p.StartInventory.Add(new ItemStack { ItemId = "potion", Count = 3 });   // page 0 slot 0 (up)
            p.StartInventory.Add(new ItemStack { ItemId = "hipotion", Count = 1 }); // slot 1 (up-right)
            p.StartInventory.Add(new ItemStack { ItemId = "herb", Count = 1 });     // slot 2 (right)
            p.StartInventory.Add(new ItemStack { ItemId = "scroll", Count = 1 });   // slot 3 (down-right)
            p.StartInventory.Add(new ItemStack { ItemId = "key", Count = 1 });      // slot 4 (down)
            if (twoPages)
                for (var i = 0; i < 5; i++)
                {
                    p.Items.Add(new ItemDefinition { Id = "f" + i, Name = "Filler " + i, Kind = ItemKind.Consumable, Effect = ItemEffectType.Heal, Amount = 1f, Cooldown = 0.5f });
                    p.StartInventory.Add(new ItemStack { ItemId = "f" + i, Count = 2 }); // f0..f2 page 0 slots 5..7, f3,f4 page 1
                }
            return p;
        }

        public static SimHarness Ring(string tc, WorldData world = null, List<EnemyDefinition> defs = null, RingProfile ring = null) =>
            new SimHarness(tc, Fixtures.Unlocks(true, true, true), world ?? CombatFixtures.World(), null, CombatFixtures.Profile(),
                CombatFixtures.Unlocks(), defs ?? Defs(), ring ?? Profile());

        public static RingController R(this SimHarness h) => h.Sim.Ring;
        public static Inventory Inv(this SimHarness h) => h.Sim.Items.Inventory;

        /// <summary>Opens the ring and points the stick at a ring slot (slot 0 = up, clockwise).</summary>
        public static SimHarness OpenAt(this SimHarness h, int slot)
        {
            if (!h.R().IsOpen) h.Ui(LogicalAction.RingToggle);
            return h.Point(slot);
        }

        public static SimHarness Point(this SimHarness h, int slot)
        {
            if (slot < 0) return h.Cursor(0f, 0f);
            var a = System.Math.PI / 2 - slot * System.Math.PI / 4;
            return h.Cursor((float)System.Math.Cos(a), (float)System.Math.Sin(a));
        }
    }

    public class RingTests
    {
        [Test, Category("TC_02_03")]
        public void LB_OpensAndCloses_SimulationKeepsGameTime()
        {
            var h = RingFixtures.Ring("TC-02-03");
            h.Ui(LogicalAction.RingToggle);
            Assert.IsTrue(h.R().IsOpen);
            var t = h.Sim.GameTime;
            h.Step(5);
            Assert.AreEqual(t + 5 * SimHarness.Dt, h.Sim.GameTime, 1e-6, "world is slowed by the host's time scale, not by skipping ticks");
            h.Ui(LogicalAction.RingToggle);
            Assert.IsFalse(h.R().IsOpen);
            Assert.AreEqual(1, h.Count("ring.open"));
            Assert.AreEqual(1, h.Count("ring.close", "Manual(LB)"));
        }

        [Test, Category("TC_02_04")]
        public void Cursor_FollowsStick_NeutralClearsSelection_NoUse()
        {
            var h = RingFixtures.Ring("TC-02-04");
            h.OpenAt(0);
            Assert.AreEqual("potion", h.R().SelectedItem);
            h.Point(2);
            Assert.AreEqual("herb", h.R().SelectedItem);
            h.Cursor(0.1f, 0.1f); // inside the deadzone
            Assert.AreEqual(-1, h.R().SelectedSlot);
            Assert.IsNull(h.R().SelectedItem);
            h.Ui(LogicalAction.Jump).Step(2);
            Assert.AreEqual(0, h.Count("item.request"), "A at neutral uses nothing (R-02-04)");
            Assert.IsTrue(h.R().IsOpen, "neutral keeps the ring open (R-02-09)");
            Assert.AreEqual(3, h.Inv().Count("potion"));
        }

        [Test, Category("TC_02_05")]
        public void StickWhileOpen_DoesNotMovePlayer_HeldMoveReleasedAndResumed()
        {
            var h = RingFixtures.Ring("TC-02-05");
            h.Dir(Dir8.Right).Step(10);
            Assert.Greater(h.P.VelX, 0f);
            h.Ui(LogicalAction.RingToggle).Step(60);
            Assert.AreEqual(0, h.P.HeldH, "opening releases held movement for the game (EXP-RING-03)");
            var x = h.P.Position.X;
            h.Dir(Dir8.Left).Step(10).Dir(Dir8.Right).Step(10);
            Assert.AreEqual(x, h.P.Position.X, 1e-4, "stick moves the cursor, not the player (R-02-05)");
            Assert.Greater(h.Count("input.route", "Cursor(R-02-05)"), 0);
            h.Ui(LogicalAction.RingToggle).Step(10);
            Assert.AreEqual(1, h.P.HeldH, "still-held direction resumes after closing");
            Assert.Greater(h.P.Position.X, x);
        }

        [Test, Category("TC_02_05")]
        public void Pages_LT_RT_Wrap()
        {
            var h = RingFixtures.Ring("TC-02-05");
            h.OpenAt(0);
            Assert.AreEqual(2, h.Inv().PageCount);
            h.Ui(LogicalAction.PageNext);
            Assert.AreEqual(1, h.R().Page);
            Assert.AreEqual("f3", h.R().SelectedItem, "page 1 slot 0");
            h.Ui(LogicalAction.PageNext);
            Assert.AreEqual(0, h.R().Page, "wraps (EXP-RING-04)");
            h.Ui(LogicalAction.PagePrev);
            Assert.AreEqual(1, h.R().Page);
            h.Point(2);
            Assert.IsNull(h.R().SelectedItem, "empty slot on the last page");
        }

        [Test, Category("TC_02_05")]
        public void X_ShowsDetail_RingStays_XClosesDetail()
        {
            var h = RingFixtures.Ring("TC-02-05");
            h.OpenAt(1).Ui(LogicalAction.Attack);
            Assert.AreEqual(RingMode.Detail, h.R().Mode);
            Assert.AreEqual("hipotion", h.R().TargetItem);
            h.Step(5);
            Assert.IsTrue(h.R().IsOpen);
            Assert.AreEqual(0, h.C.Attack == null ? 0 : 1, "X in the ring is not a game attack");
            h.Ui(LogicalAction.Attack);
            Assert.AreEqual(RingMode.Browse, h.R().Mode);
        }

        [Test, Category("TC_02_06")]
        public void Quickslots_UseOnlyTheirItem_EmptyAfterZero()
        {
            var h = RingFixtures.Ring("TC-02-06");
            var q = h.Inv().Quickslots;
            q[0] = "potion"; q[1] = "hipotion"; q[2] = "herb"; q[3] = "f0";
            h.C.PlayerHp = 10f;
            h.Ui(LogicalAction.QuickRight).Step();
            Assert.AreEqual(0, h.Inv().Count("hipotion"));
            Assert.AreEqual(60f, h.C.PlayerHp, 1e-4);
            Assert.AreEqual(3, h.Inv().Count("potion"), "other items untouched");
            Assert.AreEqual("", q[1], "slot cleared when the item runs out (EXP-QS-02)");
            h.Seconds(1.1).Ui(LogicalAction.QuickRight).Step();
            Assert.AreEqual(1, h.Count("input.route", "EmptySlot:Right"));
            h.Ui(LogicalAction.QuickDown).Step();
            Assert.AreEqual(0, h.Inv().Count("herb"));
            h.Seconds(0.6).Ui(LogicalAction.QuickLeft).Step();
            Assert.AreEqual(1, h.Inv().Count("f0"));
            h.Seconds(0.6).Ui(LogicalAction.QuickUp).Step();
            Assert.AreEqual(2, h.Inv().Count("potion"));
            Assert.AreEqual(4, h.Count("item.use"));
        }

        [Test, Category("TC_02_06")]
        public void Quickslot_DuringCooldown_Rejected_NothingConsumed()
        {
            var h = RingFixtures.Ring("TC-02-06");
            h.Inv().Quickslots[0] = "potion";
            h.Ui(LogicalAction.QuickUp).Step().Ui(LogicalAction.QuickUp).Step();
            Assert.AreEqual(2, h.Inv().Count("potion"));
            Assert.AreEqual(1, h.Count("item.request", "Rejected:Cooldown"));
        }

        [Test, Category("TC_02_07")]
        public void Register_Y_Then_Dpad_AssignsWithoutUse_AndCloses()
        {
            var h = RingFixtures.Ring("TC-02-07");
            h.OpenAt(0).Ui(LogicalAction.SpecialAttack);
            Assert.AreEqual(RingMode.Registering, h.R().Mode);
            Assert.IsTrue(h.R().IsOpen, "entering registration keeps the ring (R-02-09)");
            h.Point(-1);
            Assert.AreEqual("potion", h.R().TargetItem, "neutral cursor keeps the fixed target (R-02-07)");
            h.Point(2).Ui(LogicalAction.Jump);
            Assert.AreEqual(RingMode.Registering, h.R().Mode, "A in registration does nothing");
            h.Ui(LogicalAction.QuickLeft).Step(3);
            Assert.AreEqual("potion", h.Inv().Quickslots[(int)QuickSlot.Left]);
            Assert.IsFalse(h.R().IsOpen);
            Assert.AreEqual(3, h.Inv().Count("potion"), "registration consumes nothing");
            Assert.AreEqual(0, h.Count("item.request"));
            Assert.AreEqual(0, h.Count("item.use"));
        }

        [Test, Category("TC_UI_12")]
        public void DpadInsideRing_Registration_vs_Browse()
        {
            var h = RingFixtures.Ring("TC-UI-12");
            h.Inv().Quickslots[0] = "herb";
            h.OpenAt(0).Ui(LogicalAction.QuickUp).Step(2);
            Assert.IsTrue(h.R().IsOpen);
            Assert.AreEqual(1, h.Inv().Count("herb"), "browse: D-pad ignored (EXP-RING-10), no quickslot use");
            Assert.AreEqual(1, h.Count("ring.ignore", "DpadOutsideRegistration"));
            h.Ui(LogicalAction.SpecialAttack).Ui(LogicalAction.QuickUp).Step(2);
            Assert.AreEqual("potion", h.Inv().Quickslots[0], "registration: D-pad assigns only, replacing herb (EXP-RING-08)");
            Assert.AreEqual(1, h.Inv().Count("herb"));
            Assert.AreEqual(0, h.Count("item.use"));
        }

        [Test, Category("TC_02_08")]
        public void Discard_CancelKeeps_ConfirmRemoves_QuestBlocked()
        {
            var h = RingFixtures.Ring("TC-02-08");
            h.OpenAt(0).Ui(LogicalAction.ButtonB);
            Assert.AreEqual(RingMode.ConfirmDiscard, h.R().Mode);
            h.Ui(LogicalAction.ButtonB);
            Assert.AreEqual(RingMode.Browse, h.R().Mode);
            Assert.IsTrue(h.R().IsOpen);
            Assert.AreEqual(3, h.Inv().Count("potion"));

            h.Point(4).Ui(LogicalAction.ButtonB);
            Assert.AreEqual(RingMode.Browse, h.R().Mode, "quest item: no confirm step");
            Assert.IsTrue(h.R().IsOpen);
            Assert.AreEqual(1, h.Inv().Count("key"));
            Assert.AreEqual(1, h.Count("ring.discard", "Blocked:Quest"));

            h.Point(0).Ui(LogicalAction.ButtonB).Ui(LogicalAction.Jump);
            Assert.AreEqual(2, h.Inv().Count("potion"), "one unit discarded (EXP-RING-07)");
            Assert.IsFalse(h.R().IsOpen, "confirmed discard closes (R-02-09)");
        }

        [Test, Category("TC_02_09")]
        public void AutoClose_OnlyAfterUseRegisterDiscard()
        {
            var h = RingFixtures.Ring("TC-02-09");
            h.OpenAt(0).Ui(LogicalAction.Jump).Step();
            Assert.IsFalse(h.R().IsOpen, "use closes");
            Assert.AreEqual(2, h.Inv().Count("potion"));
            h.Seconds(1.1);
            h.OpenAt(2).Ui(LogicalAction.SpecialAttack);
            Assert.IsTrue(h.R().IsOpen, "registration entry stays");
            h.Ui(LogicalAction.QuickDown);
            Assert.IsFalse(h.R().IsOpen, "registration complete closes");
            h.OpenAt(-1);
            Assert.IsTrue(h.R().IsOpen, "neutral stays");
            h.Point(1).Ui(LogicalAction.ButtonB).Ui(LogicalAction.ButtonB);
            Assert.IsTrue(h.R().IsOpen, "discard cancel stays");
            h.Point(4).Ui(LogicalAction.ButtonB);
            Assert.IsTrue(h.R().IsOpen, "discard blocked stays");
            h.Point(1).Ui(LogicalAction.ButtonB).Ui(LogicalAction.Jump);
            Assert.IsFalse(h.R().IsOpen, "discard confirm closes");
            var reasons = h.Events("ring.close").Select(e => e.Reason).ToList();
            CollectionAssert.AreEqual(new[] { "Used(R-02-09)", "Registered(R-02-09)", "Discarded(R-02-09)" }, reasons);
        }

        [Test, Category("TC_02_09")]
        public void RejectedUse_KeepsRingOpen()
        {
            var h = RingFixtures.Ring("TC-02-09");
            h.OpenAt(4).Ui(LogicalAction.Jump);
            Assert.IsTrue(h.R().IsOpen, "quest item cannot be used; ring stays (EXP-RING-11)");
            Assert.AreEqual(1, h.Count("item.request", "Rejected:NotUsable:Quest"));
            h.Point(0).Ui(LogicalAction.Jump).Step();
            h.OpenAt(0).Ui(LogicalAction.Jump);
            Assert.IsTrue(h.R().IsOpen, "cooldown rejection keeps the ring");
            Assert.AreEqual(1, h.Count("item.request", "Rejected:Cooldown"));
        }

        [Test, Category("TC_02_10")]
        public void Scroll_UsesSharedMagicDefinition_ConsumedAndRemoved_Cooldown()
        {
            var h = RingFixtures.Ring("TC-02-10");
            h.OpenAt(3).Ui(LogicalAction.Jump).Step();
            var m = h.Events("magic.request").Single();
            Assert.AreEqual("fire", m.Get("magicId"));
            Assert.AreEqual(30f, (float)m.Get("power"));
            Assert.AreEqual(0.2f, (float)m.Get("castTime"));
            Assert.AreEqual(0, h.Inv().Count("scroll"));
            Assert.AreEqual(9, h.Inv().StackCount, "exhausted scroll leaves the ring (R-02-10)");
            Assert.AreEqual(1.0, h.Sim.Items.CooldownLeft, 0.03);
        }

        [Test, Category("TC_02_11")]
        public void Ring_OpensInAir_AndDuringAttack_UseWaitsForMotionEnd()
        {
            var h = RingFixtures.Ring("TC-02-11");
            h.C.PlayerHp = 50f;
            h.Press(LogicalAction.Jump).Step(5);
            Assert.IsFalse(h.P.Grounded);
            h.Ui(LogicalAction.RingToggle);
            Assert.IsTrue(h.R().IsOpen, "opens in the air");
            h.Ui(LogicalAction.RingToggle);
            h.StepUntilLanded();
            h.Press(LogicalAction.Attack).Step(2);
            Assert.AreEqual(MotorState.GroundAttack, h.P.State);
            h.OpenAt(0).Ui(LogicalAction.Jump);
            Assert.IsFalse(h.R().IsOpen);
            Assert.AreEqual(1, h.Count("item.request", "Reserved:Motion:GroundAttack"));
            h.Step();
            Assert.AreEqual(50f, h.C.PlayerHp, 1e-4, "no effect during the attack (EXP-ITEM-03, CF-07)");
            h.StepUntil(() => h.Count("item.use") > 0, 60);
            Assert.AreEqual(70f, h.C.PlayerHp, 1e-4);
            Assert.AreNotEqual(MotorState.GroundAttack, h.P.State);
            Assert.Greater(h.Count("attack.end"), 0, "the attack ran to its end");
        }

        [Test, Category("TC_05_05")]
        public void HitReaction_RingOpens_UseReservedThenRunsOnce()
        {
            var h = RingFixtures.Ring("TC-05-05", CombatFixtures.World(("e1", "hitter", 11f)), Defs(Hitter()));
            h.StepUntil(() => h.P.State == MotorState.HitStun);
            var hp = h.C.PlayerHp;
            h.Ui(LogicalAction.RingToggle);
            Assert.IsTrue(h.R().IsOpen, "ring is the exception during hit reaction (R-05-04)");
            h.Point(0).Ui(LogicalAction.Jump);
            Assert.IsFalse(h.R().IsOpen, "request closes the ring (R-05-05)");
            Assert.AreEqual(1, h.Count("item.request", "Reserved:HitReaction"));
            h.Step();
            Assert.AreEqual(hp, h.C.PlayerHp, 1e-4, "no effect during hit reaction");
            Assert.AreEqual(3, h.Inv().Count("potion"), "not consumed yet (EXP-ITEM-04)");
            h.StepUntil(() => h.P.State != MotorState.HitStun);
            h.Step();
            Assert.AreEqual(1, h.Count("item.use"));
            Assert.AreEqual(System.Math.Min(100f, hp + 20f), h.C.PlayerHp, 1e-4);
            Assert.AreEqual(2, h.Inv().Count("potion"));
            var use = h.Events("item.use").Single();
            Assert.Greater((long)use.Get("execTick"), (long)use.Get("requestTick"));
        }

        [Test, Category("TC_CF_08a")]
        public void Quickslot_DuringHitReaction_Rejected_RingRequestPendingBlocksSecond()
        {
            var h = RingFixtures.Ring("TC-CF-08a", CombatFixtures.World(("e1", "hitter", 11f)), Defs(Hitter()));
            h.Inv().Quickslots[0] = "herb";
            h.StepUntil(() => h.P.State == MotorState.HitStun);
            h.Ui(LogicalAction.QuickUp);
            Assert.AreEqual(1, h.Count("item.request", "Rejected:QuickslotDuringHitReaction"));
            h.OpenAt(0).Ui(LogicalAction.Jump);
            h.OpenAt(1).Ui(LogicalAction.Jump);
            Assert.AreEqual(1, h.Count("item.request", "Rejected:PendingExists"));
            Assert.IsTrue(h.R().IsOpen, "second request rejected, ring stays");
            Assert.AreEqual(1, h.Inv().Count("herb"));
            Assert.AreEqual(1, h.Inv().Count("hipotion"));
        }

        [Test, Category("TC_CF_08a")]
        public void Death_CancelsPendingUse_AndClosesRing()
        {
            var h = RingFixtures.Ring("TC-CF-08a");
            h.Press(LogicalAction.Attack).Step(2);
            h.OpenAt(0).Ui(LogicalAction.Jump);
            Assert.IsNotNull(h.Sim.Items.Pending, "use waits for the attack (EXP-ITEM-03)");
            h.OpenAt(-1);
            h.P.EnterLocked(MotorState.Dead, "TestDeath");
            h.Step();
            Assert.IsFalse(h.R().IsOpen, "death closes the ring (EXP-RING-06)");
            Assert.AreEqual(1, h.Count("item.cancel", "PlayerDead"));
            Assert.IsNull(h.Sim.Items.Pending);
            Assert.AreEqual(3, h.Inv().Count("potion"), "nothing consumed (EXP-ITEM-06)");
            h.Ui(LogicalAction.RingToggle);
            Assert.IsFalse(h.R().IsOpen, "ring cannot be opened when dead");
        }
    }
}
