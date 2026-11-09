using System.Collections.Generic;
using Corum2.Core;
using NUnit.Framework;

namespace Corum2.Tests
{
    public class MapTests
    {
        [Test, Category("TC_01_02")]
        public void CameraFollowsHorizontally_InsideCameraRange()
        {
            var h = new SimHarness("TC-01-02");
            Assert.AreEqual(10f, h.Sim.Camera.X, 1e-3f);
            h.Dir(Dir8.Right).Seconds(5);
            Assert.AreEqual("T1", h.Sim.CurrentMap.Id);
            Assert.AreEqual(h.P.Position.X, h.Sim.Camera.X, 1e-3f, "sharpness 0 locks to player");
            Assert.AreEqual(2.5f, h.Sim.Camera.Y, 1e-3f, "camera height fixed (EXP-CAM-01)");

            h.Teleport(2).Step();
            Assert.AreEqual(8f, h.Sim.Camera.X, 1e-3f, "clamped to camera MinX");
        }

        [Test, Category("TC_01_03")]
        public void PushingPastConnectedEdge_SwitchesMap_ToTargetSpawn()
        {
            var h = new SimHarness("TC-01-03");
            h.Teleport(97).Dir(Dir8.Right).Seconds(1);
            Assert.AreEqual("T2", h.Sim.CurrentMap.Id);
            var t = h.Events("map.transition")[0];
            Assert.AreEqual("C-T1-T2", t.Get("connectionId"));
            Assert.AreEqual("L", t.Get("spawnId"));
            Assert.AreEqual(1, h.Sim.TransitionCount, "one immediate switch, no continuous scroll into T2");

            h.Dir(Dir8.None).Step().Dir(Dir8.Left).Seconds(1.5);
            Assert.AreEqual("T1", h.Sim.CurrentMap.Id);
            Assert.AreEqual("R", h.Events("map.transition")[1].Get("spawnId"));
        }

        [Test, Category("TC_01_03")]
        public void BlockedEdge_DoesNotSwitch()
        {
            var h = new SimHarness("TC-01-03");
            h.Teleport(2).Dir(Dir8.Left).Seconds(1);
            Assert.AreEqual("T1", h.Sim.CurrentMap.Id);
            Assert.AreEqual(0, h.Sim.TransitionCount);
            Assert.AreEqual(1, h.Events("map.edgeBlocked").Count);
            Assert.AreEqual(0.3f, h.P.Position.X, 1e-3f);
        }

        [Test, Category("TC_01_04")]
        public void UpDown_OnlyAtPoints()
        {
            var h = new SimHarness("TC-01-04");
            h.Dir(Dir8.Up).Step(2).Dir(Dir8.None).Step();
            Assert.AreEqual("T1", h.Sim.CurrentMap.Id, "ordinary place: no switch");
            Assert.IsNotNull(h.LastWithReason("NoPointHere"));

            h.Dir(Dir8.Down).Step(2).Dir(Dir8.None).Step();
            Assert.AreEqual("T1", h.Sim.CurrentMap.Id);

            h.Teleport(31).Step().Dir(Dir8.Up).Step();
            Assert.AreEqual("T3", h.Sim.CurrentMap.Id, "up point -> T3");
            h.Dir(Dir8.None).Step().Dir(Dir8.Down).Step();
            Assert.AreEqual("T1", h.Sim.CurrentMap.Id, "down point -> T1");
            Assert.AreEqual(31f, h.P.Position.X, 1e-3f, "arrives at spawn UpRet");
        }

        [Test, Category("TC_01_04")]
        public void UpPoint_IgnoresDiagonalAndAirborne()
        {
            var h = new SimHarness("TC-01-04");
            h.Teleport(31).Step().Dir(Dir8.UpRight).Step(2);
            Assert.AreEqual("T1", h.Sim.CurrentMap.Id);
            h.Dir(Dir8.None).Teleport(31).Step().Press(LogicalAction.Jump).Step(3).Dir(Dir8.Up).Step();
            Assert.AreEqual("T1", h.Sim.CurrentMap.Id);
            Assert.IsNotNull(h.LastWithReason("PlayerNotReady"));
        }

        [Test, Category("EXP_MAP_01")]
        public void Transition_ResetsAirUsesAndRun()
        {
            var h = new SimHarness("TC-01-03", Fixtures.Unlocks(true, true, true));
            h.Teleport(98.5f).Dir(Dir8.Right).Step(2).Press(LogicalAction.Jump).Step(2).Press(LogicalAction.Jump).Step(30);
            Assert.AreEqual("T2", h.Sim.CurrentMap.Id);
            Assert.IsFalse(h.P.Air.ExtraJumpUsed);
            Assert.AreEqual(0, h.P.RunDir);
        }
    }

    public class InputTransportTests
    {
        [Test, Category("TC_UNITY_06")]
        public void PressAndReleaseInsideOneFrame_BothReachTheNextTick_Once()
        {
            var t = new InputTransport();
            t.Enqueue(new InputEvent(t.NextEventId(), LogicalAction.Jump, InputEdge.Press, 1.000, "kb", "space"));
            t.Enqueue(new InputEvent(t.NextEventId(), LogicalAction.Jump, InputEdge.Release, 1.004, "kb", "space"));
            var tick = new TickInput();
            t.Drain(tick);
            Assert.AreEqual(2, tick.Events.Count);
            Assert.AreEqual(InputEdge.Press, tick.Events[0].Edge);

            t.Drain(tick);
            Assert.AreEqual(0, tick.Events.Count, "second tick in the same frame gets nothing new");
        }

        [Test, Category("TC_UNITY_06")]
        public void DuplicateEnqueue_IsDropped()
        {
            var t = new InputTransport();
            var e = new InputEvent(t.NextEventId(), LogicalAction.Attack, InputEdge.Press, 2.0, "pad", "buttonWest");
            t.Enqueue(e);
            var tick = new TickInput();
            t.Drain(tick);
            t.Enqueue(e);
            t.Drain(tick);
            Assert.AreEqual(0, tick.Events.Count);
            Assert.AreEqual(1, t.DuplicatesDropped);
        }

        [Test, Category("TC_UNITY_06")]
        public void EventsAreOrderedByRawTime()
        {
            var t = new InputTransport();
            var late = t.NextEventId();
            var early = t.NextEventId();
            t.Enqueue(new InputEvent(late, LogicalAction.Jump, InputEdge.Press, 5.2, "pad", "a"));
            t.Enqueue(new InputEvent(early, LogicalAction.MoveRight, InputEdge.Press, 5.1, "pad", "stick"));
            var tick = new TickInput();
            t.Drain(tick);
            Assert.AreEqual(LogicalAction.MoveRight, tick.Events[0].Action);
        }

        [Test]
        public void StickSectors()
        {
            Assert.AreEqual(Dir8.None, Dir8Util.FromStick(0.2f, 0.1f, 0.35f));
            Assert.AreEqual(Dir8.Right, Dir8Util.FromStick(1f, 0.3f, 0.35f));
            Assert.AreEqual(Dir8.DownRight, Dir8Util.FromStick(0.7f, -0.7f, 0.35f));
            Assert.AreEqual(Dir8.Down, Dir8Util.FromStick(0.1f, -0.9f, 0.35f));
            Assert.AreEqual(Dir8.UpLeft, Dir8Util.FromStick(-0.7f, 0.7f, 0.35f));
        }

        [Test]
        public void DirectionTracker_EmitsEdgesPerComponent()
        {
            var tr = new DirectionEdgeTracker();
            var list = new List<InputEvent>();
            long id = 0;
            tr.Update(Dir8.Right, 0, "kb", "d", () => ++id, list);
            tr.Update(Dir8.DownRight, 0.1, "kb", "s", () => ++id, list);
            tr.Update(Dir8.Down, 0.2, "kb", "d", () => ++id, list);
            Assert.AreEqual(3, list.Count, "Right press, Down press, Right release");
            Assert.AreEqual(LogicalAction.MoveRight, list[0].Action);
            Assert.AreEqual(LogicalAction.MoveDown, list[1].Action);
            Assert.AreEqual(LogicalAction.MoveRight, list[2].Action);
            Assert.AreEqual(InputEdge.Release, list[2].Edge);
            Assert.AreEqual(Dir8.Down, tr.Current);
        }
    }

    public class DataValidationTests
    {
        static List<ValidationIssue> Validate(WorldData w, MovementProfile m = null) =>
            DataValidator.Validate(w, m ?? Fixtures.Movement(), Fixtures.Input(), Fixtures.Unlocks(false, false, false));

        [Test, Category("TC_UNITY_03")]
        public void Fixture_IsValid()
        {
            var issues = Validate(Fixtures.World());
            Assert.IsEmpty(issues, string.Join("\n", issues));
        }

        [Test, Category("TC_UNITY_03")]
        public void MissingTargets_AreReported_NotDefaulted()
        {
            var w = Fixtures.World();
            w.Maps[0].Right.TargetMapId = "NOPE";
            w.Maps[1].Left.ConnectionId = "";
            w.Maps[2].Points[0].TargetSpawnId = "nowhere";
            var issues = Validate(w);
            Assert.AreEqual(3, issues.Count, string.Join("\n", issues));
        }

        [Test, Category("TC_UNITY_03")]
        public void WalkNotSlowerThanRun_IsReported()
        {
            var m = Fixtures.Movement();
            m.WalkSpeed = m.RunSpeed;
            Assert.IsTrue(Validate(Fixtures.World(), m).Exists(i => i.Message.Contains("WalkSpeed")));
        }

        [Test, Category("TC_UNITY_03")]
        public void SpawnInsideSolid_IsReported()
        {
            var w = Fixtures.World();
            w.Maps[0].Spawns.Add(new SpawnPoint { Id = "bad", Position = new Vec2(61, 1.5f), Facing = 1 });
            Assert.IsTrue(Validate(w).Exists(i => i.Message.Contains("overlaps")));
        }

        [Test]
        public void JsonLine_IsEscapedAndInvariant()
        {
            var e = new DiagEvent { Type = "t", Reason = "a\"b" };
            e.Fields.Add(new KeyValuePair<string, object>("v", 1.5f));
            var s = DiagJson.Serialize(e);
            StringAssert.Contains("\"reason\":\"a\\\"b\"", s);
            StringAssert.Contains("\"v\":1.5", s);
        }
    }
}
