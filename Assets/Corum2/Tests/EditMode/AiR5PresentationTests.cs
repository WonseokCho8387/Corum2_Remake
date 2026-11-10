using System.Linq;
using Corum2.Core;
using Corum2.Unity;
using NUnit.Framework;
using UnityEngine;
using static Corum2.Tests.CombatFixtures;

namespace Corum2.Tests
{
    /// <summary>WP-04-R5 regression for WP04-F05-R1: a Burst VFX emits nothing after the judged end (rate and bursts off,
    /// non-looping), only the particles alive at the end fade.</summary>
    public class AiR5PresentationTests
    {
        static PresentationLibrary Lib()
        {
            var lib = ScriptableObject.CreateInstance<PresentationLibrary>();
            lib.vfx.Add(new VfxCue { id = "vfx.burst", kind = VfxCueKind.Burst, burstCount = 30, rate = 40f, lifetime = 0.8f, speed = 1f });
            return lib;
        }

        static EnemyDefinition Boss(float transition)
        {
            var p1 = AiFixtures.Phase("p1", AiFixtures.Pattern("a", 6f, 9f));
            p1.PreferredMinDistance = 0f;
            p1.PreferredMaxDistance = 4f;
            p1.Transitions.Add(AiFixtures.Ev("go", "p2"));
            var p2 = AiFixtures.Phase("p2", AiFixtures.Pattern("b", 6f, 9f));
            p2.PreferredMinDistance = 0f;
            p2.PreferredMaxDistance = 4f;
            p2.TransitionTime = transition;
            p2.TransitionVfx = "vfx.burst";
            return AiFixtures.Boss("P", "p1", p1, p2);
        }

        static int BornWithin(ParticleSystem ps, float seconds)
        {
            var buf = new ParticleSystem.Particle[1000];
            var n = ps.GetParticles(buf);
            return Enumerable.Range(0, n).Count(i => buf[i].startLifetime - buf[i].remainingLifetime < seconds - 1e-4f);
        }

        [TestCase(0.5f)]
        [TestCase(1.0f)]
        public void F05R1_BurstTail_NoEmissionAfterJudgedEnd(float transition)
        {
            var root = new GameObject("PresentationTestRoot");
            try
            {
                var h = AiFixtures.H("TC-08-11", World(("b", "P", 11f)), Boss(transition));
                var b = h.C.Find("b");
                var world = new WorldView(root.transform);
                var combat = new CombatView(root.transform, world, new EnemyDefinitionAsset[0]);
                var view = new BossPresentationView(root.transform, combat, Lib());
                void Tick()
                {
                    h.Step();
                    combat.Sync(h.Sim.Combat, 0f);
                    view.Sync(h.Sim, 0f, SimHarness.Dt);
                }
                h.C.PlayerHp = 100000f;
                for (var i = 0; i < 300 && !(b.State == EnemyState.Idle && b.Hitstop <= 0f); i++) Tick();
                Tick();
                h.C.RaiseBossEvent("b", "go");
                for (var i = 0; i < 4; i++) Tick();
                var ps = root.GetComponentInChildren<ParticleSystem>();
                Assert.IsNotNull(ps);
                Assert.IsFalse(ps.main.loop, "one cycle = the transition; the start burst cannot repeat");
                Assert.GreaterOrEqual(ps.particleCount, 30, "start burst emitted once at the transition start");
                for (var i = 0; i < 200 && h.Count("presentation.vfxTail") == 0; i++) Tick();
                var tail = h.Events("presentation.vfxTail").Single();
                Assert.AreEqual(false, tail.Get("emissionEnabled"));
                Assert.AreEqual(false, tail.Get("loop"));
                Assert.AreEqual(transition, System.Convert.ToSingle(tail.Get("vfxTimeAtEnd")), 1e-3);
                var atEnd = System.Convert.ToInt32(tail.Get("particlesAtEnd"));
                Assert.Greater(atEnd, 0, "particles alive at the end (lifetime 0.8 s)");
                var counts = new System.Collections.Generic.List<int>();
                var tailTime = 0f;
                for (var i = 0; i < 60 && root.GetComponentInChildren<ParticleSystem>() != null; i++)
                {
                    Tick();
                    tailTime += SimHarness.Dt;
                    var p = root.GetComponentInChildren<ParticleSystem>();
                    if (p == null) break;
                    counts.Add(p.particleCount);
                    Assert.AreEqual(0, BornWithin(p, tailTime), "no particle younger than the tail time");
                }
                for (var i = 1; i < counts.Count; i++) Assert.LessOrEqual(counts[i], counts[i - 1], "count only falls in the tail");
                var done = h.Events("presentation.vfxTailEnd").Single();
                Assert.AreEqual(0, System.Convert.ToInt32(done.Get("bornAfterEnd")));
                Assert.AreEqual(atEnd, System.Convert.ToInt32(done.Get("particlesAtEnd")));
                Assert.IsNull(root.GetComponentInChildren<ParticleSystem>(), "removed after the lifetime");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void F05R1_BirthDetector_Control_SeesNewParticlesWhenEmissionIsOn()
        {
            // Control for the age-based detector used in the tail check: a looping burst system does create young particles.
            var go = new GameObject("Control");
            try
            {
                var ps = go.AddComponent<ParticleSystem>();
                var main = ps.main;
                main.playOnAwake = false;
                main.loop = true;
                main.duration = 0.5f;
                main.startLifetime = 0.8f;
                var em = ps.emission;
                em.rateOverTime = 0f;
                em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)30) });
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                ps.Simulate(0.5f, true, true, false); // end of the first cycle
                ps.Simulate(0.2f, true, false, false); // the loop restarts and bursts again
                Assert.Greater(BornWithin(ps, 0.25f), 0, "looping burst re-emits: what R4 did not rule out");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
