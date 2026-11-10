using System;
using System.Collections.Generic;
using System.Text;

namespace Corum2.Core
{
    public enum EnemyState
    {
        Idle,
        AttackStartup,
        AttackActive,
        AttackRecovery,
        Stagger,
        Launched,
        Down,
        Wakeup,
        DodgeReact,
        Dead,
        Gone,
        /// <summary>Boss phase transition in progress (R-08-11 transition presentation; CF-09 details EXP-BOSS-03).</summary>
        PhaseTransition,
        /// <summary>Boss guard stance (R-08-11 guard frequency, EXP-BOSS-05/06).</summary>
        Guard,
        /// <summary>Boss back step (R-08-11 evade frequency, EXP-BOSS-05/07).</summary>
        Evade,
    }

    /// <summary>Timed AI action that runs while the enemy is otherwise free (WP-04).</summary>
    public enum AiAction
    {
        None,
        Flee,
        Retreat,
        Jump,
        /// <summary>Fled and configured not to reapproach (R-08-04).</summary>
        Hold,
    }

    /// <summary>Training dummy (test fixture). Its attack timer is NOT the P0-08 AI.</summary>
    public sealed class EnemyActor
    {
        public readonly EnemyDefinition Def;
        public readonly string Id;
        public Vec2 Position;
        public float VelX;
        public float VelY;
        public int Facing;
        public float Hp;
        public EnemyState State = EnemyState.Idle;
        public float Timer;
        public float Cooldown;
        public float Hitstop;
        public float JuggleTimer;
        public bool Grounded = true;
        public long AttackInstanceId;
        public bool AttackSpent;
        public bool ArmorBrokenThisAttack;
        public bool Vanishing;
        internal bool InvulnLogged;

        // WP-04 AI runtime
        public readonly EnemyAIProfile Ai;
        public readonly BossDefinition Boss;
        public AttackDefinition CurAttack;
        public ProjectileDefinition CurProjectile;
        public string CurPattern = "";
        public string StartReason = "";
        /// <summary>Walking / fleeing / jump speed, separate from knockback velocity (which decays).</summary>
        public float MoveVelX;
        public AiAction Action;
        public float ActionTimer;
        public float JumpTimer;
        public bool AirAttackUsed;
        public string LastDecision = "";
        public string LastBlocked = "";
        public float AttackRange;
        // values a boss phase may override (R-08-11); copied from the definition otherwise
        public float StaggerResistance;
        public LaunchResponse Launch;
        public float AttackSpeed = 1f;
        public bool PhaseSuperArmor;
        public BossPhase Phase;
        public string PhaseId = "";
        public readonly Dictionary<string, int> PatternUses = new Dictionary<string, int>();
        public readonly HashSet<string> BossEvents = new HashSet<string>();
        /// <summary>Remaining phase transition time, separate from the reaction Timer (WP04-F03, EXP-BOSS-03). 0 = none.</summary>
        public float TransitionTimer;
        /// <summary>Last player attack instance this boss has seen start (guard / evade trigger, EXP-BOSS-05).</summary>
        public long SeenPlayerAttackId;
        public bool NewPlayerAttack;
        public float GuardAcc;
        public float EvadeAcc;

        public EnemyActor(EnemyDefinition def, string id, Vec2 pos, int facing)
        {
            Def = def;
            Id = id;
            Position = pos;
            Facing = facing;
            Hp = def.MaxHp;
            Cooldown = def.FirstAttackDelay;
            Ai = def.Ai != null && def.Ai.Type != AiType.Fixture ? def.Ai : null;
            Boss = Ai != null && Ai.Type == AiType.Boss ? def.Boss : null;
            CurAttack = def.Attack;
            AttackRange = def.AttackRange;
            StaggerResistance = def.StaggerResistance;
            Launch = def.Launch;
        }

        public Aabb Body => Aabb.FromFeet(Position, Def.BodyWidth, Def.BodyHeight);
        public bool Targetable => State != EnemyState.Dead && State != EnemyState.Gone;
        public bool InAttack => State == EnemyState.AttackStartup || State == EnemyState.AttackActive || State == EnemyState.AttackRecovery;
    }

    public sealed class PlayerAttack
    {
        public AttackDefinition Def;
        public long InstanceId;
        public float Elapsed;
        public int ChainStep;
        public int Hits;
        public readonly List<HashSet<string>> HitByEvent = new List<HashSet<string>>();
        public readonly HashSet<string> SkipLogged = new HashSet<string>();
        /// <summary>Hit events of this attack whose attacker hitstop was already applied (R-06-03, WP02-F02).
        /// Key = hit event index; -1 = dive landing area.</summary>
        public readonly HashSet<int> HitstopEvents = new HashSet<int>();
        public bool MotorDriven => Def.Kind == AttackKind.AirDash || Def.Kind == AttackKind.Dive;
    }

    /// <summary>
    /// P0-04~07 combat core for WP-02: attack chain and combo, cancels, guard/parry, hit reactions, simultaneous-hit
    /// Dodge, enemy death. Undecided rules carry EXP-* ids; nothing here approves them.
    /// Order per tick: hitstop -> (motor) -> motor-driven attacks -> attack timeline -> chain window/reservation ->
    /// player locks/guard -> enemies -> collect hit candidates -> resolve (Dodge pairs first) -> apply.
    /// </summary>
    public sealed partial class CombatSystem
    {
        readonly Simulation _sim;
        readonly CombatProfile _p;
        public readonly CombatUnlocks Unlocks;
        readonly Dictionary<string, EnemyDefinition> _defs = new Dictionary<string, EnemyDefinition>();
        public readonly List<EnemyActor> Enemies = new List<EnemyActor>();

        public float PlayerHp;
        public float PlayerMaxHp => _p.PlayerMaxHp;
        public PlayerAttack Attack { get; private set; }

        public long ChainId { get; private set; }
        public bool ChainAlive { get; private set; }
        public int ChainSteps { get; private set; }
        public int GroundSteps { get; private set; }
        public int AirSteps { get; private set; }
        /// <summary>R-04-01 standard chain maximum (4+1+4+1). Enforced on every non-dash path (WP02-F03, EXP-COMBO-04).</summary>
        public const int StandardChainMax = 10;
        /// <summary>True once an air dash joined the current chain: the CF-03 branch (held, not decided).</summary>
        public bool ChainHasDash { get; private set; }
        public int ComboHits { get; private set; }
        public float ChainWindow { get; private set; }
        public string LastChainEndReason { get; private set; } = "";
        public int LastComboHits { get; private set; }
        readonly HashSet<string> _endedLinks = new HashSet<string>();

        bool _buffered;
        LogicalAction _bufferedAction;
        long _bufferedEventId;
        double _bufferedPressTime;
        bool _landingReservation;
        long _reservationEventId;
        double _reservationGameTime;

        public bool GuardHeld { get; private set; }
        double _guardPressGameTime = -999;
        float _playerLockTimer;
        public float PlayerHitstop { get; private set; }
        long _nextAttackId = 1;
        long _nextChainId = 1;
        readonly HashSet<long> _invulnLogged = new HashSet<long>();

        public CombatSystem(Simulation sim, CombatProfile profile, CombatUnlocks unlocks, IEnumerable<EnemyDefinition> defs)
        {
            _sim = sim;
            _p = profile;
            Unlocks = unlocks;
            foreach (var d in defs) _defs[d.Id] = d;
            PlayerHp = profile.PlayerMaxHp;
        }

        /// <summary>Float tolerance for fixed-step timers (0.02 s steps accumulate rounding).</summary>
        const float TimerEps = 1e-4f;

        public CombatProfile Profile => _p;
        DiagLogger Log => _sim.Log;
        PlayerMotor P => _sim.Player;
        const string Pl = PlayerMotor.Actor;

        // ================================================================ map / spawn

        public void OnMapEntered(MapData map, string reason)
        {
            if (Attack != null) CancelAttack("MapTransition");
            if (ChainAlive) EndChain("MapTransition");
            _buffered = false;
            _landingReservation = false;
            Enemies.Clear();
            Projectiles.Clear();
            foreach (var s in map.Enemies)
            {
                if (!_defs.TryGetValue(s.EnemyId, out var def)) continue;
                var e = new EnemyActor(def, s.InstanceId, Collision.SnapToGround(map, s.Position, def.BodyWidth), s.Facing);
                Enemies.Add(e);
                Log.Emit("enemy.spawn", e.Id, "", "", e.State.ToString(), reason, ("enemyId", def.Id), ("position", e.Position), ("hp", e.Hp),
                    ("fixture", e.Ai != null ? "AI:" + e.Ai.Type + " (P0-08)" : def.Attack == null ? "punching bag" : "timed attack (test fixture, not P0-08 AI)"));
                if (e.Boss != null) EnterPhase(e, e.Boss.Find(e.Boss.StartPhase), "StartPhase(R-08-09)", "start");
            }
        }

        public EnemyActor Find(string id)
        {
            foreach (var e in Enemies)
                if (e.Id == id) return e;
            return null;
        }

        // ================================================================ input

        /// <summary>Returns an outcome when combat consumed the event; null lets the motor handle it.</summary>
        public string HandleEvent(MapData map, in InputEvent e)
        {
            switch (e.Action)
            {
                case LogicalAction.Guard:
                    return e.Edge == InputEdge.Press ? GuardPress(e) : GuardRelease();
                case LogicalAction.Attack:
                case LogicalAction.SpecialAttack:
                    return e.Edge == InputEdge.Press ? AttackPress(map, e) : null;
                default:
                    return null;
            }
        }

        string GuardPress(in InputEvent e)
        {
            GuardHeld = true;
            _guardPressGameTime = _sim.GameTime;
            var s = P.State;
            if (s == MotorState.HitStun || s == MotorState.DodgeReact || s == MotorState.Dead)
                return Reject(e, "ActionLock:" + s, "R-05-04 / R-07-07: no guard before the reaction ends");
            if (s == MotorState.AirDash || s == MotorState.Dive)
                return Reject(e, "ActionLock:" + s, "R-05-03: dash/dive cannot be cancelled by RB");
            if (s == MotorState.Slide || s == MotorState.Roll)
                return Reject(e, "ActionLock:" + s, "EXP-GUARD-03 (not in the P0-05 table)");
            if (s == MotorState.GroundAttack || s == MotorState.AirAttack)
            {
                // R-05-02: ground normal / launcher / air normal cancel to guard at any time; hitbox removed, chain ends.
                CancelAttack("GuardCancel");
                EndChain("GuardCancel");
            }
            P.EnterLocked(MotorState.Guard, "GuardPress");
            if (P.Grounded) P.VelX = 0f;
            return "guard";
        }

        string GuardRelease()
        {
            GuardHeld = false;
            if (P.State == MotorState.Guard) P.ReleaseLocked("GuardReleased");
            return "guardRelease";
        }

        string AttackPress(MapData map, in InputEvent e)
        {
            var special = e.Action == LogicalAction.SpecialAttack;
            var s = P.State;
            var dir = P.HeldDir;
            var h = Dir8Util.Horizontal(dir);

            if (s == MotorState.HitStun || s == MotorState.DodgeReact || s == MotorState.Dead)
                return Reject(e, "ActionLock:" + s, "R-05-04");
            if (s == MotorState.Slide || s == MotorState.Roll)
                return Reject(e, "ActionLock:" + s, "EXP-ATK-04");
            if (s == MotorState.Guard)
            {
                // R-07-05: after a parry the player may attack at once. Attack from guard is allowed (EXP-GUARD-07).
                P.ReleaseLocked("GuardToAttack");
                s = P.State;
            }
            if (s == MotorState.Dive) return Reject(e, "ActionLock:Dive", "R-05-03 / R-03-09");
            if (s == MotorState.AirDash)
                return special ? Reject(e, "ActionLock:AirDash", "EXP-MOVE-05") : ReserveLanding(e);
            if (s == MotorState.GroundAttack || s == MotorState.AirAttack)
            {
                if (_buffered)
                    return Reject(e, "BufferFull", "EXP-COMBO-01 keeps one pending press");
                _buffered = true;
                _bufferedAction = e.Action;
                _bufferedEventId = e.EventId;
                _bufferedPressTime = e.RawTime;
                Log.Emit("attack.buffered", Pl, "", s.ToString(), s.ToString(), "PressDuringMotion(EXP-COMBO-01)",
                    ("inputEventId", e.EventId), ("action", e.Action), ("pressTime", e.RawTime), ("attackInstanceId", Attack?.InstanceId ?? 0));
                return "buffered";
            }
            if (s == MotorState.Airborne)
            {
                if (h != 0) return null; // motor: direction+X dash, direction+Y dive (R-03-06)
                if (special) return null; // motor logs: neutral air special undefined (U-02)
                if (P.Air.DashUsed) return ReserveLanding(e); // R-04-07: no air normal after a dash
                return StartTimed(AttackKind.AirNormal, e);
            }
            // ground states
            if (special)
            {
                if ((dir == Dir8.DownLeft || dir == Dir8.DownRight) && FindDowned(h) != null)
                    return StartTimed(AttackKind.DownAttack, e);
                if (dir == Dir8.DownLeft || dir == Dir8.DownRight)
                    Log.Emit("attack.note", Pl, "", s.ToString(), s.ToString(), "NoDownedTarget->Launcher(EXP-DOWN-02, CF-10)", ("inputEventId", e.EventId));
                return StartTimed(AttackKind.Launcher, e);
            }
            return StartTimed(AttackKind.GroundNormal, e);
        }

        string ReserveLanding(in InputEvent e)
        {
            if (_landingReservation) return Reject(e, "ReservationExists", "R-04-07");
            _landingReservation = true;
            _reservationEventId = e.EventId;
            _reservationGameTime = _sim.GameTime;
            Log.Emit("attack.reserved", Pl, "", P.State.ToString(), P.State.ToString(), "AfterDash->GroundAttackOnLanding(R-04-07)",
                ("inputEventId", e.EventId), ("pressTime", e.RawTime), ("keep", "until landing (EXP-COMBO-03, hold time U-05)"));
            return "reservedForLanding";
        }

        EnemyActor FindDowned(int h)
        {
            EnemyActor best = null;
            var bestD = float.MaxValue;
            foreach (var en in Enemies)
            {
                if (en.State != EnemyState.Down) continue;
                var dx = en.Position.X - P.Position.X;
                if (Math.Sign(dx) != h || Math.Abs(dx) > _p.DownAttackRange) continue;
                if (Math.Abs(dx) < bestD)
                {
                    bestD = Math.Abs(dx);
                    best = en;
                }
            }
            return best;
        }

        string StartTimed(AttackKind kind, in InputEvent e)
        {
            AttackDefinition def;
            var newChain = !ChainAlive;
            if (!newChain && ChainSteps >= StandardChainMax)
                return Reject(e, "ChainMax:Total" + StandardChainMax,
                    "R-04-01 standard max " + StandardChainMax + "; extra press rejected, chain kept until its window ends (EXP-COMBO-04)" +
                    (ChainHasDash ? "; dash branch CF-03 held: only the dive after the dash may pass " + StandardChainMax : ""));
            if (newChain) BeginChain(e.EventId);
            switch (kind)
            {
                case AttackKind.GroundNormal:
                    if (GroundSteps >= _p.GroundNormals.Length)
                        return Reject(e, "ChainMax:Ground" + _p.GroundNormals.Length, "R-04-01");
                    def = _p.GroundNormals[GroundSteps];
                    GroundSteps++;
                    break;
                case AttackKind.AirNormal:
                    if (AirSteps >= _p.AirNormals.Length)
                        return Reject(e, "ChainMax:Air" + _p.AirNormals.Length, "R-04-01");
                    def = _p.AirNormals[AirSteps];
                    AirSteps++;
                    break;
                case AttackKind.Launcher:
                    def = _p.Launcher;
                    break;
                default:
                    def = _p.DownAttack;
                    break;
            }
            ChainSteps++;
            var h = Dir8Util.Horizontal(P.HeldDir);
            if (kind == AttackKind.DownAttack) P.Facing = h;
            else if (h != 0) P.Facing = h; // EXP-ATK-01 facing set at attack start, no turn during the motion (U-05)
            Attack = NewAttack(def);
            P.EnterLocked(kind == AttackKind.AirNormal ? MotorState.AirAttack : MotorState.GroundAttack, "Attack:" + kind);
            if (kind != AttackKind.AirNormal) P.VelX = 0f;
            P.LockedMaxFall = kind == AttackKind.AirNormal ? _p.AirAttackFallSpeed : float.MaxValue;
            Log.Emit("attack.start", Pl, "", "", P.State.ToString(), "Attack:" + kind,
                ("attackInstanceId", Attack.InstanceId), ("attackId", def.Id), ("chainId", ChainId), ("attackChainStep", ChainSteps),
                ("groundSteps", GroundSteps), ("airSteps", AirSteps), ("newChain", newChain), ("inputEventId", e.EventId),
                ("pressTime", e.RawTime), ("startGameTime", _sim.GameTime), ("comboHits", ComboHits));
            return "attack:" + kind;
        }

        PlayerAttack NewAttack(AttackDefinition def)
        {
            var a = new PlayerAttack { Def = def, InstanceId = _nextAttackId++, ChainStep = ChainSteps };
            for (var i = 0; i < Math.Max(1, def.HitEvents); i++) a.HitByEvent.Add(new HashSet<string>());
            return a;
        }

        void BeginChain(long eventId)
        {
            ChainId = _nextChainId++;
            ChainAlive = true;
            ChainSteps = 0;
            ChainHasDash = false;
            GroundSteps = 0;
            AirSteps = 0;
            ComboHits = 0;
            ChainWindow = 0f;
            _endedLinks.Clear();
            Log.Emit("combo.begin", Pl, "", "", "", "NewChain", ("chainId", ChainId), ("inputEventId", eventId));
        }

        void EndChain(string reason)
        {
            if (!ChainAlive) return;
            ChainAlive = false;
            LastChainEndReason = reason;
            LastComboHits = ComboHits;
            Log.Emit("combo.end", Pl, "", "", "", reason, ("chainId", ChainId), ("attackChainStep", ChainSteps), ("comboHits", ComboHits),
                ("groundSteps", GroundSteps), ("airSteps", AirSteps));
            ComboHits = 0;
        }

        void CancelAttack(string reason)
        {
            var a = Attack;
            if (a == null) return;
            Attack = null;
            Log.Emit("attack.end", Pl, "", "", P.State.ToString(), reason, ("attackInstanceId", a.InstanceId), ("attackId", a.Def.Id),
                ("elapsed", a.Elapsed), ("hits", a.Hits), ("hitboxRemoved", true));
            _buffered = false;
        }

        void EndAttack(MapData map, string reason)
        {
            var a = Attack;
            Attack = null;
            Log.Emit("attack.end", Pl, "", "", P.State.ToString(), reason, ("attackInstanceId", a.InstanceId), ("attackId", a.Def.Id),
                ("elapsed", a.Elapsed), ("hits", a.Hits));
            if (P.State == MotorState.GroundAttack || P.State == MotorState.AirAttack) P.ReleaseLocked("AttackEnd");
            if (ChainAlive)
            {
                if (a.Hits > 0)
                {
                    ChainWindow = _p.ComboWindow;
                    Log.Emit("combo.window", Pl, "", "", "", "WindowOpen(R-04-09)", ("chainId", ChainId), ("window", ChainWindow));
                }
                else EndChain("Miss");
            }
            if (_buffered)
            {
                _buffered = false;
                var ev = new InputEvent(_bufferedEventId, _bufferedAction, InputEdge.Press, _bufferedPressTime, "buffer", "buffered");
                var outcome = HandleEvent(map, ev) ?? P.HandleEvent(map, ev);
                Log.Emit("attack.bufferExecuted", Pl, "", "", P.State.ToString(), outcome, ("inputEventId", _bufferedEventId),
                    ("pressTime", _bufferedPressTime), ("execGameTime", _sim.GameTime), ("chainAlive", ChainAlive));
            }
        }

        // ================================================================ tick

        /// <summary>Called before the motor integrates. Returns true while the player is frozen by hitstop.</summary>
        public bool BeginTick(float dt)
        {
            if (PlayerHitstop <= 0f) return false;
            PlayerHitstop -= dt;
            return true;
        }

        public void Step(MapData map, float dt, bool playerFrozen)
        {
            SyncMotorAttacks(map);

            if (!playerFrozen && Attack != null && !Attack.MotorDriven)
            {
                Attack.Elapsed += dt;
                if (Attack.Def.Kind == AttackKind.AirNormal && P.Grounded) EndAttack(map, "LandedDuringAirAttack(EXP-ATK-02)");
                else if (Attack.Elapsed >= Attack.Def.Total - TimerEps) EndAttack(map, "MotionEnd");
            }

            if (_landingReservation && P.Grounded && (P.State == MotorState.Idle || P.State == MotorState.Walk || P.State == MotorState.Run || P.State == MotorState.Crouch))
            {
                _landingReservation = false;
                var ev = new InputEvent(_reservationEventId, LogicalAction.Attack, InputEdge.Press, 0, "reservation", "afterDash");
                var outcome = StartTimed(AttackKind.GroundNormal, ev);
                Log.Emit("attack.reservationExecuted", Pl, "", "", P.State.ToString(), outcome, ("inputEventId", _reservationEventId),
                    ("reservedGameTime", _reservationGameTime), ("execGameTime", _sim.GameTime), ("ruleIds", "R-04-07, R-05-06"));
            }
            else if (!playerFrozen && ChainAlive && Attack == null && !_landingReservation)
            {
                ChainWindow -= dt;
                if (ChainWindow <= 0f) EndChain("Timeout");
            }

            if (!playerFrozen && (P.State == MotorState.HitStun || P.State == MotorState.DodgeReact))
            {
                _playerLockTimer -= dt;
                if (_playerLockTimer <= 0f) P.ReleaseLocked(P.State + "End");
            }
            if (P.State == MotorState.Guard)
            {
                if (!GuardHeld) P.ReleaseLocked("GuardReleased");
                else if (P.HeldH != 0 && P.HeldH != P.Facing)
                {
                    P.Facing = P.HeldH; // EXP-GUARD-02 turn while guarding (U-09)
                    Log.Emit("guard.turn", Pl, "", "Guard", "Guard", "TurnWhileGuarding(EXP-GUARD-02)", ("facing", P.Facing));
                }
            }

            foreach (var en in Enemies) UpdateEnemy(map, en, dt);
            StepProjectiles(map, dt);
            ResolveHits(map, playerFrozen);
        }

        void SyncMotorAttacks(MapData map)
        {
            if (Attack != null && Attack.MotorDriven)
            {
                var stillIn = (Attack.Def.Kind == AttackKind.AirDash && P.State == MotorState.AirDash) ||
                              (Attack.Def.Kind == AttackKind.Dive && P.State == MotorState.Dive);
                if (!stillIn)
                {
                    if (Attack.Def.Kind == AttackKind.Dive && P.LandedThisTick && Unlocks.DiveArea) DiveAreaHit();
                    EndAttack(map, Attack.Def.Kind + "End");
                }
            }
            if (Attack == null && (P.State == MotorState.AirDash || P.State == MotorState.Dive))
            {
                var kind = P.State == MotorState.AirDash ? AttackKind.AirDash : AttackKind.Dive;
                var cf03 = "";
                if (ChainAlive && ChainSteps >= StandardChainMax)
                {
                    if (kind == AttackKind.Dive && ChainHasDash)
                        cf03 = "CF-03 held: dive after a dash counted past " + StandardChainMax + " (not approved)";
                    else
                        EndChain("ChainMax:Total" + StandardChainMax + "(R-04-01, EXP-COMBO-04)"); // movement cannot be rejected; new chain
                }
                if (!ChainAlive) BeginChain(0);
                if (kind == AttackKind.AirDash) ChainHasDash = true;
                ChainSteps++;
                Attack = NewAttack(kind == AttackKind.AirDash ? _p.AirDash : _p.Dive);
                Log.Emit("attack.start", Pl, "", "", P.State.ToString(), "Attack:" + kind, ("attackInstanceId", Attack.InstanceId),
                    ("attackId", Attack.Def.Id), ("chainId", ChainId), ("attackChainStep", ChainSteps), ("startGameTime", _sim.GameTime),
                    ("upgrade", kind == AttackKind.AirDash ? "pierce=" + Unlocks.DashPierce : "area=" + Unlocks.DiveArea),
                    ("chainHasDash", ChainHasDash),
                    ("cf", cf03 != "" ? cf03 : "CF-03: step counting of the dash branch is undecided"));
            }
        }

        // ================================================================ enemies

        void UpdateEnemy(MapData map, EnemyActor en, float dt)
        {
            if (en.State == EnemyState.Gone) return;
            if (en.State == EnemyState.Dead)
            {
                en.Timer -= dt;
                if (en.Timer > 0f) return;
                if (!en.Vanishing)
                {
                    en.Vanishing = true;
                    en.Timer = en.Def.VanishTime;
                    Log.Emit("enemy.vanish", en.Id, "", "Dead", "Dead", "DeathMotionDone->VanishEffect(R-06-17)");
                }
                else
                {
                    en.State = EnemyState.Gone;
                    Log.Emit("enemy.removed", en.Id, "", "Dead", "Gone", "Removed(R-06-17)");
                }
                return;
            }
            // WP04-M02-B: a player attack start is observed on its start tick, also during hitstop, so a busy boss never
            // answers it later (EXP-BOSS-05).
            if (en.Boss != null && Attack != null && Attack.InstanceId != en.SeenPlayerAttackId)
            {
                en.SeenPlayerAttackId = Attack.InstanceId;
                en.NewPlayerAttack = true;
            }
            if (en.Hitstop > 0f)
            {
                en.Hitstop -= dt;
                if (en.NewPlayerAttack) DefenseSkip(en, "Busy:Hitstop");
                return;
            }

            var before = en.State;
            if (en.TransitionTimer > 0f) StepTransition(en, dt);
            switch (en.State)
            {
                case EnemyState.Idle:
                    if (en.Ai != null)
                    {
                        AiIdle(map, en, dt);
                        break;
                    }
                    if (en.Def.Attack != null && P.State != MotorState.Dead)
                    {
                        en.Cooldown -= dt;
                        var dx = P.Position.X - en.Position.X;
                        if (en.Cooldown <= 0f && Math.Abs(dx) <= en.Def.AttackRange && en.Grounded)
                        {
                            en.Facing = dx >= 0 ? 1 : -1;
                            en.State = EnemyState.AttackStartup;
                            en.CurAttack = en.Def.Attack;
                            en.StartReason = "FixtureAttackStart";
                            en.Timer = en.Def.Attack.Startup;
                            en.AttackInstanceId = _nextAttackId++;
                            en.AttackSpent = false;
                            en.ArmorBrokenThisAttack = false;
                            en.InvulnLogged = false;
                        }
                    }
                    break;
                case EnemyState.AttackStartup:
                    if ((en.Timer -= dt) <= TimerEps)
                    {
                        en.State = EnemyState.AttackActive;
                        en.Timer = en.CurAttack.Active / en.AttackSpeed;
                        AiAttackActive(en);
                    }
                    break;
                case EnemyState.AttackActive:
                    if ((en.Timer -= dt) <= TimerEps)
                    {
                        en.State = EnemyState.AttackRecovery;
                        en.Timer = en.CurAttack.Recovery / en.AttackSpeed;
                    }
                    break;
                case EnemyState.AttackRecovery:
                    if ((en.Timer -= dt) <= TimerEps)
                    {
                        en.State = EnemyState.Idle;
                        en.Cooldown = en.Def.AttackInterval;
                        AiAfterAttack(en);
                    }
                    break;
                case EnemyState.Stagger:
                case EnemyState.DodgeReact:
                    if ((en.Timer -= dt) <= TimerEps && en.Grounded)
                    {
                        en.State = AfterReactionState(en);
                        AiAfterReaction(en);
                    }
                    break;
                case EnemyState.PhaseTransition:
                    break; // ends in StepTransition (TransitionTimer), not via the reaction Timer (WP04-F03)
                case EnemyState.Guard:
                case EnemyState.Evade:
                    if ((en.Timer -= dt) <= TimerEps)
                    {
                        en.State = EnemyState.Idle;
                        en.MoveVelX = 0f;
                        en.LastDecision = "";
                    }
                    break;
                case EnemyState.Down:
                    if ((en.Timer -= dt) <= TimerEps)
                    {
                        en.State = EnemyState.Wakeup;
                        en.Timer = en.Def.WakeupTime;
                    }
                    break;
                case EnemyState.Wakeup:
                    if ((en.Timer -= dt) <= TimerEps)
                    {
                        en.State = AfterReactionState(en);
                        en.Cooldown = en.Def.AttackInterval;
                        AiAfterReaction(en);
                    }
                    break;
            }

            if (en.NewPlayerAttack) DefenseSkip(en, "Busy:" + before + (en.Grounded ? "" : ":Airborne")); // not free on the start tick

            // physics
            if (!en.Grounded)
            {
                en.VelY -= en.Def.Gravity * dt;
                if (en.JuggleTimer > 0f)
                {
                    en.JuggleTimer -= dt;
                    if (en.VelY < -en.Def.JuggleFallSpeed) en.VelY = -en.Def.JuggleFallSpeed; // R-06-07 slowed, still falling
                }
            }
            en.VelX = MathUtil.MoveTowards(en.VelX, 0f, _p.KnockbackDecel * dt);
            // AI movement only while free to move (Idle), during a boss back step, or carried through a jump attack (WP-04).
            // Every hit reaction state stops it; gravity and knockback (VelX) are separate and stay (WP04-F01, EXP-AI-09).
            var keepMove = en.State == EnemyState.Idle || en.State == EnemyState.Evade ||
                           (en.InAttack && !en.Grounded && en.Action == AiAction.Jump);
            if (!keepMove) en.MoveVelX = 0f;
            var r = Collision.Move(map, en.Position, en.Def.BodyWidth, en.Def.BodyHeight, (en.VelX + en.MoveVelX) * dt, en.Grounded ? 0f : en.VelY * dt);
            en.Position = r.Position;
            if (r.Landed)
            {
                en.Grounded = true;
                en.VelY = 0f;
                en.JuggleTimer = 0f;
                if (en.Action == AiAction.Jump)
                {
                    en.Action = AiAction.None;
                    en.MoveVelX = 0f;
                    en.LastDecision = "";
                    Log.Emit("ai.land", en.Id, "", "Air", en.State.ToString(), "JumpLanded", ("position", en.Position));
                }
                if (en.State == EnemyState.Launched)
                {
                    en.State = EnemyState.Down; // EXP-DOWN-01: a launched enemy that lands goes down (CF-10)
                    en.Timer = en.Def.DownTime;
                }
            }
            else if (en.Grounded && !Collision.HasGround(map, en.Position, en.Def.BodyWidth)) en.Grounded = false;

            if (en.State != before)
                Log.Emit("enemy.state", en.Id, "", before.ToString(), en.State.ToString(),
                    en.State == EnemyState.AttackStartup ? en.StartReason : StateReason(before, en.State),
                    ("position", en.Position), ("hp", en.Hp), ("attackInstanceId", en.AttackInstanceId));
        }

        static string StateReason(EnemyState from, EnemyState to)
        {
            if (to == EnemyState.Wakeup) return "WakeupStart(invincible, R-06-10)";
            if (from == EnemyState.Wakeup) return "WakeupEnd(invincibility off, R-06-10)";
            if (to == EnemyState.Down) return "LandedFromLaunch->Down(EXP-DOWN-01)";
            if (to == EnemyState.AttackStartup) return "FixtureAttackStart";
            if (to == EnemyState.PhaseTransition)
                return from == EnemyState.Idle ? "TransitionStart(R-08-09/10, EXP-BOSS-04)" : from + "End->TransitionResumes(EXP-BOSS-03)";
            if (to == EnemyState.Guard) return "BossGuard(R-08-11, EXP-BOSS-05/06)";
            if (to == EnemyState.Evade) return "BossEvade(R-08-11, EXP-BOSS-05/07)";
            if (from == EnemyState.PhaseTransition && to == EnemyState.Idle) return "TransitionDone(EXP-BOSS-03)";
            return from + "End";
        }

        // ================================================================ hits

        readonly List<(EnemyActor en, int ev)> _pc = new List<(EnemyActor, int)>();
        readonly List<EnemyActor> _ec = new List<EnemyActor>();

        Aabb PlayerHitbox(AttackDefinition d)
        {
            var cx = P.Position.X + P.Facing * d.HitboxOffsetX;
            var cy = P.Position.Y + d.HitboxOffsetY;
            return Aabb.FromCenterSize(cx, cy, d.HitboxWidth, d.HitboxHeight);
        }

        public Aabb? ActivePlayerHitbox()
        {
            if (Attack == null) return null;
            if (Attack.MotorDriven) return PlayerHitbox(Attack.Def);
            var t = Attack.Elapsed;
            if (t < Attack.Def.Startup - TimerEps || t >= Attack.Def.Startup + Attack.Def.Active - TimerEps) return null;
            return PlayerHitbox(Attack.Def);
        }

        static Aabb EnemyHitbox(EnemyActor en)
        {
            var d = en.CurAttack;
            return Aabb.FromCenterSize(en.Position.X + en.Facing * d.HitboxOffsetX, en.Position.Y + d.HitboxOffsetY, d.HitboxWidth, d.HitboxHeight);
        }

        public bool PlayerInvincible =>
            P.State == MotorState.Roll && P.RollElapsed >= _p.RollInvulnStart && P.RollElapsed <= _p.RollInvulnEnd;

        void ResolveHits(MapData map, bool playerFrozen)
        {
            _pc.Clear();
            _ec.Clear();
            // WP02-F01: capture the attack once. A Dodge cancels Attack; later candidates of this tick keep using this object.
            var atk = Attack;
            var box = playerFrozen ? null : ActivePlayerHitbox();
            if (box.HasValue)
            {
                var a = Attack;
                var ev = 0;
                if (!a.MotorDriven && a.Def.HitEvents > 1)
                    ev = Math.Min(a.Def.HitEvents - 1, (int)((a.Elapsed - a.Def.Startup) / (a.Def.Active / a.Def.HitEvents)));
                EnemyActor first = null;
                var firstD = float.MaxValue;
                foreach (var en in Enemies)
                {
                    if (!en.Targetable || !box.Value.Overlaps(en.Body) || a.HitByEvent[ev].Contains(en.Id)) continue;
                    if (en.State == EnemyState.Wakeup)
                    {
                        if (a.SkipLogged.Add(en.Id + ":inv"))
                            Log.Emit("hit.skip", Pl, en.Id, "", en.State.ToString(), "TargetInvincible:Wakeup(R-06-10)", ("attackInstanceId", a.InstanceId));
                        continue;
                    }
                    if (en.State == EnemyState.Down && !a.Def.HitsDowned)
                    {
                        if (a.SkipLogged.Add(en.Id + ":down"))
                            Log.Emit("hit.skip", Pl, en.Id, "", "Down", "DownedNotHittableByThisAttack(R-06-10)", ("attackInstanceId", a.InstanceId), ("attackId", a.Def.Id));
                        continue;
                    }
                    var single = (a.Def.Kind == AttackKind.Dive && !Unlocks.DiveArea) || (a.Def.Kind == AttackKind.AirDash && !Unlocks.DashPierce);
                    if (single)
                    {
                        if (a.Hits > 0) break; // initial dash / dive: first target only (R-04-06, R-04-08)
                        var d = Math.Abs(en.Position.X - P.Position.X);
                        if (d < firstD)
                        {
                            firstD = d;
                            first = en;
                        }
                        continue;
                    }
                    if (a.Def.Kind == AttackKind.Dive && Unlocks.DiveArea && a.Hits > 0) continue; // area applies on landing
                    _pc.Add((en, ev));
                }
                if (first != null) _pc.Add((first, ev)); // closest target (U-05 selection, EXP-ATK-05)
            }

            if (P.State != MotorState.Dead)
            {
                foreach (var en in Enemies)
                {
                    if (en.State != EnemyState.AttackActive || en.AttackSpent) continue;
                    if (!EnemyHitbox(en).Overlaps(P.Bounds)) continue;
                    if (PlayerInvincible)
                    {
                        if (!en.InvulnLogged)
                        {
                            en.InvulnLogged = true;
                            Log.Emit("hit.skip", en.Id, Pl, "", P.State.ToString(), "PlayerInvincible:RollWindow(R-06-13)",
                                ("attackInstanceId", en.AttackInstanceId), ("rollElapsed", P.RollElapsed));
                        }
                        continue;
                    }
                    _ec.Add(en);
                }
            }

            // Simultaneous hit (same tick, EXP-DODGE-01 window; CF-05): Dodge for both sides (R-06-15/16).
            for (var i = _ec.Count - 1; i >= 0; i--)
            {
                var en = _ec[i];
                var idx = _pc.FindIndex(x => x.en == en);
                if (idx < 0) continue;
                _pc.RemoveAt(idx);
                _ec.RemoveAt(i);
                Dodge(en);
            }

            if (_pc.Count > 0)
            {
                var a = atk;
                var how = Attack == null ? "Overlap(sameTickAfterDodge, EXP-DODGE-01)" : "Overlap";
                foreach (var (en, ev) in _pc) ApplyHitToEnemy(a, en, ev, how);
                ApplyAttackerHitstop(a, _pc[0].ev, _pc.Count);
                if (a.Def.Kind == AttackKind.AirDash && !Unlocks.DashPierce && P.State == MotorState.AirDash)
                    P.EndDash("DashStoppedAtFirstTarget(R-04-06)");
            }

            foreach (var en in _ec) ApplyAttackToPlayer(en.Id, en.CurAttack, en.AttackInstanceId, en.Position.X, en, "Melee");
        }

        /// <summary>R-06-03: the attacker stops once per hit event, however many targets it reaches and on whichever ticks
        /// they enter the hitbox (WP02-F02). A new hit event (multi-hit data, dive landing area) stops again.
        /// Each struck target still gets its own reaction stop (EXP-HITSTOP-01; composition is U-06).</summary>
        void ApplyAttackerHitstop(PlayerAttack a, int eventKey, int targets)
        {
            if (!a.HitstopEvents.Add(eventKey))
            {
                Log.Emit("hitstop.skip", Pl, "", "", "", "SameHitEventAlreadyStopped(R-06-03)", ("attackInstanceId", a.InstanceId),
                    ("hitEventId", a.InstanceId + ":" + eventKey), ("targets", targets));
                return;
            }
            PlayerHitstop = Math.Max(PlayerHitstop, a.Def.Hitstop);
            Log.Emit("hitstop", Pl, "", "", "", "Hitstop(R-06-03, EXP-HITSTOP-01 attacker+targets)", ("attackInstanceId", a.InstanceId),
                ("hitEventId", a.InstanceId + ":" + eventKey), ("seconds", a.Def.Hitstop), ("targets", targets));
        }

        void DiveAreaHit()
        {
            var a = Attack;
            var n = 0;
            foreach (var en in Enemies)
            {
                if (!en.Targetable || en.State == EnemyState.Wakeup || a.HitByEvent[0].Contains(en.Id)) continue;
                if (Math.Abs(en.Position.X - P.Position.X) > _p.DiveAreaRadius || Math.Abs(en.Position.Y - P.Position.Y) > 1.5f) continue;
                ApplyHitToEnemy(a, en, 0, "DiveArea(R-04-08 upgraded)");
                n++;
            }
            if (n > 0) ApplyAttackerHitstop(a, -1, n); // landing area = its own hit event (EXP-HITSTOP-02)
        }

        void Dodge(EnemyActor en)
        {
            var away = Math.Sign(P.Position.X - en.Position.X);
            if (away == 0) away = -P.Facing;
            Log.Emit("dodge", Pl, en.Id, P.State.ToString(), "DodgeReact", "SimultaneousHit(R-06-15)",
                ("playerAttackInstanceId", Attack?.InstanceId ?? 0), ("enemyAttackInstanceId", en.AttackInstanceId),
                ("playerHp", PlayerHp), ("enemyHp", en.Hp), ("damage", 0), ("comboBefore", ComboHits),
                ("window", "same tick (EXP-DODGE-01, CF-05)"), ("enemyArmor", en.CurAttack.SuperArmor || en.PhaseSuperArmor));
            if (Attack != null) CancelAttack("Dodge");
            EndChain("Dodge");
            P.EnterLocked(MotorState.DodgeReact, "Dodge");
            P.VelX = away * _p.DodgeKnockback;
            _playerLockTimer = _p.DodgeReactionTime;
            CutAiPropulsion(en, "Dodge");
            en.AttackSpent = true;
            en.State = EnemyState.DodgeReact;
            en.Timer = _p.DodgeReactionTime;
            en.VelX = -away * _p.DodgeKnockback;
        }

        void ApplyHitToEnemy(PlayerAttack a, EnemyActor en, int ev, string how)
        {
            var d = a.Def;
            var dmg = TestDamageResolver.Resolve(d);
            var hpBefore = en.Hp;
            var stateBefore = en.State;
            // R-08-11 boss guard (EXP-BOSS-06): front physical hits only; magic / unblockable / back hits are not guarded.
            var guarded = en.State == EnemyState.Guard && d.DamageType != DamageType.Magic && !d.Unblockable &&
                          (P.Position.X - en.Position.X) * en.Facing >= 0f;
            var taken = guarded ? GuardRule.Apply(dmg) : dmg;
            en.Hp = Math.Max(0f, en.Hp - taken);
            a.Hits++;
            a.HitByEvent[ev].Add(en.Id);
            var comboBefore = ComboHits;
            if (ChainAlive) ComboHits++;
            var away = Math.Sign(en.Position.X - P.Position.X);
            if (away == 0) away = P.Facing;
            var armor = en.InAttack && en.CurAttack != null && (en.CurAttack.SuperArmor || en.PhaseSuperArmor) && !en.ArmorBrokenThisAttack;
            string reaction;
            float kb = 0f;

            if (en.Hp <= 0f)
            {
                en.State = EnemyState.Dead;
                en.Timer = en.Def.DeathTime;
                en.VelX = 0f;
                reaction = "Death(R-06-17)";
                _endedLinks.Add(en.Id);
                if (en.TransitionTimer > 0f)
                {
                    en.TransitionTimer = 0f;
                    Log.Emit("boss.transitionCancel", en.Id, "", stateBefore.ToString(), "Dead", "Death(EXP-BOSS-02)", ("phase", en.PhaseId));
                }
            }
            else if (guarded)
            {
                reaction = "BossGuard:GuardRule(R-08-11, EXP-BOSS-06)";
            }
            else if (d.DamageType == DamageType.Magic)
            {
                if (armor) en.ArmorBrokenThisAttack = true; // this action only (R-06-12)
                en.AttackSpent = true;
                en.State = EnemyState.Stagger;
                en.Timer = Math.Max(0.1f, en.Def.StaggerTime);
                kb = away * d.Knockback * en.Def.MagicKnockbackScale; // > 0 by validation (R-06-06)
                en.VelX = kb;
                reaction = armor ? "MagicBreaksArmor:Stagger+Knockback+Cancel(R-06-12)" : "MagicStagger+Knockback(R-06-06)";
            }
            else if (armor)
            {
                reaction = "SuperArmor:DamageOnly(R-06-11)";
            }
            else if (en.State == EnemyState.Down)
            {
                reaction = "DownHit(no extension, CF-10)";
            }
            else if (d.Kind == AttackKind.Launcher && en.Grounded)
            {
                var v = en.Launch == LaunchResponse.Immune ? 0f : d.LaunchVelocity * (en.Launch == LaunchResponse.Resist ? en.Def.LaunchResistScale : 1f);
                if (v > 0f)
                {
                    en.State = EnemyState.Launched;
                    en.Grounded = false;
                    en.VelY = v;
                    en.AttackSpent = true;
                    reaction = "Launched:" + en.Launch + "(R-06-08)";
                }
                else
                {
                    en.State = EnemyState.Stagger;
                    en.Timer = en.Def.StaggerTime * (1f - en.StaggerResistance);
                    en.AttackSpent = true;
                    reaction = "LaunchImmune:DamageAndCount(R-04-04)";
                }
            }
            else if (!en.Grounded)
            {
                en.JuggleTimer = en.Def.JuggleSlowTime;
                if (en.VelY < -en.Def.JuggleFallSpeed) en.VelY = -en.Def.JuggleFallSpeed;
                reaction = "AirHit:SlowFall(R-06-07)";
            }
            else
            {
                if (en.StaggerResistance < 1f)
                {
                    en.State = EnemyState.Stagger;
                    en.Timer = en.Def.StaggerTime * (1f - en.StaggerResistance); // refreshed each hit (R-06-04)
                    en.AttackSpent = true;
                    reaction = "Stagger(R-06-04)";
                }
                else reaction = "StaggerResisted(R-06-04)";
                kb = away * d.Knockback * en.Def.PhysicalKnockbackScale;
                en.VelX = kb;
            }
            en.Hitstop = d.Hitstop;
            if (!guarded && !reaction.StartsWith("SuperArmor")) CutAiPropulsion(en, reaction);

            Log.Emit("hit", Pl, en.Id, stateBefore.ToString(), en.State.ToString(), reaction,
                ("attackInstanceId", a.InstanceId), ("hitEventId", a.InstanceId + ":" + ev), ("attackId", d.Id), ("attackKind", d.Kind),
                ("damageType", d.DamageType), ("how", how), ("damageD", dmg), ("damagePolicy", TestDamageResolver.PolicyId),
                ("hpBefore", hpBefore), ("hpAfter", en.Hp), ("knockbackVel", kb), ("superArmor", armor), ("guarded", guarded), ("damageTaken", taken),
                ("chainId", ChainId), ("attackChainStep", a.ChainStep), ("comboBefore", comboBefore), ("comboAfter", ComboHits),
                ("motionProgress", a.MotorDriven ? -1f : a.Elapsed / d.Total));
            if (en.State == EnemyState.Dead)
                Log.Emit("combo.linkEnd", Pl, en.Id, "", "", "TargetDeath(R-04-10: this target only)", ("chainId", ChainId), ("chainAlive", ChainAlive));
        }

        /// <summary>
        /// An enemy attack reaches the player: melee (parryTarget = the attacker) or a projectile (parryTarget = null:
        /// guard rule only, no parry, EXP-AI-05). srcX = where the hit comes from (front/back and knockback direction).
        /// </summary>
        void ApplyAttackToPlayer(string ownerId, AttackDefinition d, long attackInstanceId, float srcX, EnemyActor parryTarget, string via)
        {
            var en = parryTarget;
            if (en != null) en.AttackSpent = true;
            var dmg = TestDamageResolver.Resolve(d);
            var hpBefore = PlayerHp;
            var front = (srcX - P.Position.X) * P.Facing >= 0f; // equal X counts as front (EXP-GUARD-04, U-09)
            var stateBefore = P.State;
            var away = Math.Sign(P.Position.X - srcX);
            if (away == 0) away = -P.Facing;

            if (P.State == MotorState.Guard && front && !d.Unblockable)
            {
                var sinceRb = _sim.GameTime - _guardPressGameTime;
                var g = GuardRule.Apply(dmg);
                PlayerHp = Math.Max(0f, PlayerHp - g);
                if (en != null && sinceRb <= _p.ParryWindow + 1e-6)
                {
                    // R-07-05: enemy attack stopped, knockback, vulnerable; player free to act. Damage on parry: U-09 (EXP-PARRY-02 guard rule).
                    en.State = EnemyState.Stagger;
                    en.Timer = _p.ParryStunTime;
                    en.VelX = -away * _p.ParryKnockback;
                    CutAiPropulsion(en, "Parry");
                    Log.Emit("guard.parry", Pl, en.Id, "Guard", "Guard", "Parry(R-07-04/05)",
                        ("enemyAttackInstanceId", en.AttackInstanceId), ("rbToHit", sinceRb), ("parryWindow", _p.ParryWindow),
                        ("windowMeaning", "last W s before the hit, game time (EXP-PARRY-01, CF-04)"), ("damageD", dmg), ("damageTaken", g),
                        ("damagePolicy", "EXP-PARRY-02 guard rule"), ("hpBefore", hpBefore), ("hpAfter", PlayerHp), ("enemyStun", _p.ParryStunTime));
                    return;
                }
                Log.Emit("guard.block", Pl, ownerId, "Guard", d.GuardBreak ? "released" : "Guard", d.GuardBreak ? "GuardBreak(R-07-06)" : "FrontGuard(R-07-01/02)",
                    ("enemyAttackInstanceId", attackInstanceId), ("via", via), ("parry", en == null ? "not for projectiles (EXP-AI-05)" : "outside window"), ("rbToHit", sinceRb), ("damageD", dmg), ("damageTaken", g),
                    ("guardRule", "D*0.1, <1 -> 0; " + GuardRule.RoundingPolicy), ("damagePolicy", TestDamageResolver.PolicyId),
                    ("hpBefore", hpBefore), ("hpAfter", PlayerHp));
                if (d.GuardBreak) P.ReleaseLocked("GuardBreak(R-07-06, extra stun undecided U-09)");
                return;
            }

            var why = P.State == MotorState.Guard ? (d.Unblockable ? "Unblockable(R-07-06)" : "HitFromBehind(R-07-01)") : "Hit";
            PlayerHp = Math.Max(0f, PlayerHp - dmg);
            if (Attack != null) CancelAttack("PlayerHit");
            EndChain("PlayerHit(EnemyCancel)");
            _buffered = false;
            if (PlayerHp <= 0f)
            {
                P.EnterLocked(MotorState.Dead, "PlayerDeath");
                Log.Emit("player.death", Pl, ownerId, stateBefore.ToString(), "Dead", "HP0(death flow R-06-19 not in WP-02)");
            }
            else
            {
                P.EnterLocked(MotorState.HitStun, "PlayerHit");
                P.VelX = away * d.Knockback;
                _playerLockTimer = _p.PlayerHitstun;
            }
            Log.Emit("hit.player", ownerId, Pl, stateBefore.ToString(), P.State.ToString(), why,
                ("enemyAttackInstanceId", attackInstanceId), ("via", via), ("attackId", d.Id), ("damageD", dmg), ("damagePolicy", TestDamageResolver.PolicyId),
                ("hpBefore", hpBefore), ("hpAfter", PlayerHp), ("front", front), ("knockbackVel", P.VelX), ("hitstun", _p.PlayerHitstun),
                ("extraInvulnerability", "none (R-06-13/14)"));
        }

        string Reject(in InputEvent e, string code, string why)
        {
            Log.Emit("input.reject", Pl, "", P.State.ToString(), P.State.ToString(), code, ("inputEventId", e.EventId), ("action", e.Action), ("detail", why));
            return "rejected:" + code;
        }

        // ================================================================ test hooks / summaries

        /// <summary>Core-only test entry for magic rules (R-06-06/12). Not bound to any input; magic is P1.</summary>
        public void TestApplyHit(AttackDefinition def, string enemyId)
        {
            var en = Find(enemyId);
            if (en == null || !en.Targetable) return;
            if (!ChainAlive) BeginChain(0);
            var a = NewAttack(def);
            ApplyHitToEnemy(a, en, 0, "TestHook");
        }

        public string DescribeEnemies()
        {
            var sb = new StringBuilder();
            foreach (var en in Enemies)
            {
                if (sb.Length > 0) sb.Append('|');
                sb.Append(en.Id).Append(':').Append(en.State).Append('@').Append(en.Position.X.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture))
                  .Append(',').Append(en.Position.Y.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)).Append(" hp").Append(en.Hp.ToString("0", System.Globalization.CultureInfo.InvariantCulture));
                if (en.Boss != null) sb.Append(" ph:").Append(en.PhaseId);
            }
            return sb.ToString();
        }
    }
}
