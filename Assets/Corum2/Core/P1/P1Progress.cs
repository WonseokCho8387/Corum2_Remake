using System;
using System.Collections.Generic;

namespace Corum2.Core.P1
{
    /// <summary>Per-mandatory pressure state, stored independently per id (GRO-27, §3.5 step 1).</summary>
    public sealed class MandatoryState
    {
        public int DisplayedCount;
        public int DeclineCount;
        public int FirstEligibleLevel;
        public int FirstOfferedLevel;
        public int LastOfferedLevel;
    }

    /// <summary>The growth screen currently shown (or saved mid-choice, GRO-48).</summary>
    public sealed class GrowthOffer
    {
        public int TargetLevel;
        /// <summary>Slot budget from the strongest mandatory pressure: 3, 2 or 1.</summary>
        public int Budget = 3;
        public readonly List<string> Cards = new List<string>();
        public readonly List<string> MandatoryShown = new List<string>();
        public readonly List<string> FallbackShown = new List<string>();
        public bool RerollUsed;
        public string RerollSource = "";
        /// <summary>Filter statistics written when the offer was built (GRO-32 warning detail).</summary>
        public string FilterStats = "";

        public bool IsNatural(string id) => !MandatoryShown.Contains(id) && !FallbackShown.Contains(id);
    }

    public sealed class UnclaimedReward
    {
        public string ItemId = "";
        public int Count;
        public string Source = "";
    }

    /// <summary>
    /// Character growth state (one per character id, §1.1 C). Level EXP wallet and the cumulative history are separate
    /// (MER-12 conflict note). Everything here goes into the save snapshot.
    /// </summary>
    public sealed class P1Progress
    {
        public string CharacterId = "volker";
        public int Level = 1;
        /// <summary>Unspent EXP of the current level (merchant currency below Lv50).</summary>
        public int LevelExp;
        public long TotalExp;
        /// <summary>Lv50 merchant currency (MER-22, GRO-49). Not Karz.</summary>
        public int Lv50Currency;
        public int Karz;
        public StoryStage Stage = StoryStage.Early;
        /// <summary>Level-ups earned but not yet resolved (GRO-46 sequential queue).</summary>
        public int PendingLevels;
        public GrowthOffer Offer;
        public readonly Dictionary<string, float> Axes = new Dictionary<string, float>();
        public readonly HashSet<string> Learned = new HashSet<string>();
        public readonly Dictionary<string, int> CardCounts = new Dictionary<string, int>();
        public readonly Dictionary<string, MandatoryState> Mandatory = new Dictionary<string, MandatoryState>();
        public int RerollTokens;
        /// <summary>Equipment reroll option (minimal equipment model, EXP-P1-EQUIP-01).</summary>
        public bool EquipRerollOption;
        public readonly HashSet<string> EquipTraits = new HashSet<string>();
        public readonly HashSet<long> ExpEvents = new HashSet<long>();
        public readonly List<UnclaimedReward> Unclaimed = new List<UnclaimedReward>();
        public int PermPackUses;
        public float CurHp;
        public float CurMp;
        public readonly P1Rng GrowthRng = new P1Rng(0x5EEDC0DE12345UL);

        public int TargetLevel => Level + PendingLevels;
        public float Axis(string k) => Axes.TryGetValue(k, out var v) ? v : 0f;
        public int Count(string cardId) => CardCounts.TryGetValue(cardId, out var v) ? v : 0;
        public bool Has(string flag) => Learned.Contains(flag);

        public MandatoryState M(string id)
        {
            if (!Mandatory.TryGetValue(id, out var s)) Mandatory[id] = s = new MandatoryState();
            return s;
        }

        public static P1Progress New(P1Profile p, string characterId = "volker")
        {
            var c = p.Character(characterId);
            var g = new P1Progress { CharacterId = c.Id, Karz = c.StartKarz };
            g.CurHp = c.BaseMaxHp;
            g.CurMp = c.BaseMaxMp;
            return g;
        }

        // ---------------- snapshot ----------------

        public MiniJson.Obj ToJson()
        {
            var o = new MiniJson.Obj()
                .Set("characterId", CharacterId).Set("level", Level).Set("levelExp", LevelExp).Set("totalExp", (double)TotalExp)
                .Set("lv50Currency", Lv50Currency).Set("karz", Karz).Set("stage", Stage.ToString()).Set("pendingLevels", PendingLevels)
                .Set("rerollTokens", RerollTokens).Set("equipReroll", EquipRerollOption).Set("permPackUses", PermPackUses)
                .Set("curHp", (double)CurHp).Set("curMp", (double)CurMp).Set("growthRng", GrowthRng.State.ToString());
            var ax = new MiniJson.Obj();
            foreach (var k in Sorted(Axes.Keys)) ax.Set(k, (double)Axes[k]);
            o.Set("axes", ax);
            o.Set("learned", Sorted(Learned));
            var cc = new MiniJson.Obj();
            foreach (var k in Sorted(CardCounts.Keys)) cc.Set(k, CardCounts[k]);
            o.Set("cardCounts", cc);
            var ms = new MiniJson.Obj();
            foreach (var k in Sorted(Mandatory.Keys))
            {
                var m = Mandatory[k];
                ms.Set(k, new MiniJson.Obj().Set("displayed", m.DisplayedCount).Set("declined", m.DeclineCount)
                    .Set("firstEligible", m.FirstEligibleLevel).Set("firstOffered", m.FirstOfferedLevel).Set("lastOffered", m.LastOfferedLevel));
            }
            o.Set("mandatory", ms);
            o.Set("equipTraits", Sorted(EquipTraits));
            var ev = new List<object>();
            var evs = new List<long>(ExpEvents);
            evs.Sort();
            foreach (var e in evs) ev.Add((double)e);
            o.Set("expEvents", ev);
            var un = new List<object>();
            foreach (var u in Unclaimed) un.Add(new MiniJson.Obj().Set("itemId", u.ItemId).Set("count", u.Count).Set("source", u.Source));
            o.Set("unclaimed", un);
            if (Offer != null)
                o.Set("offer", new MiniJson.Obj().Set("targetLevel", Offer.TargetLevel).Set("budget", Offer.Budget)
                    .Set("cards", new List<string>(Offer.Cards)).Set("mandatoryShown", new List<string>(Offer.MandatoryShown))
                    .Set("fallbackShown", new List<string>(Offer.FallbackShown)).Set("rerollUsed", Offer.RerollUsed)
                    .Set("rerollSource", Offer.RerollSource).Set("filterStats", Offer.FilterStats));
            return o;
        }

        public static P1Progress FromJson(MiniJson.Obj o)
        {
            var g = new P1Progress
            {
                CharacterId = o.Str("characterId"), Level = o.Int("level"), LevelExp = o.Int("levelExp"), TotalExp = o.Long("totalExp"),
                Lv50Currency = o.Int("lv50Currency"), Karz = o.Int("karz"), PendingLevels = o.Int("pendingLevels"),
                RerollTokens = o.Int("rerollTokens"), EquipRerollOption = o.Bool("equipReroll"), PermPackUses = o.Int("permPackUses"),
                CurHp = o.Flt("curHp"), CurMp = o.Flt("curMp"),
            };
            g.Stage = (StoryStage)Enum.Parse(typeof(StoryStage), o.Str("stage", "Early"));
            g.GrowthRng.State = ulong.Parse(o.Str("growthRng", "1"));
            var ax = o.O("axes");
            if (ax != null) foreach (var k in ax.Keys) g.Axes[k] = ax.Flt(k);
            foreach (var x in o.L("learned")) g.Learned.Add((string)x);
            var cc = o.O("cardCounts");
            if (cc != null) foreach (var k in cc.Keys) g.CardCounts[k] = cc.Int(k);
            var ms = o.O("mandatory");
            if (ms != null)
                foreach (var k in ms.Keys)
                {
                    var m = ms.O(k);
                    g.Mandatory[k] = new MandatoryState
                    {
                        DisplayedCount = m.Int("displayed"), DeclineCount = m.Int("declined"), FirstEligibleLevel = m.Int("firstEligible"),
                        FirstOfferedLevel = m.Int("firstOffered"), LastOfferedLevel = m.Int("lastOffered"),
                    };
                }
            foreach (var x in o.L("equipTraits")) g.EquipTraits.Add((string)x);
            foreach (var x in o.L("expEvents")) g.ExpEvents.Add((long)Math.Round((double)x));
            foreach (var x in o.L("unclaimed"))
            {
                var u = (MiniJson.Obj)x;
                g.Unclaimed.Add(new UnclaimedReward { ItemId = u.Str("itemId"), Count = u.Int("count"), Source = u.Str("source") });
            }
            var of = o.O("offer");
            if (of != null)
            {
                g.Offer = new GrowthOffer
                {
                    TargetLevel = of.Int("targetLevel"), Budget = of.Int("budget"), RerollUsed = of.Bool("rerollUsed"),
                    RerollSource = of.Str("rerollSource"), FilterStats = of.Str("filterStats"),
                };
                foreach (var x in of.L("cards")) g.Offer.Cards.Add((string)x);
                foreach (var x in of.L("mandatoryShown")) g.Offer.MandatoryShown.Add((string)x);
                foreach (var x in of.L("fallbackShown")) g.Offer.FallbackShown.Add((string)x);
            }
            return g;
        }

        static List<string> Sorted(IEnumerable<string> s)
        {
            var l = new List<string>(s);
            l.Sort(StringComparer.Ordinal);
            return l;
        }
    }
}
