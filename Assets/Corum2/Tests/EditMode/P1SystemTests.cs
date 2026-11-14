using System;
using System.Linq;
using Corum2.Core;
using Corum2.Core.P1;
using NUnit.Framework;
using static Corum2.Tests.CombatFixtures;

namespace Corum2.Tests
{
    /// <summary>P1-D: buffs, merchant, checkpoint, growth screen events (GRO-44~50, MER-01~30, BUF-01~11, T201~204 core replays).</summary>
    public class P1SystemTests
    {
        static EnemyDefinition ExpBag(string id, float hp, int exp)
        {
            var d = Bag(id, hp);
            d.Exp = exp;
            return d;
        }

        static Action<P1Profile> Regions(bool safeR1 = true, bool blocked = false, float chance = 1f) => p =>
        {
            p.Merchant.BaseChance = chance;
            var r1 = new RegionDef { Id = "R1", MapIds = { "T1", "T3" } };
            if (safeR1) r1.SafeSpawns.Add(new SafeSpawnDef { MapId = "T1", X = 20, Y = 0, Blocked = blocked });
            var r2 = new RegionDef { Id = "R2", MapIds = { "T2" }, SafeSpawns = { new SafeSpawnDef { MapId = "T2", X = 10, Y = 0 } } };
            p.Regions.Add(r1);
            p.Regions.Add(r2);
        };

        static P1MagicTests.M Rig(Action<P1Profile> t = null, params (string, EnemyDefinition, float)[] e) => new P1MagicTests.M(t, e);

        // ---------------------------------------------------------------- buffs

        [Test]
        public void T09_TwoByTwoRarity()
        {
            var p = P1Defaults.Create();
            var b = p.Buff("BF-ASPD");
            var r = p.BuffRules;
            Assert.AreEqual(r.Strength(b, Rarity.Normal), r.Strength(b, Rarity.Rare));
            Assert.AreEqual(r.Duration(Rarity.Normal), r.Duration(Rarity.Epic));
            Assert.AreEqual(r.Strength(b, Rarity.Epic), r.Strength(b, Rarity.Legendary));
            Assert.AreEqual(r.Duration(Rarity.Rare), r.Duration(Rarity.Legendary));
            Assert.AreEqual(0.075f, r.Strength(b, Rarity.Epic), 1e-6, "+7.5 % [시험]");
            Assert.AreEqual(240f, r.Duration(Rarity.Legendary));
        }

        [Test]
        public void T10_T177_AdditiveCapIndependentTimersStackCap()
        {
            var p = P1Defaults.Create();
            var bs = new BuffSystem(p);
            Assert.IsTrue(bs.Add("BF-PATK", Rarity.Epic));   // 12 %, 180 s
            Assert.IsTrue(bs.Add("BF-PATK", Rarity.Legendary)); // 12 %, 240 s
            Assert.AreEqual(0.24f, bs.PhysAtk, 1e-6);
            Assert.IsTrue(bs.Add("BF-PATK", Rarity.Epic));
            Assert.AreEqual(0.30f, bs.PhysAtk, 1e-6, "family cap 30 %");
            Assert.IsFalse(bs.Add("BF-PATK", Rarity.Normal), "3 stacks max");
            var rules = new GrowthRules(p) { BuffStacks = bs.Stacks };
            StringAssert.StartsWith("BuffStackCap", rules.Reject(P1Progress.New(p), p.Card("card.buff.BF-PATK.N"), 5));
            bs.Tick(100, false);
            Assert.AreEqual(3, bs.Active.Count, "not controllable: no time passes");
            bs.Tick(181, true);
            Assert.AreEqual(1, bs.Active.Count, "180 s stacks expired, 240 s stack left");
            Assert.AreEqual(0.12f, bs.PhysAtk, 1e-6, "recomputed after expiry");
            Assert.IsNull(rules.Reject(P1Progress.New(p), p.Card("card.buff.BF-PATK.N"), 5), "room again");
        }

        [Test]
        public void T12_T175_T176_SpeedAndAirScope()
        {
            var m = Rig(null, ("e", Bag("e", 1000), 12f));
            var br = m.Rt.Bridge;
            m.Rt.Buffs.Add("BF-ASPD", Rarity.Normal);
            Assert.AreEqual(1.05f, br.MotionSpeed(AttackKind.GroundNormal), 1e-6);
            Assert.AreEqual(1.05f, br.MotionSpeed(AttackKind.AirNormal), 1e-6);
            foreach (var k in new[] { AttackKind.Launcher, AttackKind.AirDash, AttackKind.Dive, AttackKind.Spell, AttackKind.DownAttack })
                Assert.AreEqual(1f, br.MotionSpeed(k), k.ToString());
            m.Rt.Buffs.Active.Clear();
            m.Rt.Buffs.Active.Add(new BuffInstance { BuffId = "BF-PATK", Strength = 0.25f, Remaining = 10 });
            m.Rt.Buffs.Active.Add(new BuffInstance { BuffId = "BF-AIR", Strength = 0.20f, Remaining = 10 });
            Assert.AreEqual(1.45, DamageCalc.Raw(1, br.PhysicalBuff(AttackKind.AirNormal)), 1e-6, "1 + 0.25 + 0.20, not 1.25 x 1.20");
            Assert.AreEqual(0.45f, br.PhysicalBuff(AttackKind.AirDash), 1e-6);
            Assert.AreEqual(0.45f, br.PhysicalBuff(AttackKind.Dive), 1e-6);
            Assert.AreEqual(0.25f, br.PhysicalBuff(AttackKind.GroundNormal), 1e-6, "ground: no AIR");
            m.Rt.Buffs.Active.Clear();
            m.Rt.Buffs.Add("BF-MOVE", Rarity.Normal);
            m.H.Step();
            Assert.AreEqual(1.05f, m.H.P.GroundSpeedScale, 1e-6);
        }

        [Test]
        public void T13_T179_ItemBuffPotionsOnlyOneFloorPerResource()
        {
            var m = Rig();
            m.Rt.Buffs.Active.Add(new BuffInstance { BuffId = "BF-ITEM", Strength = 0.15f, Remaining = 100 });
            m.H.C.SetPlayerHp(1, "test");
            m.G.CurMp = 0;
            var item = m.H.Sim.RingProfile.FindItem("p1test.potion.mix");
            m.Rt.ItemHeal(item);
            Assert.AreEqual(4f, m.H.C.PlayerHp, "floor(3 x 1.15) = 3");
            Assert.AreEqual(5f, m.G.CurMp, "floor(5 x 1.15) = 5");
            // the heal spell is not affected by BF-ITEM
            m.Learn("spell.heal");
            m.H.C.SetPlayerHp(1, "test");
            m.G.CurMp = 5;
            m.Cast(LogicalAction.ButtonB, Dir8.Down, Dir8.DownRight, Dir8.Right).Settle(30);
            Assert.AreEqual(6f, m.H.C.PlayerHp, "heal spell +5 only");
        }

        // ---------------------------------------------------------------- checkpoint / revive

        [Test]
        public void T11_T126_ReviveKeepsCheckpointRestoresSnapshot()
        {
            var m = Rig(p => p.Spell("spell.archangel").BaseMp = 1).Learn("spell.archangel");
            m.Rt.Buffs.Add("BF-DEF", Rarity.Rare);
            m.Rt.SaveCheckpoint("test");
            var saved = m.Rt.Buffs.Active[0].Remaining;
            m.Settle(50);
            m.Cast(LogicalAction.Attack, Dir8.Left, Dir8.DownLeft, Dir8.Down, Dir8.DownRight, Dir8.Right).Settle(30);
            Assert.IsNotNull(m.Mg.Summon, "summoned after the checkpoint");
            // field revive keeps everything
            m.H.C.Revive("test");
            Assert.IsNotNull(m.Mg.Summon);
            Assert.AreEqual(1, m.Rt.Buffs.Active.Count);
            // checkpoint restart restores the snapshot: buff time as saved, no summon
            Assert.IsTrue(m.Rt.RestoreCheckpoint("test"));
            Assert.AreEqual(saved, m.Rt.Buffs.Active[0].Remaining, 1e-4);
            Assert.IsNull(m.Mg.Summon, "summon cast after the checkpoint is gone");
        }

        [Test]
        public void T11_DeathMenuCheckpointRestart()
        {
            var m = Rig(null, ("h", Hitter("h", 50), 11f));
            m.H.Sim.Items.Inventory.TestSetCount("p1test.revive", 0); // no revive item: straight to the game-over menu
            m.Rt.SaveCheckpoint("test");
            m.H.StepUntil(() => m.H.Sim.Death.Stage == DeathStage.GameOver, 600);
            Assert.AreEqual(DeathStage.GameOver, m.H.Sim.Death.Stage);
            m.H.Press(LogicalAction.Jump).Step(); // first option: CheckpointRestart
            Assert.AreEqual(DeathStage.None, m.H.Sim.Death.Stage);
            Assert.AreEqual(10f, m.H.C.PlayerHp);
            Assert.AreNotEqual(MotorState.Dead, m.H.P.State);
        }

        // ---------------------------------------------------------------- growth events GRO-44~50

        [Test]
        public void T188_LevelUpAtFirstSafeBoundaryPausesGame()
        {
            var m = Rig(null, ("a", ExpBag("a", 1, 100), 10.8f), ("b", Bag("b", 100), 18f));
            m.H.Press(LogicalAction.Attack).StepUntil(() => m.Rt.GrowthOpen, 60);
            Assert.IsTrue(m.Rt.GrowthOpen);
            var hit = m.H.Sink.Events.First(e => e.Type == "hit" && (string)e.TargetId == "a");
            var open = m.H.Sink.Events.First(e => e.Type == "p1.growth.open");
            Assert.LessOrEqual(hit.SimulationTick, open.SimulationTick, "the hit event finished first");
            Assert.IsTrue(m.E("b").Targetable, "other enemies alive: not waiting for the fight to end");
            var t = m.H.Sim.GameTime;
            m.H.Step(20);
            Assert.AreEqual(t, m.H.Sim.GameTime, "game stopped");
            m.Rt.ChooseGrowth(0);
            m.H.Step();
            Assert.Greater(m.H.Sim.GameTime, t);
        }

        [Test]
        public void T189_SameEventLethalHitLevelUpFirst()
        {
            var m = Rig(null, ("a", ExpBag("a", 1, 100), 30f));
            m.H.C.SetPlayerHp(1, "test");
            m.H.C.TestApplyHit(new AttackDefinition { Id = "k", Kind = AttackKind.GroundNormal, TestDamage = 5, Knockback = 1 }, "a");
            m.H.C.TestHitPlayer(new AttackDefinition { Id = "lethal", Kind = AttackKind.EnemyMelee, TestDamage = 5, Knockback = 1 }, "x", 12f);
            m.H.C.TestFinishTick();
            Assert.AreNotEqual(MotorState.Dead, m.H.P.State, "death held for the level-up");
            Assert.AreEqual(1, m.H.Count("player.deathHeld"));
            Assert.IsTrue(m.Rt.GrowthOpen);
            m.Rt.ChooseGrowth(0);
            Assert.AreEqual(m.H.C.PlayerMaxHp, m.H.C.PlayerHp, "full heal -> survives");
            // no level-up in the same event: normal death
            var n = Rig(null, ("a", ExpBag("a", 1, 10), 30f));
            n.H.C.SetPlayerHp(1, "test");
            n.H.C.TestApplyHit(new AttackDefinition { Id = "k", Kind = AttackKind.GroundNormal, TestDamage = 5, Knockback = 1 }, "a");
            n.H.C.TestHitPlayer(new AttackDefinition { Id = "lethal", Kind = AttackKind.EnemyMelee, TestDamage = 5, Knockback = 1 }, "x", 12f);
            n.H.C.TestFinishTick();
            Assert.AreEqual(MotorState.Dead, n.H.P.State);
            // a death committed earlier is not undone by a later level-up
            var o = Rig(null, ("a", ExpBag("a", 1, 100), 30f));
            o.H.C.SetPlayerHp(1, "test");
            o.H.C.TestHitPlayer(new AttackDefinition { Id = "lethal", Kind = AttackKind.EnemyMelee, TestDamage = 5, Knockback = 1 }, "x", 12f);
            o.H.C.TestFinishTick();
            Assert.AreEqual(MotorState.Dead, o.H.P.State);
            o.H.C.TestApplyHit(new AttackDefinition { Id = "k", Kind = AttackKind.GroundNormal, TestDamage = 5, Knockback = 1 }, "a");
            o.H.C.TestFinishTick();
            Assert.AreEqual(MotorState.Dead, o.H.P.State, "no retroactive revive");
        }

        [Test]
        public void T191_RingAndPendingUseClosedWithoutCost()
        {
            var m = Rig(null, ("a", ExpBag("a", 1, 100), 30f));
            m.H.Ui(LogicalAction.RingToggle).Step();
            Assert.IsTrue(m.H.Sim.Ring.IsOpen);
            m.H.C.TestApplyHit(new AttackDefinition { Id = "k", Kind = AttackKind.GroundNormal, TestDamage = 5, Knockback = 1 }, "a");
            m.H.Step();
            Assert.IsFalse(m.H.Sim.Ring.IsOpen, "ring force-closed");
            Assert.IsTrue(m.Rt.GrowthOpen);
            // pending item use cancelled without consuming
            var n = Rig(null, ("a", ExpBag("a", 1, 100), 30f));
            n.H.Press(LogicalAction.Attack).Step();
            n.H.Sim.Items.Inventory.Quickslots[0] = "p1test.potion.hp";
            n.H.Ui(LogicalAction.QuickUp);
            Assert.IsNotNull(n.H.Sim.Items.Pending);
            n.H.C.TestApplyHit(new AttackDefinition { Id = "k", Kind = AttackKind.GroundNormal, TestDamage = 5, Knockback = 1 }, "a");
            n.H.Step();
            Assert.IsNull(n.H.Sim.Items.Pending);
            Assert.AreEqual(3, n.H.Sim.Items.Inventory.Count("p1test.potion.hp"));
        }

        [Test]
        public void T194_MerchantBlockedWhileGrowthPending()
        {
            var m = Rig(Regions(), ("a", ExpBag("a", 1, 100), 30f));
            Assert.IsTrue(m.Rt.Merchant.PresentHere);
            m.G.PendingLevels = 1;
            Assert.AreEqual("GrowthPending(GRO-50)", m.Rt.Merchant.OpenShop());
            Assert.IsFalse(m.Rt.Merchant.ShopOpen);
        }

        // ---------------------------------------------------------------- merchant

        [Test]
        public void T14_ChanceStepsAndReset()
        {
            var p = P1Defaults.Create();
            for (var i = 0; i < 30; i++)
                p.Regions.Add(new RegionDef { Id = "r" + i, MapIds = { "m" + i }, SafeSpawns = { new SafeSpawnDef { MapId = "m" + i, X = 1 } } });
            var m = Rig(x => x.Regions.AddRange(p.Regions));
            var mer = m.Rt.Merchant;
            var expected = 0.15;
            for (var i = 0; i < 30; i++)
            {
                mer.OnMapEntered("m" + i);
                var st = mer.State("r" + i);
                Assert.AreEqual(Math.Min(1.0, expected), st.Chance, 1e-6, "region " + i);
                expected = st.State == RegionMerchant.Present ? 0.15 : expected + 0.10;
                if (Math.Abs(st.Chance - 1.0) < 1e-9) Assert.AreEqual(RegionMerchant.Present, st.State, "100 % appears");
            }
            var before = mer.MissStreak;
            mer.OnMapEntered("m0");
            Assert.AreEqual(before, mer.MissStreak, "re-entry does not roll (MER-03)");
        }

        [Test]
        public void T15_T16_SpawnStockCloseConfirm()
        {
            var m = Rig(Regions());
            var mer = m.Rt.Merchant;
            Assert.IsTrue(mer.PresentHere);
            Assert.AreEqual(20f, mer.Here.X);
            Assert.That(mer.Here.Stock.Count, Is.InRange(3, 5));
            m.H.P.Position = new Vec2(20, 0);
            Assert.IsNull(mer.OpenShop());
            mer.RequestClose();
            mer.AnswerClose(false);
            Assert.IsTrue(mer.ShopOpen, "cancel keeps the shop");
            mer.RequestClose();
            mer.AnswerClose(true);
            Assert.IsFalse(mer.ShopOpen);
            Assert.AreEqual(RegionMerchant.Departed, mer.Here.State, "merchant leaves, icon gone");
        }

        static int StockIndex(MerchantSystem mer, string kind)
        {
            var i = mer.Here.Stock.FindIndex(s => s.Kind == kind && !s.Sold);
            if (i < 0) { mer.Here.Stock.Add(new MerchantStock { Kind = kind, BuffId = kind == "Buff" ? "BF-ITEM" : "" }); i = mer.Here.Stock.Count - 1; }
            return i;
        }

        [Test]
        public void T17_T180_T181_PriceAndExpPayment()
        {
            var m = Rig(Regions());
            var mer = m.Rt.Merchant;
            m.G.Level = 2; // reference 100
            Assert.AreEqual(120, mer.Price("PermPack"), "max(80, 120)");
            Assert.AreEqual(80, mer.Price("RerollToken"), "max(45, 80)");
            m.P.Product("PermPack").MinPrice = 200;
            Assert.AreEqual(200, mer.Price("PermPack"), "edited minimum");
            m.G.Level = 1; // reference 100 too; use a custom reference
            m.P.ExpToNext[1] = 101;
            m.P.Product("Buff").MinPrice = 0;
            Assert.AreEqual(36, mer.Price("Buff"), "ceil(35.35)");
            m.P.Product("Buff").MinPrice = 60;
            m.G.LevelExp = 90;
            mer.OpenShop();
            var bi = StockIndex(mer, "RerollToken");
            Assert.IsNull(mer.Buy(bi));
            Assert.AreEqual(10, m.G.LevelExp, "current level EXP only");
            Assert.AreEqual(1, m.G.Level, "no level loss");
            Assert.AreEqual(1, m.G.RerollTokens);
            var bj = StockIndex(mer, "RerollToken");
            StringAssert.StartsWith("WalletShort", mer.Buy(bj));
            Assert.AreEqual(10, m.G.LevelExp);
        }

        [Test]
        public void T18_T153_T182_T184_PermPackRules()
        {
            var m = Rig(Regions());
            var mer = m.Rt.Merchant;
            m.G.LevelExp = 10000;
            mer.OpenShop();
            var i = StockIndex(mer, "PermPack");
            Assert.IsNull(mer.Buy(i));
            Assert.AreEqual(3, mer.PackCards.Count);
            Assert.IsFalse(mer.PackCards.Any(c => c.StartsWith("card.mandatory.") || c.StartsWith("Fallback")), "no mandatory unlocks or fallback");
            var wallet = m.G.LevelExp;
            mer.CancelPack("test");
            Assert.AreEqual(wallet, m.G.LevelExp, "cancel: nothing paid");
            Assert.IsFalse(mer.Here.Stock[i].Sold);
            Assert.AreEqual(0, m.G.PermPackUses);
            Assert.IsNull(mer.Buy(i));
            var card = mer.PackCards[0];
            Assert.IsNull(mer.ChoosePack(0));
            Assert.AreEqual(1, m.G.PermPackUses);
            Assert.IsTrue(mer.Here.Stock[i].Sold);
            Assert.Less(m.G.LevelExp, wallet);
            Assert.AreEqual(1, m.G.Count(card));
            // limit 5 (editable, saved)
            m.G.PermPackUses = 5;
            StringAssert.StartsWith("PermPackLimit", mer.Buy(StockIndex(mer, "PermPack")));
            var g2 = P1Progress.FromJson((MiniJson.Obj)MiniJson.Parse(MiniJson.Write(m.G.ToJson())));
            Assert.AreEqual(5, g2.PermPackUses);
            // fewer than 3 eligible: blocked for free
            m.G.PermPackUses = 0;
            m.P.Cards.RemoveAll(c => c.Permanent && c.Kind != CardKind.Mandatory && c.Id != "card.stat.MaxHp.N" && c.Id != "card.stat.MaxMp.N");
            wallet = m.G.LevelExp;
            StringAssert.StartsWith("PackFewerThan3", mer.Buy(StockIndex(mer, "PermPack")));
            Assert.AreEqual(wallet, m.G.LevelExp);
        }

        [Test]
        public void T183_FirstVisitPremiumKeepsStoryRarity()
        {
            for (var seed = 1; seed < 40; seed++)
            {
                var m = Rig(Regions());
                var st = m.Rt.Merchant.Here;
                Assert.IsTrue(st.Stock.Where(s => s.Kind == "Buff").All(s => s.Rarity <= Rarity.Rare), "early: N/R only");
                m.Rt.Merchant.Rng.State = (ulong)seed * 7919UL;
            }
            var r = Rig(Regions());
            r.G.LevelExp = 100000;
            r.Rt.Merchant.OpenShop();
            for (var k = 0; k < 20; k++)
            {
                r.Rt.Merchant.Here.Stock.Add(new MerchantStock { Kind = "PermPack" });
                r.Rt.Merchant.Buy(r.Rt.Merchant.Here.Stock.Count - 1);
                Assert.IsTrue(r.Rt.Merchant.PackCards.All(c => r.P.Card(c).Rarity <= Rarity.Rare));
                r.Rt.Merchant.CancelPack("t");
            }
        }

        [Test]
        public void T177_MerchantBuffRefusedAtStackCap()
        {
            var m = Rig(Regions());
            m.G.LevelExp = 10000;
            for (var i = 0; i < 3; i++) m.Rt.Buffs.Add("BF-ITEM", Rarity.Normal);
            m.Rt.Merchant.OpenShop();
            var bi = StockIndex(m.Rt.Merchant, "Buff");
            m.Rt.Merchant.Here.Stock[bi].BuffId = "BF-ITEM";
            var w = m.G.LevelExp;
            StringAssert.StartsWith("BuffStackCap", m.Rt.Merchant.Buy(bi));
            Assert.AreEqual(w, m.G.LevelExp);
            Assert.IsFalse(m.Rt.Merchant.Here.Stock[bi].Sold);
        }

        [Test]
        public void T185_T186_Lv50CurrencyAndPrices()
        {
            var m = Rig(Regions());
            m.G.Level = 50;
            m.G.Karz = 100;
            m.Rt.Prog.GrantExp(37, 5);
            Assert.AreEqual(37, m.G.Lv50Currency);
            Assert.AreEqual(100, m.G.Karz);
            Assert.AreEqual(50, m.G.Level);
            var mer = m.Rt.Merchant;
            Assert.AreEqual(400, mer.Price("PermPack"));
            Assert.AreEqual(200, mer.Price("TempPack"));
            Assert.AreEqual(175, mer.Price("Buff"));
            Assert.AreEqual(225, mer.Price("RerollToken"));
            m.P.Lv50CurrencyRate = 2;
            m.Rt.Prog.GrantExp(10, 6);
            Assert.AreEqual(57, m.G.Lv50Currency, "new rate for later gains only");
        }

        [Test]
        public void T187_RerollTokenFirstThenEquipment()
        {
            var m = Rig();
            m.G.RerollTokens = 1;
            m.G.EquipRerollOption = true;
            m.Rt.Prog.GrantExp(100, 1);
            Assert.AreEqual(GrowthOfferGenerator.RerollResult.Changed, m.Rt.Prog.Reroll());
            Assert.AreEqual("Token", m.G.Offer.RerollSource);
            Assert.AreEqual(0, m.G.RerollTokens);
            Assert.AreEqual(GrowthOfferGenerator.RerollResult.AlreadyUsed, m.Rt.Prog.Reroll(), "once per level-up even with equipment");
            m.Rt.ChooseGrowth(0);
            m.Rt.Prog.GrantExp(100, 2);
            Assert.AreEqual(GrowthOfferGenerator.RerollResult.Changed, m.Rt.Prog.Reroll());
            Assert.AreEqual("EquipmentOption", m.G.Offer.RerollSource);
        }

        [Test]
        public void T195_RegionExitConfirm()
        {
            var m = Rig(Regions());
            Assert.IsTrue(m.Rt.Merchant.PresentHere);
            // same region (T1 -> T3 by the up point) asks nothing
            Assert.IsTrue(m.Rt.BeforeTransition("T3", "B", "P-UP", "test"));
            // T1 -> T2 leaves R1
            m.H.P.Position = new Vec2(99.5f, 0);
            m.H.Dir(Dir8.Right).StepUntil(() => m.Rt.PendingExit != null, 60);
            Assert.IsNotNull(m.Rt.PendingExit);
            Assert.AreEqual("T1", m.H.Sim.CurrentMap.Id);
            m.H.Dir(Dir8.None);
            m.Rt.AnswerExit(false);
            Assert.AreEqual("T1", m.H.Sim.CurrentMap.Id);
            Assert.AreEqual(RegionMerchant.Present, m.Rt.Merchant.State("R1").State, "cancel: merchant and stock stay");
            m.H.P.Position = new Vec2(99.5f, 0);
            m.H.Dir(Dir8.Right).StepUntil(() => m.Rt.PendingExit != null, 60);
            m.Rt.AnswerExit(true);
            Assert.AreEqual("T2", m.H.Sim.CurrentMap.Id);
            Assert.AreEqual(RegionMerchant.Departed, m.Rt.Merchant.State("R1").State);
        }

        [Test]
        public void T196_LoadDuringPackCancelsFree()
        {
            var m = Rig(Regions());
            m.G.LevelExp = 1000;
            m.Rt.SaveCheckpoint("t");
            m.Rt.Merchant.OpenShop();
            m.Rt.Merchant.Buy(StockIndex(m.Rt.Merchant, "PermPack"));
            Assert.IsNotNull(m.Rt.Merchant.PackStock);
            m.Rt.RestoreCheckpoint("t");
            Assert.IsNull(m.Rt.Merchant.PackStock);
            Assert.IsFalse(m.Rt.Merchant.ShopOpen);
            Assert.AreEqual(1000, m.G.LevelExp);
            Assert.AreEqual(0, m.G.PermPackUses);
        }

        [Test]
        public void T197_NoSafeSpawnNotEligible()
        {
            var m = Rig(Regions(safeR1: false));
            Assert.AreEqual(RegionMerchant.NotEligible, m.Rt.Merchant.State("R1").State);
            Assert.AreEqual(0, m.Rt.Merchant.MissStreak);
            Assert.AreEqual(-1, m.Rt.Merchant.State("R1").Roll, "no roll");
        }

        [Test]
        public void T198_BlockedAfterSuccessKeepsCountersAndState()
        {
            var m = Rig(Regions(blocked: true));
            var st = m.Rt.Merchant.State("R1");
            Assert.AreEqual(RegionMerchant.SpawnBlockedAfterSuccessfulRoll, st.State);
            Assert.AreEqual(0, m.Rt.Merchant.MissStreak, "not counted as success or miss");
            m.Rt.SaveCheckpoint("t");
            m.Rt.RestoreCheckpoint("t");
            Assert.AreEqual(RegionMerchant.SpawnBlockedAfterSuccessfulRoll, m.Rt.Merchant.State("R1").State, "no reroll after load");
        }

        [Test]
        public void T199_T200_RegionSnapshotAndCheckpointTogether()
        {
            var m = Rig(Regions());
            m.G.LevelExp = 100;
            var stock = MerchantSystem.Describe(m.Rt.Merchant.Here);
            m.Rt.SaveCheckpoint("t");
            m.G.LevelExp = 1000;
            m.Rt.Merchant.OpenShop();
            m.Rt.Merchant.Buy(StockIndex(m.Rt.Merchant, "PermPack"));
            m.Rt.Merchant.ChoosePack(0);
            m.Rt.Merchant.RequestClose();
            m.Rt.Merchant.AnswerClose(true);
            Assert.AreEqual(1, m.G.PermPackUses);
            m.Rt.RestoreCheckpoint("t");
            Assert.AreEqual(100, m.G.LevelExp);
            Assert.AreEqual(0, m.G.PermPackUses);
            Assert.AreEqual(RegionMerchant.Present, m.Rt.Merchant.State("R1").State);
            Assert.AreEqual(stock, MerchantSystem.Describe(m.Rt.Merchant.Here), "same stock, no reroll");
            Assert.AreEqual(0, m.G.CardCounts.Count, "bought growth undone with the checkpoint");
        }

        // ---------------------------------------------------------------- integration replays (core) T201~T204

        [Test]
        public void T201_RingOpenSameEventLethalLevelUpSaveLoad()
        {
            var m = Rig(null, ("a", ExpBag("a", 1, 100), 30f));
            m.Learn("MagicMissile");
            m.Motion(Dir8.Right, Dir8.Up);
            m.H.Ui(LogicalAction.RingToggle).Step();
            m.H.C.SetPlayerHp(1, "test");
            m.H.C.TestApplyHit(new AttackDefinition { Id = "dot", Kind = AttackKind.Spell, DamageType = DamageType.Magic, TestDamage = 5, Knockback = 1 }, "a");
            m.H.C.TestHitPlayer(new AttackDefinition { Id = "lethal", Kind = AttackKind.EnemyMelee, TestDamage = 5, Knockback = 1 }, "x", 12f);
            m.H.C.TestFinishTick();
            m.H.Step();
            Assert.IsTrue(m.Rt.GrowthOpen);
            Assert.IsFalse(m.H.Sim.Ring.IsOpen);
            Assert.AreEqual(0, m.Mg.Recognizer.History.Count, "no input buffer left");
            var json = P1SaveSnapshot.Save(m.Rt);
            P1SaveSnapshot.Load(m.Rt, json, "t");
            Assert.IsTrue(m.Rt.GrowthOpen, "growth resumes after load");
            Assert.IsFalse(m.Rt.Prog.GrantExp(100, m.G.NextEventId - 1), "EXP event not paid twice");
            m.Rt.ChooseGrowth(0);
            Assert.AreEqual(m.H.C.PlayerMaxHp, m.H.C.PlayerHp);
            Assert.AreEqual(0, m.H.Count("player.death"));
            Assert.AreEqual(2, m.G.Level);
        }

        [Test]
        public void T202_Lv49ToLv50ChainMerchantCheckpoint()
        {
            var m = Rig(Regions());
            m.G.Level = 48;
            m.G.LevelExp = 0;
            m.Rt.Prog.GrantExp(m.P.ExpNeeded(48) + m.P.ExpNeeded(49) + 30, 1);
            Assert.AreEqual(2, m.G.PendingLevels);
            Assert.AreEqual(30, m.G.Lv50Currency, "excess converted once");
            Assert.AreEqual("GrowthPending(GRO-50)", m.Rt.Merchant.OpenShop());
            m.Rt.ChooseGrowth(0);
            m.Rt.ChooseGrowth(0);
            Assert.AreEqual(50, m.G.Level);
            m.Rt.SaveCheckpoint("t");
            m.G.Lv50Currency += 1000;
            Assert.IsNull(m.Rt.Merchant.OpenShop());
            Assert.IsNull(m.Rt.Merchant.Buy(StockIndex(m.Rt.Merchant, "RerollToken")));
            Assert.AreEqual(1030 - 225, m.G.Lv50Currency);
            m.Rt.RestoreCheckpoint("t");
            Assert.AreEqual(30, m.G.Lv50Currency);
            Assert.AreEqual(0, m.G.RerollTokens);
        }

        [Test]
        public void T203_SpawnFailureAndReentryStable()
        {
            var m = Rig(p =>
            {
                p.Merchant.BaseChance = 1f;
                p.Regions.Add(new RegionDef { Id = "R1", MapIds = { "T1" } });
                p.Regions.Add(new RegionDef { Id = "R2", MapIds = { "T2" }, SafeSpawns = { new SafeSpawnDef { MapId = "T2", X = 5, Blocked = true } } });
                p.Regions.Add(new RegionDef { Id = "R3", MapIds = { "T3" }, SafeSpawns = { new SafeSpawnDef { MapId = "T3", X = 5 } } });
            });
            var mer = m.Rt.Merchant;
            Assert.AreEqual(RegionMerchant.NotEligible, mer.State("R1").State);
            mer.OnMapEntered("T2");
            Assert.AreEqual(RegionMerchant.SpawnBlockedAfterSuccessfulRoll, mer.State("R2").State);
            Assert.AreEqual(0, mer.MissStreak);
            mer.OnMapEntered("T3");
            Assert.AreEqual(RegionMerchant.Present, mer.State("R3").State);
            var s3 = MerchantSystem.Describe(mer.State("R3"));
            mer.OnMapEntered("T2");
            mer.OnMapEntered("T3");
            Assert.AreEqual(RegionMerchant.SpawnBlockedAfterSuccessfulRoll, mer.State("R2").State);
            Assert.AreEqual(s3, MerchantSystem.Describe(mer.State("R3")));
        }

        [Test]
        public void T204_UiCloseVsCommittedTrade()
        {
            var m = Rig(Regions(), ("a", ExpBag("a", 1, 100), 30f));
            m.G.LevelExp = 1000;
            m.Rt.Merchant.OpenShop();
            m.Rt.Merchant.Buy(StockIndex(m.Rt.Merchant, "PermPack"));
            m.Rt.Merchant.Interrupt("GrowthPreempt(GRO-47)");
            Assert.AreEqual(1000, m.G.LevelExp, "uncommitted: nothing paid");
            Assert.AreEqual(0, m.G.PermPackUses);
            m.Rt.Merchant.OpenShop();
            m.Rt.Merchant.Buy(StockIndex(m.Rt.Merchant, "PermPack"));
            m.Rt.Merchant.ChoosePack(0);
            var paid = m.G.LevelExp;
            m.Rt.Merchant.Interrupt("GrowthPreempt(GRO-47)");
            Assert.AreEqual(paid, m.G.LevelExp, "committed trade kept");
            Assert.AreEqual(1, m.G.PermPackUses);
        }
    }
}
