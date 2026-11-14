using System;
using System.Linq;
using Corum2.Core;
using Corum2.Core.P1;
using NUnit.Framework;
using static Corum2.Tests.CombatFixtures;

namespace Corum2.Tests
{
    /// <summary>P1-C: commands, MP, scrolls, commit-time spending, the 11 spells (P1 master §2, MAG-01~107, BAL-10~12).</summary>
    public class P1MagicTests
    {
        public sealed class M
        {
            public readonly P1Profile P;
            public readonly SimHarness H;
            public P1Runtime Rt => H.Sim.P1;
            public P1Progress G => Rt.G;
            public P1MagicSystem Mg => Rt.Magic;

            public M(Action<P1Profile> tweak = null, params (string id, EnemyDefinition def, float x)[] enemies)
            {
                P = P1Defaults.Create();
                tweak?.Invoke(P);
                var ring = new RingProfile { Meta = new DataMeta { DomainId = "ring.p1test", DataVersion = "p1-1", Status = "experimental" } };
                P1Runtime.AddRingItems(P, ring);
                foreach (var t in P.Items) ring.StartInventory.Add(new ItemStack { ItemId = t.Id, Count = 3 });
                var defs = enemies.Select(e => e.def).GroupBy(d => d.Id).Select(g => g.First()).ToList();
                H = new SimHarness("p1-magic", Fixtures.Unlocks(false, false, false), World(enemies.Select(e => (e.id, e.def.Id, e.x)).ToArray()), null,
                    Profile(), Unlocks(), defs, ring, P);
            }

            public M Learn(params string[] f) { foreach (var x in f) G.Learned.Add(x); return this; }
            public M Mp(float v) { G.CurMp = v; return this; }
            public EnemyActor E(string id) => H.C.Enemies.First(e => e.Id == id);
            public int Scrolls(string spell) => H.Sim.Items.Inventory.Count("p1test.scroll." + P1Defaults.Short(spell));

            public M Motion(params Dir8[] dirs)
            {
                foreach (var d in dirs) H.Dir(d).Step();
                return this;
            }

            public M Button(LogicalAction a)
            {
                H.Press(a).Step();
                H.Release(a).Dir(Dir8.None).Step();
                return this;
            }

            public M Cast(LogicalAction a, params Dir8[] dirs) => Motion(dirs).Button(a);

            public M Scroll(string spell)
            {
                H.Sim.Items.Inventory.Quickslots[0] = "p1test.scroll." + P1Defaults.Short(spell);
                H.Ui(LogicalAction.QuickUp).Step();
                return this;
            }

            public M Settle(int ticks = 30) { H.Step(ticks); return this; }
        }

        static readonly Dir8[] Missile = { Dir8.Right, Dir8.Up, Dir8.Left };
        static readonly Dir8[] QCF = { Dir8.Down, Dir8.DownRight, Dir8.Right };
        static readonly Dir8[] QCB = { Dir8.Down, Dir8.DownLeft, Dir8.Left };
        static readonly Dir8[] DP = { Dir8.Right, Dir8.Down, Dir8.DownRight };
        static readonly Dir8[] RDP = { Dir8.Left, Dir8.Down, Dir8.DownLeft };
        static readonly Dir8[] HCF = { Dir8.Left, Dir8.DownLeft, Dir8.Down, Dir8.DownRight, Dir8.Right };
        static readonly Dir8[] HCB = { Dir8.Right, Dir8.DownRight, Dir8.Down, Dir8.DownLeft, Dir8.Left };

        // ---------------------------------------------------------------- learning / scrolls / no fallback

        [Test]
        public void T05_T58_T62_UnlearnedMissileCommandRefusedScrollWorks()
        {
            var m = new M(null, ("e", Bag("e", 100), 14f));
            m.Cast(LogicalAction.Attack, Missile).Settle(20);
            Assert.AreEqual(1, m.H.Count("p1.magic.refuse", "NotLearned"));
            Assert.AreEqual(0, m.H.Count("attack.start"), "no fallback to the X attack (MAG-104)");
            Assert.AreEqual(5f, m.G.CurMp);
            m.H.P.Facing = 1; // the refused →↑← turned the walking character left
            m.Scroll(P1Defaults.MissileId).Settle(80);
            Assert.AreEqual(1, m.H.Count("p1.magic.commit"));
            Assert.AreEqual(5f, m.G.CurMp, "scroll: MP 0");
            Assert.AreEqual(2, m.Scrolls(P1Defaults.MissileId), "one scroll consumed");
            Assert.IsFalse(m.G.Has("MagicMissile"), "scroll is not learning");
            Assert.Less(m.E("e").Hp, 100f);
        }

        [Test]
        public void T06_T77_CommandAndScrollSameEffectAfterLearning()
        {
            var a = new M(null, ("e", Bag("e", 100), 14f)).Learn("MagicMissile");
            a.Cast(LogicalAction.Attack, Missile).Settle(60);
            var b = new M(null, ("e", Bag("e", 100), 14f)).Learn("MagicMissile");
            b.Scroll(P1Defaults.MissileId).Settle(60);
            Assert.AreEqual(a.E("e").Hp, b.E("e").Hp, "same damage");
            Assert.AreEqual(4f, a.G.CurMp, "command MP 1 [시험]");
            Assert.AreEqual(5f, b.G.CurMp);
            var ca = a.H.Sink.Events.First(e => e.Type == "p1.magic.commit").SimulationTick - a.H.Sink.Events.First(e => e.Type == "p1.magic.castStart").SimulationTick;
            var cb = b.H.Sink.Events.First(e => e.Type == "p1.magic.commit").SimulationTick - b.H.Sink.Events.First(e => e.Type == "p1.magic.castStart").SimulationTick;
            Assert.AreEqual(ca, cb, "same cast time");
            // 10 other spells: unlearned scroll gives the base effect, learning flag unchanged
            foreach (var s in a.P.Spells.Where(x => x.Kind != SpellKind.Missile && x.Kind != SpellKind.Archangel && x.Kind != SpellKind.Demon))
            {
                var t = new M(null, ("e", Bag("e", 1000), 13f));
                t.Scroll(s.Id).Settle(40);
                Assert.AreEqual(1, t.H.Count("p1.magic.commit"), s.Id);
                Assert.AreEqual(2, t.Scrolls(s.Id), s.Id);
                Assert.IsFalse(t.G.Has(s.Id), s.Id);
            }
        }

        [Test]
        public void T75_T76_T79_T80_UnlockCardsAndNoHiddenActions()
        {
            var p = P1Defaults.Create();
            Assert.AreEqual(11, p.Spells.Count);
            Assert.IsFalse(p.Cards.Any(c => c.Id.Contains("712") || c.Id.Contains("789")));
            var lv = p.Cards.Where(c => c.Kind == CardKind.SpellUnlock).ToDictionary(c => c.Unlocks, c => c.MinLevel);
            CollectionAssert.AreEqual(new[] { 4, 6, 8, 14, 16, 18, 28, 34, 36, 38 },
                new[] { "light_orb", "dark_orb", "heal", "fire_ball", "light_sword", "dark_sword", "shield", "archangel", "demon", "fire_wall" }.Select(x => lv["spell." + x]));
            Assert.IsTrue(p.Cards.Where(c => c.Kind == CardKind.SpellUnlock).All(c => c.Rarity == Rarity.Normal && c.OneTime));
            Assert.IsFalse(p.Mandatory.Any(m => m.Id.StartsWith("spell.")), "unlocks never get 3->2->1 pressure");
            var g = P1Progress.New(p);
            var r = new GrowthRules(p);
            g.Level = 40;
            Assert.IsFalse(g.Has("spell.light_orb"), "reaching the level does not unlock");
            Assert.AreEqual(2, r.SpellCost(g, "spell.light_orb"), "no automatic cost change with level");
            CollectionAssert.AreEqual(new[] { 2, 3, 3, 5, 6, 7, 10, 25, 30, 15 },
                new[] { "light_orb", "dark_orb", "heal", "fire_ball", "light_sword", "dark_sword", "shield", "archangel", "demon", "fire_wall" }.Select(x => p.Spell("spell." + x).BaseMp), "T88 origin MP seeds");
        }

        [Test]
        public void T89_T141_MpShortFailsNoDowngradeNoFallback()
        {
            var m = new M(null, ("e", Bag("e", 100), 14f)).Learn("spell.fire_ball").Mp(4);
            m.Cast(LogicalAction.Attack, DP).Settle(30);
            Assert.AreEqual(1, m.H.Count("p1.magic.refuse", "MpShort"));
            Assert.AreEqual(0, m.H.Count("p1.magic.commit"));
            Assert.AreEqual(0, m.H.Count("attack.start"), "no X fallback");
            Assert.AreEqual(4f, m.G.CurMp);
            var a = new M(null).Mp(5);
            a.Cast(LogicalAction.Jump, QCF).Settle(30);
            Assert.AreEqual(1, a.H.Count("p1.magic.refuse", "NotLearned"));
            Assert.AreEqual(0, a.H.Sink.Events.Count(e => e.Type == "input" && e.Reason != null && e.Reason.Contains("jump")), "no A fallback");
            Assert.IsTrue(a.H.P.Grounded);
        }

        [Test]
        public void T133_T140_HealQcfB_FailureDoesNotUseItem()
        {
            var m = new M(null).Learn("spell.heal");
            m.H.C.SetPlayerHp(3, "test");
            m.Cast(LogicalAction.ButtonB, QCF).Settle(30);
            Assert.AreEqual(1, m.H.Count("p1.magic.commit"));
            Assert.AreEqual(8f, m.H.C.PlayerHp, "fixed +5");
            Assert.AreEqual(2f, m.G.CurMp, "MP 3 spent");
            var f = new M(null).Mp(0).Learn("spell.heal");
            var items = f.H.Sim.Items.Inventory.Describe();
            f.Cast(LogicalAction.ButtonB, QCF).Settle(30);
            Assert.AreEqual(1, f.H.Count("p1.magic.refuse", "MpShort"));
            Assert.AreEqual(items, f.H.Sim.Items.Inventory.Describe(), "no item used by the failed B");
            Assert.AreEqual(0, f.H.Count("item.use"));
        }

        [Test]
        public void T131_T132_RbIsGuardOnly()
        {
            var m = new M(null).Learn("spell.heal");
            m.H.C.SetPlayerHp(3, "test");
            m.Motion(QCF);
            m.H.Press(LogicalAction.Guard).Step(5);
            Assert.AreEqual(MotorState.Guard, m.H.P.State);
            Assert.AreEqual(0, m.H.Count("p1.magic.command"), "QCF+RB is not a heal");
            Assert.AreEqual(3f, m.H.C.PlayerHp);
        }

        [Test]
        public void T78_T127_T128_CommandMapAndMirroring()
        {
            var p = P1Defaults.Create();
            var rec = new P1CommandRecognizer(p);
            string Match(Dir8[] seq, SpellButton b, int facing)
            {
                rec.Clear(Dir8.None);
                var t = 0.0;
                foreach (var d in seq) { rec.Observe(d, t, facing); t += 0.03; }
                return rec.Match(b, t)?.Id;
            }
            Assert.AreEqual("spell.light_orb", Match(QCF, SpellButton.X, 1));
            Assert.AreEqual("spell.dark_orb", Match(QCB, SpellButton.X, 1));
            Assert.AreEqual("spell.fire_ball", Match(DP, SpellButton.X, 1));
            Assert.AreEqual("spell.fire_wall", Match(RDP, SpellButton.X, 1));
            Assert.AreEqual("spell.archangel", Match(HCF, SpellButton.X, 1));
            Assert.AreEqual("spell.demon", Match(HCB, SpellButton.X, 1));
            Assert.AreEqual("spell.heal", Match(QCF, SpellButton.B, 1));
            Assert.AreEqual("spell.light_sword", Match(QCF, SpellButton.A, 1));
            Assert.AreEqual("spell.dark_sword", Match(QCB, SpellButton.A, 1));
            Assert.AreEqual("spell.shield", Match(DP, SpellButton.A, 1));
            Assert.AreEqual(P1Defaults.MissileId, Match(Missile, SpellButton.X, 1));
            // facing left: forward/back mirrored; the missile stays absolute
            Assert.AreEqual("spell.light_orb", Match(QCF.Select(P1CommandRecognizer.Mirror).ToArray(), SpellButton.X, -1));
            Assert.AreEqual("spell.dark_orb", Match(QCF, SpellButton.X, -1));
            Assert.AreEqual(P1Defaults.MissileId, Match(Missile, SpellButton.X, -1));
            Assert.IsNull(Match(new[] { Dir8.Down, Dir8.Right }, SpellButton.X, 1), "incomplete motion = plain X");
        }

        [Test]
        public void T129_LongestMotionWins()
        {
            var m = new M(p => p.Spell("spell.archangel").BaseMp = 1).Learn("spell.archangel", "spell.light_orb");
            m.Cast(LogicalAction.Attack, HCF).Settle(30);
            Assert.AreEqual("spell.archangel", m.H.Sink.Events.First(e => e.Type == "p1.magic.commit").Get("spellId"));
            Assert.AreEqual(1, m.H.Count("p1.magic.commit"), "the QCF tail did not cast a light orb too");
        }

        [Test]
        public void T134_T135_T136_NonCombatContextDropsBuffer()
        {
            var m = new M(null).Learn("MagicMissile");
            m.Motion(Dir8.Right, Dir8.Up);
            m.H.Ui(LogicalAction.RingToggle).Step();
            Assert.IsTrue(m.H.Sim.Ring.IsOpen);
            Assert.AreEqual(0, m.Mg.Recognizer.History.Count, "partial motion dropped on ring open");
            m.H.Dir(Dir8.Left).Step();
            m.H.Ui(LogicalAction.RingToggle).Step();
            m.Button(LogicalAction.Attack).Settle(20);
            Assert.AreEqual(0, m.H.Count("p1.magic.commit"), "no replay after the ring closes");
            Assert.AreEqual(5f, m.G.CurMp);
            m.Cast(LogicalAction.Attack, Missile).Settle(30);
            Assert.AreEqual(1, m.H.Count("p1.magic.commit"), "a new complete command works");
        }

        [Test]
        public void T142_CannotCastStateDiscardsNoLaterAutoCast()
        {
            var m = new M(null).Learn("MagicMissile");
            m.H.Press(LogicalAction.Attack).Step();
            m.H.Release(LogicalAction.Attack).Step();
            Assert.AreEqual(MotorState.GroundAttack, m.H.P.State);
            m.Motion(Missile).Button(LogicalAction.Attack);
            Assert.AreEqual(1, m.H.Count("p1.magic.refuse", "StateCannotCast"));
            m.Settle(40);
            Assert.AreEqual(0, m.H.Count("p1.magic.commit"), "not cast later");
            Assert.AreEqual(5f, m.G.CurMp);
            Assert.AreEqual(1, m.H.Count("attack.start"), "the refused press is not a buffered combo either");
        }

        [Test]
        public void T143_TwoSpellsSameTickAllRefused()
        {
            var m = new M(null).Learn("spell.light_orb", "spell.heal");
            m.Motion(QCF);
            m.H.Press(LogicalAction.Attack).Press(LogicalAction.ButtonB).Step();
            m.Settle(20);
            Assert.AreEqual(2, m.H.Count("p1.magic.refuse", "MultipleSpells"));
            Assert.AreEqual(0, m.H.Count("p1.magic.commit"));
            Assert.AreEqual(5f, m.G.CurMp);
            Assert.AreEqual(0, m.H.Count("attack.start"));
        }

        [Test]
        public void T144_T172_SpendOnceAtCommitRecheckAndCancel()
        {
            var m = new M(null, ("e", Bag("e", 100), 14f)).Learn("MagicMissile");
            m.G.Axes["mm.proj"] = 4;
            m.Cast(LogicalAction.Attack, Missile);
            Assert.AreEqual(5f, m.G.CurMp, "nothing spent at the start");
            m.Settle(30);
            Assert.AreEqual(4f, m.G.CurMp, "5 projectiles, one payment");
            // cancelled by guard before the effect: nothing spent
            var c = new M(null).Learn("MagicMissile");
            c.Cast(LogicalAction.Attack, Missile);
            c.H.Press(LogicalAction.Guard).Step(20);
            Assert.AreEqual(0, c.H.Count("p1.magic.commit"));
            Assert.AreEqual(1, c.H.Count("p1.magic.cancelled"));
            Assert.AreEqual(5f, c.G.CurMp);
            // MP drops to below the cost during the cast: commit fails, nothing spent
            var r = new M(null).Learn("spell.fire_ball");
            r.Cast(LogicalAction.Attack, DP);
            r.G.CurMp = 3;
            r.Settle(30);
            Assert.AreEqual(1, r.H.Count("p1.magic.commitFail", "CommitMpRecheck"));
            Assert.AreEqual(3f, r.G.CurMp);
            // scroll gone during the cast
            var s = new M(null);
            s.Scroll("spell.light_orb");
            s.H.Sim.Items.Inventory.TestSetCount("p1test.scroll.light_orb", 0);
            s.Settle(30);
            Assert.AreEqual(1, s.H.Count("p1.magic.commitFail", "CommitScrollRecheck"));
        }

        [Test]
        public void T173_FullHpHealStillSpends()
        {
            var m = new M(null).Learn("spell.heal");
            m.Cast(LogicalAction.ButtonB, QCF).Settle(30);
            Assert.AreEqual(1, m.H.Count("p1.magic.commit"));
            Assert.AreEqual(2f, m.G.CurMp);
            Assert.AreEqual(m.H.C.PlayerMaxHp, m.H.C.PlayerHp);
        }

        [Test]
        public void T93_NoCooldown()
        {
            var m = new M(null, ("e", Bag("e", 1000), 14f)).Learn("MagicMissile");
            m.Cast(LogicalAction.Attack, Missile).Settle(25);
            m.Cast(LogicalAction.Attack, Missile).Settle(25);
            Assert.AreEqual(2, m.H.Count("p1.magic.commit"));
            Assert.AreEqual(3f, m.G.CurMp);
        }

        // ---------------------------------------------------------------- missile model

        [Test]
        public void T26_T28_T34_T35_T36_MissileCardArithmetic()
        {
            var p = P1Defaults.Create();
            var r = new GrowthRules(p);
            P1Progress G0() { var g = P1Progress.New(p); g.Learned.Add("MagicMissile"); return g; }
            int Cost(P1Progress g) => r.SpellCost(g, P1Defaults.MissileId);
            var g1 = G0(); r.Apply(g1, p.Card("card.missile.N"));
            Assert.AreEqual(5f, r.MissilePermanentDamage(g1)); Assert.AreEqual(2, Cost(g1));
            var g2 = G0(); r.Apply(g2, p.Card("card.missile.R"));
            Assert.AreEqual(6f, r.MissilePermanentDamage(g2)); Assert.AreEqual(1, Cost(g2));
            var g3 = G0(); r.Apply(g3, p.Card("card.missile.E.proj"));
            Assert.AreEqual(3f, r.MissilePermanentDamage(g3)); Assert.AreEqual(2, r.MissileProjectiles(g3));
            var g4 = G0(); r.Apply(g4, p.Card("card.missile.L"));
            Assert.AreEqual(8f, r.MissilePermanentDamage(g4)); Assert.AreEqual(1, Cost(g4), "legendary floor 1");
            var g5 = G0(); r.Apply(g5, p.Card("card.missile.N")); r.Apply(g5, p.Card("card.missile.R")); r.Apply(g5, p.Card("card.missile.L"));
            var g6 = G0(); r.Apply(g6, p.Card("card.missile.L")); r.Apply(g6, p.Card("card.missile.R")); r.Apply(g6, p.Card("card.missile.N"));
            Assert.AreEqual(13f, r.MissilePermanentDamage(g5)); Assert.AreEqual(1, Cost(g5));
            Assert.AreEqual(r.MissilePermanentDamage(g5), r.MissilePermanentDamage(g6), "order independent");
            Assert.AreEqual(Cost(g5), Cost(g6));
            StringAssert.StartsWith("RequiresLearned", r.Reject(P1Progress.New(p), p.Card("card.missile.N"), 10), "no upgrade before learning");
        }

        [Test]
        public void T29_T30_T31_T40_ProjectileCapAndFilter()
        {
            var p = P1Defaults.Create();
            p.StoryRarity[0].Epic = 10; // allow epic for the filter check
            var r = new GrowthRules(p);
            var g = P1Progress.New(p);
            g.Learned.Add("MagicMissile");
            Assert.AreEqual(1, r.MissileProjectiles(g));
            for (var i = 2; i <= 5; i++) { r.Apply(g, p.Card("card.missile.E.proj")); Assert.AreEqual(i, r.MissileProjectiles(g)); }
            r.Apply(g, p.Card("card.missile.E.proj"));
            Assert.AreEqual(5, r.MissileProjectiles(g), "never above 5");
            StringAssert.StartsWith("NoEffect", r.Reject(g, p.Card("card.missile.E.proj"), 10));
            Assert.IsNull(r.Reject(g, p.Card("card.missile.E.homing"), 10), "homing still a candidate at 5");
            var json = MiniJson.Write(g.ToJson());
            var g2 = P1Progress.FromJson((MiniJson.Obj)MiniJson.Parse(json));
            Assert.AreEqual(5, r.MissileProjectiles(g2));
        }

        [Test]
        public void T32_T33_T38_T42_T43_T44_HomingAxes()
        {
            var p = P1Defaults.Create();
            var r = new GrowthRules(p);
            void H(int n, float a, float d, float t) { r.Homing(n, out var x, out var y, out var z); Assert.AreEqual(a, x); Assert.AreEqual(d, y, 1e-4); Assert.AreEqual(t, z); }
            H(0, 0, 0, 0);
            H(1, 12, 5, 40); H(2, 24, 6.5f, 65); H(3, 36, 8, 90); H(4, 48, 9.5f, 115); H(5, 48, 11, 140);
            var g = P1Progress.New(p);
            g.Learned.Add("MagicMissile");
            p.StoryRarity[0].Epic = 10;
            g.Axes["mm.homing"] = 4;
            Assert.IsNull(r.Reject(g, p.Card("card.missile.E.homing"), 10), "angle capped, two axes still grow");
            g.Axes["mm.homing"] = 5;
            StringAssert.StartsWith("NoEffect", r.Reject(g, p.Card("card.missile.E.homing"), 10), "all three capped");
            var before = r.MissilePermanentDamage(g);
            r.Apply(g, p.Card("card.missile.E.homing"));
            Assert.AreEqual(before, r.MissilePermanentDamage(g));
            Assert.AreEqual(1, r.MissileProjectiles(g), "homing card adds no projectile");
            Assert.AreEqual(1, r.SpellCost(g, P1Defaults.MissileId));
            p.HomingAngleCap = 30;
            H(4, 30, 9.5f, 115);
        }

        [Test]
        public void T52_FanAngles()
        {
            var m = new M(null).Learn("MagicMissile");
            m.G.Axes["mm.proj"] = 4;
            m.Cast(LogicalAction.Attack, Missile);
            m.H.StepUntil(() => m.Mg.Projectiles.Count > 0, 30);
            CollectionAssert.AreEquivalent(new[] { -12f, -6f, 0f, 6f, 12f }, m.Mg.Projectiles.Select(x => (float)Math.Round(x.Angle)).ToArray());
        }

        [Test]
        public void T37_T54_T55_T60_EachProjectileHitsOnceAndStops()
        {
            var m = new M(null, ("e", Bag("e", 1000), 12f), ("far", Bag("far", 1000), 20f)).Learn("MagicMissile");
            m.G.Axes["mm.proj"] = 2;
            m.G.Axes["stat.MagAtk"] = 2;
            m.Cast(LogicalAction.Attack, Missile).Settle(60);
            Assert.AreEqual(1000 - 3 * 5, m.E("e").Hp, "3 projectiles x (3 + 2 magic attack) each");
            Assert.AreEqual(1000f, m.E("far").Hp, "first collision ends the projectile");
            Assert.AreEqual(3, m.H.Sink.Events.Count(e => e.Type == "hit" && (string)e.TargetId == "e"), "independent hit events");
        }

        [Test]
        public void T41_T46_T48_HomingSelection()
        {
            var m = new M(null, ("near", Bag("near", 100), 13f), ("farFront", Bag("ff", 100), 14.5f), ("back", Bag("b", 100), 8f)).Learn("MagicMissile");
            var mg = m.Mg;
            var from = new Vec2(10.5f, 1f);
            Assert.AreEqual("near", mg.SelectTarget(from, 0, 12, 5));
            Assert.AreEqual("", mg.SelectTarget(from, 180, 12, 1), "nothing in the cone -> no target");
            Assert.AreNotEqual("back", mg.SelectTarget(from, 0, 48, 11), "back enemy never chosen");
            Assert.AreEqual("", mg.SelectTarget(from, 90, 12, 5), "outside the angle");
        }

        [Test]
        public void T47_T49_TargetInvalidStraightNoResearch()
        {
            var m = new M(null, ("a", Bag("a", 100), 18f), ("b", Bag("b", 100), 19f)).Learn("MagicMissile");
            m.G.Axes["mm.homing"] = 1;
            m.H.Dir(Dir8.Up);
            m.Cast(LogicalAction.Attack, Missile);
            m.H.StepUntil(() => m.Mg.Projectiles.Count > 0, 30);
            var p = m.Mg.Projectiles[0];
            Assert.AreEqual("", p.TargetId, "aimed up: no enemy in the cone at launch");
            Assert.IsTrue(p.TargetLost);
            var m2 = new M(null, ("a", Bag("a", 100), 15f), ("b", Bag("b", 100), 16f)).Learn("MagicMissile");
            m2.G.Axes["mm.homing"] = 1;
            m2.Cast(LogicalAction.Attack, Missile);
            m2.H.StepUntil(() => m2.Mg.Projectiles.Count > 0, 30);
            var q = m2.Mg.Projectiles[0];
            Assert.AreEqual("a", q.TargetId);
            m2.E("a").State = EnemyState.Gone; // removed from the world
            m2.H.Step();
            Assert.IsTrue(q.TargetLost);
            Assert.AreEqual("a", q.TargetId, "no re-search to b");
        }

        [Test]
        public void T56_MapChangeClearsProjectiles()
        {
            var m = new M(null).Learn("MagicMissile");
            m.Cast(LogicalAction.Attack, Missile);
            m.H.StepUntil(() => m.Mg.Projectiles.Count > 0, 30);
            m.Mg.OnMapChanged();
            Assert.AreEqual(0, m.Mg.Projectiles.Count);
            Assert.AreEqual(1, m.H.Count("p1.magic.mapClear"));
        }

        [Test]
        public void T57_T90_T137_T138_T98_EligibleMaxMpAndPostCardCost()
        {
            var p = P1Defaults.Create();
            var r = new GrowthRules(p);
            var g = P1Progress.New(p);
            g.CurMp = 0;
            StringAssert.StartsWith("PostCardMpCost6", r.Reject(g, p.Card("card.spell.light_sword.unlock"), 20));
            g.Axes["stat.MaxMp"] = 1;
            Assert.IsNull(r.Reject(g, p.Card("card.spell.light_sword.unlock"), 20), "permanent MP +1 -> eligible; current MP irrelevant");
            g.Learned.Add("MagicMissile");
            g.Axes["stat.MaxMp"] = 0;
            g.Axes["cost." + P1Defaults.MissileId] = 3; // cost 4
            Assert.IsNull(r.Reject(g, p.Card("card.missile.N"), 10), "4 + 1 = 5 <= 5");
            g.Axes["cost." + P1Defaults.MissileId] = 4;
            StringAssert.StartsWith("PostCardMpCost6", r.Reject(g, p.Card("card.missile.N"), 10));
            Assert.IsNull(r.Reject(g, p.Card("card.missile.R"), 10), "rare keeps eligible (no MP change)");
            g.Learned.Add("spell.dark_orb");
            p.StoryRarity[0].Epic = 10;
            g.Axes["cost.spell.dark_orb"] = 2; // 5
            StringAssert.StartsWith("PostCardMpCost6", r.Reject(g, p.Card("card.spell.dark_orb.E"), 30), "MAG-63 high tier +1 -> 6 > 5");
        }

        [Test]
        public void T139_PerSpellMinimumAndScrollZero()
        {
            var p = P1Defaults.Create();
            var r = new GrowthRules(p);
            var g = P1Progress.New(p);
            g.Axes["cost.spell.heal"] = -10;
            Assert.AreEqual(1, r.SpellCost(g, "spell.heal"));
            p.Spell("spell.heal").MinMp = 2;
            Assert.AreEqual(2, r.SpellCost(g, "spell.heal"), "editable per spell");
            Assert.AreEqual(1, r.SpellCost(g, P1Defaults.MissileId));
        }

        [Test]
        public void T178_MpDiscountFloorThenMinimum()
        {
            Assert.AreEqual(2, DamageCalc.CommandMp(3, 1, 0.10f, 0.3f));
            Assert.AreEqual(1, DamageCalc.CommandMp(1, 1, 0.30f, 0.3f));
            Assert.AreEqual(2, DamageCalc.CommandMp(3, 2, 0.30f, 0.3f));
            var m = new M(null).Learn("spell.fire_ball");
            m.Rt.Buffs.Add("BF-MPCOST", Rarity.Normal);
            m.Mp(5).Cast(LogicalAction.Attack, DP).Settle(30);
            Assert.AreEqual(1f, m.G.CurMp, "floor(5 x 0.9) = 4 spent");
        }

        [Test]
        public void T130_AimFollowsDirectionNotCommandEnd()
        {
            var m = new M(null).Learn("MagicMissile");
            m.Motion(Missile);
            m.H.Press(LogicalAction.Attack).Step();
            m.H.Release(LogicalAction.Attack).Dir(Dir8.UpRight);
            m.H.StepUntil(() => m.Mg.Projectiles.Count > 0, 30);
            Assert.AreEqual(45f, m.Mg.Projectiles[0].Angle, 0.01f, "aim = held direction at commit");
            var n = new M(null).Learn("MagicMissile");
            n.Cast(LogicalAction.Attack, Missile);
            n.H.StepUntil(() => n.Mg.Projectiles.Count > 0, 30);
            Assert.AreEqual(0f, n.Mg.Projectiles[0].Angle, "no input -> facing, the last ← does not force the angle");
        }

        // ---------------------------------------------------------------- other spells

        [Test]
        public void T82_T94_LightOrbFastWeakChainExtra()
        {
            var p = P1Defaults.Create();
            Assert.Greater(p.Spell("spell.light_orb").P("speed"), p.Spell("spell.dark_orb").P("speed"));
            Assert.Less(p.Spell("spell.light_orb").P("damage"), p.Spell("spell.dark_orb").P("damage"));
            var m = new M(null, ("a", Bag("a", 100), 13f), ("b", Bag("b", 100), 15f)).Learn("spell.light_orb");
            m.G.Axes["spell.light_orb.chain"] = 1;
            m.Cast(LogicalAction.Attack, QCF).Settle(60);
            Assert.AreEqual(95f, m.E("a").Hp);
            Assert.AreEqual(95f, m.E("b").Hp, "chained to a new target");
            var x = new M(null, ("a", Bag("a", 100), 13f)).Learn("spell.light_orb");
            x.G.Axes["spell.light_orb.extraShots"] = 2;
            x.Cast(LogicalAction.Attack, QCF).Settle(60);
            Assert.AreEqual(85f, x.E("a").Hp, "1 + 2 extra shots");
            Assert.AreEqual(1, x.H.Count("p1.magic.commit"), "one payment");
        }

        [Test]
        public void T95_T99_DarkOrbPierceAndShock()
        {
            var m = new M(null, ("a", Bag("a", 100), 13f), ("b", Bag("b", 100), 14.5f)).Learn("spell.dark_orb");
            m.G.Axes["spell.dark_orb.pierce"] = 1;
            m.Cast(LogicalAction.Attack, QCB).Settle(80);
            Assert.AreEqual(90f, m.E("a").Hp, "pierced once, a hit only once");
            Assert.AreEqual(90f, m.E("b").Hp);
            var s = new M(null, ("a", Bag("a", 100), 13f), ("b", Bag("b", 100), 13.6f)).Learn("spell.dark_orb");
            s.G.Axes["spell.dark_orb.shock"] = 2;
            s.Cast(LogicalAction.Attack, QCB).Settle(80);
            Assert.AreEqual(1, s.H.Count("p1.magic.explode", "DarkOrb:Shockwave"));
            Assert.Less(s.E("b").Hp, 100f, "shockwave on the final collision");
        }

        [Test]
        public void T96_T174_T100_FireBallBurnDelayedSeparateEvents()
        {
            var m = new M(null, ("a", Bag("a", 200), 13f)).Learn("spell.fire_ball");
            m.G.Axes["spell.fire_ball.burn"] = 3;
            m.G.Axes["spell.fire_ball.delayed"] = 1;
            m.Cast(LogicalAction.Attack, DP).Settle(250);
            Assert.AreEqual(1, m.H.Count("p1.magic.explode", "FireBall:Explosion"));
            Assert.AreEqual(1, m.H.Count("p1.magic.explode", "FireBall:Delayed1"));
            var hits = m.H.Sink.Events.Where(e => e.Type == "hit" && (string)e.TargetId == "a").ToList();
            Assert.AreEqual(2, hits.Count(e => !((string)e.Reason).StartsWith("DamageOnly")), "explosion + delayed explosion both react (BAL-12)");
            Assert.GreaterOrEqual(hits.Count(e => ((string)e.Reason).StartsWith("DamageOnly")), 3, "burn ticks are damage only (MAG-65)");
            Assert.AreEqual(200 - 15 - 7 - 3, m.E("a").Hp, 1e-3);
        }

        [Test]
        public void T97_T101_T102_FireWallTicksReplaceFixed()
        {
            var m = new M(p => p.Spell("spell.fire_wall").BaseMp = 1, ("a", Bag("a", 200), 12.2f)).Learn("spell.fire_wall");
            m.Cast(LogicalAction.Attack, RDP);
            m.H.StepUntil(() => m.Mg.Walls.Count > 0, 40);
            var x = m.Mg.Walls[0].X;
            Assert.AreEqual(m.H.P.Position.X + 2f, x, 0.05f, "2 u in front, fixed on the ground");
            m.Settle(5);
            Assert.AreEqual(x, m.Mg.Walls[0].X, "does not follow");
            m.Settle(120);
            var hits = m.H.Sink.Events.Where(e => e.Type == "hit" && (string)e.TargetId == "a").ToList();
            Assert.AreEqual(1, hits.Count(e => !((string)e.Reason).StartsWith("DamageOnly")), "first contact reacts once");
            Assert.GreaterOrEqual(hits.Count, 4);
            m.G.Axes["spell.fire_wall.extraWalls"] = 1;
            m.G.CurMp = 5;
            m.Cast(LogicalAction.Attack, RDP);
            m.H.StepUntil(() => m.H.Count("p1.magic.wall") >= 2, 40);
            Assert.AreEqual(2, m.Mg.Walls.Count, "recast replaced the old wall; legendary: 2 walls in one cast");
        }

        [Test]
        public void T84_T104_T108_HealFixedClampSelfHot()
        {
            var m = new M(null).Learn("spell.heal");
            m.G.Axes["spell.heal.heal"] = 2;
            m.G.Axes["spell.heal.hot"] = 3;
            m.Rt.Fx.Statuses.Add("Poison");
            m.G.Axes["spell.heal.cure"] = 1;
            m.H.C.SetPlayerHp(1, "test");
            m.Cast(LogicalAction.ButtonB, QCF).Settle(30);
            Assert.AreEqual(8f, m.H.C.PlayerHp, "1 + 5 + 2");
            Assert.IsFalse(m.Rt.Fx.Statuses.Contains("Poison"));
            m.Settle(200);
            Assert.AreEqual(10f, m.H.C.PlayerHp, "HoT clamped at max");
        }

        [Test]
        public void T86_T109_T110_T111_ShieldAfterGuardRecastReplaces()
        {
            var m = new M(p => p.Spell("spell.shield").BaseMp = 1).Learn("spell.shield");
            m.Cast(LogicalAction.Jump, DP).Settle(30);
            Assert.AreEqual(10, m.Rt.Fx.MagicShield.Remaining);
            var atk = new AttackDefinition { Id = "enemy.hit" };
            Assert.AreEqual(0f, m.Rt.Bridge.IncomingDamage(atk, 20, true, "e"), "front guard 20 -> 2, absorbed by the shield");
            Assert.AreEqual(8, m.Rt.Fx.MagicShield.Remaining);
            Assert.AreEqual(12f, m.Rt.Bridge.IncomingDamage(atk, 20, false, "e"), "unguarded 20: 8 absorbed, 12 to HP (physical or magic alike)");
            Assert.IsEmpty(m.Rt.Fx.ShieldOrder());
            m.Rt.Fx.MagicShield.Remaining = 3;
            m.G.CurMp = 5;
            m.Cast(LogicalAction.Jump, DP).Settle(30);
            Assert.AreEqual(10, m.Rt.Fx.MagicShield.Remaining, "recast replaces, no add");
        }

        [Test]
        public void T85_T112_SwordsExclusiveFailedCastKeepsOld()
        {
            var m = new M(p => { p.Spell("spell.light_sword").BaseMp = 1; p.Spell("spell.dark_sword").BaseMp = 1; }).Learn("spell.light_sword", "spell.dark_sword");
            m.Cast(LogicalAction.Jump, QCF).Settle(30);
            Assert.IsTrue(m.Rt.Fx.Sword.Light);
            m.Cast(LogicalAction.Jump, QCB).Settle(30);
            Assert.IsTrue(m.Rt.Fx.Sword.Dark, "dark replaced light");
            m.G.CurMp = 0;
            m.Cast(LogicalAction.Jump, QCF).Settle(30);
            Assert.IsTrue(m.Rt.Fx.Sword.Dark, "failed light cast keeps the dark sword");
        }

        [Test]
        public void T106_T113_LightSwordSwordTaggedAndFourthHit()
        {
            var r = new M(null, ("e", Bag("e", 1000), 12f));
            r.Rt.Fx.Sword = new SwordState { SpellId = "spell.light_sword", Bonus = 3, LastHit = 2, Remaining = 10 };
            var hp = r.E("e").Hp;
            r.H.C.TestApplyHit(new AttackDefinition { Id = "x", Kind = AttackKind.GroundNormal, TestDamage = 1, Knockback = 1 }, "e");
            Assert.AreEqual(4f, hp - r.E("e").Hp, "sword-tagged: +3");
            hp = r.E("e").Hp;
            r.H.C.TestApplyHit(new AttackDefinition { Id = "y", Kind = AttackKind.GroundNormal, TestDamage = 1, Knockback = 1, Sword = false }, "e");
            Assert.AreEqual(1f, hp - r.E("e").Hp, "not sword-tagged");
            hp = r.E("e").Hp;
            r.H.C.TestApplyHit(new AttackDefinition { Id = "z", Kind = AttackKind.Spell, DamageType = DamageType.Magic, TestDamage = 1, Knockback = 1 }, "e");
            Assert.AreEqual(1f, hp - r.E("e").Hp, "magic: no sword bonus");
        }

        [Test]
        public void T107_T114_DarkSwordActualLossKillHeal()
        {
            var r = new M(null, ("e", Bag("e", 1), 12f));
            r.Rt.Fx.Sword = new SwordState { SpellId = "spell.dark_sword", Rate = 0.25f, KillHeal = 2, Remaining = 10 };
            r.H.C.SetPlayerHp(5, "test");
            r.H.C.TestApplyHit(new AttackDefinition { Id = "x", Kind = AttackKind.GroundNormal, TestDamage = 10, Knockback = 1 }, "e");
            r.Rt.Bridge.EndTick();
            Assert.AreEqual(7f, r.H.C.PlayerHp, "actual loss 1 -> floor(0.25) = 0 lifesteal; epic kill heal +2");
        }

        [Test]
        public void T87_T118_T119_T120_T121_SummonRules()
        {
            var m = new M(p => { p.Spell("spell.archangel").BaseMp = 1; p.Spell("spell.demon").BaseMp = 1; }, ("e", Bag("e", 1000), 13f)).Learn("spell.archangel", "spell.demon");
            m.Cast(LogicalAction.Attack, HCF).Settle(30);
            Assert.AreEqual(SpellKind.Archangel, m.Mg.Summon.Kind);
            var mp = m.G.CurMp;
            m.Cast(LogicalAction.Attack, HCB).Settle(30);
            Assert.AreEqual(1, m.H.Count("p1.magic.refuse", "OtherSummonActive"));
            Assert.AreEqual(mp, m.G.CurMp, "refused without spending");
            Assert.AreEqual(SpellKind.Archangel, m.Mg.Summon.Kind);
            m.H.P.Facing = 1; // the refused HCB ended facing back
            var left = m.Mg.Summon.Remaining;
            m.Cast(LogicalAction.Attack, HCF).Settle(30);
            Assert.AreEqual(Math.Min(36f, left + 18f), m.Mg.Summon.Remaining, 1.0f, "same kind extends (minus the cast time)");
            m.Mg.Summon.Remaining = 36;
            m.H.P.Facing = 1;
            mp = m.G.CurMp;
            m.Cast(LogicalAction.Attack, HCF).Settle(30);
            Assert.AreEqual(mp - 1, m.G.CurMp, "at the cap the legal recast still spends (BAL-11)");
            Assert.IsFalse(m.H.C.Enemies.Any(e => e.Def.Id == "summon"), "summon is not an enemy target");
        }

        [Test]
        public void T115_T122_T123_DemonHitsNearestFrontMagic()
        {
            var m = new M(p => p.Spell("spell.demon").BaseMp = 1, ("near", Bag("near", 1000), 13f), ("far", Bag("far", 1000), 15f), ("back", Bag("back", 1000), 8f)).Learn("spell.demon");
            m.Cast(LogicalAction.Attack, HCB).Settle(150);
            Assert.Less(m.E("near").Hp, 1000f);
            Assert.AreEqual(1000f, m.E("far").Hp);
            Assert.AreEqual(1000f, m.E("back").Hp);
            var hit = m.H.Sink.Events.First(e => e.Type == "hit" && (string)e.TargetId == "near");
            Assert.AreEqual("Magic", hit.Get("damageType").ToString());
        }

        [Test]
        public void T124_ArchangelHealsOnlyBelowThreshold()
        {
            var m = new M(p => p.Spell("spell.archangel").BaseMp = 1).Learn("spell.archangel");
            m.Cast(LogicalAction.Attack, HCF).Settle(60);
            Assert.AreEqual(0, m.H.Count("p1.summon.heal"));
            m.H.C.SetPlayerHp(4, "test");
            m.Settle(60);
            Assert.AreEqual(1, m.H.Count("p1.summon.heal"));
            Assert.AreEqual(9f, m.H.C.PlayerHp);
        }

        [Test]
        public void T125_SummonKeepsTimeAcrossMaps()
        {
            var m = new M(p => p.Spell("spell.archangel").BaseMp = 1).Learn("spell.archangel");
            m.Cast(LogicalAction.Attack, HCF).Settle(30);
            m.Mg.Summon.Remaining = 7;
            m.Mg.OnMapChanged();
            Assert.IsNotNull(m.Mg.Summon);
            Assert.AreEqual(7f, m.Mg.Summon.Remaining);
        }

        [Test]
        public void T91_MaintainedTimersPauseInRing()
        {
            var m = new M(p => p.Spell("spell.shield").BaseMp = 1).Learn("spell.shield");
            m.Cast(LogicalAction.Jump, DP).Settle(30);
            var t = m.Rt.Fx.MagicShield.Time;
            m.H.Ui(LogicalAction.RingToggle).Step(50);
            Assert.AreEqual(t, m.Rt.Fx.MagicShield.Time, 1e-4, "ring open: no time passes");
            m.H.Ui(LogicalAction.RingToggle).Step(50);
            Assert.Less(m.Rt.Fx.MagicShield.Time, t);
        }

        [Test]
        public void T24_T25_EquipmentTraitsOnly()
        {
            var m = new M(null).Learn("AirDash");
            m.H.Step();
            Assert.IsFalse(m.H.C.Unlocks.DashPierce);
            m.G.EquipTraits.Add("AirDashPierce");
            m.G.EquipTraits.Add("AirDiveAreaAttack");
            m.H.Step();
            Assert.IsTrue(m.H.C.Unlocks.DashPierce);
            Assert.IsTrue(m.H.C.Unlocks.DiveArea);
            Assert.IsTrue(m.H.Sim.Unlocks.AirDash);
            Assert.IsFalse(m.H.Sim.Unlocks.Dive, "equipment alone does not unlock the dive");
            m.G.EquipTraits.Clear();
            m.H.Step();
            Assert.IsFalse(m.H.C.Unlocks.DashPierce);
        }

        [Test]
        public void T05_AirActionsLockedUntilLearned()
        {
            var m = new M(null);
            m.H.Step();
            Assert.IsFalse(m.H.Sim.Unlocks.ExtraJump);
            Assert.IsFalse(m.H.Sim.Unlocks.AirDash);
            Assert.IsFalse(m.H.Sim.Unlocks.Dive);
            m.Learn("DoubleJump");
            m.H.Step();
            Assert.IsTrue(m.H.Sim.Unlocks.ExtraJump);
        }
    }
}
