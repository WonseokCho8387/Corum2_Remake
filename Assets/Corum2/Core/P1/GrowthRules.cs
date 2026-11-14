using System;
using System.Collections.Generic;

namespace Corum2.Core.P1
{
    /// <summary>
    /// Card eligibility and effect arithmetic shared by level-up, reroll and merchant packs (MAG-100/101 single function,
    /// GRO-09/28, MAG-21/33/55/57/68). Pure: reads profile + progress, never mutates except Apply.
    /// </summary>
    public sealed class GrowthRules
    {
        public readonly P1Profile P;
        /// <summary>Active stack count of a buff id (BUF-09 cap, BUF-04 weight). Defaults to 0.</summary>
        public Func<string, int> BuffStacks = _ => 0;

        public GrowthRules(P1Profile p) => P = p;

        public CharacterDef Char(P1Progress g) => P.Character(g.CharacterId);

        // ---------------- derived stats ----------------

        public int BaseStat(P1Progress g, string stat)
        {
            var c = Char(g);
            switch (stat)
            {
                case "MaxHp": return c.BaseMaxHp;
                case "MaxMp": return c.BaseMaxMp;
                default: return 0;
            }
        }

        public int StatCap(P1Progress g, string stat)
        {
            var c = Char(g);
            switch (stat)
            {
                case "MaxHp": return c.CapMaxHp;
                case "MaxMp": return c.CapMaxMp;
                case "PhysAtk": return c.CapPhysAtk;
                case "MagAtk": return c.CapMagAtk;
                default: return c.CapDefense;
            }
        }

        /// <summary>Base + permanent value of a stat (already clamped to the absolute cap when applied).</summary>
        public float Stat(P1Progress g, string stat) => BaseStat(g, stat) + g.Axis("stat." + stat);

        public int MaxHp(P1Progress g) => (int)Stat(g, "MaxHp");
        public int MaxMp(P1Progress g) => (int)Stat(g, "MaxMp");

        /// <summary>MAG-100: character base max MP + permanent growth only (no equipment, buff or current MP).</summary>
        public int EligibleMaxMp(P1Progress g) => Math.Min(Char(g).CapMaxMp, MaxMp(g));

        /// <summary>Permanent command cost after the given extra change, with the per-spell floor (MAG-09/101/102).</summary>
        public int SpellCost(P1Progress g, string spellId, int extraDelta = 0)
        {
            var s = P.Spell(spellId);
            if (s == null) return 0;
            return Math.Max(s.MinMp, s.BaseMp + (int)g.Axis("cost." + spellId) + extraDelta);
        }

        // ---------------- magic missile model ----------------

        public int MissileProjectiles(P1Progress g) =>
            Math.Min(P.MissileMaxProjectiles, P.MissileBaseProjectiles + (int)g.Axis("mm.proj"));

        public float MissileBaseDamage => P.Spell(P1Defaults.MissileId)?.P("damage", 3) ?? 3;

        /// <summary>Per-projectile permanent damage segment: base + cards + magic attack, capped (MAG-36/37).</summary>
        public float MissilePermanentDamage(P1Progress g) =>
            Math.Min(P.MissilePermanentDamageCap, MissileBaseDamage + g.Axis("mm.dmg") + Stat(g, "MagAtk"));

        public void Homing(int n, out float angle, out float range, out float turn)
        {
            if (n <= 0) { angle = range = turn = 0; return; }
            angle = Math.Min(P.HomingAngleFirst + (n - 1) * P.HomingAngleStep, P.HomingAngleCap);
            range = Math.Min(P.HomingRangeFirst + (n - 1) * P.HomingRangeStep, P.HomingRangeCap);
            turn = Math.Min(P.HomingTurnFirst + (n - 1) * P.HomingTurnStep, P.HomingTurnCap);
        }

        // ---------------- axes ----------------

        /// <summary>Limit of an axis' accumulated value in the direction of its delta.</summary>
        public float AxisLimit(P1Progress g, CardAxis a)
        {
            if (a.Axis.StartsWith("stat.", StringComparison.Ordinal))
            {
                var st = a.Axis.Substring(5);
                return StatCap(g, st) - BaseStat(g, st);
            }
            switch (a.Axis)
            {
                case "mm.dmg":
                    // the card part may grow until base + cards + magic attack reaches the per-projectile cap
                    return Math.Max(g.Axis("mm.dmg"), P.MissilePermanentDamageCap - MissileBaseDamage - Stat(g, "MagAtk"));
                case "mm.proj": return P.MissileMaxProjectiles - P.MissileBaseProjectiles;
                default: return a.Limit;
            }
        }

        /// <summary>Value the axis would have after one more card.</summary>
        public float AxisAfter(P1Progress g, CardAxis a)
        {
            var cur = g.Axis(a.Axis);
            if (a.Axis == "mm.homing") return cur + 1;
            var step = cur == 0f && a.First != 0f ? a.First : a.Delta;
            var lim = AxisLimit(g, a);
            return a.Delta >= 0 ? Math.Max(cur, Math.Min(lim, cur + step)) : Math.Min(cur, Math.Max(lim, cur + step));
        }

        /// <summary>Real effect gained on one axis (0 = capped).</summary>
        public float AxisGain(P1Progress g, CardAxis a)
        {
            if (a.Axis == "mm.homing")
            {
                var n = (int)g.Axis("mm.homing");
                Homing(n, out var a0, out var r0, out var t0);
                Homing(n + 1, out var a1, out var r1, out var t1);
                return (a1 - a0) + (r1 - r0) + (t1 - t0);
            }
            return Math.Abs(AxisAfter(g, a) - g.Axis(a.Axis));
        }

        /// <summary>Total useful effect of a card now; MP cost increase alone is not an effect (§4.7).</summary>
        public float CardGain(P1Progress g, CardDef c)
        {
            float gain = 0;
            foreach (var a in c.Axes) gain += AxisGain(g, a);
            if (c.MpDelta < 0 && !string.IsNullOrEmpty(c.SpellId) && SpellCost(g, c.SpellId, c.MpDelta) < SpellCost(g, c.SpellId)) gain += 1;
            return gain;
        }

        // ---------------- eligibility ----------------

        /// <summary>Pre-draw filter (GRO-28). Returns null when eligible, else the reason. Mandatory and fallback cards are
        /// never natural candidates.</summary>
        public string Reject(P1Progress g, CardDef c, int targetLevel, bool merchantPack = false)
        {
            if (c.Kind == CardKind.Fallback) return "FallbackNotNatural";
            if (c.Kind == CardKind.Mandatory) return merchantPack ? "MandatoryNotInPack(MER-19)" : "MandatoryReservedSlot";
            if (merchantPack && c.ExcludedFromMerchantPack) return "ExcludedFromPack";
            if (c.OneTime && g.Count(c.Id) > 0) return "OneTimeOwned";
            if (!string.IsNullOrEmpty(c.Unlocks) && g.Has(c.Unlocks)) return "AlreadyLearned";
            if (!string.IsNullOrEmpty(c.RequiresLearned) && !g.Has(c.RequiresLearned)) return "RequiresLearned:" + c.RequiresLearned;
            if (c.MinLevel > targetLevel) return "Level<" + c.MinLevel;
            if (c.Kind != CardKind.Buff && c.Kind != CardKind.SpellUnlock && P.Rarities(g.Stage).Get(c.Rarity) <= 0f)
                return "StoryRarityLocked:" + c.Rarity;
            if (!string.IsNullOrEmpty(c.SpellId) && (c.Kind == CardKind.SpellUnlock || c.Kind == CardKind.SpellUpgrade || c.Kind == CardKind.Missile))
            {
                var cost = SpellCost(g, c.SpellId, c.Kind == CardKind.SpellUnlock ? 0 : c.MpDelta);
                if (cost > EligibleMaxMp(g)) return "PostCardMpCost" + cost + ">EligibleMaxMp" + EligibleMaxMp(g);
            }
            if (c.Kind == CardKind.Buff)
            {
                var b = P.Buff(c.BuffId);
                if (b == null) return "UnknownBuff";
                if (BuffStacks(c.BuffId) >= b.MaxStacks) return "BuffStackCap(BUF-09)";
                if (P.Rarities(g.Stage).Get(c.Rarity) <= 0f) return "StoryRarityLocked:" + c.Rarity;
                return null;
            }
            if (c.Kind == CardKind.SpellUnlock) return null;
            if (CardGain(g, c) <= 0f) return "NoEffect(capped)";
            return null;
        }

        public bool MandatoryEligible(P1Progress g, MandatoryDef m, int targetLevel)
        {
            if (g.Has(m.Id) || targetLevel < m.FirstLevel) return false;
            foreach (var pre in m.PrerequisiteMandatoryIds) if (!g.Has(pre)) return false;
            var card = P.Card(m.CardId);
            if (card != null && !string.IsNullOrEmpty(card.SpellId) && SpellCost(g, card.SpellId) > EligibleMaxMp(g)) return false;
            return true;
        }

        /// <summary>3 -&gt; 2 -&gt; 1 budget of one mandatory from its decline count (§3.4).</summary>
        public static int Budget(MandatoryState s) => s.DeclineCount <= 0 ? 3 : s.DeclineCount == 1 ? 2 : 1;

        // ---------------- apply ----------------

        /// <summary>Applies a permanent card's axes / unlocks (buff and fallback effects are applied by their systems).</summary>
        public void Apply(P1Progress g, CardDef c)
        {
            foreach (var a in c.Axes) g.Axes[a.Axis] = AxisAfter(g, a);
            if (c.MpDelta != 0 && !string.IsNullOrEmpty(c.SpellId)) g.Axes["cost." + c.SpellId] = g.Axis("cost." + c.SpellId) + c.MpDelta;
            if (!string.IsNullOrEmpty(c.Unlocks)) g.Learned.Add(c.Unlocks);
            g.CardCounts[c.Id] = g.Count(c.Id) + 1;
        }
    }
}
