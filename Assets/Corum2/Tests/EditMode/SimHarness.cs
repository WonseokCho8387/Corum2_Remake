using System.Collections.Generic;
using Corum2.Core;

namespace Corum2.Tests
{
    /// <summary>
    /// Drives the Core simulation at a fixed 50 Hz (current project fixedDeltaTime 0.02) with timestamped input edges.
    /// Real time advances with game time here because the harness has no slow motion.
    /// </summary>
    public sealed class SimHarness
    {
        public const float Dt = 0.02f;

        public readonly Simulation Sim;
        public readonly MemorySink Sink = new MemorySink();
        public readonly InputTransport Transport = new InputTransport();
        readonly DirectionEdgeTracker _dir = new DirectionEdgeTracker();
        readonly TickInput _tick = new TickInput();
        readonly List<InputEvent> _scratch = new List<InputEvent>();
        readonly List<InputEvent> _routed = new List<InputEvent>();
        readonly List<InputEvent> _preRouted = new List<InputEvent>();
        double _now;

        public SimHarness(string testCaseId, AirUnlocks unlocks = null, WorldData world = null, MovementProfile move = null,
            CombatProfile combat = null, CombatUnlocks combatUnlocks = null, List<EnemyDefinition> enemies = null,
            RingProfile ring = null)
        {
            var log = new DiagLogger(Sink) { RunId = "editmode", TestCaseId = testCaseId, TickSamples = true };
            Sim = new Simulation(world ?? Fixtures.World(), move ?? Fixtures.Movement(), Fixtures.Input(), unlocks ?? Fixtures.Unlocks(false, false, false), log,
                combat, combatUnlocks, enemies, ring);
            Sim.Start(0);
        }

        public PlayerMotor P => Sim.Player;
        public CombatSystem C => Sim.Combat;

        public SimHarness StepUntil(System.Func<bool> cond, int limit = 300)
        {
            for (var i = 0; i < limit && !cond(); i++) Step();
            return this;
        }

        public int Count(string type, string reasonPrefix = null)
        {
            var n = 0;
            foreach (var e in Sink.Events)
                if (e.Type == type && (reasonPrefix == null || e.Reason.StartsWith(reasonPrefix))) n++;
            return n;
        }
        public double Now => _now;

        /// <summary>Sets the held direction at the current time (emits Move* edges).</summary>
        public SimHarness Dir(Dir8 d, double offset = 0)
        {
            _scratch.Clear();
            _dir.Update(d, _now + offset, "test", "dir", Transport.NextEventId, _scratch);
            foreach (var e in _scratch) Transport.Enqueue(e);
            return this;
        }

        public SimHarness Press(LogicalAction a, double offset = 0)
        {
            Transport.Enqueue(new InputEvent(Transport.NextEventId(), a, InputEdge.Press, _now + offset, "test", a.ToString()));
            return this;
        }

        public SimHarness Release(LogicalAction a, double offset = 0)
        {
            Transport.Enqueue(new InputEvent(Transport.NextEventId(), a, InputEdge.Release, _now + offset, "test", a.ToString()));
            return this;
        }

        /// <summary>Ring cursor stick sample at the current time (the Unity host sends one per frame).</summary>
        public SimHarness Cursor(float x, float y)
        {
            Sim.Ring.Cursor(x, y, _now);
            return this;
        }

        /// <summary>Press and release, routed at once (between ticks, like the host's Update).</summary>
        public SimHarness Ui(LogicalAction a)
        {
            Tap(a);
            Transport.Drain(_tick);
            _routed.Clear();
            foreach (var e in _tick.Events) Sim.RouteInput(e, _routed);
            _preRouted.AddRange(_routed);
            return this;
        }

        public SimHarness Tap(LogicalAction a)
        {
            Press(a);
            Release(a, 0.001);
            return this;
        }

        public SimHarness Step(int ticks = 1)
        {
            for (var i = 0; i < ticks; i++)
            {
                _now += Dt;
                Transport.Drain(_tick);
                _tick.RealTime = _now;
                _tick.Dir = _dir.Current;
                if (Sim.Router != null)
                {
                    // Same order as the Unity host: route between ticks, then the game gets what the router forwards.
                    _routed.Clear();
                    foreach (var e in _tick.Events) Sim.RouteInput(e, _routed);
                    _tick.Events.Clear();
                    _tick.Events.AddRange(_preRouted);
                    _tick.Events.AddRange(_routed);
                    _preRouted.Clear();
                    _tick.Dir = Sim.Router.GameDir;
                    Sim.UiUpdate(_now);
                }
                _tick.Device = "test";
                Sim.Step(_tick, Dt);
            }
            return this;
        }

        public SimHarness Seconds(double s) => Step((int)System.Math.Round(s / Dt));

        /// <summary>Steps until the player lands (or the limit), returning max height reached.</summary>
        public float StepUntilLanded(int limit = 500)
        {
            var maxY = P.Position.Y;
            for (var i = 0; i < limit; i++)
            {
                Step();
                if (P.Position.Y > maxY) maxY = P.Position.Y;
                if (P.Grounded) break;
            }
            return maxY;
        }

        public List<DiagEvent> Events(string type) => Sink.OfType(type);

        public DiagEvent LastWithReason(string reasonPrefix)
        {
            for (var i = Sink.Events.Count - 1; i >= 0; i--)
                if (Sink.Events[i].Reason.StartsWith(reasonPrefix)) return Sink.Events[i];
            return null;
        }

        /// <summary>Puts the player at a position on the current map, standing and idle.</summary>
        public SimHarness Teleport(float x, int facing = 1)
        {
            var spawn = new SpawnPoint { Id = "test", Position = new Vec2(x, 0f), Facing = facing };
            P.PlaceAt(Sim.CurrentMap, spawn, "TestTeleport");
            return this;
        }
    }

    public static class Fixtures
    {
        static DataMeta Meta(string id) => new DataMeta { DomainId = id, DataVersion = "test-1", Status = "experimental", RelatedRIds = new[] { "R-03-01" } };

        public static MovementProfile Movement() => new MovementProfile { Meta = Meta("move.test") };
        public static InputProfile Input() => new InputProfile { Meta = Meta("input.test") };

        public static AirUnlocks Unlocks(bool extraJump, bool dash, bool dive) =>
            new AirUnlocks { Meta = Meta("unlock.test"), ExtraJump = extraJump, AirDash = dash, Dive = dive };

        /// <summary>
        /// T1: 0..100, low passage at x 60..62 (gap 1.2 m), up point at x 30..32 -> T3, right edge -> T2, left blocked.
        /// T2: 0..30, left edge -> T1 spawn R, right blocked.
        /// T3: 0..20, down point at x 9..11 -> T1 spawn UpRet.
        /// </summary>
        public static WorldData World()
        {
            var w = new WorldData { StartMapId = "T1", StartSpawnId = "S" };

            var t1 = new MapData { Meta = Meta("map.T1"), Id = "T1", MinX = 0, MaxX = 100 };
            t1.Solids.Add(new Aabb(-1, -1, 101, 0));
            t1.Solids.Add(new Aabb(60, 1.2f, 62, 4));
            t1.Spawns.Add(new SpawnPoint { Id = "S", Position = new Vec2(10, 0), Facing = 1 });
            t1.Spawns.Add(new SpawnPoint { Id = "R", Position = new Vec2(98, 0), Facing = -1 });
            t1.Spawns.Add(new SpawnPoint { Id = "UpRet", Position = new Vec2(31, 0), Facing = 1 });
            t1.Points.Add(new TransitionPoint { Id = "P-UP", Area = new Aabb(30, 0, 32, 2), Direction = PointDirection.Up, TargetMapId = "T3", TargetSpawnId = "B" });
            t1.Left = new EdgeRule { Policy = EdgePolicy.Blocked };
            t1.Right = new EdgeRule { Policy = EdgePolicy.Connected, ConnectionId = "C-T1-T2", TargetMapId = "T2", TargetSpawnId = "L" };
            t1.Camera = new CameraProfile { MinX = 8, MaxX = 92, Y = 2.5f, FollowSharpness = 0 };
            w.Maps.Add(t1);

            var t2 = new MapData { Meta = Meta("map.T2"), Id = "T2", MinX = 0, MaxX = 30 };
            t2.Solids.Add(new Aabb(-1, -1, 31, 0));
            t2.Spawns.Add(new SpawnPoint { Id = "L", Position = new Vec2(2, 0), Facing = 1 });
            t2.Left = new EdgeRule { Policy = EdgePolicy.Connected, ConnectionId = "C-T2-T1", TargetMapId = "T1", TargetSpawnId = "R" };
            t2.Right = new EdgeRule { Policy = EdgePolicy.Blocked };
            t2.Camera = new CameraProfile { MinX = 8, MaxX = 22, Y = 2.5f };
            w.Maps.Add(t2);

            var t3 = new MapData { Meta = Meta("map.T3"), Id = "T3", MinX = 0, MaxX = 20 };
            t3.Solids.Add(new Aabb(-1, -1, 21, 0));
            t3.Spawns.Add(new SpawnPoint { Id = "B", Position = new Vec2(10, 0), Facing = 1 });
            t3.Points.Add(new TransitionPoint { Id = "P-DOWN", Area = new Aabb(9, 0, 11, 2), Direction = PointDirection.Down, TargetMapId = "T1", TargetSpawnId = "UpRet" });
            t3.Left = new EdgeRule { Policy = EdgePolicy.Blocked };
            t3.Right = new EdgeRule { Policy = EdgePolicy.Blocked };
            t3.Camera = new CameraProfile { MinX = 8, MaxX = 12, Y = 2.5f };
            w.Maps.Add(t3);
            return w;
        }
    }
}
