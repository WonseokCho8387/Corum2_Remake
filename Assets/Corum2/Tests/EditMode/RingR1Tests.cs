using System.Collections.Generic;
using System.Linq;
using Corum2.Core;
using NUnit.Framework;
using static Corum2.Tests.CombatFixtures;

namespace Corum2.Tests
{
    /// <summary>WP-03-R1 regressions for the GPT review: WP03-F01 (stick release before registration),
    /// WP03-F02 (same-frame stick + button order, through Simulation.RouteFrame like the host), WP03-F03 (non-finite
    /// ring data), WP03-M01 (ring in AirAttack, ring use after an air hit).</summary>
    public class RingR1Tests
    {
        static (float, float) Stick(int slot)
        {
            if (slot < 0) return (0f, 0f);
            var a = System.Math.PI / 2 - slot * System.Math.PI / 4;
            return ((float)System.Math.Cos(a), (float)System.Math.Sin(a));
        }

        // ------------------------------------------------------------------ F01

        [Test, Category("TC_02_07")]
        public void Registration_DpadBeforeRelease_DoesNothing_AfterRelease_Assigns()
        {
            var h = RingFixtures.Ring("TC-02-07");
            h.OpenAt(0).Ui(LogicalAction.SpecialAttack);
            h.Ui(LogicalAction.QuickUp).Step(2);
            Assert.AreEqual("", h.Inv().Quickslots[0], "no assignment before the stick release (R-02-07)");
            Assert.AreEqual(RingMode.Registering, h.R().Mode);
            Assert.IsTrue(h.R().IsOpen, "no auto-close");
            Assert.AreEqual(1, h.Count("ring.ignore", "Registering:StickNotReleased"));
            h.Point(2); // still tilted: moving the stick elsewhere is not a release
            h.Ui(LogicalAction.QuickUp);
            Assert.AreEqual("", h.Inv().Quickslots[0]);
            h.Point(-1).Point(1).Ui(LogicalAction.QuickUp).Step();
            Assert.AreEqual("potion", h.Inv().Quickslots[0], "released once since Y: the fixed target is assigned");
            Assert.IsFalse(h.R().IsOpen);
            Assert.AreEqual(3, h.Inv().Count("potion"));
            Assert.AreEqual(0, h.Count("item.request"));
            Assert.AreEqual(1, h.Count("ring.release"));
        }

        [Test, Category("TC_02_07")]
        public void Registration_SameFrameOrder_ReleaseThenDpad_vs_DpadThenRelease()
        {
            var h = RingFixtures.Ring("TC-02-07");
            h.Frame(LogicalAction.RingToggle, Stick(0), LogicalAction.SpecialAttack, LogicalAction.QuickLeft, Stick(-1));
            Assert.AreEqual("", h.Inv().Quickslots[3], "D-pad came before the release in the frame");
            Assert.AreEqual(RingMode.Registering, h.R().Mode);
            h.Frame(LogicalAction.QuickLeft);
            Assert.AreEqual("potion", h.Inv().Quickslots[3], "release already observed");
            h.Step(2);
            h.Frame(LogicalAction.RingToggle, Stick(2), LogicalAction.SpecialAttack, Stick(-1), LogicalAction.QuickDown);
            Assert.AreEqual("herb", h.Inv().Quickslots[2], "release then D-pad in one frame assigns");
            Assert.AreEqual(0, h.Count("item.use"));
        }

        // ------------------------------------------------------------------ F02

        static SimHarness Selected(string tc)
        {
            var h = RingFixtures.Ring(tc);
            h.C.PlayerHp = 50f;
            h.Frame(LogicalAction.RingToggle, Stick(0)); // potion selected in an earlier frame
            Assert.AreEqual("potion", h.R().SelectedItem);
            return h;
        }

        [Test, Category("TC_02_04")]
        public void SameFrame_NeutralThenA_UsesNothing()
        {
            var h = Selected("TC-02-04");
            h.Frame(Stick(-1), LogicalAction.Jump).Step(2);
            Assert.AreEqual(0, h.Count("item.request"));
            Assert.IsTrue(h.R().IsOpen);
            Assert.AreEqual(3, h.Inv().Count("potion"));
            Assert.AreEqual(50f, h.C.PlayerHp, 1e-4);
        }

        [Test, Category("TC_02_05")]
        public void SameFrame_NewItemThenA_UsesNewItem()
        {
            var h = Selected("TC-02-05");
            h.Frame(Stick(2), LogicalAction.Jump).Step(2);
            Assert.AreEqual("herb", h.Events("item.use").Single().TargetId);
            Assert.AreEqual(55f, h.C.PlayerHp, 1e-4);
            Assert.AreEqual(3, h.Inv().Count("potion"));
            Assert.AreEqual(0, h.Inv().Count("herb"), "fixture has one herb");
        }

        [Test, Category("TC_02_05")]
        public void SameFrame_AThenNeutral_UsesItemSelectedAtThePress()
        {
            var h = Selected("TC-02-05");
            h.Frame(LogicalAction.Jump, Stick(-1)).Step(2);
            Assert.AreEqual("potion", h.Events("item.use").Single().TargetId);
            Assert.AreEqual(70f, h.C.PlayerHp, 1e-4);
        }

        [Test, Category("TC_02_05")]
        public void SameFrame_AThenNewItem_UsesItemSelectedAtThePress()
        {
            var h = Selected("TC-02-05");
            h.Frame(LogicalAction.Jump, Stick(2)).Step(2);
            Assert.AreEqual("potion", h.Events("item.use").Single().TargetId);
            Assert.AreEqual(1, h.Inv().Count("herb"), "herb untouched");
        }

        [Test, Category("TC_02_05")]
        public void SameFrame_OpenSelectUse_AllInOneFrame()
        {
            var h = RingFixtures.Ring("TC-02-05");
            h.C.PlayerHp = 50f;
            h.Frame(LogicalAction.RingToggle, Stick(1), LogicalAction.Jump).Step(2);
            Assert.AreEqual("hipotion", h.Events("item.use").Single().TargetId);
            Assert.AreEqual(100f, h.C.PlayerHp, 1e-4);
            h.Seconds(1.1);
            // Hi-Potion is gone, so the stacks moved up: slot 1 is now Herb.
            h.Frame(Stick(1), LogicalAction.RingToggle, LogicalAction.Jump).Step(2);
            Assert.AreEqual("herb", h.Events("item.use").Last().TargetId, "stick held before opening: cursor takes it at open");
        }

        [Test, Category("TC_02_04")]
        public void SameFrame_AnalogInsideDeadzone_IsNeutral()
        {
            var h = Selected("TC-02-04");
            h.Frame(((float, float))(0.2f, 0.2f), LogicalAction.Jump).Step(2);
            Assert.AreEqual(0, h.Count("item.request"), "|(0.2,0.2)| = 0.28 < deadzone 0.35");
            h.Frame(((float, float))(0.36f, 0f), LogicalAction.Jump).Step(2);
            Assert.AreEqual("herb", h.Events("item.use").Single().TargetId, "just past the deadzone selects the right slot");
        }

        // ------------------------------------------------------------------ F03

        static RingProfile Valid() => RingFixtures.Profile();

        static List<ValidationIssue> V(System.Action<RingProfile> change)
        {
            var p = Valid();
            change(p);
            return RingValidator.Validate(p);
        }

        [Test, Category("DATA")]
        public void Validator_RejectsNonFinite()
        {
            var bad = new System.Action<RingProfile>[]
            {
                p => p.FindItem("potion").Amount = float.NaN,
                p => p.FindItem("potion").Amount = float.PositiveInfinity,
                p => p.FindItem("potion").Cooldown = float.NaN,
                p => p.FindItem("potion").Cooldown = float.PositiveInfinity,
                p => p.FindItem("potion").Cooldown = -0.1f,
                p => p.FindItem("potion").Amount = -1f,
                p => p.FindItem("potion").Amount = 0f,
                p => p.CursorDeadzone = float.NaN,
                p => p.CursorDeadzone = float.NegativeInfinity,
                p => p.SlowScale = float.NaN,
                p => p.SlowScale = float.PositiveInfinity,
                p => p.SlowScale = 0f,
                p => p.FindMagic("fire").Power = float.NaN,
                p => p.FindMagic("fire").CastTime = float.PositiveInfinity,
            };
            for (var i = 0; i < bad.Length; i++)
                Assert.IsNotEmpty(V(bad[i]), "case " + i);
            StringAssert.Contains("potion", V(bad[0]).First().ToString(), "issue names the item");
            StringAssert.Contains("Amount", V(bad[0]).First().ToString(), "issue names the field");
        }

        [Test, Category("DATA")]
        public void Validator_AcceptsBoundaries()
        {
            Assert.IsEmpty(V(p => { }));
            Assert.IsEmpty(V(p => p.SlowScale = 1f));
            Assert.IsEmpty(V(p => p.SlowScale = 0.01f));
            Assert.IsEmpty(V(p => p.CursorDeadzone = 0.05f));
            Assert.IsEmpty(V(p => p.CursorDeadzone = 0.95f));
            Assert.IsEmpty(V(p => p.FindItem("potion").Cooldown = 0f));
            Assert.IsEmpty(V(p => p.FindItem("scroll").Amount = 0f));
            Assert.IsEmpty(V(p => p.FindMagic("fire").CastTime = 0f));
        }

        // ------------------------------------------------------------------ M01

        [Test, Category("TC_02_11")]
        public void Ring_OpensDuringAirAttack_UseWaitsForTheAttack()
        {
            var h = RingFixtures.Ring("TC-02-11");
            h.C.PlayerHp = 50f;
            h.Press(LogicalAction.Jump).Step(8);
            h.Press(LogicalAction.Attack).Step(2);
            Assert.AreEqual(MotorState.AirAttack, h.P.State);
            h.OpenAt(0);
            Assert.IsTrue(h.R().IsOpen);
            h.Ui(LogicalAction.Jump);
            Assert.AreEqual(1, h.Count("item.request", "Reserved:Motion:AirAttack"));
            h.Step();
            Assert.AreEqual(50f, h.C.PlayerHp, 1e-4);
            h.StepUntil(() => h.Count("item.use") > 0, 120);
            Assert.AreEqual(70f, h.C.PlayerHp, 1e-4);
        }

        [Test, Category("TC_INT_02")]
        public void AirHit_RingUseReserved_RunsOnceAfterReaction()
        {
            var h = RingFixtures.Ring("TC-INT-02", CombatFixtures.World(("e1", "hitter", 11f)), Defs(Hitter()));
            h.StepUntil(() => h.C.Find("e1").State == EnemyState.AttackStartup);
            h.Step(8).Press(LogicalAction.Jump);
            h.StepUntil(() => h.P.State == MotorState.HitStun, 60);
            Assert.AreEqual(MotorState.HitStun, h.P.State);
            Assert.IsFalse(h.P.Grounded, "hit while airborne");
            var hp = h.C.PlayerHp;
            h.OpenAt(0).Ui(LogicalAction.Jump);
            Assert.IsFalse(h.R().IsOpen);
            Assert.AreEqual(1, h.Count("item.request", "Reserved:HitReaction"));
            h.Step();
            Assert.AreEqual(hp, h.C.PlayerHp, 1e-4);
            h.StepUntil(() => h.Count("item.use") > 0, 120);
            Assert.AreEqual(1, h.Count("item.use"));
            Assert.AreEqual(System.Math.Min(100f, hp + 20f), h.C.PlayerHp, 1e-4);
        }
    }
}
