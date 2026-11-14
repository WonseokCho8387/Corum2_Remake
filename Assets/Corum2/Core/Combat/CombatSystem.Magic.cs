using System;

namespace Corum2.Core
{
    /// <summary>
    /// WP-05 magic casting (R-02-02 command, R-02-10 scroll) and revive support (R-06-19). A command and a scroll of the
    /// same magic go through CastMagic with the same MagicDefinition, so power and cast time are identical (R-02-10).
    /// Temporary rules (the magic system is P1; U-02, CF-01, CF-07 stay open):
    ///   EXP-MAGIC-01 a cast is a player motion: startup = CastTime, then the effect, then Recovery; R-05-01 movement lock
    ///                applies; it starts from the same states as a ground/air attack (free or guard), not during another
    ///                attack, dash, dive, slide, roll, hit reaction or death
    ///   EXP-MAGIC-02 a cast ends the current attack chain and is not a chain step; magic hits do not add combo
    ///   EXP-MAGIC-03 guard cancels a cast like a ground attack (R-05-02); a hit cancels it; a cancelled cast has no effect
    ///                (a scroll was already consumed when its use ran, EXP-ITEM-04)
    ///   EXP-MAGIC-04 Attack effect = one magic-type hit event in front (R-06-06/12 apply); Heal = +Power HP;
    ///                Buff = player attack damage x Power for BuffDuration (a new buff replaces the old one)
    /// </summary>
    public sealed partial class CombatSystem
    {
        /// <summary>Player damage multiplier from a Buff magic, and the game time it ends.</summary>
        public float BuffMultiplier { get; private set; } = 1f;
        public double BuffUntil { get; private set; }
        public string BuffMagicId { get; private set; } = "";
        double _reviveInvulnUntil;

        public bool ReviveInvulnerable => _sim.GameTime < _reviveInvulnUntil;

        /// <summary>P1: called when a cast with MagicEffect.External reaches its effect time (the P1 commit point).</summary>
        public System.Action<PlayerAttack> ExternalSpellEffect;

        static AttackDefinition SpellDefinition(MagicDefinition m)
        {
            var atk = m.Effect == MagicEffect.Attack;
            return new AttackDefinition
            {
                Id = "magic." + m.Id, Kind = AttackKind.Spell, DamageType = DamageType.Magic,
                Startup = m.CastTime, Active = atk ? m.Active : 0.02f, Recovery = m.Recovery,
                HitboxOffsetX = m.HitboxOffsetX, HitboxOffsetY = m.HitboxOffsetY, HitboxWidth = m.HitboxWidth, HitboxHeight = m.HitboxHeight,
                TestDamage = atk ? m.Power : 0f, Knockback = m.Knockback, Hitstop = m.Hitstop, NoHitbox = !atk,
            };
        }

        /// <summary>Start a cast. Returns null when started, else the reject code (nothing happens).</summary>
        public string CastMagic(MagicDefinition m, string source, long inputEventId)
        {
            var s = P.State;
            string reject = null;
            if (m == null) reject = "UnknownMagic";
            else if (s == MotorState.Dead || s == MotorState.HitStun || s == MotorState.DodgeReact) reject = "ActionLock:" + s + "(R-05-04)";
            else if (s == MotorState.Slide || s == MotorState.Roll || s == MotorState.AirDash || s == MotorState.Dive) reject = "ActionLock:" + s + "(EXP-MAGIC-01)";
            else if (s == MotorState.GroundAttack || s == MotorState.AirAttack || Attack != null) reject = "ActionLock:InMotion(EXP-MAGIC-01, CF-07)";
            if (reject != null)
            {
                Log.Emit("magic.reject", Pl, "", s.ToString(), s.ToString(), reject, ("magicId", m?.Id ?? ""), ("source", source),
                    ("inputEventId", inputEventId));
                return reject;
            }
            if (s == MotorState.Guard) P.ReleaseLocked("GuardToCast(EXP-MAGIC-01)");
            if (ChainAlive) EndChain("MagicCast(EXP-MAGIC-02)");
            _buffered = false;
            var def = SpellDefinition(m);
            Attack = NewAttack(def);
            Attack.Magic = m;
            Attack.MagicSource = source;
            var air = !P.Grounded;
            P.EnterLocked(air ? MotorState.AirAttack : MotorState.GroundAttack, "Cast:" + m.Id);
            if (!air) P.VelX = 0f;
            Log.Emit("magic.cast", Pl, "", s.ToString(), P.State.ToString(), "CastStart(EXP-MAGIC-01)", ("magicId", m.Id), ("effect", m.Effect),
                ("power", m.Power), ("castTime", m.CastTime), ("recovery", m.Recovery), ("source", source), ("inputEventId", inputEventId),
                ("attackInstanceId", Attack.InstanceId), ("startGameTime", _sim.GameTime), ("facing", P.Facing));
            return null;
        }

        /// <summary>Called every tick while a cast runs; applies the effect once when the cast time has passed.</summary>
        void StepSpell()
        {
            var a = Attack;
            if (a?.Magic == null || a.EffectDone || a.Elapsed < a.Def.Startup - TimerEps) return;
            a.EffectDone = true;
            var m = a.Magic;
            switch (m.Effect)
            {
                case MagicEffect.Attack:
                    Log.Emit("magic.effect", Pl, "", "", "", "AttackActive(EXP-MAGIC-04)", ("magicId", m.Id), ("source", a.MagicSource),
                        ("attackInstanceId", a.InstanceId), ("effectGameTime", _sim.GameTime), ("damageD", m.Power), ("damageType", "Magic"));
                    break;
                case MagicEffect.Heal:
                    var before = PlayerHp;
                    PlayerHp = Math.Min(PlayerMaxHp, PlayerHp + m.Power);
                    Log.Emit("magic.effect", Pl, "", "", "", "Heal(EXP-MAGIC-04)", ("magicId", m.Id), ("source", a.MagicSource),
                        ("attackInstanceId", a.InstanceId), ("effectGameTime", _sim.GameTime), ("hpBefore", before), ("hpAfter", PlayerHp));
                    break;
                case MagicEffect.External:
                    ExternalSpellEffect?.Invoke(a);
                    break;
                case MagicEffect.Buff:
                    BuffMultiplier = m.Power;
                    BuffUntil = _sim.GameTime + m.BuffDuration;
                    BuffMagicId = m.Id;
                    Log.Emit("magic.effect", Pl, "", "", "", "Buff(EXP-MAGIC-04)", ("magicId", m.Id), ("source", a.MagicSource),
                        ("attackInstanceId", a.InstanceId), ("effectGameTime", _sim.GameTime), ("multiplier", m.Power), ("until", BuffUntil));
                    break;
            }
        }

        void StepBuff()
        {
            if (BuffMultiplier == 1f || _sim.GameTime < BuffUntil - 1e-6) return;
            Log.Emit("magic.buffEnd", Pl, "", "", "", "BuffExpired(EXP-MAGIC-04)", ("magicId", BuffMagicId), ("multiplier", BuffMultiplier));
            BuffMultiplier = 1f;
            BuffMagicId = "";
        }

        /// <summary>R-06-19 revive (values EXP-DEATH-03, U-08): HP to ratio x max, invulnerable for a while, back to free.</summary>
        public void Revive(string reason)
        {
            var before = PlayerHp;
            PlayerHp = Math.Max(1f, PlayerMaxHp * _p.ReviveHpRatio);
            _reviveInvulnUntil = _sim.GameTime + _p.ReviveInvulnTime;
            P.ReviveFromDead(reason);
            Log.Emit("player.revive", Pl, "", "Dead", P.State.ToString(), reason, ("hpBefore", before), ("hpAfter", PlayerHp),
                ("invulnUntil", _reviveInvulnUntil), ("policy", "EXP-DEATH-03 (U-08)"));
        }
    }
}
