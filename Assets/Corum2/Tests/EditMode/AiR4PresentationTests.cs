using System.Collections.Generic;
using System.Linq;
using Corum2.Core;
using Corum2.Unity;
using NUnit.Framework;
using UnityEngine;
using static Corum2.Tests.CombatFixtures;

namespace Corum2.Tests
{
    /// <summary>WP-04-R4 regressions: WP04-F04 (judged ticks kept under any render cadence, skipped transitions),
    /// WP04-F05 (VFX clock follows the transition / boss hitstop), WP04-M04 (colour and tint validation).</summary>
    public class AiR4PresentationTests
    {
        static PresentationLibrary Lib()
        {
            var lib = ScriptableObject.CreateInstance<PresentationLibrary>();
            lib.animations.Add(new AnimationCue { id = "anim.roar", kind = AnimationCueKind.Pulse, amplitude = 0.2f, frequency = 3f });
            lib.vfx.Add(new VfxCue { id = "vfx.aura", kind = VfxCueKind.Aura, rate = 80f, lifetime = 0.5f });
            lib.cameras.Add(new CameraCue { id = "cam.shake", kind = CameraCueKind.Shake });
            return lib;
        }

        static EnemyDefinition Boss(float transition = 1f, float staggerResistance = 0f)
        {
            var p1 = AiFixtures.Phase("p1", AiFixtures.Pattern("a", 6f, 9f));
            p1.PreferredMinDistance = 0f;
            p1.PreferredMaxDistance = 4f;
            p1.Transitions.Add(AiFixtures.Ev("go", "p2"));
            var p2 = AiFixtures.Phase("p2", AiFixtures.Pattern("b", 6f, 9f));
            p2.PreferredMinDistance = 0f;
            p2.PreferredMaxDistance = 4f;
            p2.TransitionTime = transition;
            p2.StaggerResistance = staggerResistance;
            p2.TransitionAnimation = "anim.roar";
            p2.TransitionVfx = "vfx.aura";
            p2.TransitionCamera = "cam.shake";
            return AiFixtures.Boss("P", "p1", p1, p2);
        }

        static void Settle(SimHarness h, EnemyActor b)
        {
            h.C.PlayerHp = 100000f;
            h.StepUntil(() => b.State == EnemyState.Idle && b.Hitstop <= 0f, 300).Step(2);
        }

        /// <summary>Simulation ticks with a tracker update only every <paramref name="every"/> ticks (render cadence).</summary>
        static void Run(SimHarness h, TransitionPresentationTracker t, int ticks, int every)
        {
            for (var i = 1; i <= ticks; i++)
            {
                h.Step();
                if (i % every == 0) t.Update(h.Sim);
            }
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(5)]
        public void F04_RenderCadence_KeepsJudgedTicks_RecordsShownSeparately(int every)
        {
            var h = AiFixtures.H("TC-08-11", World(("b", "P", 11f)), Boss());
            var t = new TransitionPresentationTracker(Lib());
            var b = h.C.Find("b");
            Settle(h, b);
            h.C.RaiseBossEvent("b", "go");
            Run(h, t, 90, every);
            var judgedStart = h.Events("boss.phase").Single(e => e.StateAfter == "p2").SimulationTick;
            var judgedEnd = h.Events("boss.transitionEnd").Single().SimulationTick;
            var p = t.History.Single();
            Assert.AreEqual(judgedStart, p.StartTick, "judged start kept");
            Assert.AreEqual(judgedEnd, p.EndTick, "judged end kept");
            Assert.That(p.ShownTick - p.StartTick, Is.InRange(0, every - 1), "shown on the first rendered update after the start");
            Assert.That(p.ObservedEndTick - p.EndTick, Is.InRange(0, every - 1));
            var s = h.Events("presentation.start").Single();
            Assert.AreEqual(judgedStart, System.Convert.ToInt64(s.Get("judgedStartTick")));
            Assert.AreEqual(p.ShownTick, s.SimulationTick);
            Assert.AreEqual(p.ShownTick - p.StartTick, System.Convert.ToInt64(s.Get("displayDelayTicks")));
            var e2 = h.Events("presentation.end").Single();
            Assert.AreEqual(judgedEnd, System.Convert.ToInt64(e2.Get("judgedEndTick")));
            Assert.AreEqual(0, h.Count("presentation.skipped"));
        }

        [Test]
        public void F04_TransitionEndingBetweenRenders_IsSkippedWithJudgedTicks()
        {
            var h = AiFixtures.H("TC-08-11", World(("b", "P", 11f)), Boss(0.02f));
            var t = new TransitionPresentationTracker(Lib());
            var b = h.C.Find("b");
            Settle(h, b);
            t.Update(h.Sim);
            h.C.RaiseBossEvent("b", "go");
            Run(h, t, 4, 4); // one transition of 1 tick, render only after 4 ticks
            var p = t.History.Single();
            Assert.AreEqual("Skipped", p.EndKind);
            Assert.AreEqual(-1, p.ShownTick);
            var sk = h.Events("presentation.skipped").Single();
            Assert.AreEqual(h.Events("boss.phase").Single(e => e.StateAfter == "p2").SimulationTick, System.Convert.ToInt64(sk.Get("judgedStartTick")));
            Assert.AreEqual(h.Events("boss.transitionEnd").Single().SimulationTick, System.Convert.ToInt64(sk.Get("judgedEndTick")));
            Assert.AreEqual(0, h.Count("presentation.start"), "never shown");
            Assert.AreEqual(0, h.Count("presentation.end"));
        }

        [Test]
        public void F04_DeathBetweenRenders_CancelKeepsJudgedTick()
        {
            var h = AiFixtures.H("TC-08-10", World(("b", "P", 11f)), Boss());
            var t = new TransitionPresentationTracker(Lib());
            var b = h.C.Find("b");
            Settle(h, b);
            h.C.RaiseBossEvent("b", "go");
            Run(h, t, 6, 3);
            h.Step();
            h.C.TestApplyHit(AiFixtures.Damage(5000f), "b");
            var deathTick = h.Sim.Tick;
            Run(h, t, 6, 3);
            var c = h.Events("presentation.cancel").Single();
            Assert.AreEqual(deathTick, System.Convert.ToInt64(c.Get("judgedEndTick")), "judged cancel tick = death tick");
            Assert.Greater(c.SimulationTick, deathTick, "observed at the next rendered update");
            Assert.AreEqual(0, h.Count("presentation.end"));
        }

        [Test]
        public void F04_SameTickChain_WithSlowRender()
        {
            var x = AiFixtures.Phase("x", AiFixtures.Pattern("a", 6f, 9f));
            x.Transitions.Add(AiFixtures.Ev("go", "y"));
            var y = AiFixtures.Phase("y", AiFixtures.Pattern("b", 6f, 9f));
            y.TransitionTime = 0f;
            y.Transitions.Add(AiFixtures.Ev("go2", "z"));
            var z = AiFixtures.Phase("z", AiFixtures.Pattern("c", 6f, 9f));
            z.TransitionTime = 0.5f;
            z.TransitionAnimation = "anim.roar";
            foreach (var ph in new[] { x, y, z })
            {
                ph.PreferredMinDistance = 0f;
                ph.PreferredMaxDistance = 4f;
            }
            var h = AiFixtures.H("TC-08-10", World(("b", "C", 11f)), AiFixtures.Boss("C", "x", x, y, z));
            var t = new TransitionPresentationTracker(Lib());
            var b = h.C.Find("b");
            Settle(h, b);
            t.Update(h.Sim);
            h.C.RaiseBossEvent("b", "go");
            h.C.RaiseBossEvent("b", "go2");
            Run(h, t, 60, 3);
            Assert.AreEqual(2, t.History.Count);
            var py = t.History[0];
            var pz = t.History[1];
            Assert.AreEqual("Skipped", py.EndKind, "the 0 s transition was never rendered");
            Assert.AreEqual(pz.StartTick, py.EndTick, "y judged end = z judged start");
            Assert.AreEqual("Ended", pz.EndKind);
            Assert.AreEqual(h.Events("boss.transitionEnd").Last().SimulationTick, pz.EndTick);
        }

        [Test]
        public void F05_VfxClock_FollowsTransitionTime_StopsInBossHitstop()
        {
            var root = new GameObject("PresentationTestRoot");
            try
            {
                var h = AiFixtures.H("TC-08-12", World(("b", "P", 11f)), Boss(1f, 1f)); // p2 resists stagger: hits leave it in the transition
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
                for (var i = 0; i < 15; i++) Tick();
                var ps = root.GetComponentInChildren<ParticleSystem>();
                Assert.IsNotNull(ps, "VFX playing");
                Assert.Greater(ps.particleCount, 0);
                h.C.TestApplyHit(AiFixtures.Damage(5f), "b"); // StaggerResisted: hitstop 0.04 s, transition continues
                for (var i = 0; i < 40; i++) Tick();
                var frames = h.Events("presentation.frame").Where(e => e.ActorId == "b").ToList();
                var hs = frames.Where(e => (bool)e.Get("bossPausedThisTick")).ToList();
                Assert.Greater(hs.Count, 0, "frames during the boss hitstop");
                for (var i = 1; i < frames.Count; i++)
                {
                    var dv = System.Convert.ToSingle(frames[i].Get("vfxTime")) - System.Convert.ToSingle(frames[i - 1].Get("vfxTime"));
                    var de = System.Convert.ToSingle(frames[i].Get("elapsedGame")) - System.Convert.ToSingle(frames[i - 1].Get("elapsedGame"));
                    Assert.AreEqual(de, dv, 1e-4, "VFX advances exactly by the transition's game time");
                    if ((bool)frames[i].Get("bossPausedThisTick")) Assert.AreEqual(0f, dv, 1e-6, "VFX frozen on ticks the boss was paused by hitstop");
                }
                for (var i = 0; i < 60; i++) Tick();
                var tail = h.Events("presentation.vfxTail").Single();
                Assert.AreEqual(1f, System.Convert.ToSingle(tail.Get("vfxTimeAtEnd")), 1e-3, "VFX time at the judged end = transition time");
                Assert.IsNull(root.GetComponentInChildren<ParticleSystem>(), "tail finished and removed");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void F05_TailStep()
        {
            Assert.AreEqual(0.06f, PresentationMath.TailStep(3, 0.02f, false), 1e-6);
            Assert.AreEqual(0f, PresentationMath.TailStep(3, 0.02f, true), "boss in hitstop: tail waits");
            Assert.AreEqual(0f, PresentationMath.TailStep(0, 0.02f, false));
        }

        [Test, Category("DATA")]
        public void M04_ColourAndTintValidation()
        {
            var cases = new (string expect, System.Action<PresentationLibrary> f)[]
            {
                ("tintAmount", l => l.animations[0].tintAmount = float.NaN),
                ("tintAmount", l => l.animations[0].tintAmount = 2f),
                ("tintAmount", l => l.animations[0].tintAmount = -0.1f),
                ("tint channels", l => l.animations[0].tint = new Color(float.NaN, 0f, 0f)),
                ("tint channels", l => l.animations[0].tint = new Color(float.PositiveInfinity, 0f, 0f)),
                ("color channels", l => l.vfx[0].color = new Color(0f, float.NaN, 0f)),
                ("color channels", l => l.vfx[0].color = new Color(-1f, 0f, 0f)),
                ("color alpha", l => l.vfx[0].color = new Color(1f, 1f, 1f, 2f)),
                ("burstCount", l => l.vfx[0].burstCount = 40000),
            };
            Assert.IsEmpty(PresentationLibrary.Validate(Lib(), new BossDefinition[0]), "valid control");
            foreach (var (expect, f) in cases)
            {
                var lib = Lib();
                f(lib);
                var issues = PresentationLibrary.Validate(lib, new BossDefinition[0]);
                Assert.IsTrue(issues.Any(s => s.Contains(expect)), expect + ": " + string.Join(" | ", issues));
            }
            var edge = Lib();
            edge.animations[0].tintAmount = 1f;
            edge.vfx[0].color = new Color(4f, 0f, 0f, 1f); // HDR value: no upper bound decided (U-11)
            Assert.IsEmpty(PresentationLibrary.Validate(edge, new BossDefinition[0]), "boundaries accepted");
        }
    }
}
