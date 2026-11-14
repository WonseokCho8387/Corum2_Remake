using System;
using System.Collections.Generic;

namespace Corum2.Core.P1
{
    /// <summary>Item receiver for fallback rewards and merchant goods (GRO-38: false = bag full, goes to the unclaimed queue).</summary>
    public interface IP1Bag
    {
        bool TryAdd(string itemId, int count);
    }

    /// <summary>Simple bag used by pure tests and as the P1 runtime bag model (capacity EXP-P1-BAG-01).</summary>
    public sealed class SimpleBag : IP1Bag
    {
        public readonly Dictionary<string, int> Items = new Dictionary<string, int>();
        public int StackLimit = 16;
        public int CountLimit = 99;

        public int Count(string id) => Items.TryGetValue(id, out var n) ? n : 0;

        public bool TryAdd(string itemId, int count)
        {
            var have = Count(itemId);
            if (have == 0 && Items.Count >= StackLimit) return false;
            if (have + count > CountLimit) return false;
            Items[itemId] = have + count;
            return true;
        }

        public bool Remove(string itemId, int n)
        {
            var have = Count(itemId);
            if (have < n) return false;
            if (have == n) Items.Remove(itemId);
            else Items[itemId] = have - n;
            return true;
        }
    }

    /// <summary>
    /// EXP and level-up queue (GRO-01~05, 44~50). EXP grants carry an event id and are applied once (GRO-48/49 replay
    /// safety). Each earned level is resolved in order: offer -> choice -> apply -> full heal to the new max (GRO-04/46).
    /// Lv50: the exp that fills the last level is used first and only that event's excess turns into the separate merchant
    /// currency (GRO-49, MER-22); later exp goes there directly.
    /// </summary>
    public sealed class ProgressionSystem
    {
        public readonly P1Profile P;
        public readonly GrowthRules Rules;
        public readonly GrowthOfferGenerator Gen;
        public readonly P1Progress G;
        public DiagLogger Log;
        public IP1Bag Bag;
        /// <summary>Applies a temporary buff card (BuffSystem). Returns false if refused (stack cap).</summary>
        public Func<string, Rarity, bool> AddBuff = (_, __) => true;
        /// <summary>Called after a level completes, with the new max HP/MP, to refill the live vitals (GRO-04).</summary>
        public Action<int, int> FullHeal;
        public readonly List<string> Warnings = new List<string>();

        public ProgressionSystem(P1Profile p, P1Progress g, DiagLogger log = null)
        {
            P = p;
            G = g;
            Rules = new GrowthRules(p);
            Gen = new GrowthOfferGenerator(Rules);
            Log = log;
        }

        public bool GrowthPending => G.PendingLevels > 0;

        void Emit(string type, string reason, params (string, object)[] f) => Log?.Emit(type, "player", "", "", "", reason, f);

        /// <summary>Grants EXP once per event id. Returns false when the event was already applied.</summary>
        public bool GrantExp(int amount, long eventId, string source = "")
        {
            if (amount <= 0) return true;
            if (eventId != 0 && !G.ExpEvents.Add(eventId))
            {
                Emit("p1.exp.duplicate", "EventAlreadyApplied(GRO-48)", ("eventId", eventId), ("amount", amount));
                return false;
            }
            G.TotalExp += amount;
            var levelsBefore = G.PendingLevels;
            var currencyBefore = G.Lv50Currency;
            if (G.TargetLevel >= P.MaxLevel)
            {
                G.Lv50Currency += (int)Math.Floor(amount * P.Lv50CurrencyRate);
            }
            else
            {
                G.LevelExp += amount;
                while (G.TargetLevel < P.MaxLevel && G.LevelExp >= P.ExpNeeded(G.TargetLevel))
                {
                    G.LevelExp -= P.ExpNeeded(G.TargetLevel);
                    G.PendingLevels++;
                }
                if (G.TargetLevel >= P.MaxLevel && G.LevelExp > 0)
                {
                    G.Lv50Currency += (int)Math.Floor(G.LevelExp * P.Lv50CurrencyRate);
                    G.LevelExp = 0;
                }
            }
            Emit("p1.exp.grant", source, ("eventId", eventId), ("amount", amount), ("level", G.Level), ("pendingLevels", G.PendingLevels),
                ("newLevels", G.PendingLevels - levelsBefore), ("levelExp", G.LevelExp), ("lv50Currency", G.Lv50Currency),
                ("lv50CurrencyGain", G.Lv50Currency - currencyBefore));
            EnsureOffer();
            return true;
        }

        /// <summary>Makes sure the next pending level has an offer; resolves zero-candidate levels on the spot (GRO-32).</summary>
        public void EnsureOffer()
        {
            while (G.PendingLevels > 0 && G.Offer == null)
            {
                var target = G.Level + 1;
                var offer = Gen.Generate(G, target);
                if (offer.Cards.Count == 0)
                {
                    var w = "GrowthSkipped(GRO-32) level=" + target + " " + offer.FilterStats;
                    Warnings.Add(w);
                    Emit("p1.growth.skip", "NoCandidates(GRO-32)", ("targetLevel", target), ("filterStats", offer.FilterStats));
                    CompleteLevel("skip");
                    continue;
                }
                G.Offer = offer;
                Emit("p1.growth.offer", "Offer(GRO-06/26)", ("targetLevel", target), ("budget", offer.Budget), ("cards", string.Join(",", offer.Cards)),
                    ("mandatory", string.Join(",", offer.MandatoryShown)), ("fallback", string.Join(",", offer.FallbackShown)), ("filterStats", offer.FilterStats));
            }
        }

        public GrowthOfferGenerator.RerollResult Reroll()
        {
            var r = Gen.Reroll(G);
            Emit("p1.growth.reroll", r.ToString(), ("source", G.Offer?.RerollSource ?? ""), ("cards", G.Offer != null ? string.Join(",", G.Offer.Cards) : ""),
                ("tokensLeft", G.RerollTokens));
            return r;
        }

        /// <summary>Choose card index of the current offer. Returns null on success, else the reject code.</summary>
        public string Choose(int index)
        {
            var o = G.Offer;
            if (o == null) return "NoOffer";
            if (index < 0 || index >= o.Cards.Count) return "BadIndex";
            var id = o.Cards[index];
            if (o.FallbackShown.Contains(id))
            {
                var f = P.Fallback(id);
                if (Bag == null || !Bag.TryAdd(f.ItemId, f.Count))
                {
                    G.Unclaimed.Add(new UnclaimedReward { ItemId = f.ItemId, Count = f.Count, Source = id + "@Lv" + o.TargetLevel });
                    Emit("p1.reward.unclaimed", "BagFull(GRO-38)", ("itemId", f.ItemId), ("count", f.Count));
                }
                G.CardCounts[id] = G.Count(id) + 1;
            }
            else
            {
                var c = P.Card(id);
                if (c.Kind == CardKind.Buff)
                {
                    if (!AddBuff(c.BuffId, c.Rarity)) return "BuffRefused";
                    G.CardCounts[id] = G.Count(id) + 1;
                }
                else Rules.Apply(G, c);
            }
            foreach (var mid in o.MandatoryShown)
            {
                if (mid == id) continue;
                var m = Gen.MandatoryByCard(mid);
                G.M(m.Id).DeclineCount++;
            }
            Emit("p1.growth.choose", "Chosen", ("targetLevel", o.TargetLevel), ("card", id), ("offer", string.Join(",", o.Cards)),
                ("declined", string.Join(",", o.MandatoryShown.FindAll(x => x != id))));
            G.Offer = null;
            CompleteLevel(id);
            EnsureOffer();
            return null;
        }

        void CompleteLevel(string how)
        {
            G.Level++;
            G.PendingLevels--;
            var hp = Rules.MaxHp(G);
            var mp = Rules.MaxMp(G);
            G.CurHp = hp;
            G.CurMp = mp;
            FullHeal?.Invoke(hp, mp);
            Emit("p1.level.up", "LevelComplete(GRO-04)", ("level", G.Level), ("how", how), ("maxHp", hp), ("maxMp", mp));
        }

        /// <summary>Moves queued rewards into the bag while there is room (GRO-38). Each queued entry is delivered once.</summary>
        public int ClaimUnclaimed()
        {
            var n = 0;
            for (var i = 0; i < G.Unclaimed.Count;)
            {
                var u = G.Unclaimed[i];
                if (Bag != null && Bag.TryAdd(u.ItemId, u.Count))
                {
                    G.Unclaimed.RemoveAt(i);
                    n++;
                    Emit("p1.reward.claim", "Claimed(GRO-38)", ("itemId", u.ItemId), ("count", u.Count));
                }
                else i++;
            }
            return n;
        }

        /// <summary>Merchant price reference (MER-13/18/23).</summary>
        public int PriceReference => G.Level >= P.MaxLevel ? P.Merchant.Lv50PriceReference : P.ExpNeeded(G.Level);

        public int Wallet => G.Level >= P.MaxLevel ? G.Lv50Currency : G.LevelExp;

        public bool Spend(int price)
        {
            if (Wallet < price) return false;
            if (G.Level >= P.MaxLevel) G.Lv50Currency -= price;
            else G.LevelExp -= price;
            return true;
        }
    }
}
