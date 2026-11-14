using System;
using System.Collections.Generic;
using System.Linq;
using Corum2.Core;
using Corum2.Core.P1;
using NUnit.Framework;
using static Corum2.Tests.CombatFixtures;

namespace Corum2.Tests
{
    /// <summary>P1 ids not yet covered by name in the other P1 test classes.</summary>
    public class P1CoverageTests
    {
        static readonly Dir8[] Missile = { Dir8.Right, Dir8.Up, Dir8.Left };
        static P1MagicTests.M Rig(Action<P1Profile> t = null, params (string, EnemyDefinition, float)[] e) => new P1MagicTests.M(t, e);

        [Test]
        public void T27_CommandAndScrollSameProjectilesAtCap()
        {
            var a = Rig(null, ("e", Bag("e", 1000), 12f)).Learn("MagicMissile");
            a.G.Axes["mm.proj"] = 9;
            a.Cast(LogicalAction.Attack, Missile).Settle(60);
            var b = Rig(null, ("e", Bag("e", 1000), 12f));
            b.G.Axes["mm.proj"] = 9;
            b.Scroll(P1Defaults.MissileId).Settle(80);
            var na = a.H.Sink.Events.First(e => e.Type == "p1.magic.missile").Get("count");
            var nb = b.H.Sink.Events.First(e => e.Type == "p1.magic.missile").Get("count");
            Assert.AreEqual(5, Convert.ToInt32(na), "max 5");
            Assert.AreEqual(na, nb, "scroll = command");
            Assert.AreEqual(a.E("e").Hp, b.E("e").Hp);
            Assert.AreEqual(5f, b.G.CurMp, "scroll MP 0");
        }

        [Test]
        public void T39_HomingTurnsGradually()
        {
            var m = Rig(null, ("a", Bag("a", 100), 14f)).Learn("MagicMissile");
            m.G.Axes["mm.homing"] = 1; // 40 deg/s
            m.H.Dir(Dir8.UpRight);
            m.Motion(Missile);
            m.H.Press(LogicalAction.Attack).Step();
            m.H.Release(LogicalAction.Attack).Dir(Dir8.UpRight);
            m.H.StepUntil(() => m.Mg.Projectiles.Count > 0, 30);
            m.H.Dir(Dir8.None);
            var p = m.Mg.Projectiles[0];
            var prev = p.Angle;
            var maxStep = 0f;
            for (var i = 0; i < 10 && !p.Dead; i++)
            {
                m.H.Step();
                maxStep = Math.Max(maxStep, Math.Abs(p.Angle - prev));
                prev = p.Angle;
            }
            Assert.LessOrEqual(maxStep, 40f * 0.02f + 1e-3f, "no instant turn: <= turn rate x dt");
        }

        [Test]
        public void T50_T53_LaunchTargetIgnoresObstaclePerProjectile()
        {
            var m = Rig(null, ("hidden", Bag("hidden", 100), 63f), ("open", Bag("open", 100), 66f)).Learn("MagicMissile");
            // the fixture solid (60..62, y 1.2..4) stands between x 58 and the hidden enemy
            Assert.AreEqual("hidden", m.Mg.SelectTarget(new Vec2(58, 1.0f), 0, 12, 11), "no line-of-sight filter at launch");
            var two = Rig(null, ("low", Bag("low", 100), 15f), ("high", Bag("high", 100), 15f)).Learn("MagicMissile");
            two.E("high").Grounded = false;
            two.E("high").Position = new Vec2(15f, 4f);
            Assert.AreEqual("low", two.Mg.SelectTarget(new Vec2(10.5f, 1f), -6, 12, 11));
            Assert.AreEqual("high", two.Mg.SelectTarget(new Vec2(10.5f, 1f), 30, 12, 11), "each projectile searches from its own direction");
        }

        [Test]
        public void T51_T63_MissileCommandKeptAimSeparate()
        {
            var p = P1Defaults.Create();
            Assert.AreEqual(MotionKind.MissileOriginal, p.Spell(P1Defaults.MissileId).Motion);
            CollectionAssert.AreEqual(Missile, P1CommandRecognizer.Sequence(p.Spell(P1Defaults.MissileId), -1), "original absolute →↑←");
            var m = Rig().Learn("MagicMissile");
            m.Motion(Missile);
            m.H.Press(LogicalAction.Attack).Step();
            m.H.Release(LogicalAction.Attack).Dir(Dir8.Right);
            m.H.StepUntil(() => m.Mg.Projectiles.Count > 0, 30);
            Assert.AreEqual(0f, m.Mg.Projectiles[0].Angle, 0.01f, "the last ← of the command did not set the angle");
        }

        [Test]
        public void T59_EpicVariantsTogetherAndHalfHalf()
        {
            var p = P1Defaults.Create();
            p.StoryRarity[0].Epic = 50;
            var g = P1Progress.New(p);
            g.Learned.Add("MagicMissile");
            var r = new GrowthRules(p);
            var gen = new GrowthOfferGenerator(r);
            var rng = new P1Rng(5);
            int proj = 0, hom = 0;
            for (var i = 0; i < 4000; i++)
            {
                var pool = gen.NaturalPool(g, 10, null).Where(c => c.Id.StartsWith("card.missile.E")).ToList();
                var c = gen.Draw(g, pool, rng);
                if (c.Id.EndsWith("proj")) proj++; else hom++;
            }
            Assert.That(proj / (double)(proj + hom), Is.InRange(0.45, 0.55));
            var both = false;
            for (var i = 0; i < 400 && !both; i++)
            {
                var pool = gen.NaturalPool(g, 10, null).Where(c => c.Kind == CardKind.Missile).ToList();
                var drawn = new List<string>();
                for (var k = 0; k < 3; k++) drawn.Add(gen.Draw(g, pool, rng).Id);
                both = drawn.Contains("card.missile.E.proj") && drawn.Contains("card.missile.E.homing");
            }
            Assert.IsTrue(both, "both epic variants can share one screen");
        }

        [Test]
        public void T61_MissileCapEditableAndCardExcludedAtCap()
        {
            var p = P1Defaults.Create();
            var r = new GrowthRules(p);
            var g = P1Progress.New(p);
            g.Learned.Add("MagicMissile");
            g.Axes["mm.dmg"] = 17; // 3 + 17 = 20
            StringAssert.StartsWith("NoEffect", r.Reject(g, p.Card("card.missile.R"), 10));
            p.MissilePermanentDamageCap = 25;
            Assert.IsNull(r.Reject(g, p.Card("card.missile.R"), 10));
            r.Apply(g, p.Card("card.missile.L"));
            Assert.AreEqual(25f, r.MissilePermanentDamage(g));
        }

        [Test]
        public void T81_TenSpellsFourDistinctRarityFunctions()
        {
            var p = P1Defaults.Create();
            foreach (var s in p.Spells.Where(x => x.Kind != SpellKind.Missile))
            {
                var ups = p.Cards.Where(c => c.Kind == CardKind.SpellUpgrade && c.SpellId == s.Id).ToList();
                Assert.AreEqual(4, ups.Count, s.Id);
                var sets = ups.Select(c => string.Join("+", c.Axes.Select(a => a.Axis)) + (c.MpDelta < 0 ? "+mpdown" : "")).ToList();
                // shield normal / legendary share the absorb axis (MAG-70: same absorb cap); every other spell has 4 different functions
                var expected = s.Kind == SpellKind.Shield ? 3 : 4;
                Assert.AreEqual(expected, sets.Distinct().Count(), s.Id + ": " + string.Join(" | ", sets));
            }
        }

        [Test]
        public void T83_FireBallBurstVsFireWallDuration()
        {
            var m = Rig(p => p.Spell("spell.fire_wall").BaseMp = 1, ("a", Bag("a", 500), 12.2f)).Learn("spell.fire_ball", "spell.fire_wall");
            m.Cast(LogicalAction.Attack, Dir8.Right, Dir8.Down, Dir8.DownRight).Settle(80);
            Assert.AreEqual(1, m.H.Count("p1.magic.explode"), "fire ball: one burst event");
            m.G.CurMp = 5;
            m.H.P.Facing = 1;
            m.Cast(LogicalAction.Attack, Dir8.Left, Dir8.Down, Dir8.DownLeft);
            m.H.StepUntil(() => m.Mg.Walls.Count > 0, 40);
            m.Settle(100);
            Assert.AreEqual(1, m.Mg.Walls.Count, "fire wall stays for its duration");
        }

        [Test]
        public void T88_OriginMpSeedsLoaded()
        {
            var p = P1Defaults.Create();
            var ids = new[] { "light_orb", "dark_orb", "heal", "fire_ball", "light_sword", "dark_sword", "shield", "archangel", "demon", "fire_wall" };
            CollectionAssert.AreEqual(new[] { 2, 3, 3, 5, 6, 7, 10, 25, 30, 15 }, ids.Select(x => p.Spell("spell." + x).BaseMp));
            p.Spell("spell.heal").BaseMp = 4;
            Assert.AreEqual(4, new GrowthRules(p).SpellCost(P1Progress.New(p), "spell.heal"), "remake profile edit only");
        }

        [Test]
        public void T92_T103_PerEffectCapsOnlyAllCappedExcluded()
        {
            var p = P1Defaults.Create();
            p.StoryRarity[0].Legendary = 10;
            var r = new GrowthRules(p);
            var g = P1Progress.New(p);
            g.Learned.Add("spell.light_orb");
            g.Axes["spell.light_orb.dmg"] = 10;
            StringAssert.StartsWith("NoEffect", r.Reject(g, p.Card("card.spell.light_orb.N"), 10), "damage capped");
            Assert.IsNull(r.Reject(g, p.Card("card.spell.light_orb.R"), 10), "speed still grows");
            g.Axes["spell.light_orb.extraShots"] = 2;
            StringAssert.StartsWith("NoEffect", r.Reject(g, p.Card("card.spell.light_orb.L"), 10), "MP-only card (effect capped) excluded");
            p.Card("card.spell.light_orb.N").Axes[0].Limit = 12;
            Assert.IsNull(r.Reject(g, p.Card("card.spell.light_orb.N"), 10), "cap editable");
        }

        [Test]
        public void T105_ShieldRarityFunctions()
        {
            var m = Rig(p => p.Spell("spell.shield").BaseMp = 1).Learn("spell.shield");
            m.G.Axes["spell.shield.absorb"] = 20;
            m.G.Axes["spell.shield.duration"] = 12;
            m.G.Axes["spell.shield.statusBlock"] = 1;
            m.Cast(LogicalAction.Jump, Dir8.Right, Dir8.Down, Dir8.DownRight).Settle(30);
            Assert.AreEqual(30, m.Rt.Fx.MagicShield.Remaining, "normal + legendary share the cap 30");
            Assert.Greater(m.Rt.Fx.MagicShield.Time, 20f);
            Assert.IsFalse(m.Rt.Fx.TryApplyStatus("Poison"), "epic: new status blocked");
            Assert.AreEqual(0, m.H.Count("p1.magic.explode"), "no break shockwave");
            var r = new GrowthRules(m.P);
            m.P.StoryRarity[0].Legendary = 10;
            StringAssert.StartsWith("NoEffect", r.Reject(m.G, m.P.Card("card.spell.shield.L"), 30));
        }

        [Test]
        public void T116_ArchangelCureAndShield()
        {
            var m = Rig(p => p.Spell("spell.archangel").BaseMp = 1).Learn("spell.archangel");
            m.G.Axes["spell.archangel.cure"] = 1;
            m.G.Axes["spell.archangel.shield"] = 4;
            m.Rt.Fx.Statuses.Add("Confusion");
            m.H.C.SetPlayerHp(3, "test");
            m.Cast(LogicalAction.Attack, Dir8.Left, Dir8.DownLeft, Dir8.Down, Dir8.DownRight, Dir8.Right).Settle(40);
            Assert.AreEqual(1, m.H.Count("p1.summon.heal"));
            Assert.IsFalse(m.Rt.Fx.Statuses.Contains("Confusion"));
            Assert.AreEqual(4, m.Rt.Fx.ArchangelShield.Remaining);
        }

        [Test]
        public void T117_DemonAreaAndFlameDamageOnly()
        {
            var m = Rig(p => p.Spell("spell.demon").BaseMp = 1, ("a", Bag("a", 1000), 13f), ("b", Bag("b", 1000), 13.6f)).Learn("spell.demon");
            m.G.Axes["spell.demon.aoe"] = 2;
            m.G.Axes["spell.demon.flame"] = 3;
            m.Cast(LogicalAction.Attack, Dir8.Right, Dir8.DownRight, Dir8.Down, Dir8.DownLeft, Dir8.Left).Settle(200);
            Assert.Less(m.E("b").Hp, 1000f, "area hit on the neighbour");
            var flame = m.H.Sink.Events.Where(e => e.Type == "hit" && (string)e.TargetId == "a" && ((string)e.Reason).StartsWith("DamageOnly")).Count();
            Assert.GreaterOrEqual(flame, 2, "black flame ticks are damage only");
        }
    }
}
