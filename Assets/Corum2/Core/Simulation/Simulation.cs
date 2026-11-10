using System;
using System.Collections.Generic;

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
            "EXP-MOVE-06 roll refused under a ceiling lower than standing height (stays crouched, no movement)",
            "EXP-AIR-01 air uses consumed when the action starts (U-04)",
            "EXP-AIR-02 extra jump/dash/dive allowed after walking off a ledge (U-04)",
            "EXP-MAP-01 map transition resets velocity, run and air uses; player placed grounded at spawn (U-01)",
            "EXP-MAP-02 up/down points need a press of pure Up/Down while grounded and not locked",
            "EXP-MAP-03 edge transition when movement is blocked by a connected edge (push outward)",
            "EXP-CAM-01 camera Y fixed per map, exponential horizontal follow, snap on transition (U-01)",
            "IMPL-PHYS-01 Core AABB kinematic movement instead of Unity physics",
            // WP-02 combat (only reported when combat data is present)
            "TEST-DMG-01 fixed D per attack from the test DamageResolver (not the remake formula, U-09)",
            "EXP-ATK-00 attack timings/hitboxes/knockback/hitstop are tuning values",
            "EXP-ATK-01 facing set at attack start, no turning during a motion (U-05)",
            "EXP-ATK-02 landing ends an air normal attack; chain window opens if it hit",
            "EXP-ATK-03 air normal attack caps fall speed (AirAttackFallSpeed) (U-06)",
            "EXP-ATK-04 attack input refused during slide/roll",
            "EXP-ATK-05 initial dash/dive pick the closest overlapping target (U-05)",
            "EXP-COMBO-01 one attack press during a motion is kept and executed at motion end (buffer length R-05-06 undecided)",
            "EXP-COMBO-02 chain window counts from motion end; ComboWindow editable (R-04-09)",
            "EXP-COMBO-03 after-dash reservation kept until landing; chain window paused while it waits (U-05)",
            "EXP-COMBO-04 R-04-01 max 10 steps: a timed attack past 10 is rejected (chain kept); a dive past 10 without a dash opens a new chain; the dive after a dash may pass 10 only as the CF-03 held branch",
            "EXP-GUARD-01 guard damage floor for g>=1 (U-09)",
            "EXP-GUARD-02 held direction turns facing while guarding, no movement (U-09)",
            "EXP-GUARD-03 RB refused during slide/roll",
            "EXP-GUARD-04 attacker at equal X counts as front (U-09)",
            "EXP-GUARD-05 guard break releases guard without extra stun or damage change (U-09)",
            "EXP-GUARD-07 attack press from guard leaves guard and attacks",
            "EXP-PARRY-01 parry = RB pressed within ParryWindow game seconds before the hit (CF-04)",
            "EXP-PARRY-02 parry damage uses the guard rule (U-09); unblockable attacks are not parried (EXP-PARRY-03)",
            "EXP-DODGE-01 simultaneous = same 50 Hz tick (CF-05); other targets hit in that tick still apply (CF-06)",
            "EXP-HITSTOP-01 hitstop freezes the attacker and the hit targets only (U-06)",
            "EXP-HITSTOP-02 attacker hitstop once per hit event (late targets of the same event do not restart it); dive landing area is its own event (R-06-03, U-06)",
            "EXP-DOWN-01 a launched enemy that lands goes down (CF-10)",
            "EXP-DOWN-02 down-diagonal + special without a downed enemy in range becomes the launcher (CF-10)",
            "EXP-BODY-01 bodies pass through each other (no push)",
            "FIXTURE-ENEMY-01 training dummies attack on a timer when in range (test fixture, not P0-08 AI)",
            // WP-03 ring / items (only active when ring data is present)
            "EXP-RING-01 ring slow motion = Unity Time.timeScale 0.2 while open (spec proposal value); simulation stays 50 Hz game time; ring UI runs at real time (U-06)",
            "EXP-RING-02 8 ring slots per page, one per 8-way stick sector, slot 0 = up, clockwise (U-03)",
            "EXP-RING-03 opening the ring releases held movement for the game; closing re-presses the still-held direction; button releases still reach the game",
            "EXP-RING-04 LT/RT wrap around the ring pages (U-03)",
            "EXP-RING-05 B in registration cancels registration; X or B closes the description; the ring stays (U-03)",
            "EXP-RING-06 being hit keeps the ring open; death closes it (U-03)",
            "EXP-RING-07 discard removes one unit (U-03)",
            "EXP-RING-08 registering onto a used quickslot replaces it; one item may sit in several slots (spec proposal, U-03)",
            "EXP-RING-10 D-pad outside registration is ignored while the ring is open (U-03, TC-UI-12)",
            "EXP-RING-11 a rejected use keeps the ring open with feedback (U-03)",
            "EXP-ITEM-01 an accepted item use runs at the start of the next simulation tick",
            "EXP-ITEM-02 one shared item cooldown after a use, length from the used item, game seconds (U-03)",
            "EXP-ITEM-03 a use requested during an attack, dash or dive waits until that motion ends (CF-07)",
            "EXP-ITEM-04 the item is consumed when the effect runs; gone by then = cancelled, nothing consumed (CF-08)",
            "EXP-ITEM-05 one pending use at a time; more requests are rejected and consume nothing (CF-08)",
            "EXP-ITEM-06 death cancels a pending use without consuming it (CF-08)",
            "EXP-QS-01 a quickslot press during hit reaction is rejected, not reserved (CF-08)",
            "EXP-QS-02 a quickslot whose item runs out is cleared (U-03)",
            "EXP-RING-12 the ring is centred on the player but clamped to stay fully on screen (U-03/U-11 layout); test UI is placeholder",
            "EXP-INPUT-04 temporary keyboard keys for B (E), LT (Z), RT (C)",
            "TEST-ITEM-01 test items, heal amounts and magic numbers are fixtures, not content",
            // WP-04 enemy AI / bosses (only active for enemies with AI data)
            "EXP-AI-01 beyond TrackRange the enemy waits (U-10 tracking range)",
            "EXP-AI-02 walking stops at walls; ledges and blocked paths are not checked (U-10)",
            "EXP-AI-03 fleeing lasts FleeTime, then reapproach or hold per monster (U-10 after-retreat behaviour)",
            "EXP-AI-04 ranged enemies retreat for RetreatTime after each attack, then wait",
            "EXP-AI-05 projectiles: front guard rule applies; no parry and no Dodge (CF-04/CF-06 projectile scope)",
            "EXP-AI-06 projectiles fly straight, vanish after MaxDistance, keep flying after the player dies but cannot hit a dead player (U-10)",
            "EXP-AI-07 no randomness: flee, jump and pattern choices are deterministic",
            "EXP-AI-08 enemies face the player when an attack or move starts and do not turn while attacking",
            "EXP-BOSS-01 pattern choice: distance band and player grounded/airborne filter, highest priority, ties to the least used, then list order",
            "EXP-BOSS-02 death wins over a phase transition (CF-09)",
            "EXP-BOSS-03 no invulnerability during a phase transition; hits react normally; no new attack until it ends (CF-09)",
            "EXP-BOSS-04 transitions are checked when the boss is free to decide; first satisfied in list order; one per check (CF-09)",
            "TEST-AI-01 enemy/boss speeds, ranges, intervals and patterns in the WP-04 scene are test data",
        };

        /// <summary>Null when the run has no combat data (WP-01 scenes).</summary>
        public readonly CombatSystem Combat;
        /// <summary>Null when the run has no ring data (WP-01/02 scenes).</summary>
        public readonly ItemUseSystem Items;
        public readonly RingController Ring;
        public readonly RingInputRouter Router;
        long _syntheticEventId = 1000000000;

        public Simulation(WorldData world, MovementProfile movement, InputProfile input, AirUnlocks unlocks, DiagLogger log,
            CombatProfile combat = null, CombatUnlocks combatUnlocks = null, IEnumerable<EnemyDefinition> enemies = null,
            RingProfile ring = null)
        {
            World = world;
            Movement = movement;
            Input = input;
            Unlocks = unlocks;
            Log = log;
            Log.DataVersion = movement.Meta.Tag + ";" + input.Meta.Tag + ";" + unlocks.Meta.Tag + ";world:" + WorldVersion(world) +
                              (combat != null ? ";" + combat.Meta.Tag + ";" + (combatUnlocks?.Meta.Tag ?? "") : "") +
                              (ring != null ? ";" + ring.Meta.Tag : "");
            Player = new PlayerMotor(movement, input, unlocks, log);
            if (combat != null)
                Combat = new CombatSystem(this, combat, combatUnlocks ?? new CombatUnlocks(), enemies ?? new EnemyDefinition[0]);
            if (ring != null)
            {
                Items = new ItemUseSystem(this, ring);
                Ring = new RingController(ring, Items, log, () => Tick);
                Router = new RingInputRouter(Ring, Items, log, () => Player.State, () => _syntheticEventId++);
            }
        }

        /// <summary>
        /// Routes raw input between ticks (ring / quickslot / game). Without ring data every event goes to the game.
        /// The Unity host calls this from Update; tests call it before Step.
        /// </summary>
        public void RouteInput(in InputEvent e, List<InputEvent> toGame)
        {
            if (Router == null) { toGame.Add(e); return; }
            Router.Handle(e, toGame);
        }

        float _stickX, _stickY;

        /// <summary>
        /// Routes one rendered frame of input (WP03-F02). Stick samples and button/direction events are replayed in the
        /// order they happened (device time, then arrival order), so the ring cursor always reflects the stick at the
        /// moment of each press: "stick to neutral, then A" uses nothing and "A, then stick moves" uses the item that was
        /// selected when A was pressed. When the ring opens, the cursor takes the stick as it is at that moment.
        /// Events must be sorted by (RawTime, EventId) and samples by (Time, Seq).
        /// </summary>
        public void RouteFrame(List<InputEvent> events, List<StickSample> samples, List<InputEvent> toGame, double now)
        {
            var si = 0;
            foreach (var e in events)
            {
                while (si < samples.Count && samples[si].Before(e)) ApplyStick(samples[si++]);
                var wasOpen = Ring != null && Ring.IsOpen;
                RouteInput(e, toGame);
                if (Ring != null && !wasOpen && Ring.IsOpen) Ring.Cursor(_stickX, _stickY, e.RawTime);
            }
            while (si < samples.Count) ApplyStick(samples[si++]);
            UiUpdate(now);
        }

        void ApplyStick(in StickSample s)
        {
            _stickX = s.X;
            _stickY = s.Y;
            if (Ring != null && Ring.IsOpen) Ring.Cursor(s.X, s.Y, s.Time);
        }

        /// <summary>Real-time ring housekeeping between ticks (death closes the ring, EXP-RING-06).</summary>
        public void UiUpdate(double realTime)
        {
            if (Ring != null && Ring.IsOpen && Player.State == MotorState.Dead) Ring.Close("PlayerDead(EXP-RING-06)", realTime);
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
            Combat?.OnMapEntered(map, "RunStart");
        }

        public void Step(TickInput input, float dt)
        {
            Tick++;
            GameTime += dt;
            Log.Tick = Tick;
            Log.GameTime = GameTime;
            Log.RealTime = input.RealTime;

            Items?.BeginTick();

            foreach (var e in input.Events)
            {
                var stateBefore = Player.State;
                var outcome = Combat?.HandleEvent(CurrentMap, e) ?? Player.HandleEvent(CurrentMap, e);
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

            var frozen = Combat != null && Combat.BeginTick(dt);
            var r = frozen ? new MoveResult { Position = Player.Position } : Player.Integrate(CurrentMap, dt);
            Combat?.Step(CurrentMap, dt, frozen);
            if (r.StartedInside)
                Log.Emit("collision.startInside", PlayerMotor.Actor, "", Player.State.ToString(), Player.State.ToString(),
                    "BodyOverlappedSolidBeforeMove(diagnostic)", ("position", Player.Position), ("height", Player.Height));

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
                    ("air", Player.Air.Describe()), ("camX", Camera.X), ("camY", Camera.Y), ("planeZ", CurrentMap.CombatPlaneZ),
                    ("hp", Combat?.PlayerHp ?? -1f), ("chainId", Combat?.ChainId ?? 0), ("combo", Combat?.ComboHits ?? 0),
                    ("enemies", Combat?.DescribeEnemies() ?? ""));
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
            Combat?.OnMapEntered(target, "MapTransition");
            Log.Emit("map.transition", PlayerMotor.Actor, "", from.Id, target.Id, reason,
                ("fromMapId", from.Id), ("toMapId", target.Id), ("connectionId", connectionId), ("spawnId", spawnId),
                ("fromPosition", fromPos), ("toPosition", Player.Position), ("inputEventId", inputEventId),
                ("playerStateBefore", stateBefore), ("statePolicy", "EXP-MAP-01 (U-01)"), ("cameraX", Camera.X), ("cameraSnap", true));
        }
    }
}
