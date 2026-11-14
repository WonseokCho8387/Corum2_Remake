using System;
using System.Collections.Generic;

namespace Corum2.Core.P1
{
    /// <summary>
    /// ICombatExtension implementation: puts the P1 damage order (BAL-01~09), permanent bonuses (GRO-15~20, §4.2),
    /// sword enchants (MAG-71/72/78/79), shields (MAG-74/75, BAL-09), lifesteal per attack event (BAL-08), EXP on kills
    /// and the same-tick level-up survival (GRO-45) into the P0 combat core.
    /// </summary>
    public sealed class P1CombatBridge : ICombatExtension
    {
        readonly P1Profile _p;
        readonly ProgressionSystem _prog;
        readonly BuffSystem _buffs;
        readonly P1Effects _fx;
        readonly CombatSystem _combat;
        readonly DiagLogger _log;
        P1Progress G => _prog.G;
        GrowthRules R => _prog.Rules;

        struct StealAcc { public float Lost; public float Rate; }
        readonly Dictionary<(long, int), StealAcc> _steal = new Dictionary<(long, int), StealAcc>();
        bool _levelUpThisTick, _holdCandidate;

        public P1CombatBridge(P1Profile p, ProgressionSystem prog, BuffSystem buffs, P1Effects fx, CombatSystem combat, DiagLogger log)
        {
            _p = p;
            _prog = prog;
            _buffs = buffs;
            _fx = fx;
            _combat = combat;
            _log = log;
        }

        public float MaxHp => R.MaxHp(G);
        public float LaunchHeightBonus => G.Axis("skill.launcher.height");
        public float LaunchHangBonus => G.Axis("skill.launcher.time");

        static bool IsAir(AttackKind k) => k == AttackKind.AirNormal || k == AttackKind.AirDash || k == AttackKind.Dive;

        /// <summary>Capped permanent technique bonus for this attack and target (GRO-15~20; BAL-07 two down bonuses added).</summary>
        public float TechniqueBonus(AttackKind kind, bool targetDowned)
        {
            switch (kind)
            {
                case AttackKind.GroundNormal: return G.Axis("skill.groundBasic.dmg");
                case AttackKind.AirNormal: return G.Axis("skill.airBasic.dmg");
                case AttackKind.AirDash: return G.Axis("skill.airDash.dmg");
                case AttackKind.Dive: return targetDowned ? G.Axis("skill.airDiveDowned.dmg") + G.Axis("skill.downAttack.dmg") : 0f;
                case AttackKind.DownAttack: return targetDowned ? G.Axis("skill.downAttack.dmg") : 0f;
                default: return 0f;
            }
        }

        /// <summary>Pre-buff attack of a player physical motion (BAL-01).</summary>
        public float PhysicalPreBuff(PlayerAttack a, bool targetDowned, float d)
        {
            var pre = d + TechniqueBonus(a.Def.Kind, targetDowned) + R.Stat(G, "PhysAtk");
            var sw = _fx.Sword;
            if (sw != null && sw.Light && a.Def.Sword)
            {
                pre += sw.Bonus;
                if (sw.LastHit > 0f && a.NormalIndex == 4) pre += sw.LastHit; // epic: bonus on the same 4th-hit event
            }
            return pre;
        }

        /// <summary>Temporary attack buff ratio for a physical attack: PATK and AIR capped separately then added (BUF-07/08).</summary>
        public float PhysicalBuff(AttackKind kind) => _buffs.PhysAtk + (IsAir(kind) ? _buffs.AirAtk : 0f);

        public float OutgoingDamage(PlayerAttack a, EnemyActor en, float d, bool enemyGuarded)
        {
            var magic = a.Def.DamageType == DamageType.Magic;
            float pre, buff;
            if (a.P1PreBuff >= 0f)
            {
                pre = a.P1PreBuff;
                buff = magic ? _buffs.MagAtk : _buffs.PhysAtk;
            }
            else if (magic)
            {
                pre = d + R.Stat(G, "MagAtk");
                buff = _buffs.MagAtk;
            }
            else
            {
                pre = PhysicalPreBuff(a, en.State == EnemyState.Down, d);
                buff = PhysicalBuff(a.Def.Kind);
            }
            var raw = DamageCalc.Raw(pre, buff);
            var dmg = DamageCalc.PreShield(_p.Damage, raw, enemyGuarded, en.Def.Defense, 0f);
            _log?.Emit("p1.damage.out", "player", en.Id, "", "", "BAL-01~06",
                ("attackInstanceId", a.InstanceId), ("attackKind", a.Def.Kind), ("source", a.P1Source), ("base", d), ("preBuff", pre), ("buff", buff),
                ("raw", raw), ("guarded", enemyGuarded), ("targetDefense", en.Def.Defense), ("damage", dmg), ("normalIndex", a.NormalIndex));
            return dmg;
        }

        public float IncomingDamage(AttackDefinition d, float dmg, bool guarded, string ownerId)
        {
            var def = R.Stat(G, "Defense");
            var pre = DamageCalc.PreShield(_p.Damage, dmg, guarded, def, _buffs.DamageReduction);
            var absorbed = new List<(string, int)>();
            var hp = DamageCalc.Absorb(pre, _fx.ShieldOrder(), absorbed);
            _log?.Emit("p1.damage.in", ownerId, "player", "", "", "BAL-05/06/09",
                ("attackId", d.Id), ("raw", dmg), ("guarded", guarded), ("defense", def), ("tempReduction", _buffs.DamageReduction),
                ("preShield", pre), ("absorbed", string.Join(",", absorbed.ConvertAll(x => x.Item1 + ":" + x.Item2))), ("hpDamage", hp));
            return hp;
        }

        public void OnEnemyHpLost(PlayerAttack a, EnemyActor en, int hitEvent, float lost, bool killed)
        {
            var sw = _fx.Sword;
            var physical = a.Def.DamageType != DamageType.Magic && !a.External;
            if (sw != null && sw.Dark && physical && lost > 0f)
            {
                var key = (a.InstanceId, hitEvent);
                _steal.TryGetValue(key, out var acc);
                acc.Lost += lost;
                var combo = Math.Max(0, _combat.ComboHits - 1);
                acc.Rate = sw.Rate + Math.Min(sw.ComboCap, sw.ComboRate * combo);
                _steal[key] = acc;
            }
            if (!killed) return;
            if (sw != null && sw.Dark && physical && sw.KillHeal > 0f)
            {
                var before = _combat.PlayerHp;
                _combat.SetPlayerHp(before + sw.KillHeal, "DarkSwordKillHeal(MAG-72 epic)");
            }
            if (en.Def.Exp > 0)
            {
                var pending = G.PendingLevels;
                _prog.GrantExp(en.Def.Exp, G.NextEventId++, "kill:" + en.Id);
                if (G.PendingLevels > pending) _levelUpThisTick = true;
            }
        }

        public void EndTick()
        {
            foreach (var kv in _steal)
            {
                var heal = DamageCalc.Lifesteal(kv.Value.Lost, kv.Value.Rate, _combat.PlayerHp, _combat.PlayerMaxHp);
                _log?.Emit("p1.lifesteal", "player", "", "", "", "BAL-08", ("attackInstanceId", kv.Key.Item1), ("hitEvent", kv.Key.Item2),
                    ("summedLoss", kv.Value.Lost), ("rate", kv.Value.Rate), ("heal", heal));
                if (heal > 0) _combat.SetPlayerHp(_combat.PlayerHp + heal, "DarkSwordLifesteal(BAL-08)");
            }
            _steal.Clear();
            _holdCandidate = _levelUpThisTick;
            _levelUpThisTick = false;
        }

        public bool HoldDeath() => _holdCandidate && _prog.GrowthPending;
    }
}
