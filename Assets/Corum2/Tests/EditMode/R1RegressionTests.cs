using System;
using System.Collections.Generic;
using Corum2.Core;
using Corum2.Unity;
using NUnit.Framework;
using UnityEngine;

namespace Corum2.Tests
{
    /// <summary>WP-01-R1 regressions for GPT review findings F-01 (low ceiling posture) and F-04 (non-finite data).</summary>
    public class LowCeilingRegressionTests
    {
        /// <summary>GPT repro (EV-GPT-WP01-CORE-01 low_passage_roll): x=57.5 facing right, down+A, 19 ticks.</summary>
        static SimHarness IntoPassage()
        {
            var h = new SimHarness("TC-03-04");
            h.Teleport(57.5f, 1).Step();
            h.Dir(Dir8.Down).Step().Press(LogicalAction.Jump).Step(19);
            Assert.AreEqual(MotorState.Crouch, h.P.State, "kept low under the ceiling");
            Assert.Greater(h.P.Position.X, 60f, "inside the passage");
            Assert.Less(h.P.Position.X, 62.3f);
            return h;
        }

        [Test, Category("TC_03_05"), Category("TC_UNITY_02")]
        public void RollUnderLowCeiling_IsRefused_PositionContinuous([Values(Dir8.DownRight, Dir8.DownLeft)] Dir8 dir)
        {
            var h = IntoPassage();
            var x0 = h.P.Position.X;
            h.Dir(dir).Step().Press(LogicalAction.Jump).Step();
            Assert.AreEqual(MotorState.Crouch, h.P.State);
            Assert.AreEqual(x0, h.P.Position.X, 1e-4f, "no teleport");
            Assert.IsNotNull(h.LastWithReason("CeilingBlocked"));
            Assert.AreEqual(0, h.Events("collision.startInside").Count, "body never inside a solid");
        }

        [Test, Category("TC_03_04")]
        public void ReleasingDownUnderCeiling_StaysCrouched_JumpRefused()
        {
            var h = IntoPassage();
            var x0 = h.P.Position.X;
            h.Dir(Dir8.None).Step(5);
            Assert.AreEqual(MotorState.Crouch, h.P.State);
            h.Press(LogicalAction.Jump).Step();
            Assert.AreEqual(MotorState.Crouch, h.P.State);
            Assert.AreEqual(x0, h.P.Position.X, 1e-4f);
            Assert.IsNotNull(h.LastWithReason("CeilingBlocked"));
        }

        [Test, Category("TC_03_04")]
        public void SlideAgain_ExitsPassage_AndStands()
        {
            var h = IntoPassage();
            h.Press(LogicalAction.Jump).Step(20).Dir(Dir8.None).Step(3);
            Assert.Greater(h.P.Position.X, 62.3f);
            Assert.AreEqual(MotorState.Idle, h.P.State);
            Assert.AreEqual(0, h.Events("collision.startInside").Count);
        }

        [Test, Category("TC_03_05")]
        public void RollRightAtPassageEntrance_IsBlockedNotReversed()
        {
            var h = new SimHarness("TC-03-05");
            h.Teleport(59.0f, -1).Step();
            var x0 = h.P.Position.X;
            h.Dir(Dir8.DownRight).Step().Press(LogicalAction.Jump).Step(25);
            Assert.GreaterOrEqual(h.P.Position.X, x0 - 1e-4f, "never moves against the roll direction");
            Assert.LessOrEqual(h.P.Position.X, 60f - 0.3f + 1e-3f, "stopped by the ceiling block");
        }

        [Test, Category("TC_UNITY_02")]
        public void CollisionMove_StartingInsideSolid_DoesNotPushBackwards()
        {
            var map = new MapData { Id = "c", MinX = 0, MaxX = 100 };
            map.Solids.Add(new Aabb(60, 1.2f, 62, 4));
            var r = Corum2.Core.Collision.Move(map, new Vec2(60.74f, 0f), 0.6f, 1.8f, 0.14f, 0f);
            Assert.IsTrue(r.StartedInside);
            Assert.AreEqual(60.88f, r.Position.X, 1e-4f, "moves with the input, no push-out to the left");
        }
    }

    public class NonFiniteValidationTests
    {
        static List<ValidationIssue> Validate(WorldData w = null, MovementProfile m = null, InputProfile i = null) =>
            DataValidator.Validate(w ?? Fixtures.World(), m ?? Fixtures.Movement(), i ?? Fixtures.Input(), Fixtures.Unlocks(false, false, false));

        static void ExpectIssue(List<ValidationIssue> issues, string field)
        {
            Assert.IsTrue(issues.Exists(x => x.Message.Contains(field)), field + " not reported:\n" + string.Join("\n", issues));
        }

        [Test, Category("TC_UNITY_03")]
        public void ValidFixture_StillPasses() => Assert.IsEmpty(Validate());

        [Test, Category("TC_UNITY_03")]
        public void InputNonFinite_IsRejected([Values(float.NaN, float.PositiveInfinity)] float v)
        {
            var i = Fixtures.Input();
            i.StickDeadzone = v;
            ExpectIssue(Validate(i: i), "StickDeadzone");
            var j = Fixtures.Input();
            j.DoubleTapWindow = v;
            ExpectIssue(Validate(i: j), "DoubleTapWindow");
        }

        [Test, Category("TC_UNITY_03")]
        public void MovementNonFinite_IsRejected([Values(float.NaN, float.PositiveInfinity, float.NegativeInfinity)] float v)
        {
            foreach (var field in new[] { "WalkSpeed", "Gravity", "DiveAngleDeg", "DashEndSpeed", "CrouchHeight" })
            {
                var m = Fixtures.Movement();
                typeof(MovementProfile).GetField(field).SetValue(m, v);
                ExpectIssue(Validate(m: m), field);
            }
        }

        [Test, Category("TC_UNITY_03")]
        public void MapNonFinite_IsRejected()
        {
            var w = Fixtures.World();
            w.Maps[0].KillY = float.NaN;
            w.Maps[0].Camera.Y = float.PositiveInfinity;
            w.Maps[0].Solids[1] = new Aabb(60, float.NaN, 62, 4);
            w.Maps[1].Spawns[0].Position = new Vec2(float.NaN, 0);
            w.Maps[2].CombatPlaneZ = float.NaN;
            var issues = Validate(w);
            ExpectIssue(issues, "KillY");
            ExpectIssue(issues, "Camera.Y");
            ExpectIssue(issues, "Solids[1].MinY");
            ExpectIssue(issues, "Spawn L.X");
            ExpectIssue(issues, "CombatPlaneZ");
        }

        /// <summary>Inspector data path: ScriptableObject -> ToCore -> validator (the same call GameHost.Start makes).</summary>
        [Test, Category("TC_UNITY_03")]
        public void ScriptableObjectPath_RejectsNaN()
        {
            var move = ScriptableObject.CreateInstance<MovementConfig>();
            var input = ScriptableObject.CreateInstance<InputProfileConfig>();
            var unlocks = ScriptableObject.CreateInstance<AirUnlockFixture>();
            try
            {
                move.diveAngleDeg = float.NaN;
                input.doubleTapWindow = float.NaN;
                var issues = DataValidator.Validate(Fixtures.World(), move.ToCore(), input.ToCore(), unlocks.ToCore());
                ExpectIssue(issues, "DiveAngleDeg");
                ExpectIssue(issues, "DoubleTapWindow");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(move);
                UnityEngine.Object.DestroyImmediate(input);
                UnityEngine.Object.DestroyImmediate(unlocks);
            }
        }
    }
}
