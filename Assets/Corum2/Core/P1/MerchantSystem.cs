using System;
using System.Collections.Generic;

namespace Corum2.Core.P1
{
    [Serializable]
    public sealed class SafeSpawnDef
    {
        public string MapId = "";
        public float X, Y;
        /// <summary>Test switch for MER-28: this registered spawn is occupied / blocked right now.</summary>
        public bool Blocked;
    }

    /// <summary>A region = several connected maps; the merchant is judged per region (MER-01). Sandbox content (EXP-P1-REGION-01).</summary>
    [Serializable]
    public sealed class RegionDef
    {
        public string Id = "";
        public List<string> MapIds = new List<string>();
        public List<SafeSpawnDef> SafeSpawns = new List<SafeSpawnDef>();
    }

    public sealed class MerchantStock
    {
        public string Kind = "";
        public string BuffId = "";
        public Rarity Rarity;
        public bool Sold;
    }

    public enum RegionMerchant { NotJudged, NotEligible, Absent, Present, Departed, SpawnBlockedAfterSuccessfulRoll }

    public sealed class RegionState
    {
        public string RegionId = "";
        public RegionMerchant State = RegionMerchant.NotJudged;
        public double Roll = -1;
        public double Chance;
        public string MapId = "";
        public float X, Y;
        public readonly List<MerchantStock> Stock = new List<MerchantStock>();
    }

    /// <summary>
    /// Wandering magic merchant (MER-01~30, GRO-34/50). Region judged once (15 %, +10 %p per miss, reset on appearance,
    /// max 100 %); a region with no registered safe spawn is not eligible (no roll, no counter change, MER-27); a
    /// successful roll whose spawns are all blocked records an error state and changes no counter (MER-28); results,
    /// positions, stock and the rng are saved so re-entry / load never rerolls (MER-29). Shop: 3~5 fixed goods; packs are
    /// an in-shop 3-pick-1 committed atomically at the final choice (MER-21); leaving the shop or the region asks first and
    /// a confirmed leave removes the merchant (MER-06/25); interruptions cancel the uncommitted choice for free (MER-26).
    /// </summary>
    public sealed class MerchantSystem
    {
        readonly P1Runtime _rt;
        P1Profile P => _rt.P;
        P1Progress G => _rt.G;
        DiagLogger Log => _rt.Sim.Log;
        public readonly List<RegionDef> Regions = new List<RegionDef>();
        public readonly Dictionary<string, RegionState> States = new Dictionary<string, RegionState>();
        public int MissStreak;
        public bool FirstEncounterDone;
        public readonly P1Rng Rng = new P1Rng(0xC0FFEE1234UL);
        public string CurrentRegion = "";

        // shop UI state (not saved: an open shop is never part of a checkpoint)
        public bool ShopOpen { get; private set; }
        public bool ConfirmLeave { get; private set; }
        public int Cursor;
        public MerchantStock PackStock;
        public readonly List<string> PackCards = new List<string>();
        public string LastResult = "";

        public MerchantSystem(P1Runtime rt) => _rt = rt;

        void Emit(string type, string reason, params (string, object)[] f) => Log.Emit(type, "merchant", "", "", "", reason, f);

        public RegionDef RegionOfMap(string mapId)
        {
            foreach (var r in Regions) if (r.MapIds.Contains(mapId)) return r;
            return null;
        }

        public RegionState State(string regionId)
        {
            if (!States.TryGetValue(regionId, out var s)) States[regionId] = s = new RegionState { RegionId = regionId };
            return s;
        }

        public RegionState Here => CurrentRegion == "" ? null : State(CurrentRegion);
        public bool PresentHere => Here != null && Here.State == RegionMerchant.Present;
        public double ChanceNow => Math.Min(P.Merchant.MaxChance, P.Merchant.BaseChance + P.Merchant.ChanceStep * MissStreak);

        /// <summary>Map entered (start, transition, load). A different region is judged once.</summary>
        public void OnMapEntered(string mapId)
        {
            var r = RegionOfMap(mapId);
            var id = r?.Id ?? "";
            if (id == CurrentRegion) return;
            CurrentRegion = id;
            if (r == null) return;
            var st = State(id);
            if (st.State != RegionMerchant.NotJudged)
            {
                Emit("p1.merchant.reenter", "AlreadyJudged(MER-03/29)", ("region", id), ("state", st.State));
                return;
            }
            if (r.SafeSpawns.Count == 0)
            {
                st.State = RegionMerchant.NotEligible;
                Emit("p1.merchant.notEligible", "NoSafeSpawn(MER-27)", ("region", id), ("missStreak", MissStreak));
                return;
            }
            st.Chance = ChanceNow;
            st.Roll = Rng.NextDouble();
            if (st.Roll >= st.Chance)
            {
                st.State = RegionMerchant.Absent;
                MissStreak++;
                Emit("p1.merchant.roll", "Miss(MER-02)", ("region", id), ("chance", st.Chance), ("roll", st.Roll), ("missStreak", MissStreak));
                return;
            }
            var open = new List<SafeSpawnDef>();
            foreach (var s in r.SafeSpawns) if (!s.Blocked) open.Add(s);
            if (open.Count == 0)
            {
                st.State = RegionMerchant.SpawnBlockedAfterSuccessfulRoll;
                Emit("p1.merchant.error", "SpawnBlockedAfterSuccessfulRoll(MER-28)", ("region", id), ("chance", st.Chance), ("roll", st.Roll),
                    ("missStreak", MissStreak));
                return;
            }
            var sp = open[Rng.Range(0, open.Count)];
            st.State = RegionMerchant.Present;
            st.MapId = sp.MapId;
            st.X = sp.X;
            st.Y = sp.Y;
            MakeStock(st);
            MissStreak = 0;
            FirstEncounterDone = true;
            Emit("p1.merchant.appear", "Appear(MER-02/04)", ("region", id), ("chance", st.Chance), ("roll", st.Roll), ("map", st.MapId), ("x", st.X), ("y", st.Y),
                ("stock", Describe(st)));
        }

        void MakeStock(RegionState st)
        {
            var n = Rng.Range(P.Merchant.MinStock, P.Merchant.MaxStock + 1);
            var first = !FirstEncounterDone;
            for (var i = 0; i < n; i++)
            {
                var w = new List<double>();
                foreach (var p in P.Merchant.Products) w.Add(first ? p.FirstVisitWeight : p.Weight);
                var prod = P.Merchant.Products[Math.Max(0, Rng.Weighted(w))];
                var s = new MerchantStock { Kind = prod.Kind };
                if (prod.Kind == "Buff")
                {
                    var b = P.Buffs[Rng.Range(0, P.Buffs.Count)];
                    s.BuffId = b.Id;
                    // MER-20: only rarities open in the current story stage
                    var rw = new List<double>();
                    for (var r = 0; r < 4; r++) rw.Add(P.Rarities(G.Stage).Get((Rarity)r));
                    s.Rarity = (Rarity)Math.Max(0, Rng.Weighted(rw));
                }
                st.Stock.Add(s);
            }
        }

        public static string Describe(RegionState st)
        {
            var l = new List<string>();
            foreach (var s in st.Stock) l.Add(s.Kind + (s.BuffId != "" ? ":" + s.BuffId + "." + P1Defaults.R(s.Rarity) : "") + (s.Sold ? "(sold)" : ""));
            return string.Join(",", l);
        }

        /// <summary>MER-13/18/23: ceil(max(reference x ratio, minimum)).</summary>
        public int Price(string kind)
        {
            var p = P.Product(kind);
            // ratios are editor floats (0.8f = 0.80000001): round to 6 decimals before the ceil so 500 x 0.8 stays 400
            var raw = Math.Round(_rt.Prog.PriceReference * Math.Round((double)p.PriceRatio, 6), 6);
            return (int)Math.Ceiling(Math.Max(raw, p.MinPrice));
        }

        public bool NearMerchant(float range = 1.5f)
        {
            var st = Here;
            if (st == null || st.State != RegionMerchant.Present || _rt.Sim.CurrentMap.Id != st.MapId) return false;
            return Math.Abs(_rt.Sim.Player.Position.X - st.X) <= range;
        }

        /// <summary>Opens the shop. Refused while growth choices are pending (GRO-50).</summary>
        public string OpenShop()
        {
            if (!PresentHere) return "NoMerchant";
            if (_rt.Prog.GrowthPending) { Emit("p1.merchant.refuse", "GrowthPending(GRO-50)"); return "GrowthPending(GRO-50)"; }
            ShopOpen = true;
            ConfirmLeave = false;
            Cursor = 0;
            _rt.UiPause = true;
            _rt.Magic.ClearBuffer("ShopOpen(MAG-99)");
            Emit("p1.merchant.open", "ShopOpen", ("stock", Describe(Here)), ("wallet", _rt.Prog.Wallet));
            return null;
        }

        /// <summary>Picks a stock entry. Packs open the in-shop 3-pick-1 (nothing paid yet); buffs and tokens commit at once.</summary>
        public string Buy(int index)
        {
            if (!ShopOpen || PackStock != null) return "NotReady";
            var st = Here;
            if (index < 0 || index >= st.Stock.Count || st.Stock[index].Sold) return "NoStock";
            var s = st.Stock[index];
            var price = Price(s.Kind);
            if (_rt.Prog.Wallet < price) return Result("WalletShort:" + _rt.Prog.Wallet + "<" + price + "(MER-18)");
            switch (s.Kind)
            {
                case "PermPack":
                case "TempPack":
                {
                    if (s.Kind == "PermPack" && G.PermPackUses >= P.Merchant.PermPackUseLimit) return Result("PermPackLimit(MER-11)");
                    var cards = DrawPack(s.Kind == "PermPack");
                    if (cards.Count < 3) return Result("PackFewerThan3Eligible(GRO-34):" + cards.Count);
                    PackStock = s;
                    PackCards.Clear();
                    PackCards.AddRange(cards);
                    Emit("p1.merchant.pack", "PackShown(MER-21, not paid)", ("kind", s.Kind), ("cards", string.Join(",", cards)), ("price", price));
                    return null;
                }
                case "Buff":
                    if (_rt.Buffs.AtCap(s.BuffId)) return Result("BuffStackCap(BUF-09)");
                    _rt.Prog.Spend(price);
                    s.Sold = true;
                    _rt.Buffs.Add(s.BuffId, s.Rarity, "Merchant");
                    return Result(null, "BuffBought", price);
                default:
                    _rt.Prog.Spend(price);
                    s.Sold = true;
                    G.RerollTokens++;
                    return Result(null, "RerollTokenBought", price);
            }
        }

        string Result(string reject, string ok = "", int price = 0)
        {
            LastResult = reject ?? ok;
            Emit(reject == null ? "p1.merchant.buy" : "p1.merchant.refuse", reject ?? ok, ("price", price), ("wallet", _rt.Prog.Wallet), ("permPackUses", G.PermPackUses));
            return reject;
        }

        List<string> DrawPack(bool permanent)
        {
            var pool = _rt.Prog.Gen.NaturalPool(G, G.Level, null, null, merchantPack: true);
            pool.RemoveAll(c => c.Permanent != permanent);
            var cards = new List<string>();
            while (cards.Count < 3 && pool.Count > 0) cards.Add(_rt.Prog.Gen.Draw(G, pool, Rng).Id);
            return cards;
        }

        /// <summary>Final 3-pick-1 choice: re-check eligibility, wallet and limit, then pay + stock + use count + growth together.</summary>
        public string ChoosePack(int i)
        {
            var s = PackStock;
            if (s == null || i < 0 || i >= PackCards.Count) return "NoPack";
            var card = P.Card(PackCards[i]);
            var price = Price(s.Kind);
            string reject = null;
            if (_rt.Prog.Rules.Reject(G, card, G.Level, merchantPack: true) != null) reject = "CardNoLongerEligible";
            else if (_rt.Prog.Wallet < price) reject = "WalletShort";
            else if (s.Kind == "PermPack" && G.PermPackUses >= P.Merchant.PermPackUseLimit) reject = "PermPackLimit";
            else if (card.Kind == CardKind.Buff && _rt.Buffs.AtCap(card.BuffId)) reject = "BuffStackCap(BUF-09)";
            if (reject != null)
            {
                CancelPack("FinalCheckFailed:" + reject + "(MER-21)");
                return reject;
            }
            _rt.Prog.Spend(price);
            s.Sold = true;
            if (s.Kind == "PermPack") G.PermPackUses++;
            if (card.Kind == CardKind.Buff) { _rt.Buffs.Add(card.BuffId, card.Rarity, "MerchantPack"); G.CardCounts[card.Id] = G.Count(card.Id) + 1; }
            else _rt.Prog.Rules.Apply(G, card);
            Emit("p1.merchant.commit", "AtomicPackCommit(MER-21)", ("card", card.Id), ("price", price), ("wallet", _rt.Prog.Wallet), ("permPackUses", G.PermPackUses));
            PackStock = null;
            PackCards.Clear();
            LastResult = "PackCommitted:" + card.Id;
            return null;
        }

        public void CancelPack(string reason)
        {
            if (PackStock == null) return;
            Emit("p1.merchant.cancel", reason, ("consumed", false), ("wallet", _rt.Prog.Wallet));
            PackStock = null;
            PackCards.Clear();
        }

        /// <summary>Close request: ask first (MER-06). confirm = merchant leaves; cancel = shop stays.</summary>
        public void RequestClose()
        {
            CancelPack("CloseRequested");
            ConfirmLeave = true;
        }

        public void AnswerClose(bool leave)
        {
            ConfirmLeave = false;
            if (!leave) return;
            Depart("ShopClosedConfirmed(MER-06)");
            ShopOpen = false;
            _rt.UiPause = false;
        }

        public void Depart(string reason)
        {
            var st = Here;
            if (st == null || st.State != RegionMerchant.Present) return;
            st.State = RegionMerchant.Departed;
            Emit("p1.merchant.depart", reason, ("region", st.RegionId));
        }

        /// <summary>Growth pre-emption / death / forced move / load (GRO-47, MER-26): the open choice is cancelled for free.</summary>
        public void Interrupt(string reason)
        {
            if (!ShopOpen) return;
            CancelPack(reason);
            ShopOpen = false;
            ConfirmLeave = false;
            _rt.UiPause = false;
            Emit("p1.merchant.interrupt", reason);
        }

        /// <summary>Shop input (EXP-P1-SHOP-01): up/down cursor, A buy / pick, B close (asks).</summary>
        public void HandleInput(in InputEvent e)
        {
            if (!ShopOpen || e.Edge != InputEdge.Press) return;
            if (ConfirmLeave)
            {
                if (e.Action == LogicalAction.Jump) AnswerClose(true);
                else if (e.Action == LogicalAction.ButtonB) AnswerClose(false);
                return;
            }
            var n = PackStock != null ? PackCards.Count : Here.Stock.Count;
            switch (e.Action)
            {
                case LogicalAction.MoveUp: Cursor = (Cursor + n - 1) % Math.Max(1, n); break;
                case LogicalAction.MoveDown: Cursor = (Cursor + 1) % Math.Max(1, n); break;
                case LogicalAction.MoveLeft: Cursor = (Cursor + n - 1) % Math.Max(1, n); break;
                case LogicalAction.MoveRight: Cursor = (Cursor + 1) % Math.Max(1, n); break;
                case LogicalAction.Jump:
                    if (PackStock != null) ChoosePack(Cursor); else Buy(Cursor);
                    Cursor = 0;
                    break;
                case LogicalAction.ButtonB:
                    if (PackStock != null) CancelPack("PackClosed(MER-21)"); else RequestClose();
                    break;
            }
        }

        // ---------------- snapshot ----------------

        public MiniJson.Obj ToJson()
        {
            var o = new MiniJson.Obj().Set("missStreak", MissStreak).Set("firstEncounterDone", FirstEncounterDone).Set("rng", Rng.State.ToString())
                .Set("currentRegion", CurrentRegion);
            var rs = new MiniJson.Obj();
            var keys = new List<string>(States.Keys);
            keys.Sort(StringComparer.Ordinal);
            foreach (var k in keys)
            {
                var s = States[k];
                var stock = new List<object>();
                foreach (var x in s.Stock) stock.Add(new MiniJson.Obj().Set("kind", x.Kind).Set("buff", x.BuffId).Set("rarity", x.Rarity.ToString()).Set("sold", x.Sold));
                rs.Set(k, new MiniJson.Obj().Set("state", s.State.ToString()).Set("roll", s.Roll).Set("chance", s.Chance).Set("map", s.MapId)
                    .Set("x", (double)s.X).Set("y", (double)s.Y).Set("stock", stock));
            }
            o.Set("regions", rs);
            return o;
        }

        public void FromJson(MiniJson.Obj o)
        {
            Interrupt("Load(MER-26)");
            MissStreak = o.Int("missStreak");
            FirstEncounterDone = o.Bool("firstEncounterDone");
            Rng.State = ulong.Parse(o.Str("rng", "1"));
            CurrentRegion = o.Str("currentRegion");
            States.Clear();
            var rs = o.O("regions");
            if (rs == null) return;
            foreach (var k in rs.Keys)
            {
                var r = rs.O(k);
                var s = new RegionState
                {
                    RegionId = k, State = (RegionMerchant)Enum.Parse(typeof(RegionMerchant), r.Str("state")), Roll = r.Num("roll"), Chance = r.Num("chance"),
                    MapId = r.Str("map"), X = r.Flt("x"), Y = r.Flt("y"),
                };
                foreach (var x in r.L("stock"))
                {
                    var so = (MiniJson.Obj)x;
                    s.Stock.Add(new MerchantStock { Kind = so.Str("kind"), BuffId = so.Str("buff"), Rarity = (Rarity)Enum.Parse(typeof(Rarity), so.Str("rarity")), Sold = so.Bool("sold") });
                }
                States[k] = s;
            }
        }
    }
}
