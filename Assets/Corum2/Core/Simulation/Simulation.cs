using System;

namespace Corum2.Core
{
    /// <summary>Horizontal camera follow inside the map's camera range (R-01-02). Speed/vertical behaviour: U-01.</summary>
    public sealed class CameraModel
    {
        public float X;
        public float Y;

        public void Snap(MapData map, Vec2 target)
        {
            X = MathUtil.Clamp(target.X, map.Camera.MinX, map.Camera.MaxX);
            Y = map.Camera.Y;
        }

        public void Update(MapData map, Vec2 target, float dt)
        {
            var tx = MathUtil.Clamp(target.X, map.Camera.MinX, map.Camera.MaxX);
            var k = map.Camera.FollowSharpness;
            X = k <= 0f ? tx : X + (tx - X) * (float)(1.0 - Math.Exp(-k * dt));
            Y = map.Camera.Y; // vertical follow undecided (U-01) -> fixed per map (EXP-CAM-01)
        }
    }

    /// <summary>
    /// Fixed-step simulation core (appendix 3.1). The Unity host calls Step from FixedUpdate; tests call it directly.
    /// Core never reads Unity time: real time and dt come in with each step.
    /// </summary>
    public sealed class Simulation
    {
        public readonly WorldData World;
        public readonly MovementProfile Movement;
        public readonly InputProfile Input;
        public readonly AirUnlocks Unlocks;
        public readonly DiagLogger Log;
        public readonly PlayerMotor Player;
        public readonly CameraModel Camera = new CameraModel();

        public MapData CurrentMap { get; private set; }
        public long Tick { get; private set; }
        public double GameTime { get; private set; }
        public int TransitionCount { get; private set; }

        bool _edgeBlockedLogged;

        /// <summary>Experimental policies active in this build (reported in logs and the run manifest).</summary>
        public static readonly string[] ExperimentalPolicies =
        {
            "EXP-INPUT-01 SpecialAttack on pad Y (temporary)",
            "EXP-INPUT-02 temporary keyboard mapping",
            "EXP-INPUT-03 8-sector direction, radial deadzone",
            "EXP-TIME-01 double tap measured press-to-press on raw input time",
            "EXP-MOVE-01 all MovementProfile numbers are tuning values (U-04)",
            "EXP-MOVE-02 roll keeps facing; down-diagonals crouch when held without A",
            "EXP-MOVE-03 dash/dive need a horizontal input component",
            "EXP-MOVE-05 slide/roll/dash/dive cannot be cancelled by other movement input",
            "EXP-AIR-01 air uses consumed when the action starts (U-04)",
            "EXP-AIR-02 extra jump/dash/dive allowed after walking off a ledge (U-04)",
            "EXP-MAP-01 map transition resets velocity, run and air uses; player placed grounded at spawn (U-01)",
            "EXP-MAP-02 up/down points need a press of pure Up/Down while grounded and not locked",
            "EXP-MAP-03 edge transition when movement is blocked by a connected edge (push outward)",
            "EXP-CAM-01 camera Y fixed per map, exponential horizontal follow, snap on transition (U-01)",
            "IMPL-PHYS-01 Core AABB kinematic movement instead of Unity physics",
        };

        public Simulation(WorldData world, MovementProfile movement, InputProfile input, AirUnlocks unlocks, DiagLogger log)
        {
            World = world;
            Movement = movement;
            Input = input;
            Unlocks = unlocks;
            Log = log;
            Log.DataVersion = movement.Meta.Tag + ";" + input.Meta.Tag + ";" + unlocks.Meta.Tag + ";world:" + WorldVersion(world);
            Player = new PlayerMotor(movement, input, unlocks, log);
        }

        static string WorldVersion(WorldData w)
        {
            var s = "";
            foreach (var m in w.Maps) s += (s.Length > 0 ? "," : "") + m.Meta.Tag;
            return s;
        }

        public void Start(double realTime)
        {
            Log.RealTime = realTime;
            Log.GameTime = 0;
            Log.Tick = 0;
            Log.Emit("run.start", "", "", "", "", "Start",
                ("experimentalPolicies", string.Join(" | ", ExperimentalPolicies)),
                ("unlocks", "extraJump=" + Unlocks.ExtraJump + ",airDash=" + Unlocks.AirDash + ",dive=" + Unlocks.Dive));
            var map = World.FindMap(World.StartMapId);
            CurrentMap = map;
            Player.PlaceAt(map, map.FindSpawn(World.StartSpawnId), "RunStart");
            Camera.Snap(map, Player.Position);
        }

        public void Step(TickInput input, float dt)
        {
            Tick++;
            GameTime += dt;
            Log.Tick = Tick;
            Log.GameTime = GameTime;
            Log.RealTime = input.RealTime;

            foreach (var e in input.Events)
            {
                var stateBefore = Player.State;
                var outcome = Player.HandleEvent(CurrentMap, e);
                Log.Emit("input", PlayerMotor.Actor, "", stateBefore.ToString(), Player.State.ToString(), outcome,
                    ("device", e.Device), ("control", e.Control), ("action", e.Action), ("edge", e.Edge),
                    ("inputEventId", e.EventId), ("requestTime", e.RawTime), ("deliveredTick", Tick),
                    ("latency", input.RealTime - e.RawTime), ("consumer", "game"), ("heldDir", Player.HeldDir));

                if (e.Edge == InputEdge.Press && (e.Action == LogicalAction.MoveUp || e.Action == LogicalAction.MoveDown))
                    TryPointTransition(e);
            }

            // Reconcile with the sampled direction (they should match; a mismatch means a lost edge).
            var sampledH = Dir8Util.Horizontal(input.Dir);
            var sampledV = Dir8Util.Vertical(input.Dir);
            if (input.Events.Count == 0 && (sampledH != Player.HeldH || sampledV != Player.HeldV) && input.Device != "none")
            {
                Log.Emit("input.desync", PlayerMotor.Actor, "", Player.HeldDir.ToString(), input.Dir.ToString(), "HeldDirectionResynced",
                    ("stick", new Vec2(input.StickX, input.StickY)));
                Player.HeldH = sampledH;
                Player.HeldV = sampledV;
                if (Player.RunDir != 0 && Player.RunDir != sampledH) Player.RunDir = 0;
            }

            var r = Player.Integrate(CurrentMap, dt);

            if (Player.Position.Y < CurrentMap.KillY)
            {
                var spawn = CurrentMap.Spawns[0];
                Log.Emit("map.safeguard", PlayerMotor.Actor, "", CurrentMap.Id, CurrentMap.Id, "FellBelowKillY(test safeguard, not a game rule)",
                    ("position", Player.Position), ("spawnId", spawn.Id));
                Player.PlaceAt(CurrentMap, spawn, "KillYSafeguard");
                Camera.Snap(CurrentMap, Player.Position);
            }
            else if (r.HitLeftBound && CurrentMap.Left.Policy == EdgePolicy.Connected)
            {
                DoTransition(CurrentMap.Left.TargetMapId, CurrentMap.Left.TargetSpawnId, CurrentMap.Left.ConnectionId, "LeftEdge(R-01-03)", -1);
            }
            else if (r.HitRightBound && CurrentMap.Right.Policy == EdgePolicy.Connected)
            {
                DoTransition(CurrentMap.Right.TargetMapId, CurrentMap.Right.TargetSpawnId, CurrentMap.Right.ConnectionId, "RightEdge(R-01-03)", -1);
            }
            else
            {
                var blocked = r.HitLeftBound || r.HitRightBound;
                if (blocked && !_edgeBlockedLogged)
                    Log.Emit("map.edgeBlocked", PlayerMotor.Actor, "", CurrentMap.Id, CurrentMap.Id, "EdgePolicy:Blocked",
                        ("edge", r.HitLeftBound ? "Left" : "Right"), ("position", Player.Position));
                _edgeBlockedLogged = blocked;
                Camera.Update(CurrentMap, Player.Position, dt);
            }

            if (Log.TickSamples)
                Log.Emit("tick", PlayerMotor.Actor, "", "", Player.State.ToString(), "",
                    ("mapId", CurrentMap.Id), ("x", Player.Position.X), ("y", Player.Position.Y), ("vx", Player.VelX), ("vy", Player.VelY),
                    ("grounded", Player.Grounded), ("facing", Player.Facing), ("runDir", Player.RunDir), ("heldDir", Player.HeldDir),
                    ("air", Player.Air.Describe()), ("camX", Camera.X), ("camY", Camera.Y), ("planeZ", CurrentMap.CombatPlaneZ));
        }

        void TryPointTransition(in InputEvent e)
        {
            var wanted = e.Action == LogicalAction.MoveUp ? PointDirection.Up : PointDirection.Down;
            TransitionPoint hit = null;
            var body = Player.Bounds;
            foreach (var p in CurrentMap.Points)
                if (p.Direction == wanted && body.Overlaps(p.Area))
                    hit = p;

            if (hit == null)
            {
                // R-01-04: up/down in an ordinary place never changes the map. Logged for TC-01-04.
                Log.Emit("map.pointCheck", PlayerMotor.Actor, "", CurrentMap.Id, CurrentMap.Id, "NoPointHere",
                    ("inputEventId", e.EventId), ("direction", wanted), ("position", Player.Position));
                return;
            }
            var pure = Player.HeldDir == (wanted == PointDirection.Up ? Dir8.Up : Dir8.Down);
            if (!pure || !Player.CanUseMapPoint)
            {
                Log.Emit("map.pointCheck", PlayerMotor.Actor, "", CurrentMap.Id, CurrentMap.Id,
                    !pure ? "NotPureDirection(EXP-MAP-02)" : "PlayerNotReady(EXP-MAP-02):" + Player.State,
                    ("inputEventId", e.EventId), ("pointId", hit.Id), ("heldDir", Player.HeldDir), ("cf", "CF-01 command overlap not evaluated (magic not implemented)"));
                return;
            }
            DoTransition(hit.TargetMapId, hit.TargetSpawnId, hit.Id, wanted == PointDirection.Up ? "UpPoint(R-01-04)" : "DownPoint(R-01-04)", e.EventId);
        }

        void DoTransition(string mapId, string spawnId, string connectionId, string reason, long inputEventId)
        {
            var from = CurrentMap;
            var fromPos = Player.Position;
            var target = World.FindMap(mapId);
            var spawn = target.FindSpawn(spawnId);
            var stateBefore = Player.State + "," + Player.Air.Describe() + ",runDir=" + Player.RunDir;
            CurrentMap = target;
            TransitionCount++;
            Player.PlaceAt(target, spawn, "MapTransition(EXP-MAP-01)");
            Camera.Snap(target, Player.Position);
            Log.Emit("map.transition", PlayerMotor.Actor, "", from.Id, target.Id, reason,
                ("fromMapId", from.Id), ("toMapId", target.Id), ("connectionId", connectionId), ("spawnId", spawnId),
                ("fromPosition", fromPos), ("toPosition", Player.Position), ("inputEventId", inputEventId),
                ("playerStateBefore", stateBefore), ("statePolicy", "EXP-MAP-01 (U-01)"), ("cameraX", Camera.X), ("cameraSnap", true));
        }
    }
}
