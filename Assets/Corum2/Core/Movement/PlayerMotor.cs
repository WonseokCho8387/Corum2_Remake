using System;

namespace Corum2.Core
{
    public enum MotorState
    {
        Idle,
        Walk,
        Run,
        Crouch,
        Slide,
        Roll,
        Airborne,
        AirDash,
        Dive,
    }

    /// <summary>AirAbilityState (spec 11.2): three independent once-per-airtime uses (R-03-07).</summary>
    public sealed class AirAbilityState
    {
        public bool ExtraJumpUsed;
        public bool DashUsed;
        public bool DiveUsed;
        /// <summary>Set by the dive; ends remaining extra jump / dash even if unused (R-03-09).</summary>
        public bool DiveFinished;

        public void Reset()
        {
            ExtraJumpUsed = false;
            DashUsed = false;
            DiveUsed = false;
            DiveFinished = false;
        }

        public string Describe() =>
            "jump=" + (ExtraJumpUsed ? "used" : "free") + ",dash=" + (DashUsed ? "used" : "free") +
            ",dive=" + (DiveUsed ? "used" : "free") + (DiveFinished ? ",diveFinished" : "");
    }

    /// <summary>
    /// Player movement and air actions (P0-03). Combat actions that are not movement are reported as
    /// not implemented in WP-01 rather than guessed.
    /// </summary>
    public sealed class PlayerMotor
    {
        public const string Actor = "player";

        readonly MovementProfile _m;
        readonly InputProfile _input;
        readonly AirUnlocks _unlocks;
        readonly DiagLogger _log;

        public Vec2 Position;
        public float VelX;
        public float VelY;
        public int Facing = 1;
        public MotorState State = MotorState.Idle;
        public bool Grounded = true;
        public readonly AirAbilityState Air = new AirAbilityState();

        /// <summary>Held direction components, updated by Move* edges in event order.</summary>
        public int HeldH;
        public int HeldV;
        /// <summary>Direction the run was armed for by a double tap (0 = not running).</summary>
        public int RunDir;

        double _lastPressLeft = double.NegativeInfinity;
        double _lastPressRight = double.NegativeInfinity;
        float _stateTimer;
        int _actionDir;
        float _launchSpeedAbs;

        public PlayerMotor(MovementProfile movement, InputProfile input, AirUnlocks unlocks, DiagLogger log)
        {
            _m = movement;
            _input = input;
            _unlocks = unlocks;
            _log = log;
        }

        public Dir8 HeldDir => Dir8Util.FromComponents(HeldH, HeldV);
        public bool IsLow => State == MotorState.Crouch || State == MotorState.Slide;
        public float Height => IsLow ? _m.CrouchHeight : _m.StandHeight;
        public float Width => _m.BodyWidth;
        public Aabb Bounds => Aabb.FromFeet(Position, Width, Height);

        static bool IsGroundState(MotorState s) =>
            s == MotorState.Idle || s == MotorState.Walk || s == MotorState.Run || s == MotorState.Crouch;

        public bool CanUseMapPoint => IsGroundState(State) && Grounded;

        public void PlaceAt(MapData map, SpawnPoint spawn, string reason)
        {
            var before = State.ToString();
            Position = Collision.SnapToGround(map, spawn.Position, _m.BodyWidth);
            VelX = 0f;
            VelY = 0f;
            Facing = spawn.Facing;
            State = MotorState.Idle;
            Grounded = Collision.HasGround(map, Position, _m.BodyWidth);
            if (!Grounded) State = MotorState.Airborne;
            RunDir = 0;
            var airBefore = Air.Describe();
            Air.Reset();
            _log.Emit("motor.place", Actor, "", before, State.ToString(), reason,
                ("mapId", map.Id), ("spawnId", spawn.Id), ("position", Position), ("facing", Facing), ("grounded", Grounded),
                ("airBefore", airBefore), ("airAfter", Air.Describe()));
        }

        // ---------------------------------------------------------------- input

        /// <summary>Applies one input edge. Returns a short code describing what the motor did (for the input log).</summary>
        public string HandleEvent(MapData map, in InputEvent e)
        {
            switch (e.Action)
            {
                case LogicalAction.MoveLeft:
                case LogicalAction.MoveRight:
                    return HandleHorizontalEdge(e);
                case LogicalAction.MoveUp:
                    HeldV = e.Edge == InputEdge.Press ? 1 : (HeldV == 1 ? 0 : HeldV);
                    return "dir";
                case LogicalAction.MoveDown:
                    HeldV = e.Edge == InputEdge.Press ? -1 : (HeldV == -1 ? 0 : HeldV);
                    return "dir";
            }

            if (e.Edge != InputEdge.Press) return "release";

            switch (e.Action)
            {
                case LogicalAction.Jump:
                    return OnJumpButton(map, e);
                case LogicalAction.Attack:
                    return OnAttackButton(e);
                case LogicalAction.SpecialAttack:
                    return OnSpecialButton(e);
                case LogicalAction.Guard:
                    return NotImplemented(e, "Guard/parry is P0-07 (WP-02). Air RB guard (R-03-06) not implemented in WP-01.");
                case LogicalAction.RingToggle:
                    return NotImplemented(e, "Ring inventory is P0-02 ring UI (WP-03).");
                default:
                    return NotImplemented(e, "Quickslots are P0-02 (WP-03).");
            }
        }

        string HandleHorizontalEdge(in InputEvent e)
        {
            var dir = e.Action == LogicalAction.MoveRight ? 1 : -1;
            if (e.Edge == InputEdge.Press)
            {
                HeldH = dir;
                var last = dir > 0 ? _lastPressRight : _lastPressLeft;
                var gap = e.RawTime - last;
                if (dir > 0) _lastPressRight = e.RawTime;
                else _lastPressLeft = e.RawTime;

                // R-03-01: ground double tap then hold = run. Window is press-to-press on raw input time (EXP-TIME-01).
                if (Grounded && IsGroundState(State) && gap <= _input.DoubleTapWindow)
                {
                    var before = RunDir;
                    RunDir = dir;
                    _log.Emit("motor.run", Actor, "", "runDir=" + before, "runDir=" + RunDir, "DoubleTap",
                        ("inputEventId", e.EventId), ("tapGap", gap), ("window", _input.DoubleTapWindow), ("timeAxis", "rawInput(EXP-TIME-01)"));
                    return "runArmed";
                }
                if (!Grounded && gap <= _input.DoubleTapWindow)
                {
                    // R-03-06: no separate air dash from a double tap. Log so TC-03-06 can show it was ignored.
                    _log.Emit("motor.run", Actor, "", "runDir=" + RunDir, "runDir=" + RunDir, "AirDoubleTapIgnored(R-03-06)",
                        ("inputEventId", e.EventId), ("tapGap", gap));
                    return "airDoubleTapIgnored";
                }
                return "dir";
            }

            if (HeldH == dir) HeldH = 0;
            if (RunDir == dir)
            {
                RunDir = 0;
                _log.Emit("motor.run", Actor, "", "runDir=" + dir, "runDir=0", "RunDirectionReleased(R-03-01)", ("inputEventId", e.EventId));
                return "runEnded";
            }
            return "dir";
        }

        string OnJumpButton(MapData map, in InputEvent e)
        {
            var dir = HeldDir;
            if (IsGroundState(State))
            {
                if (dir == Dir8.Down) return StartSlide(map, e);
                if (dir == Dir8.DownLeft || dir == Dir8.DownRight) return StartRoll(map, e, Dir8Util.Horizontal(dir));
                return StartJump(map, e);
            }
            if (State == MotorState.Airborne) return TryExtraJump(e);
            return Reject(e, "ActionLock:" + State, State == MotorState.AirDash ? "R-03-08: dash animation is not cancelled by jump" : "EXP-MOVE-05: no cancel of this state");
        }

        string OnAttackButton(in InputEvent e)
        {
            if (State == MotorState.Airborne)
            {
                var h = Dir8Util.Horizontal(HeldDir);
                if (h != 0) return TryDash(e, h);
                return NotImplemented(e, "Neutral air attack is P0-04 (WP-02).");
            }
            if (State == MotorState.AirDash || State == MotorState.Dive)
                return Reject(e, "ActionLock:" + State, "R-03-08/R-03-09");
            return NotImplemented(e, "Ground attack is P0-04 (WP-02).");
        }

        string OnSpecialButton(in InputEvent e)
        {
            if (State == MotorState.Airborne)
            {
                var h = Dir8Util.Horizontal(HeldDir);
                if (h != 0) return TryDive(e, h);
                return NotImplemented(e, "Special air attack without direction is undefined (U-02); not implemented.");
            }
            if (State == MotorState.AirDash || State == MotorState.Dive)
                return Reject(e, "ActionLock:" + State, "EXP-MOVE-05");
            return NotImplemented(e, "Ground special attack is P0-04 / U-02 (WP-02).");
        }

        string StartJump(MapData map, in InputEvent e)
        {
            if (State == MotorState.Crouch && !Collision.Fits(map, Position, _m.BodyWidth, _m.StandHeight))
                return Reject(e, "CeilingBlocked", "cannot stand up to jump");

            var h = HeldH;
            string kind;
            float vx;
            if (h == 0)
            {
                kind = "Standing";
                vx = 0f;
            }
            else if (RunDir == h)
            {
                kind = "Run";
                vx = h * _m.RunSpeed;
            }
            else
            {
                kind = "Walk";
                vx = h * _m.WalkSpeed;
            }

            var before = State;
            VelX = vx;
            VelY = _m.JumpVelocity;
            _launchSpeedAbs = Math.Abs(vx);
            if (h != 0) Facing = h;
            State = MotorState.Airborne;
            Grounded = false;
            _log.Emit("motor.jump", Actor, "", before.ToString(), State.ToString(), "Jump:" + kind,
                ("inputEventId", e.EventId), ("kind", kind), ("vx", VelX), ("vy", VelY), ("jumpHeight", _m.JumpHeight), ("ruleIds", "R-03-02"));
            return "jump:" + kind;
        }

        string TryExtraJump(in InputEvent e)
        {
            if (!_unlocks.ExtraJump) return Reject(e, "Locked:ExtraJump", "R-03-03 (test unlock data)");
            if (Air.DiveFinished) return Reject(e, "DiveFinished", "R-03-09");
            if (Air.ExtraJumpUsed) return Reject(e, "AlreadyUsed:ExtraJump", "R-03-07");
            var airBefore = Air.Describe();
            Air.ExtraJumpUsed = true; // consumed at start (U-04, EXP-AIR-01)
            VelY = _m.ExtraJumpVelocity;
            _log.Emit("air.use", Actor, "", airBefore, Air.Describe(), "ExtraJump",
                ("inputEventId", e.EventId), ("ability", "ExtraJump"), ("unlocked", true), ("vy", VelY), ("consumeTiming", "start(EXP-AIR-01)"));
            return "extraJump";
        }

        string TryDash(in InputEvent e, int h)
        {
            if (!_unlocks.AirDash) return Reject(e, "Locked:AirDash", "R-03-03 (test unlock data)");
            if (Air.DiveFinished) return Reject(e, "DiveFinished", "R-03-09");
            if (Air.DashUsed) return Reject(e, "AlreadyUsed:AirDash", "R-03-07");
            var airBefore = Air.Describe();
            var before = State;
            Air.DashUsed = true;
            State = MotorState.AirDash;
            _stateTimer = _m.DashDuration;
            _actionDir = h;
            Facing = h;
            VelX = h * _m.DashSpeed;
            VelY = 0f;
            _log.Emit("air.use", Actor, "", airBefore, Air.Describe(), "AirDash",
                ("inputEventId", e.EventId), ("ability", "AirDash"), ("dir", h), ("stateBefore2", before.ToString()), ("note", "movement only; hit detection is P0-04 (WP-02)"));
            return "airDash";
        }

        string TryDive(in InputEvent e, int h)
        {
            if (!_unlocks.Dive) return Reject(e, "Locked:Dive", "R-03-03 (test unlock data)");
            if (Air.DiveUsed) return Reject(e, "AlreadyUsed:Dive", "R-03-07");
            var airBefore = Air.Describe();
            Air.DiveUsed = true;
            Air.DiveFinished = true;
            State = MotorState.Dive;
            _actionDir = h;
            Facing = h;
            var a = _m.DiveAngleDeg * Math.PI / 180.0;
            VelX = (float)(h * _m.DiveSpeed * Math.Cos(a));
            VelY = (float)(-_m.DiveSpeed * Math.Sin(a));
            _log.Emit("air.use", Actor, "", airBefore, Air.Describe(), "Dive",
                ("inputEventId", e.EventId), ("ability", "Dive"), ("dir", h), ("endsRemaining", "R-03-09"), ("note", "movement only; hit detection is P0-04 (WP-02)"));
            return "dive";
        }

        string StartSlide(MapData map, in InputEvent e)
        {
            var before = State;
            State = MotorState.Slide;
            _stateTimer = _m.SlideDuration;
            _actionDir = Facing; // R-03-04: toward the facing direction
            _log.Emit("motor.state", Actor, "", before.ToString(), State.ToString(), "Slide(R-03-04)",
                ("inputEventId", e.EventId), ("dir", _actionDir), ("duration", _m.SlideDuration), ("speed", _m.SlideSpeed), ("height", _m.CrouchHeight),
                ("invulnerable", "undecided(U-04)"));
            return "slide";
        }

        string StartRoll(MapData map, in InputEvent e, int h)
        {
            // WP-01-R1 F-01: rolling uses the standing body, so it needs standing clearance.
            // Under a low ceiling the roll is refused and the player stays crouched in place (EXP-MOVE-06).
            if (!Collision.Fits(map, Position, _m.BodyWidth, _m.StandHeight))
                return Reject(e, "CeilingBlocked", "EXP-MOVE-06: roll needs standing clearance; posture and position unchanged");
            var before = State;
            State = MotorState.Roll;
            _stateTimer = _m.RollDuration;
            _actionDir = h; // R-03-05: input direction regardless of facing; facing kept (EXP-MOVE-02)
            _log.Emit("motor.state", Actor, "", before.ToString(), State.ToString(), "Roll(R-03-05)",
                ("inputEventId", e.EventId), ("dir", h), ("facing", Facing), ("duration", _m.RollDuration), ("speed", _m.RollSpeed),
                ("invulnerability", "P0-06, not in WP-01"));
            return "roll";
        }

        string Reject(in InputEvent e, string code, string why)
        {
            _log.Emit("input.reject", Actor, "", State.ToString(), State.ToString(), code,
                ("inputEventId", e.EventId), ("action", e.Action), ("detail", why), ("air", Air.Describe()));
            return "rejected:" + code;
        }

        string NotImplemented(in InputEvent e, string why)
        {
            _log.Emit("input.notImplemented", Actor, "", State.ToString(), State.ToString(), "NotImplementedInWP01",
                ("inputEventId", e.EventId), ("action", e.Action), ("detail", why));
            return "notImplemented";
        }

        // ---------------------------------------------------------------- integration

        public MoveResult Integrate(MapData map, float dt)
        {
            var before = State;
            MoveResult r;
            switch (State)
            {
                case MotorState.Idle:
                case MotorState.Walk:
                case MotorState.Run:
                case MotorState.Crouch:
                    r = IntegrateGround(map, dt);
                    break;
                case MotorState.Slide:
                case MotorState.Roll:
                    r = IntegrateGroundAction(map, dt);
                    break;
                case MotorState.AirDash:
                    r = IntegrateDash(map, dt);
                    break;
                default:
                    r = IntegrateAir(map, dt);
                    break;
            }
            if (State != before && !_stateLogged)
                _log.Emit("motor.state", Actor, "", before.ToString(), State.ToString(), _transitionReason ?? "Integrate",
                    ("position", Position), ("vx", VelX), ("vy", VelY));
            _stateLogged = false;
            _transitionReason = null;
            return r;
        }

        bool _stateLogged;
        string _transitionReason;

        MoveResult IntegrateGround(MapData map, float dt)
        {
            var h = HeldH;
            var down = HeldV < 0;
            var canStand = State != MotorState.Crouch || Collision.Fits(map, Position, _m.BodyWidth, _m.StandHeight);

            if (down || !canStand)
            {
                State = MotorState.Crouch;
                VelX = 0f; // crawling is not part of P0-03
                if (!down) _transitionReason = "CeilingKeepsCrouch";
            }
            else if (h != 0)
            {
                State = RunDir == h ? MotorState.Run : MotorState.Walk;
                VelX = h * (State == MotorState.Run ? _m.RunSpeed : _m.WalkSpeed);
                Facing = h;
            }
            else
            {
                State = MotorState.Idle;
                VelX = 0f;
            }
            VelY = 0f;

            var r = Collision.Move(map, Position, _m.BodyWidth, Height, VelX * dt, 0f);
            Position = r.Position;
            if (!Collision.HasGround(map, Position, _m.BodyWidth))
            {
                // Walked off a ledge: fall with the current horizontal speed. Extra jump is still available in this
                // airtime (ledge-leave policy U-04, EXP-AIR-02).
                _transitionReason = "LeftGround(EXP-AIR-02)";
                State = MotorState.Airborne;
                Grounded = false;
                _launchSpeedAbs = Math.Abs(VelX);
            }
            return r;
        }

        MoveResult IntegrateGroundAction(MapData map, float dt)
        {
            var speed = State == MotorState.Slide ? _m.SlideSpeed : _m.RollSpeed;
            VelX = _actionDir * speed;
            VelY = 0f;
            var r = Collision.Move(map, Position, _m.BodyWidth, Height, VelX * dt, 0f);
            Position = r.Position;
            _stateTimer -= dt;

            if (!Collision.HasGround(map, Position, _m.BodyWidth))
            {
                _transitionReason = State + "LeftGround";
                State = MotorState.Airborne;
                Grounded = false;
                _launchSpeedAbs = Math.Abs(VelX);
                return r;
            }
            if (_stateTimer <= 0f)
            {
                var standOk = Collision.Fits(map, Position, _m.BodyWidth, _m.StandHeight);
                _transitionReason = State + "End" + (standOk ? "" : ":CeilingKeepsCrouch");
                State = standOk ? MotorState.Idle : MotorState.Crouch;
                VelX = 0f;
            }
            return r;
        }

        MoveResult IntegrateDash(MapData map, float dt)
        {
            VelX = _actionDir * _m.DashSpeed;
            VelY = 0f;
            var r = Collision.Move(map, Position, _m.BodyWidth, Height, VelX * dt, 0f);
            Position = r.Position;
            _stateTimer -= dt;
            if (_stateTimer <= 0f)
            {
                _transitionReason = "AirDashEnd";
                State = MotorState.Airborne;
                VelX = _actionDir * _m.DashEndSpeed;
                _launchSpeedAbs = _m.DashEndSpeed;
            }
            return r;
        }

        MoveResult IntegrateAir(MapData map, float dt)
        {
            var dy = VelY * dt;
            if (State == MotorState.Airborne)
            {
                // Trapezoidal step: the peak of a jump matches JumpHeight instead of undershooting by v*dt/2.
                var vyOld = VelY;
                VelY = Math.Max(VelY - _m.Gravity * dt, -_m.MaxFallSpeed);
                dy = (vyOld + VelY) * 0.5f * dt;
                var h = HeldH;
                if (h != 0)
                {
                    // R-03-06: direction only = position adjustment. Steering never exceeds the launch speed or AirControlSpeed.
                    var maxSpeed = Math.Max(_m.AirControlSpeed, _launchSpeedAbs);
                    VelX = MathUtil.MoveTowards(VelX, h * maxSpeed, _m.AirAcceleration * dt);
                }
            }

            var r = Collision.Move(map, Position, _m.BodyWidth, Height, VelX * dt, dy);
            Position = r.Position;
            if (r.HitCeiling && VelY > 0f) VelY = 0f;
            if ((r.HitLeft && VelX < 0f) || (r.HitRight && VelX > 0f)) VelX = 0f;

            if (r.Landed) Land(map);
            return r;
        }

        void Land(MapData map)
        {
            var before = State;
            var airBefore = Air.Describe();
            Air.Reset(); // R-03-10
            Grounded = true;
            VelY = 0f;
            VelX = 0f;
            State = MotorState.Idle; // R-03-10 basic landing: immediately actionable. Dive landing lock is CF-02 (not applied).
            _stateLogged = true;
            _log.Emit("motor.land", Actor, "", before.ToString(), State.ToString(),
                before == MotorState.Dive ? "DiveLanding(CF-02 landing lock not applied)" : "Landed(R-03-10)",
                ("position", Position), ("airBefore", airBefore), ("airAfter", Air.Describe()), ("resetReason", "Landing"));
        }
    }
}
