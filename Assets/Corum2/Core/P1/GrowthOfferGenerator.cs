using System;
using System.Collections.Generic;
using System.Text;

namespace Corum2.Core.P1
{
    /// <summary>
    /// Builds one growth screen (GRO-06~43, §3.5 deterministic procedure):
    ///  1. eligible mandatories, their 3/2/1 stage; strongest pressure sets the slot budget
    ///  2. mandatory slots by (first actually offered level, first eligible level, M order); overflow waits, no decline
    ///  3. natural slots from the pre-filtered pool: type 75/25 renormalised over non-empty types (GRO-29), rarity by
    ///     story table renormalised over open rarities that have cards (GRO-30), card weight (BUF-04 stack factor)
    ///  4. normal 3-card screen with 1~2 candidates: fallback cards to exactly 3 (GRO-31/36); 0 candidates: empty offer
    ///     (GRO-32 skip); 2/1 pressure screens are never filled
    /// </summary>
    public sealed class GrowthOfferGenerator
    {
        public readonly GrowthRules Rules;
        P1Profile P => Rules.P;

        public GrowthOfferGenerator(GrowthRules rules) => Rules = rules;

        /// <summary>Natural candidates after the pre-filter (no duplicates of ids in exclude).</summary>
        public List<CardDef> NaturalPool(P1Progress g, int targetLevel, ICollection<string> exclude, StringBuilder stats = null, bool merchantPack = false)
        {
            var pool = new List<CardDef>();
            var reasons = new Dictionary<string, int>();
            foreach (var c in P.Cards)
            {
                if (exclude != null && exclude.Contains(c.Id)) continue;
                var r = Rules.Reject(g, c, targetLevel, merchantPack);
                if (r == null) pool.Add(c);
                else if (stats != null)
                {
                    var key = r.Split(':')[0];
                    reasons[key] = reasons.TryGetValue(key, out var n) ? n + 1 : 1;
                }
            }
            if (stats != null)
            {
                stats.Append("eligible=").Append(pool.Count);
                foreach (var kv in reasons) stats.Append(' ').Append(kv.Key).Append('=').Append(kv.Value);
            }
            return pool;
        }

        double CardWeight(CardDef c)
        {
            double w = c.Weight;
            if (c.Kind == CardKind.Buff)
            {
                var n = Rules.BuffStacks(c.BuffId);
                for (var i = 0; i < n; i++) w *= P.BuffRules.ActiveStackWeightFactor;
            }
            return w;
        }

        /// <summary>One natural draw from the pool (removes the chosen card). Null when the pool is empty.</summary>
        public CardDef Draw(P1Progress g, List<CardDef> pool, P1Rng rng, Func<CardDef, double> extraWeight = null)
        {
            if (pool.Count == 0) return null;
            bool anyPerm = false, anyTemp = false;
            foreach (var c in pool) { if (c.Permanent) anyPerm = true; else anyTemp = true; }
            var typeW = new List<double> { anyPerm ? P.PermanentSlotWeight : 0, anyTemp ? P.TemporarySlotWeight : 0 };
            var t = rng.Weighted(typeW);
            var perm = t == 0;
            var rarRow = P.Rarities(g.Stage);
            var rw = new List<double>();
            for (var r = 0; r < 4; r++)
            {
                var has = false;
                foreach (var c in pool) if (c.Permanent == perm && (int)c.Rarity == r) { has = true; break; }
                rw.Add(has ? rarRow.Get((Rarity)r) : 0);
            }
            var ri = rng.Weighted(rw);
            if (ri < 0)
            {
                // only fixed-normal unlock cards in a stage whose normal weight is 0 would end here; use any card of the type
                for (var r = 0; r < 4; r++)
                    foreach (var c in pool) if (c.Permanent == perm && (int)c.Rarity == r) { ri = r; break; }
            }
            var bucket = new List<CardDef>();
            var cw = new List<double>();
            foreach (var c in pool)
                if (c.Permanent == perm && (int)c.Rarity == ri)
                {
                    bucket.Add(c);
                    cw.Add(CardWeight(c) * (extraWeight?.Invoke(c) ?? 1.0));
                }
            var k = rng.Weighted(cw);
            if (k < 0) k = 0;
            var pick = bucket[k];
            pool.Remove(pick);
            return pick;
        }

        /// <summary>Builds the offer for target level and records mandatory first-eligible / displayed state.</summary>
        public GrowthOffer Generate(P1Progress g, int targetLevel)
        {
            var offer = new GrowthOffer { TargetLevel = targetLevel };
            var eligible = new List<MandatoryDef>();
            foreach (var m in P.Mandatory)
                if (Rules.MandatoryEligible(g, m, targetLevel))
                {
                    var st = g.M(m.Id);
                    if (st.FirstEligibleLevel == 0) st.FirstEligibleLevel = targetLevel;
                    eligible.Add(m);
                }
            var budget = 3;
            foreach (var m in eligible) budget = Math.Min(budget, GrowthRules.Budget(g.M(m.Id)));
            offer.Budget = budget;
            eligible.Sort((a, b) =>
            {
                var sa = g.M(a.Id);
                var sb = g.M(b.Id);
                // strongest pressure first only matters for the 1-card case; the documented order is first offered,
                // then first eligible, then M1..M4
                var pa = GrowthRules.Budget(sa);
                var pb = GrowthRules.Budget(sb);
                if (budget == 1 && pa != pb) return pa.CompareTo(pb);
                var oa = sa.FirstOfferedLevel == 0 ? int.MaxValue : sa.FirstOfferedLevel;
                var ob = sb.FirstOfferedLevel == 0 ? int.MaxValue : sb.FirstOfferedLevel;
                if (oa != ob) return oa.CompareTo(ob);
                if (sa.FirstEligibleLevel != sb.FirstEligibleLevel) return sa.FirstEligibleLevel.CompareTo(sb.FirstEligibleLevel);
                return a.Order.CompareTo(b.Order);
            });
            foreach (var m in eligible)
            {
                if (offer.Cards.Count >= budget) break;
                offer.Cards.Add(m.CardId);
                offer.MandatoryShown.Add(m.CardId);
            }
            var stats = new StringBuilder();
            var pool = NaturalPool(g, targetLevel, offer.Cards, stats);
            while (offer.Cards.Count < budget)
            {
                var c = Draw(g, pool, g.GrowthRng);
                if (c == null) break;
                offer.Cards.Add(c.Id);
            }
            if (budget == 3 && offer.Cards.Count >= 1 && offer.Cards.Count < 3)
            {
                // GRO-31/36: fill with distinct fallback cards. One natural -> both kinds; two -> one drawn from the pool.
                var fb = new List<FallbackDef>();
                foreach (var f in P.Fallbacks) if (f.Enabled) fb.Add(f);
                while (offer.Cards.Count < 3 && fb.Count > 0)
                {
                    var i = fb.Count == 1 ? 0 : g.GrowthRng.Range(0, fb.Count);
                    offer.Cards.Add(fb[i].CardId);
                    offer.FallbackShown.Add(fb[i].CardId);
                    fb.RemoveAt(i);
                }
                stats.Append(" fallbackFilled=").Append(offer.FallbackShown.Count);
            }
            offer.FilterStats = stats.ToString();
            foreach (var id in offer.MandatoryShown)
            {
                var m = MandatoryByCard(id);
                var st = g.M(m.Id);
                st.DisplayedCount++;
                if (st.FirstOfferedLevel == 0) st.FirstOfferedLevel = targetLevel;
                st.LastOfferedLevel = targetLevel;
            }
            return offer;
        }

        public MandatoryDef MandatoryByCard(string cardId)
        {
            foreach (var m in P.Mandatory) if (m.CardId == cardId) return m;
            return null;
        }

        public enum RerollResult { Changed, NoChangePossible, AlreadyUsed, NoResource, NoOffer }

        /// <summary>
        /// GRO-10/33/40, MER-24: replace natural slots only; mandatory and fallback slots stay. Token first, then the
        /// equipment option; nothing is spent when no slot can really change. At most RerollsPerLevel per level-up.
        /// </summary>
        public RerollResult Reroll(P1Progress g)
        {
            var o = g.Offer;
            if (o == null) return RerollResult.NoOffer;
            if (o.RerollUsed) return RerollResult.AlreadyUsed;
            if (g.RerollTokens <= 0 && !g.EquipRerollOption) return RerollResult.NoResource;
            var naturalIdx = new List<int>();
            for (var i = 0; i < o.Cards.Count; i++) if (o.IsNatural(o.Cards[i])) naturalIdx.Add(i);
            var pool = NaturalPool(g, o.TargetLevel, o.Cards);
            if (naturalIdx.Count == 0 || pool.Count == 0) return RerollResult.NoChangePossible;
            if (g.RerollTokens > 0) { g.RerollTokens--; o.RerollSource = "Token"; }
            else o.RerollSource = "EquipmentOption";
            o.RerollUsed = true;
            foreach (var i in naturalIdx)
            {
                var c = Draw(g, pool, g.GrowthRng);
                if (c == null) break; // not enough distinct replacements: the rest keep their card
                o.Cards[i] = c.Id;
            }
            return RerollResult.Changed;
        }
    }
}
