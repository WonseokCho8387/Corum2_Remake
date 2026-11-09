using System;
using Corum2.Core;
using NUnit.Framework;

namespace Corum2.Tests
{
    /// <summary>
    /// Core-only checks for P0-03. These are automated evidence (A) for the Core logic; they do not replace the
    /// V/L evidence the master TCs require in play.
    /// </summary>
    public class MovementTests
    {
        const float Tol = 1e-3f;

        // ------------------------------------------------------------ TC-03-01 (R-03-01)

        [Test, Category("TC_03_01")]
        public void HoldingDirectionWalks_EvenAtFullTilt()
        {
            var h = new SimHarness("TC-03-01");
            h.Dir(Dir8.Right).Seconds(1.0);
            Assert.AreEqual(MotorState.Walk, h.P.State);
            Assert.AreEqual(h.Sim.Movement.WalkSpeed, h.P.VelX, Tol);
            Assert.AreEqual(0, h.P.RunDir);
        }

        [Test, Category("TC_03_01")]
        public void DoubleTapThenHold_Runs_AndReleasingStops()
        {
            var h = new SimHarness("TC-03-01");
            h.Dir(Dir8.Right).Step(3).Dir(Dir8.None).Step(3).Dir(Dir8.Right).Step(10);
            Assert.AreEqual(MotorState.Run, h.P.State);
            Assert.AreEqual(h.Sim.Movement.RunSpeed, h.P.VelX, Tol);
            Assert.IsNotNull(h.LastWithReason("DoubleTap"));

            h.Dir(Dir8.None).Step(15);
            Assert.AreEqual(MotorState.Idle, h.P.State);
            Assert.AreEqual(0, h.P.RunDir);
            Assert.IsNotNull(h.LastWithReason("RunDirectionReleased"));

            h.Dir(Dir8.Right).Step(2);
            Assert.AreEqual(MotorState.Walk, h.P.State, "Holding again after the window walks; the run is not remembered.");
        }

        [Test, Category("TC_03_01")]
        public void SlowSecondTap_Walks()
        {
            var h = new SimHarness("TC-03-01");
            h.Dir(Dir8.Right).Step(3).Dir(Dir8.None).Step(15).Dir(Dir8.Right).Step(5); // 0.36 s press-to-press
            Assert.AreEqual(MotorState.Walk, h.P.State);
        }

        // ------------------------------------------------------------ TC-03-02 (R-03-02)

        static (float peak, float distance) MeasureJump(Func<SimHarness, SimHarness> setup)
        {
            var h = new SimHarness("TC-03-02");
            setup(h);
            var startX = h.P.Position.X;
            var startY = h.P.Position.Y;
            h.Press(LogicalAction.Jump).Step();
            h.Dir(Dir8.None); // no extra air direction input while measuring
            var peak = h.StepUntilLanded();
            Assert.IsTrue(h.P.Grounded, "jump must land");
            return (peak - startY, Math.Abs(h.P.Position.X - startX));
        }

        [Test, Category("TC_03_02")]
        public void JumpHeightsEqual_DistancesStandingZero_WalkLessThanRun()
        {
            var standing = MeasureJump(h => h);
            var walk = MeasureJump(h => h.Dir(Dir8.Right).Step(5));
            var run = MeasureJump(h => h.Dir(Dir8.Right).Step(3).Dir(Dir8.None).Step(3).Dir(Dir8.Right).Step(5));

            Assert.AreEqual(standing.peak, walk.peak, 0.05f, "walk jump peak");
            Assert.AreEqual(standing.peak, run.peak, 0.05f, "run jump peak");
            Assert.AreEqual(0f, standing.distance, Tol, "standing jump is in place");
            Assert.Greater(walk.distance, 0.5f);
            Assert.Greater(run.distance, walk.distance + 0.5f);
            TestContext.WriteLine($"peak standing={standing.peak:0.###} walk={walk.peak:0.###} run={run.peak:0.###}; distance walk={walk.distance:0.###} run={run.distance:0.###}");
        }

        // ------------------------------------------------------------ TC-03-03 (R-03-03, 11.6)

        [Test, Category("TC_03_03")]
        public void LockedAbilities_AreRejected()
        {
            var h = new SimHarness("TC-03-03", Fixtures.Unlocks(false, false, false));
            h.Press(LogicalAction.Jump).Step(8);
            h.Press(LogicalAction.Jump).Step();
            Assert.IsNotNull(h.LastWithReason("Locked:ExtraJump"));
            h.Dir(Dir8.Right).Press(LogicalAction.Attack).Step();
            Assert.IsNotNull(h.LastWithReason("Locked:AirDash"));
            h.Press(LogicalAction.SpecialAttack).Step();
            Assert.IsNotNull(h.LastWithReason("Locked:Dive"));
            Assert.AreEqual(MotorState.Airborne, h.P.State);
        }

        [Test, Category("TC_03_03")]
        public void EachUnlockIsIndependent()
        {
            var onlyJump = new SimHarness("TC-03-03", Fixtures.Unlocks(true, false, false));
            onlyJump.Press(LogicalAction.Jump).Step(8).Press(LogicalAction.Jump).Step();
            Assert.IsTrue(onlyJump.P.Air.ExtraJumpUsed);
            onlyJump.Dir(Dir8.Right).Press(LogicalAction.Attack).Step();
            Assert.IsNotNull(onlyJump.LastWithReason("Locked:AirDash"));

            var onlyDash = new SimHarness("TC-03-03", Fixtures.Unlocks(false, true, false));
            onlyDash.Press(LogicalAction.Jump).Step(8).Dir(Dir8.Right).Press(LogicalAction.Attack).Step();
            Assert.AreEqual(MotorState.AirDash, onlyDash.P.State);
        }

        // ------------------------------------------------------------ TC-03-04 (R-03-04)

        [Test, Category("TC_03_04")]
        public void DownPlusA_SlidesTowardFacing_BothSides()
        {
            foreach (var facing in new[] { 1, -1 })
            {
                var h = new SimHarness("TC-03-04");
                h.Teleport(40, facing).Step();
                var x0 = h.P.Position.X;
                h.Dir(Dir8.Down).Step(2).Press(LogicalAction.Jump).Step();
                Assert.AreEqual(MotorState.Slide, h.P.State);
                h.Step(20);
                Assert.AreEqual(facing, Math.Sign(h.P.Position.X - x0), "slides toward facing");
                Assert.Greater(Math.Abs(h.P.Position.X - x0), 2.5f);
            }
        }

        [Test, Category("TC_03_04")]
        public void Slide_PassesLowPassage_WalkingDoesNot()
        {
            var walk = new SimHarness("TC-03-04");
            walk.Teleport(57).Dir(Dir8.Right).Seconds(2);
            Assert.Less(walk.P.Position.X, 60f, "standing body is blocked by the passage");

            var h = new SimHarness("TC-03-04");
            h.Teleport(59.6f).Step();
            h.Dir(Dir8.Down).Step().Press(LogicalAction.Jump).Step(30);
            h.Dir(Dir8.None).Step(5);
            Assert.Greater(h.P.Position.X, 62.3f, "slid through the 1.2 m gap");
            Assert.AreEqual(MotorState.Idle, h.P.State, "stands up after clearing the passage");
        }

        // ------------------------------------------------------------ TC-03-05 (R-03-05)

        [Test, Category("TC_03_05")]
        public void Roll_FollowsInputDirection_InAllFourCombinations()
        {
            foreach (var facing in new[] { 1, -1 })
            foreach (var dir in new[] { Dir8.DownLeft, Dir8.DownRight })
            {
                var h = new SimHarness("TC-03-05");
                h.Teleport(40, facing).Step();
                var x0 = h.P.Position.X;
                h.Dir(dir).Step().Press(LogicalAction.Jump).Step();
                Assert.AreEqual(MotorState.Roll, h.P.State, $"facing {facing} {dir}");
                h.Step(25);
                Assert.AreEqual(Dir8Util.Horizontal(dir), Math.Sign(h.P.Position.X - x0), $"facing {facing} {dir}");
                Assert.AreEqual(facing, h.P.Facing, "roll keeps facing (EXP-MOVE-02)");
            }
        }

        // ------------------------------------------------------------ TC-03-06 (R-03-06)

        [Test, Category("TC_03_06")]
        public void AirInputs_AreDistinct_AndAirDoubleTapIsNotADash()
        {
            var h = new SimHarness("TC-03-06", Fixtures.Unlocks(true, true, true));
            h.Press(LogicalAction.Jump).Step(5);

            h.Dir(Dir8.Right).Step(3).Dir(Dir8.None).Step(2).Dir(Dir8.Right).Step(3);
            Assert.AreEqual(MotorState.Airborne, h.P.State, "direction only / double tap = position adjustment");
            Assert.LessOrEqual(Math.Abs(h.P.VelX), h.Sim.Movement.AirControlSpeed + Tol);
            Assert.IsNotNull(h.LastWithReason("AirDoubleTapIgnored"));
            Assert.IsFalse(h.P.Air.DashUsed);

            h.Press(LogicalAction.Jump).Step();
            Assert.IsTrue(h.P.Air.ExtraJumpUsed, "air A = extra jump");

            h.Press(LogicalAction.Attack).Step();
            Assert.AreEqual(MotorState.AirDash, h.P.State, "direction + attack = straight dash");
            Assert.AreEqual(0f, h.P.VelY, Tol, "dash is straight");
            h.Step(15);

            h.Dir(Dir8.DownRight).Press(LogicalAction.SpecialAttack).Step();
            Assert.AreEqual(MotorState.Dive, h.P.State, "direction + special = diagonal dive");
            Assert.Less(h.P.VelY, 0f);
            Assert.Greater(h.P.VelX, 0f);
        }

        [Test, Category("TC_03_06")]
        public void AirGuard_IsReportedNotImplemented()
        {
            var h = new SimHarness("TC-03-06");
            h.Press(LogicalAction.Jump).Step(3).Press(LogicalAction.Guard).Step();
            var e = h.LastWithReason("NotImplementedInWP01");
            Assert.IsNotNull(e);
            Assert.AreEqual("Guard", e.Get("action").ToString());
        }

        // ------------------------------------------------------------ TC-03-07 (R-03-07, R-03-08)

        [Test, Category("TC_03_07")]
        public void JumpThenDash_AndDashThenJump_EachOnce()
        {
            var a = new SimHarness("TC-03-07", Fixtures.Unlocks(true, true, true));
            a.Press(LogicalAction.Jump).Step(5).Press(LogicalAction.Jump).Step(2);
            a.Dir(Dir8.Right).Press(LogicalAction.Attack).Step(15);
            Assert.IsTrue(a.P.Air.ExtraJumpUsed && a.P.Air.DashUsed);
            a.Press(LogicalAction.Jump).Step();
            Assert.IsNotNull(a.LastWithReason("AlreadyUsed:ExtraJump"));
            a.Press(LogicalAction.Attack).Step();
            Assert.IsNotNull(a.LastWithReason("AlreadyUsed:AirDash"));

            var b = new SimHarness("TC-03-07", Fixtures.Unlocks(true, true, true));
            b.Press(LogicalAction.Jump).Step(5).Dir(Dir8.Right).Press(LogicalAction.Attack).Step(15);
            Assert.AreEqual(MotorState.Airborne, b.P.State, "dash finished");
            b.Press(LogicalAction.Jump).Step();
            Assert.IsTrue(b.P.Air.ExtraJumpUsed, "dash -> extra jump allowed");
        }

        [Test, Category("TC_03_07")]
        public void JumpDuringDash_IsRejected()
        {
            var h = new SimHarness("TC-03-07", Fixtures.Unlocks(true, true, true));
            h.Press(LogicalAction.Jump).Step(5).Dir(Dir8.Right).Press(LogicalAction.Attack).Step(2);
            Assert.AreEqual(MotorState.AirDash, h.P.State);
            h.Press(LogicalAction.Jump).Step();
            Assert.IsNotNull(h.LastWithReason("ActionLock:AirDash"));
            Assert.IsFalse(h.P.Air.ExtraJumpUsed, "rejected jump is not consumed");
        }

        [Test, Category("TC_03_07")]
        public void NeutralAirAttack_DoesNotConsumeCounters()
        {
            var h = new SimHarness("TC-03-07", Fixtures.Unlocks(true, true, true));
            h.Press(LogicalAction.Jump).Step(5).Press(LogicalAction.Attack).Step();
            Assert.IsFalse(h.P.Air.ExtraJumpUsed || h.P.Air.DashUsed || h.P.Air.DiveUsed);
        }

        // ------------------------------------------------------------ TC-03-08 (R-03-09)

        [Test, Category("TC_03_08")]
        public void DiveFirst_EndsRemainingJumpAndDash()
        {
            var h = new SimHarness("TC-03-08", Fixtures.Unlocks(true, true, true));
            h.Press(LogicalAction.Jump).Step(10).Dir(Dir8.Right).Press(LogicalAction.SpecialAttack).Step();
            Assert.AreEqual(MotorState.Dive, h.P.State);
            h.Press(LogicalAction.Jump).Step();
            Assert.IsFalse(h.P.Air.ExtraJumpUsed);
            h.Press(LogicalAction.Attack).Step();
            Assert.IsFalse(h.P.Air.DashUsed);
            Assert.AreEqual(2, h.Events("input.reject").Count, "both rejected with a reason");
        }

        // ------------------------------------------------------------ TC-03-09 (R-03-10)

        [Test, Category("TC_03_09")]
        public void Landing_ResetsCounters_AndAllowsImmediateMovement()
        {
            var h = new SimHarness("TC-03-09", Fixtures.Unlocks(true, true, true));
            h.Press(LogicalAction.Jump).Step(5).Press(LogicalAction.Jump).Step(3).Dir(Dir8.Right).Press(LogicalAction.Attack).Step(15);
            h.Dir(Dir8.None);
            h.StepUntilLanded();
            Assert.IsTrue(h.P.Grounded);
            Assert.IsFalse(h.P.Air.ExtraJumpUsed || h.P.Air.DashUsed || h.P.Air.DiveUsed || h.P.Air.DiveFinished);
            Assert.AreEqual(MotorState.Idle, h.P.State);

            h.Dir(Dir8.Right).Step();
            Assert.AreEqual(MotorState.Walk, h.P.State, "moves on the first tick after landing");
            h.Press(LogicalAction.Jump).Step(5).Press(LogicalAction.Jump).Step();
            Assert.IsTrue(h.P.Air.ExtraJumpUsed, "extra jump available again");
        }

        [Test, Category("TC_03_09")]
        public void DiveLanding_ReportsCf02NotApplied()
        {
            var h = new SimHarness("TC-03-09", Fixtures.Unlocks(true, true, true));
            h.Press(LogicalAction.Jump).Step(10).Dir(Dir8.Right).Press(LogicalAction.SpecialAttack);
            h.StepUntilLanded();
            Assert.IsNotNull(h.LastWithReason("DiveLanding(CF-02"));
        }
    }
}
