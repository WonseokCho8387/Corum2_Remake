using System;
using System.Linq;
using Corum2.Core;
using NUnit.Framework;
using static Corum2.Tests.CombatFixtures;

namespace Corum2.Tests
{
    /// <summary>R2 diagnostics regression (GPT WP-06~08 R1 review §3): enemy.knockback says whether one source made the whole
    /// interval (SingleSource) or a second hit / AI drive / wall also moved the enemy (Ambiguous).</summary>
    public class R2KnockbackTests
    {
        static AttackDefinition Kb(SimHarness h, float knockback)
        {
            var a = h.C.Profile.GroundNormals[0];
            a.Hitstop = 0f;
            a.Knockback = knockback;
            return a;
        }

        [Test]
        public void SingleHit_IsSingleSource_WithItsInstance()
        {
            var h = Harness("R2-kb", World(("bag", "bag", 11f)), Defs(Bag()));
            h.Step().Teleport(10);
            h.C.TestApplyHit(Kb(h, 5f), "bag");
            var hit = h.Events("hit").Last();
            h.Step(20);
            var k = h.Events("enemy.knockback").Single();
            Assert.AreEqual("SingleSource", k.Get("attribution"));
            Assert.AreEqual(1, Convert.ToInt32(k.Get("velocitySets")));
            Assert.AreEqual(Convert.ToInt64(hit.Get("attackInstanceId")), Convert.ToInt64(k.Get("firstAttackInstanceId")));
            Assert.AreEqual(false, k.Get("aiMoveDuring"));
            Assert.AreEqual(false, k.Get("blocked"));
            Assert.Greater(Convert.ToSingle(k.Get("displacement")), 0f);
        }

        [Test]
        public void SecondHitInsideTheInterval_IsAmbiguous()
        {
            // GPT probe: +5 m/s at tick 1, -5 m/s from the other side two ticks later -> one record covering both.
            var h = Harness("R2-kb", World(("bag", "bag", 11f)), Defs(Bag()));
            h.Step().Teleport(10);
            var a = Kb(h, 5f);
            h.C.TestApplyHit(a, "bag");
            h.Step(2).Teleport(12, -1);
            h.C.TestApplyHit(a, "bag");
            h.Step(20);
            var k = h.Events("enemy.knockback").Single();
            Assert.AreEqual("Ambiguous", k.Get("attribution"));
            Assert.AreEqual(2, Convert.ToInt32(k.Get("velocitySets")));
            Assert.AreNotEqual(Convert.ToInt64(k.Get("firstAttackInstanceId")), Convert.ToInt64(k.Get("lastAttackInstanceId")));
        }

        [Test]
        public void WeakPositiveKnockback_StillSingleSourceAndMoves()
        {
            var h = Harness("R2-kb", World(("bag", "bag", 11f)), Defs(Bag()));
            h.Step().Teleport(10);
            h.C.TestApplyHit(Kb(h, 0.3f), "bag");
            h.Step(5);
            var k = h.Events("enemy.knockback").Single();
            Assert.AreEqual("SingleSource", k.Get("attribution"));
            Assert.AreEqual(0.006f, Convert.ToSingle(k.Get("displacement")), 0.0005f);
        }
    }
}
