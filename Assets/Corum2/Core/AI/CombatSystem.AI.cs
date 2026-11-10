using System;
using System.Collections.Generic;
using System.Globalization;

namespace Corum2.Core
{
    /// <summary>Projectile in flight (R-08-06 ranged enemies, boss patterns).</summary>
    public sealed class Projectile
    {
        public long Id;
        public string OwnerId = "";
        public long AttackInstanceId;
        public AttackDefinition Attack;
        public ProjectileDefinition Def;
        public Vec2 Position;
        public int Dir;
        public float Travelled;
        public bool Done;
        public Aabb Body => Aabb.FromCenterSize(Position.X, Position.Y, Def.Width, Def.Height);
    }

    /// <summary>
    /// P0-08 enemy AI and boss phases (WP-04). Every enemy decides on its own; there is no shared attack slot or turn
    /// (R-08-01). AI only asks for actions the combat state allows (spec 11.1): a hit, down, death or recovery always
    /// wins over a decision. Undecided parts (U-10, CF-09) are experimental:
    ///   EXP-AI-01 tracking range: beyond TrackRange the enemy waits (U-10 "추적 허용 범위")
    ///   EXP-AI-02 walking stops at map walls and edges of solid ground are not checked (U-10 막힌 길/낭떠러지)
    ///   EXP-AI-03 fleeing lasts FleeTime, then reapproach or stay per monster (U-10 후퇴 완료 후 행동)
    ///   EXP-AI-04 ranged retreat lasts RetreatTime after each attack, then waits
    ///   EXP-AI-05 projectiles: guard rule applies from the front; no parry and no Dodge (CF-04/CF-06 projectile scope)
    ///   EXP-AI-06 projectiles fly straight and vanish after MaxDistance; already fired ones keep flying after the
    ///             player dies but cannot hit a dead player (U-10)
    ///   EXP-AI-07 no randomness: fleeing, jumping and pattern choice are deterministic
    ///   EXP-AI-08 enemies do not turn while attacking; they face the player when an attack or move starts
    ///   EXP-BOSS-01 pattern choice: filter by distance band and player grounded/airborne, highest priority, ties to the
    ///               least used, then list order
    ///   EXP-BOSS-02 death wins over a phase transition
    ///   EXP-AI-09 a hit reaction stops AI propulsion and ends a jump action (WP04-F01); gravity and knockback stay
    ///   EXP-BOSS-03 no invulnerability during a transition; hits react normally; the transition time (TransitionTimer,
    ///               separate from the reaction Timer) keeps running and the boss returns to the transition after the
    ///               reaction; no new attack / guard / evade until it ends; death cancels it (CF-09, WP04-F03)
    ///   EXP-BOSS-04 transitions are checked when the boss is free to decide (not mid-attack, not in hit reaction);
    ///               the first satisfied transition in list order wins; one transition per check (CF-09)
    ///   EXP-BOSS-05..07 per-phase guard / evade frequency (R-08-11): deterministic accumulator per eligible player
    ///               attack start; guard = guard rule on front physical hits; evade = back step, no invulnerability
    /// Not implemented: real presentation assets (transition animation / VFX / camera are ids only).
    /// </summary>
    public sealed partial class CombatSystem
    {
        public readonly List<Projectile> Projectiles = new List<Projectile>();
        long _nextProjectileId = 1;

        /// <summary>Raise a named event for a boss (R-08-10). "*" = every boss on the map.</summary>
        public void RaiseBossEvent(string enemyId, string name)
        {
            foreach (var en in Enemies)
            {
                if (en.Boss == null || (enemyId != "*" && en.Id != enemyId) || !en.Targetable) continue;
                en.BossEvents.Add(name);
                Log.Emit("boss.event", en.Id, "", en.PhaseId, en.PhaseId, "Event:" + name + "(R-08-10)", ("event", name));
            }
        }

        static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        /// <summary>WP04-F01 / EXP-AI-09: a hit reaction stops AI propulsion and ends a jump action. Gravity and knockback
        /// (VelX) are not touched.</summary>
        void CutAiPropulsion(EnemyActor en, string why)
        {
            if (en.Ai == null) return;
            var wasJump = en.Action == AiAction.Jump && !en.Grounded;
            var v = en.MoveVelX;
            en.MoveVelX = 0f;
            if (en.Action == AiAction.Jump) en.Action = AiAction.None;
            if (wasJump || v != 0f)
                Log.Emit("ai.propulsionCut", en.Id, "", wasJump ? "Jump" : "Move", en.State.ToString(), "HitReaction:" + why + "(EXP-AI-09)",
                    ("moveVelBefore", v), ("airborne", !en.Grounded));
        }

        /// <summary>WP04-F03: the transition runs on its own timer, also through hit reactions (EXP-BOSS-03).</summary>
        void StepTransition(EnemyActor en, float dt)
        {
            en.TransitionTimer -= dt;
            if (en.TransitionTimer > TimerEps) return;
            en.TransitionTimer = 0f;
            var st = en.State;
            if (st == EnemyState.PhaseTransition) en.State = EnemyState.Idle;
            Log.Emit("boss.transitionEnd", en.Id, "", "PhaseTransition", en.State.ToString(),
                st == EnemyState.PhaseTransition ? "TransitionDone(EXP-BOSS-03)" : "TransitionDoneDuringReaction(EXP-BOSS-03)",
                ("phase", en.PhaseId), ("stateAtEnd", st));
        }

        /// <summary>A reaction ended: back to the transition if it is still running (EXP-BOSS-03), else free.</summary>
        static EnemyState AfterReactionState(EnemyActor en) => en.TransitionTimer > 0f ? EnemyState.PhaseTransition : EnemyState.Idle;

        /// <summary>A player attack start this boss does not answer (EXP-BOSS-05). Logged only when the boss has guard / evade
        /// settings and the attacker is within DefenseRange, so the counts stay untouched and the reason is traceable.</summary>
        void DefenseSkip(EnemyActor en, string why)
        {
            en.NewPlayerAttack = false;
            var ph = en.Phase;
            if (ph == null || (ph.GuardFrequency <= 0f && ph.EvadeFrequency <= 0f)) return;
            var dist = Math.Abs(P.Position.X - en.Position.X);
            if (dist > ph.DefenseRange) return;
            Log.Emit("boss.defenseSkip", en.Id, Pl, en.State.ToString(), en.State.ToString(), why + "(EXP-BOSS-05)",
                ("playerAttackInstanceId", en.SeenPlayerAttackId), ("distance", dist), ("facing", en.Facing), ("phase", ph.Id),
                ("guardAcc", en.GuardAcc), ("evadeAcc", en.EvadeAcc));
        }

        /// <summary>R-08-11 guard / evade frequency (EXP-BOSS-05..07). Called from BossIdle for a free, grounded boss.
        /// Eligible: observed on the attack's start tick, attacker within DefenseRange and in front of the boss's facing at
        /// that moment (equal X counts as front). The boss does not turn before this check (WP04-M02-A).</summary>
        bool BossDefense(EnemyActor en, float dist, int dir)
        {
            var ph = en.Phase;
            if (!en.NewPlayerAttack) return false;
            if (ph.GuardFrequency <= 0f && ph.EvadeFrequency <= 0f)
            {
                en.NewPlayerAttack = false;
                return false;
            }
            if (dist > ph.DefenseRange)
            {
                en.NewPlayerAttack = false;
                return false;
            }
            if ((P.Position.X - en.Position.X) * en.Facing < 0f)
            {
                DefenseSkip(en, "Behind");
                return false;
            }
            en.NewPlayerAttack = false;
            en.GuardAcc += ph.GuardFrequency;
            en.EvadeAcc += ph.EvadeFrequency;
            string pick = "None";
            if (en.EvadeAcc >= 1f - 1e-4f)
            {
                en.EvadeAcc -= 1f;
                pick = "Evade";
                en.State = EnemyState.Evade;
                en.Timer = ph.EvadeTime;
                en.MoveVelX = -dir * ph.EvadeSpeed;
            }
            else if (en.GuardAcc >= 1f - 1e-4f)
            {
                en.GuardAcc -= 1f;
                pick = "Guard";
                en.State = EnemyState.Guard;
                en.Timer = ph.GuardTime;
                en.MoveVelX = 0f;
            }
            Log.Emit("boss.defense", en.Id, Pl, ph.Id, pick, "PlayerAttackStart(R-08-11, EXP-BOSS-05)", ("playerAttackInstanceId", en.SeenPlayerAttackId),
                ("distance", dist), ("guardFrequency", ph.GuardFrequency), ("evadeFrequency", ph.EvadeFrequency),
                ("guardAcc", en.GuardAcc), ("evadeAcc", en.EvadeAcc), ("phase", ph.Id));
            if (pick == "None") return false;
            Decide(en, pick, "PlayerAttackStart:" + pick + "(R-08-11)", dist);
            return true;
        }

        void Decide(EnemyActor en, string decision, string reason, float dist)
        {
            if (en.LastDecision == decision) return;
            var before = en.LastDecision;
            en.LastDecision = decision;
            Log.Emit("ai.decide", en.Id, Pl, before, decision, reason, ("aiType", en.Ai.Type), ("distance", dist),
                ("playerState", P.State), ("playerGrounded", P.Grounded), ("cooldown", en.Cooldown), ("phase", en.PhaseId));
        }

        /// <summary>TC-08-02: the player is in range but this enemy cannot attack now. Logged once per reason change.</summary>
        void NoteBlocked(EnemyActor en, string reason)
        {
            if (en.LastBlocked == reason) return;
            en.LastBlocked = reason;
            if (reason.Length == 0) return;
            var d = Math.Abs(P.Position.X - en.Position.X);
            Log.Emit("ai.blocked", en.Id, Pl, en.State.ToString(), en.State.ToString(), reason + "(R-08-02)", ("distance", d),
                ("attackRange", en.AttackRange), ("cooldown", en.Cooldown));
        }

        float AttackRangeFor(EnemyActor en) => en.AttackRange;

        void StartEnemyAttack(EnemyActor en, AttackDefinition atk, ProjectileDefinition proj, string reason, string patternId)
        {
            var dx = P.Position.X - en.Position.X;
            en.Facing = dx >= 0 ? 1 : -1;
            en.MoveVelX = 0f;
            en.CurAttack = atk;
            en.CurProjectile = proj;
            en.CurPattern = patternId;
            en.State = EnemyState.AttackStartup;
            en.Timer = atk.Startup / en.AttackSpeed;
            en.AttackInstanceId = _nextAttackId++;
            en.AttackSpent = false;
            en.ArmorBrokenThisAttack = false;
            en.InvulnLogged = false;
            en.StartReason = reason;
        }

        /// <summary>Called for every AI enemy in Idle (free to decide).</summary>
        void AiIdle(MapData map, EnemyActor en, float dt)
        {
            var ai = en.Ai;
            var dx = P.Position.X - en.Position.X;
            var dist = Math.Abs(dx);
            var dir = dx >= 0 ? 1 : -1;
            en.Cooldown -= dt;
            if (en.JumpTimer > 0f) en.JumpTimer -= dt;

            if (en.Boss != null)
            {
                BossIdle(map, en, dt, dx, dist, dir);
                return;
            }

            if (P.State == MotorState.Dead)
            {
                en.MoveVelX = 0f;

                Decide(en, "Wait", "PlayerDead:NoNewAttack(R-08-07)", dist);
                return;
            }

            // Timed actions that run in Idle: flee (R-08-04) and retreat (R-08-06).
            if (en.Action == AiAction.Flee || en.Action == AiAction.Retreat)
            {
                en.ActionTimer -= dt;
                if (en.ActionTimer > 0f && en.Grounded)
                {
                    en.MoveVelX = -dir * (en.Action == AiAction.Flee ? ai.FleeSpeed : ai.RetreatSpeed);
                    en.Facing = -dir;
                    return;
                }
                var done = en.Action;
                en.Action = done == AiAction.Flee && !ai.ReapproachAfterFlee ? AiAction.Hold : AiAction.None;
                en.MoveVelX = 0f;
                Log.Emit("ai.actionEnd", en.Id, "", done.ToString(), en.Action.ToString(),
                    done == AiAction.Flee ? "FleeDone(EXP-AI-03)" : "RetreatDone(EXP-AI-04)", ("distance", dist));
            }

            if (!en.Grounded)
            {
                // Jumper in the air: air attack when close (R-08-05).
                if (ai.Type == AiType.Jumper && ai.AirAttack != null && en.Action == AiAction.Jump && !en.AirAttackUsed &&
                    dist <= ai.AirAttackRange)
                {
                    // WP04-F02: the air attack is a new attack and waits for this enemy's re-attack interval (R-08-02/07).
                    if (en.Cooldown > 0f)
                    {
                        NoteBlocked(en, "Cooldown:ReattackInterval:Air(R-08-07)");
                        return;
                    }
                    NoteBlocked(en, "");
                    en.AirAttackUsed = true;
                    Decide(en, "AirAttack", "InAirAttackRange(R-08-05)", dist);
                    StartEnemyAttack(en, ai.AirAttack, null, "AirAttack(R-08-05)", "");
                }
                return;
            }

            var range = AttackRangeFor(en);
            var tracking = dist <= ai.TrackRange;
            if (dist <= range && en.Cooldown <= 0f && en.Def.Attack != null)
            {
                Decide(en, "Attack", "InRange:FirstChance(R-08-02)", dist);
                NoteBlocked(en, "");
                StartEnemyAttack(en, en.Def.Attack, ai.Type == AiType.Ranged ? ai.Projectile : null, "InRange(R-08-02)", "");
                return;
            }
            if (dist <= range) NoteBlocked(en, en.Cooldown > 0f ? "Cooldown:ReattackInterval(R-08-07)" : "NoAttack");

            switch (ai.Type)
            {
                case AiType.Ranged:
                    // R-08-06: never walks in; waits outside range.
                    en.MoveVelX = 0f;
                    Decide(en, "Wait", dist > range ? "OutOfRange:WaitInPlace(R-08-06)" : "InRange:WaitCooldown", dist);
                    return;
                case AiType.Jumper:
                    if (tracking && en.JumpTimer <= 0f && dist >= ai.JumpMinDistance && dist <= ai.JumpMaxDistance)
                    {
                        en.Facing = dir;
                        en.Grounded = false;
                        en.VelY = ai.JumpVelocityY;
                        en.MoveVelX = dir * ai.JumpSpeedX;
                        en.Action = AiAction.Jump;
                        en.AirAttackUsed = false;
                        en.JumpTimer = ai.JumpCooldown;
                        Decide(en, "Jump", "JumpWindow[" + F(ai.JumpMinDistance) + "," + F(ai.JumpMaxDistance) + "](R-08-05)", dist);
                        Log.Emit("ai.jump", en.Id, Pl, "Idle", "Air", "JumpApproach(R-08-05)", ("distance", dist),
                            ("speedX", ai.JumpSpeedX), ("velocityY", ai.JumpVelocityY));
                        return;
                    }
                    break;
            }

            if (en.Action == AiAction.Hold)
            {
                en.MoveVelX = 0f;
                Decide(en, "Hold", "NoReapproachAfterFlee(EXP-AI-03)", dist);
                return;
            }
            if (!tracking)
            {
                en.MoveVelX = 0f;
                Decide(en, "Wait", "BeyondTrackRange(EXP-AI-01)", dist);
                return;
            }
            if (dist > range)
            {
                en.Facing = dir;
                en.MoveVelX = dir * ai.MoveSpeed;
                Decide(en, "Approach", "OutOfRange:Approach(R-08-03)", dist);
                return;
            }
            en.MoveVelX = 0f;
            Decide(en, "Wait", "InRange:WaitCooldown(R-08-07)", dist);
        }

        /// <summary>Hit reaction finished (Stagger / DodgeReact end) and the enemy is free again.</summary>
        void AiAfterReaction(EnemyActor en)
        {
            if (en.Ai == null) return;
            en.LastDecision = "";
            if (en.Ai.Type == AiType.Fleer && en.Hp > 0f)
            {
                en.Action = AiAction.Flee;
                en.ActionTimer = en.Ai.FleeTime;
                Log.Emit("ai.flee", en.Id, Pl, "Stagger", "Flee", "AfterHitReaction(R-08-04)", ("fleeTime", en.Ai.FleeTime),
                    ("fleeSpeed", en.Ai.FleeSpeed), ("reapproach", en.Ai.ReapproachAfterFlee));
            }
            else if (en.Action == AiAction.Hold)
            {
                // stays put (configured not to reapproach)
            }
            else en.Action = AiAction.None;
        }

        /// <summary>The attack recovery ended.</summary>
        void AiAfterAttack(EnemyActor en)
        {
            if (en.Ai == null) return;
            en.LastDecision = "";
            if (en.Boss != null)
            {
                en.Cooldown = en.Phase.AttackInterval;
                return;
            }
            en.Cooldown = en.Def.AttackInterval;
            if (en.Ai.Type == AiType.Ranged)
            {
                en.Action = AiAction.Retreat;
                en.ActionTimer = en.Ai.RetreatTime;
                Log.Emit("ai.retreat", en.Id, Pl, "AttackRecovery", "Retreat", "AfterAttack(R-08-06)", ("retreatTime", en.Ai.RetreatTime),
                    ("retreatSpeed", en.Ai.RetreatSpeed));
            }
        }

        /// <summary>Attack became active: a projectile attack spawns its projectile instead of a melee hitbox.</summary>
        void AiAttackActive(EnemyActor en)
        {
            if (en.CurProjectile == null) return;
            var pd = en.CurProjectile;
            var pr = new Projectile
            {
                Id = _nextProjectileId++, OwnerId = en.Id, AttackInstanceId = en.AttackInstanceId, Attack = en.CurAttack, Def = pd,
                Position = new Vec2(en.Position.X + en.Facing * pd.SpawnOffsetX, en.Position.Y + pd.SpawnOffsetY), Dir = en.Facing,
            };
            Projectiles.Add(pr);
            en.AttackSpent = true; // no melee hitbox for a shot
            Log.Emit("projectile.spawn", en.Id, Pl, "", "Flying", "Shot(R-08-06)", ("projectileId", pr.Id), ("attackInstanceId", pr.AttackInstanceId),
                ("position", pr.Position), ("dir", pr.Dir), ("speed", pd.Speed), ("maxDistance", pd.MaxDistance), ("pattern", en.CurPattern));
        }

        void StepProjectiles(MapData map, float dt)
        {
            foreach (var pr in Projectiles)
            {
                if (pr.Done) continue;
                var step = pr.Def.Speed * dt;
                pr.Position = new Vec2(pr.Position.X + pr.Dir * step, pr.Position.Y);
                pr.Travelled += step;
                if (P.State != MotorState.Dead && pr.Body.Overlaps(P.Bounds))
                {
                    if (PlayerInvincible)
                    {
                        if (!pr.Done && _invulnLogged.Add(-pr.Id))
                            Log.Emit("hit.skip", pr.OwnerId, Pl, "", P.State.ToString(), "PlayerInvincible:RollWindow(R-06-13)", ("projectileId", pr.Id));
                    }
                    else
                    {
                        pr.Done = true;
                        ApplyAttackToPlayer(pr.OwnerId, pr.Attack, pr.AttackInstanceId, pr.Position.X, null, "Projectile:" + pr.Id);
                        continue;
                    }
                }
                if (pr.Travelled >= pr.Def.MaxDistance || pr.Position.X < map.MinX - 1f || pr.Position.X > map.MaxX + 1f)
                {
                    pr.Done = true;
                    Log.Emit("projectile.expire", pr.OwnerId, "", "Flying", "Gone", "MaxDistance(EXP-AI-06)", ("projectileId", pr.Id),
                        ("travelled", pr.Travelled));
                }
            }
            Projectiles.RemoveAll(p => p.Done);
        }

        // ================================================================ boss (R-08-08..12)

        void EnterPhase(EnemyActor en, BossPhase ph, string reason, string condition)
        {
            var from = en.PhaseId;
            en.Phase = ph;
            en.PhaseId = ph.Id;
            en.PatternUses.Clear();
            en.StaggerResistance = ph.StaggerResistance;
            en.Launch = ph.Launch;
            en.AttackSpeed = ph.AttackSpeed;
            en.PhaseSuperArmor = ph.SuperArmor;
            en.Cooldown = 0f;
            en.JumpTimer = ph.JumpInterval;
            en.GuardAcc = 0f; // guard / evade frequency counts per phase (R-08-11 independent settings, EXP-BOSS-05)
            en.EvadeAcc = 0f;
            Log.Emit("boss.phase", en.Id, "", from, ph.Id, reason, ("condition", condition), ("hp", en.Hp), ("hpRatio", en.Hp / en.Def.MaxHp),
                ("patterns", string.Join(",", ph.Patterns.ConvertAll(x => x.Id))), ("superArmor", ph.SuperArmor),
                ("staggerResistance", ph.StaggerResistance), ("launch", ph.Launch), ("attackSpeed", ph.AttackSpeed),
                ("transitionTime", ph.TransitionTime), ("animation", ph.TransitionAnimation), ("vfx", ph.TransitionVfx), ("camera", ph.TransitionCamera));
        }

        /// <summary>EXP-BOSS-04: first satisfied transition in list order.</summary>
        bool TryTransition(EnemyActor en)
        {
            foreach (var t in en.Phase.Transitions)
            {
                string why = null;
                switch (t.Kind)
                {
                    case TransitionKind.HpBelow:
                        if (en.Hp <= t.Value * en.Def.MaxHp + 1e-4f) why = "HpBelow:" + F(t.Value);
                        break;
                    case TransitionKind.PatternUsed:
                        if (en.PatternUses.TryGetValue(t.PatternId, out var n) && n >= t.Count) why = "PatternUsed:" + t.PatternId + "x" + t.Count;
                        break;
                    case TransitionKind.Event:
                        if (en.BossEvents.Contains(t.EventName)) why = "Event:" + t.EventName;
                        break;
                }
                if (why == null) continue;
                var target = en.Boss.Find(t.TargetPhase);
                if (t.Kind == TransitionKind.Event) en.BossEvents.Remove(t.EventName);
                EnterPhase(en, target, "Transition(R-08-09/10, EXP-BOSS-04)", why);
                en.State = EnemyState.PhaseTransition;
                en.TransitionTimer = Math.Max(target.TransitionTime, 1e-6f); // own timer; hits do not overwrite it (WP04-F03)
                en.Timer = 0f;
                en.MoveVelX = 0f;
                if (en.NewPlayerAttack) DefenseSkip(en, "Busy:TransitionStart");
                return true;
            }
            return false;
        }

        void BossIdle(MapData map, EnemyActor en, float dt, float dx, float dist, int dir)
        {
            if (TryTransition(en)) return;
            if (en.NewPlayerAttack && !en.Grounded) DefenseSkip(en, "Busy:Idle:Airborne");
            if (P.State == MotorState.Dead)
            {
                en.MoveVelX = 0f;
                Decide(en, "Wait", "PlayerDead:NoNewAttack(R-08-07)", dist);
                return;
            }
            if (!en.Grounded) return;
            if (dist > en.Ai.TrackRange)
            {
                en.MoveVelX = 0f;
                Decide(en, "Wait", "BeyondTrackRange(EXP-AI-01)", dist);
                return;
            }
            if (BossDefense(en, dist, dir)) return;
            var ph = en.Phase;
            if (en.Cooldown <= 0f)
            {
                BossPattern pick = null;
                var cands = new List<string>();
                foreach (var pt in ph.Patterns)
                {
                    var okDist = dist >= pt.MinDistance && dist <= pt.MaxDistance;
                    var okAir = pt.PlayerState == PlayerAirCondition.Any ||
                                (pt.PlayerState == PlayerAirCondition.Grounded) == P.Grounded;
                    if (!okDist || !okAir) continue;
                    cands.Add(pt.Id);
                    if (pick == null || pt.Priority > pick.Priority ||
                        (pt.Priority == pick.Priority && Uses(en, pt.Id) < Uses(en, pick.Id)))
                        pick = pt;
                }
                if (pick != null)
                {
                    en.PatternUses[pick.Id] = Uses(en, pick.Id) + 1;
                    Log.Emit("boss.pattern", en.Id, Pl, ph.Id, pick.Id, "Selected(R-08-08, EXP-BOSS-01)", ("candidates", string.Join(",", cands)),
                        ("distance", dist), ("playerGrounded", P.Grounded), ("priority", pick.Priority), ("uses", en.PatternUses[pick.Id]),
                        ("special", pick.Special), ("range", "[" + F(pick.MinDistance) + "," + F(pick.MaxDistance) + "]"), ("playerCondition", pick.PlayerState));
                    Decide(en, "Pattern:" + pick.Id, "PatternFits(R-08-08)", dist);
                    StartEnemyAttack(en, pick.Attack, pick.Projectile, "Pattern:" + pick.Id, pick.Id);
                    return;
                }
            }
            if (ph.JumpInterval > 0f && en.JumpTimer <= 0f && dist > ph.PreferredMaxDistance)
            {
                en.Facing = dir;
                en.Grounded = false;
                en.VelY = ph.JumpVelocityY;
                en.MoveVelX = dir * ph.JumpSpeedX;
                en.Action = AiAction.Jump;
                en.JumpTimer = ph.JumpInterval;
                Decide(en, "Jump", "PhaseJump(R-08-11)", dist);
                Log.Emit("ai.jump", en.Id, Pl, "Idle", "Air", "BossPhaseJump(R-08-11)", ("distance", dist), ("phase", ph.Id));
                return;
            }
            if (dist > ph.PreferredMaxDistance)
            {
                en.Facing = dir;
                en.MoveVelX = dir * ph.ApproachSpeed;
                Decide(en, "Approach", "BeyondPreferred:" + F(ph.PreferredMaxDistance) + "(R-08-11)", dist);
            }
            else if (dist < ph.PreferredMinDistance)
            {
                en.Facing = dir;
                en.MoveVelX = -dir * ph.RetreatSpeed;
                Decide(en, "Retreat", "InsidePreferred:" + F(ph.PreferredMinDistance) + "(R-08-11)", dist);
            }
            else
            {
                en.Facing = dir;
                en.MoveVelX = 0f;
                Decide(en, "Wait", en.Cooldown > 0f ? "PatternInterval" : "NoPatternFits", dist);
            }
        }

        static int Uses(EnemyActor en, string id) => en.PatternUses.TryGetValue(id, out var n) ? n : 0;
    }
}
