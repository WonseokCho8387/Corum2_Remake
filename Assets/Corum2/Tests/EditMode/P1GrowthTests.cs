using System.Collections.Generic;
using System.Linq;
using Corum2.Core;
using Corum2.Core.P1;
using NUnit.Framework;

namespace Corum2.Tests
{
    /// <summary>P1-A: EXP, level-up queue, growth offers, mandatory pressure, fallback, reroll, Lv50 (P1 master v0.1.38).</summary>
    public class P1GrowthTests
    {
        sealed class Rig
        {
            public P1Profile P;
            public P1Progress G;
            public ProgressionSystem S;
            public SimpleBag Bag = new SimpleBag();
            public readonly Dictionary<string, int> Buffs = new Dictionary<string, int>();

            public Rig(P1Profile p = null, ulong seed = 7)
            {
                P = p ?? P1Defaults.Create();
                G = P1Progress.New(P);
                G.GrowthRng.State = seed;
                S = new ProgressionSystem(P, G) { Bag = Bag };
                S.Rules.BuffStacks = id => Buffs.TryGetValue(id, out var n) ? n : 0;
                S.AddBuff = (id, r) => { Buffs[id] = (Buffs.TryGetValue(id, out var n) ? n : 0) + 1; return true; };
            }

            public GrowthOffer Offer => G.Offer;
            public int IndexNot(params string[] ids) => G.Offer.Cards.FindIndex(c => !ids.Contains(c) && !G.Offer.MandatoryShown.Contains(c));
            public void LevelTo(int target) { G.Level = target - 1; G.PendingLevels = 1; S.EnsureOffer(); }
        }

        static string MM => "card.mandatory.MagicMissile";

        [Test]
        public void T01_NewGameVolkerTestProfile()
        {
            var r = new Rig();
            Assert.AreEqual(1, r.G.Level);
            Assert.AreEqual(10, r.S.Rules.MaxHp(r.G));
            Assert.AreEqual(5, r.S.Rules.MaxMp(r.G));
            Assert.AreEqual(10f, r.G.CurHp);
            Assert.AreEqual(5f, r.G.CurMp);
            Assert.AreEqual(0, r.G.LevelExp);
            Assert.AreEqual(100, r.G.Karz);
            StringAssert.StartsWith("p1-test", r.P.Version, "profile version marks test values");
            CollectionAssert.IsEmpty(r.P.Validate());
        }

        [Test]
        public void T02_LevelUpNoAutoStatsFullHealAfterCard()
        {
            var r = new Rig();
            r.G.CurHp = 3;
            r.G.CurMp = 1;
            r.S.GrantExp(100, 1);
            Assert.AreEqual(1, r.G.PendingLevels);
            Assert.AreEqual(10, r.S.Rules.MaxHp(r.G), "no automatic growth from the level itself");
            // replace the offer with a known HP card to observe the heal to the new max
            r.G.Offer.Cards[r.IndexNot()] = "card.stat.MaxHp.N";
            Assert.IsNull(r.S.Choose(r.G.Offer.Cards.IndexOf("card.stat.MaxHp.N")));
            Assert.AreEqual(2, r.G.Level);
            Assert.AreEqual(12, r.S.Rules.MaxHp(r.G));
            Assert.AreEqual(12f, r.G.CurHp);
            Assert.AreEqual(5f, r.G.CurMp);
        }

        [Test]
        public void T03_SlotWeights75_25AndAllTemporaryPossible()
        {
            var r = new Rig();
            r.G.Learned.Add("MagicMissile");
            int perm = 0, temp = 0, allTemp = 0;
            var rng = new P1Rng(99);
            for (var i = 0; i < 4000; i++)
            {
                var pool = r.S.Gen.NaturalPool(r.G, 10, null);
                var three = new List<CardDef>();
                for (var k = 0; k < 3; k++) three.Add(r.S.Gen.Draw(r.G, pool, rng));
                foreach (var c in three) if (c.Permanent) perm++; else temp++;
                if (three.All(c => !c.Permanent)) allTemp++;
            }
            var ratio = perm / (double)(perm + temp);
            Assert.That(ratio, Is.InRange(0.72, 0.78), "per-slot permanent share " + ratio);
            Assert.Greater(allTemp, 0, "three temporary cards can appear (no permanent guarantee)");
        }

        [Test]
        public void T04_T62_MandatoryThreeTwoOneAndRerollKeepsIt()
        {
            var r = new Rig();
            r.G.RerollTokens = 5;
            r.S.GrantExp(100, 1); // Lv1 -> 2: first offer of the missile (MAG-38)
            Assert.AreEqual(3, r.Offer.Cards.Count);
            CollectionAssert.Contains(r.Offer.Cards, MM);
            Assert.AreEqual(GrowthOfferGenerator.RerollResult.Changed, r.S.Reroll());
            CollectionAssert.Contains(r.Offer.Cards, MM, "reroll keeps the mandatory");
            Assert.AreEqual(0, r.G.M("MagicMissile").DeclineCount, "reroll alone does not decline");
            r.S.Choose(r.IndexNot());
            Assert.AreEqual(1, r.G.M("MagicMissile").DeclineCount);
            r.S.GrantExp(100, 2);
            Assert.AreEqual(2, r.Offer.Cards.Count);
            CollectionAssert.Contains(r.Offer.Cards, MM);
            r.S.Choose(r.IndexNot());
            r.S.GrantExp(200, 3);
            Assert.AreEqual(1, r.Offer.Cards.Count);
            Assert.AreEqual(MM, r.Offer.Cards[0]);
            r.S.Choose(0);
            Assert.IsTrue(r.G.Has("MagicMissile"));
        }

        [Test]
        public void T07_StatCardsFixedAmountsEditableAndCapped()
        {
            var p = P1Defaults.Create();
            p.Card("card.stat.MaxHp.L").Axes[0].Delta = 9; // editor change is used
            var r = new Rig(p);
            var hpN = p.Card("card.stat.MaxHp.N");
            r.S.Rules.Apply(r.G, hpN);
            Assert.AreEqual(12, r.S.Rules.MaxHp(r.G));
            r.S.Rules.Apply(r.G, p.Card("card.stat.MaxHp.L"));
            Assert.AreEqual(21, r.S.Rules.MaxHp(r.G));
            var mpL = p.Card("card.stat.MaxMp.L");
            r.S.Rules.Apply(r.G, mpL);
            Assert.AreEqual(10, r.S.Rules.MaxMp(r.G));
            // cap: residual only, then excluded
            r.G.Axes["stat.MaxHp"] = 89; // 99
            r.S.Rules.Apply(r.G, p.Card("card.stat.MaxHp.E"));
            Assert.AreEqual(100, r.S.Rules.MaxHp(r.G), "only the remaining 1 is applied");
            StringAssert.StartsWith("NoEffect", r.S.Rules.Reject(r.G, hpN, 10));
            var all20 = p.Cards.Count(c => c.Kind == CardKind.Stat);
            Assert.AreEqual(20, all20, "5 stats x 4 rarities");
        }

        [Test]
        public void T19_OriginExpCurve()
        {
            var p = P1Defaults.Create();
            Assert.AreEqual(100, p.ExpNeeded(1));
            var sum = 0;
            for (var l = 1; l <= 48; l++) sum += p.ExpNeeded(l);
            Assert.AreEqual(100 + 50 * 48 * 47, sum, "T(48)");
            sum += p.ExpNeeded(49);
            Assert.AreEqual(117700, sum, "cumulative EXP to reach Lv50 = T(49)");
        }

        [Test]
        public void T20_SecondCharacterDataDoesNotTouchVolker()
        {
            var p = P1Defaults.Create();
            p.Characters.Add(new CharacterDef { Id = "dlc_test", BaseMaxHp = 30, BaseMaxMp = 1, CapMaxHp = 200 });
            var r = new Rig(p);
            var other = P1Progress.New(p, "dlc_test");
            r.S.Rules.Apply(other, p.Card("card.stat.MaxHp.L"));
            Assert.AreEqual(38, r.S.Rules.MaxHp(other));
            Assert.AreEqual(10, r.S.Rules.MaxHp(r.G));
            Assert.AreEqual("volker", r.G.CharacterId);
            Assert.AreEqual(0f, r.G.Axis("stat.MaxHp"));
        }

        [Test]
        public void T70_T71_T72_NoExtraCardsForJumpGuardParryRollSlide()
        {
            var p = P1Defaults.Create();
            foreach (var c in p.Cards)
            {
                if (c.Kind == CardKind.Mandatory) continue;
                var s = c.Id.ToLowerInvariant();
                foreach (var bad in new[] { "jump", "guard", "parry", "roll", "slide", "pierce", "area" })
                    Assert.IsFalse(s.Contains(bad), "unexpected card " + c.Id);
            }
        }

        [Test]
        public void T73_T74_PhysicalSkillSameEffectPerRarityRequiresSkillAndCaps()
        {
            var r = new Rig();
            var groups = r.P.Cards.Where(c => c.Kind == CardKind.Skill).GroupBy(c => c.Id.Substring(0, c.Id.LastIndexOf('.')));
            foreach (var gr in groups)
            {
                var axes = gr.Select(c => string.Join("+", c.Axes.Select(a => a.Axis))).Distinct().ToList();
                Assert.AreEqual(1, axes.Count, gr.Key + " rarities change the same axes");
                Assert.AreEqual(4, gr.Count());
            }
            StringAssert.StartsWith("RequiresLearned", r.S.Rules.Reject(r.G, r.P.Card("card.skill.airDash.N"), 10));
            r.G.Learned.Add("AirDash");
            Assert.IsNull(r.S.Rules.Reject(r.G, r.P.Card("card.skill.airDash.N"), 10));
            r.G.Axes["skill.airDash.dmg"] = 11;
            r.S.Rules.Apply(r.G, r.P.Card("card.skill.airDash.L"));
            Assert.AreEqual(12f, r.G.Axis("skill.airDash.dmg"), "clamped to the absolute cap");
            StringAssert.StartsWith("NoEffect", r.S.Rules.Reject(r.G, r.P.Card("card.skill.airDash.N"), 10));
            // launcher: two axes, card stays while one axis can grow
            r.G.Axes["skill.launcher.height"] = 1.5f;
            Assert.IsNull(r.S.Rules.Reject(r.G, r.P.Card("card.skill.launcher.N"), 10));
            r.G.Axes["skill.launcher.time"] = 0.5f;
            StringAssert.StartsWith("NoEffect", r.S.Rules.Reject(r.G, r.P.Card("card.skill.launcher.N"), 10));
        }

        [Test]
        public void T145_MultipleMandatoriesShownTogether()
        {
            var p = P1Defaults.Create();
            foreach (var m in p.Mandatory.Take(3)) m.FirstLevel = 2;
            var r = new Rig(p);
            r.S.GrantExp(100, 1);
            Assert.AreEqual(3, r.Offer.MandatoryShown.Count, "three first-stage mandatories on one 3-card screen");
        }

        [Test]
        public void T146_DeclineOnlyForShownMandatories()
        {
            var p = P1Defaults.Create();
            p.Mandatory[0].FirstLevel = 2;
            p.Mandatory[1].FirstLevel = 2;
            var r = new Rig(p);
            r.S.GrantExp(100, 1);
            Assert.AreEqual(2, r.Offer.MandatoryShown.Count);
            r.S.Choose(r.IndexNot());
            Assert.AreEqual(1, r.G.M("MagicMissile").DeclineCount);
            Assert.AreEqual(1, r.G.M("DoubleJump").DeclineCount);
            Assert.AreEqual(0, r.G.M("AirDash").DeclineCount);
            // M1 at 2-stage + M2 at 2-stage: 2 slots, both shown; choose M1 -> M2 declines
            r.S.GrantExp(100, 2);
            Assert.AreEqual(2, r.Offer.Cards.Count);
            r.S.Choose(r.Offer.Cards.IndexOf(MM));
            Assert.AreEqual(2, r.G.M("DoubleJump").DeclineCount);
            // M2 now forced single; M3 waiting at its first eligible level does not decline
            p.Mandatory[2].FirstLevel = 4;
            r.S.GrantExp(200, 3);
            Assert.AreEqual(1, r.Offer.Cards.Count);
            Assert.AreEqual("card.mandatory.DoubleJump", r.Offer.Cards[0]);
            r.S.Choose(0);
            Assert.AreEqual(0, r.G.M("AirDash").DeclineCount, "waiting mandatory is not declined");
            Assert.AreEqual(0, r.G.M("AirDash").DisplayedCount);
        }

        [Test]
        public void T147_PreFilterBeforeDraw()
        {
            var r = new Rig();
            var rules = r.S.Rules;
            StringAssert.StartsWith("RequiresLearned", rules.Reject(r.G, r.P.Card("card.missile.N"), 10));
            StringAssert.StartsWith("Level", rules.Reject(r.G, r.P.Card("card.spell.light_sword.unlock"), 10));
            StringAssert.StartsWith("PostCardMpCost", rules.Reject(r.G, r.P.Card("card.spell.light_sword.unlock"), 20), "MP6 > 5");
            StringAssert.StartsWith("StoryRarityLocked", rules.Reject(r.G, r.P.Card("card.stat.MaxHp.E"), 10));
            for (var i = 0; i < 200; i++)
            {
                r.G.Level = 9; r.G.PendingLevels = 1; r.G.Offer = null;
                r.S.EnsureOffer();
                foreach (var id in r.Offer.Cards.Where(r.Offer.IsNatural))
                    Assert.IsNull(rules.Reject(r.G, r.P.Card(id), 10), id);
                Assert.AreEqual(r.Offer.Cards.Count, r.Offer.Cards.Distinct().Count(), "no duplicate ids");
                r.G.Offer = null; r.G.PendingLevels = 0;
            }
        }

        static P1Profile Minimal(bool buffs, bool stats)
        {
            var p = P1Defaults.Create();
            p.Cards.RemoveAll(c => c.Kind != CardKind.Mandatory && !(buffs && c.Kind == CardKind.Buff) && !(stats && c.Kind == CardKind.Stat));
            foreach (var m in p.Mandatory) m.FirstLevel = 99;
            return p;
        }

        [Test]
        public void T148_EmptyTypeRenormalised()
        {
            var r = new Rig(Minimal(true, true));
            foreach (var st in new[] { "MaxHp", "MaxMp", "PhysAtk", "MagAtk", "Defense" })
                r.G.Axes["stat." + st] = r.S.Rules.StatCap(r.G, st) - r.S.Rules.BaseStat(r.G, st);
            r.LevelTo(5);
            Assert.AreEqual(3, r.Offer.Cards.Count);
            Assert.IsTrue(r.Offer.Cards.All(c => r.P.Card(c).Kind == CardKind.Buff), "all permanent capped -> temporary only");
            var r2 = new Rig(Minimal(false, true));
            r2.LevelTo(5);
            Assert.IsTrue(r2.Offer.Cards.All(c => r2.P.Card(c).Kind == CardKind.Stat));
        }

        [Test]
        public void T149_RarityRenormalisedByStory()
        {
            var r = new Rig(Minimal(false, true));
            for (var i = 0; i < 300; i++)
            {
                r.G.Offer = null;
                r.LevelTo(5);
                Assert.IsTrue(r.Offer.Cards.All(c => r.P.Card(c).Rarity <= Rarity.Rare), "early: no epic/legendary");
                r.G.Offer = null; r.G.PendingLevels = 0;
            }
            // early with no rare cards valid -> normal only
            var p = Minimal(false, true);
            p.Cards.RemoveAll(c => c.Rarity == Rarity.Rare);
            var r2 = new Rig(p);
            r2.LevelTo(5);
            Assert.IsTrue(r2.Offer.Cards.All(c => r2.P.Card(c).Rarity == Rarity.Normal));
            // unlock cards stay normal in late stage
            Assert.IsTrue(r.P.Cards.Where(c => c.Kind == CardKind.SpellUnlock).All(c => c.Rarity == Rarity.Normal));
        }

        static P1Profile NaturalCount(int n)
        {
            var p = Minimal(false, true);
            p.Cards.RemoveAll(c => c.Kind == CardKind.Stat && !(c.Rarity == Rarity.Normal && new[] { "MaxHp", "MaxMp" }.Take(n).Any(s => c.Id.Contains("." + s + "."))));
            return p;
        }

        [Test]
        public void T150_T155_FallbackFillsNormalScreenOnly()
        {
            var one = new Rig(NaturalCount(1));
            one.LevelTo(5);
            Assert.AreEqual(3, one.Offer.Cards.Count);
            CollectionAssert.AreEquivalent(new[] { "FallbackHPItem", "FallbackMPItem" }, one.Offer.FallbackShown);
            var two = new Rig(NaturalCount(2));
            two.LevelTo(5);
            Assert.AreEqual(3, two.Offer.Cards.Count);
            Assert.AreEqual(1, two.Offer.FallbackShown.Count);
            Assert.AreEqual(3, two.Offer.Cards.Distinct().Count());
            // 2-card pressure screen is not filled
            var p = NaturalCount(0);
            p.Mandatory[0].FirstLevel = 2;
            var pr = new Rig(p);
            pr.G.M("MagicMissile").DeclineCount = 1;
            pr.LevelTo(5);
            Assert.AreEqual(1, pr.Offer.Cards.Count, "2-card pressure with no natural card stays 1 (no fallback)");
            Assert.AreEqual(0, pr.Offer.FallbackShown.Count);
            // fallback gives the item only
            one.S.Choose(one.Offer.Cards.IndexOf("FallbackHPItem"));
            Assert.AreEqual(1, one.Bag.Count("p1test.potion.hp"));
            Assert.AreEqual(0f, one.G.Axis("stat.MaxHp"));
            Assert.AreEqual(0, one.G.LevelExp);
        }

        [Test]
        public void T151_ZeroCandidatesSkipsWithHealAndWarning()
        {
            var r = new Rig(NaturalCount(0));
            r.G.CurHp = 1;
            r.LevelTo(5);
            Assert.IsNull(r.Offer);
            Assert.AreEqual(5, r.G.Level);
            Assert.AreEqual(10f, r.G.CurHp);
            Assert.AreEqual(1, r.S.Warnings.Count);
            StringAssert.Contains("GRO-32", r.S.Warnings[0]);
        }

        [Test]
        public void T152_T159_RerollNoChangeNoCostAndFallbackFixed()
        {
            var r = new Rig(NaturalCount(1));
            r.G.RerollTokens = 1;
            r.LevelTo(5);
            var before = r.Offer.Cards.ToList();
            Assert.AreEqual(GrowthOfferGenerator.RerollResult.NoChangePossible, r.S.Reroll());
            Assert.AreEqual(1, r.G.RerollTokens);
            Assert.IsFalse(r.Offer.RerollUsed);
            CollectionAssert.AreEqual(before, r.Offer.Cards);
            // two naturals available beyond the screen -> fallback fixed, natural changes
            var r2 = new Rig(NaturalCount(2));
            r2.G.RerollTokens = 1;
            r2.LevelTo(5);
            r2.P.Cards.Add(new CardDef { Id = "card.test.extra", Kind = CardKind.Stat, Rarity = Rarity.Normal, Axes = { new CardAxis("stat.PhysAtk", 1, 0) } });
            var fb = r2.Offer.FallbackShown.ToList();
            var fbIdx = r2.Offer.Cards.IndexOf(fb[0]);
            Assert.AreEqual(GrowthOfferGenerator.RerollResult.Changed, r2.S.Reroll());
            Assert.AreEqual(fb[0], r2.Offer.Cards[fbIdx], "fallback slot fixed");
            Assert.AreEqual(0, r2.G.RerollTokens);
            Assert.AreEqual(GrowthOfferGenerator.RerollResult.AlreadyUsed, r2.S.Reroll(), "max once per level-up");
        }

        [Test]
        public void T154_T162_MandatoryLv50WorstCaseFeasibility()
        {
            var ok = MandatoryFeasibility.Check(P1Defaults.Create());
            Assert.IsTrue(ok.Ok, ok.Detail);
            Assert.AreEqual(4, ok.LearnedAtLevel.Count);
            var p = P1Defaults.Create();
            p.Mandatory[3].FirstLevel = 49;
            var bad = MandatoryFeasibility.Check(p);
            Assert.IsFalse(bad.Ok, bad.Detail);
            Assert.IsTrue(p.Mandatory.All(m => m.PrerequisiteMandatoryIds.Count == 0), "GRO-43 independent");
        }

        [Test]
        public void T156_FallbackCountEditAppliesToLaterPicksOnly()
        {
            var r = new Rig(NaturalCount(1));
            r.Bag.StackLimit = 0; // full bag: goes to the queue
            r.LevelTo(5);
            r.S.Choose(r.Offer.Cards.IndexOf("FallbackHPItem"));
            Assert.AreEqual(1, r.G.Unclaimed[0].Count);
            r.P.Fallback("FallbackHPItem").Count = 2;
            Assert.AreEqual(1, r.G.Unclaimed[0].Count, "queued reward not changed");
            r.LevelTo(6);
            r.S.Choose(r.Offer.Cards.IndexOf("FallbackHPItem"));
            Assert.AreEqual(2, r.G.Unclaimed[1].Count);
            r.P.Fallback("FallbackMPItem").ItemId = "p1test.potion.mix";
            r.LevelTo(7);
            r.S.Choose(r.Offer.Cards.IndexOf("FallbackMPItem"));
            Assert.AreEqual("p1test.potion.mix", r.G.Unclaimed[2].ItemId);
        }

        [Test]
        public void T157_FullBagQueueClaimOnceAndSaveLoad()
        {
            var r = new Rig(NaturalCount(1));
            r.Bag.StackLimit = 0;
            r.LevelTo(5);
            r.S.Choose(r.Offer.Cards.IndexOf("FallbackHPItem"));
            Assert.AreEqual(5, r.G.Level, "level-up completes");
            Assert.AreEqual(1, r.G.Unclaimed.Count);
            var json = MiniJson.Write(r.G.ToJson());
            r.Bag.StackLimit = 16;
            Assert.AreEqual(1, r.S.ClaimUnclaimed());
            Assert.AreEqual(0, r.S.ClaimUnclaimed());
            Assert.AreEqual(1, r.Bag.Count("p1test.potion.hp"));
            var loaded = P1Progress.FromJson((MiniJson.Obj)MiniJson.Parse(json));
            Assert.AreEqual(1, loaded.Unclaimed.Count, "checkpoint before the claim restores the queue once");
        }

        [Test]
        public void T158_FallbackRepeatsAcrossLevels()
        {
            var r = new Rig(NaturalCount(1));
            for (var lv = 3; lv <= 7; lv++)
            {
                r.LevelTo(lv);
                Assert.AreEqual(2, r.Offer.FallbackShown.Distinct().Count());
                r.S.Choose(r.Offer.Cards.IndexOf("FallbackHPItem"));
            }
            Assert.AreEqual(5, r.Bag.Count("p1test.potion.hp"));
        }

        [Test]
        public void T160_T161_MandatoryFirstLevelEditableNoStoryGate()
        {
            var p = P1Defaults.Create();
            p.Mandatory[1].FirstLevel = 5;
            var r = new Rig(p);
            r.G.Learned.Add("MagicMissile");
            r.LevelTo(4);
            CollectionAssert.DoesNotContain(r.Offer.Cards, "card.mandatory.DoubleJump");
            r.G.Offer = null; r.G.PendingLevels = 0;
            r.LevelTo(5);
            Assert.AreEqual(StoryStage.Early, r.G.Stage);
            CollectionAssert.Contains(r.Offer.Cards, "card.mandatory.DoubleJump", "no story gate (GRO-42)");
        }

        [Test]
        public void T162_AirDashWithoutDoubleJump()
        {
            var r = new Rig();
            r.G.Learned.Add("MagicMissile");
            r.LevelTo(6);
            CollectionAssert.Contains(r.Offer.Cards, "card.mandatory.AirDash");
            r.S.Choose(r.Offer.Cards.IndexOf("card.mandatory.AirDash"));
            Assert.IsTrue(r.G.Has("AirDash"));
            Assert.IsFalse(r.G.Has("DoubleJump"));
        }

        [Test]
        public void T190_MultiLevelSequentialScreens()
        {
            var r = new Rig();
            r.S.GrantExp(100 + 100 + 200, 9); // Lv1 -> 4 in one event
            Assert.AreEqual(3, r.G.PendingLevels);
            var seen = new List<int>();
            while (r.Offer != null)
            {
                seen.Add(r.Offer.TargetLevel);
                r.G.CurHp = 1;
                r.S.Choose(0);
                Assert.AreEqual(r.S.Rules.MaxHp(r.G), r.G.CurHp, "full heal after each level");
            }
            CollectionAssert.AreEqual(new[] { 2, 3, 4 }, seen, "one screen per level, in order");
            Assert.AreEqual(4, r.G.Level);
        }

        [Test]
        public void T192_SaveMidOfferResumesWithoutRedraw()
        {
            var r = new Rig();
            r.G.RerollTokens = 1;
            r.S.GrantExp(400, 5); // three levels pending
            r.S.Choose(1);         // first resolved
            var cards = r.Offer.Cards.ToList();
            var json = MiniJson.Write(r.G.ToJson());
            var g2 = P1Progress.FromJson((MiniJson.Obj)MiniJson.Parse(json));
            var s2 = new ProgressionSystem(r.P, g2) { Bag = new SimpleBag() };
            s2.EnsureOffer();
            CollectionAssert.AreEqual(cards, g2.Offer.Cards, "same candidates after load");
            Assert.AreEqual(r.G.Level, g2.Level);
            Assert.AreEqual(r.G.PendingLevels, g2.PendingLevels);
            Assert.AreEqual(1, g2.RerollTokens);
            Assert.AreEqual(r.G.GrowthRng.State, g2.GrowthRng.State, "rng state resumes");
            Assert.IsFalse(s2.GrantExp(400, 5), "the same exp event is not applied twice");
            Assert.AreEqual(r.G.PendingLevels, g2.PendingLevels);
        }

        [Test]
        public void T193_Lv50ExcessOnlyToCurrencyOnce()
        {
            var r = new Rig();
            r.G.Level = 49;
            r.G.LevelExp = r.P.ExpNeeded(49) - 7;
            r.G.Karz = 100;
            r.S.GrantExp(20, 77);
            Assert.AreEqual(1, r.G.PendingLevels);
            Assert.AreEqual(13, r.G.Lv50Currency);
            Assert.AreEqual(0, r.G.LevelExp);
            Assert.AreEqual(100, r.G.Karz);
            var json = MiniJson.Write(r.G.ToJson());
            var g2 = P1Progress.FromJson((MiniJson.Obj)MiniJson.Parse(json));
            var s2 = new ProgressionSystem(r.P, g2);
            Assert.IsFalse(s2.GrantExp(20, 77));
            Assert.AreEqual(13, g2.Lv50Currency, "+13 only once after load/replay");
            r.S.Choose(0);
            r.S.GrantExp(37, 78);
            Assert.AreEqual(50, r.G.Level);
            Assert.AreEqual(50, r.G.Lv50Currency, "new exp after Lv50 -> currency 1:1");
        }
    }
}
