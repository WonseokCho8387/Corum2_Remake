using System.Collections.Generic;
using System.Linq;
using Corum2.Core;
using Corum2.Unity;
using NUnit.Framework;
using UnityEngine;
using static Corum2.Tests.CombatFixtures;

namespace Corum2.Tests
{
    /// <summary>WP-04-R3 (WP04-M01 remainder): boss transition presentation follows the judged transition (EXP-PRES-01).</summary>
    public class AiR3PresentationTests
    {
        static PresentationLibrary Lib()
        {
            var lib = ScriptableObject.CreateInstance<PresentationLibrary>();
            lib.animations.Add(new AnimationCue { id = "anim.roar", kind = AnimationCueKind.Pulse, amplitude = 0.2f, frequency = 3f });
            lib.vfx.Add(new VfxCue { id = "vfx.aura", kind = VfxCueKind.Aura });
            lib.cameras.Add(new CameraCue { id = "cam.shake", kind = CameraCueKind.Shake });
            lib.cameras.Add(new CameraCue { id = "cam.none", kind = CameraCueKind.None });
            return lib;
        }

        static EnemyDefinition Boss(float transition = 1f)
        {
            var p1 = AiFixtures.Phase("p1", AiFixtures.Pattern("a", 0f, 2f));
            p1.Transitions.Add(AiFixtures.Ev("go", "p2"));
            var p2 = AiFixtures.Phase("p2", AiFixtures.Pattern("b", 0f, 2f));
            p2.TransitionTime = transition;
            p2.TransitionAnimation = "anim.roar";
            p2.TransitionVfx = "vfx.aura";
            p2.TransitionCamera = "cam.shake";
            return AiFixtures.Boss("P", "p1", p1, p2);
        }

        /// <summary>Steps the simulation and updates the tracker once per tick, as one rendered frame per tick does in the
        /// evidence runs (captureFramerate = 50).</summary>
        static void Run(SimHarness h, TransitionPresentationTracker t, int ticks, List<float> progress = null)
        {
            for (var i = 0; i < ticks; i++)
            {
                h.Step();
                t.Update(h.Sim);
                if (progress != null && t.Active.Count > 0) progress.Add(t.Active[0].Progress);
            }
        }

        static void StartTransition(SimHarness h, TransitionPresentationTracker t, EnemyActor b)
        {
            h.C.PlayerHp = 100000f;
            for (var i = 0; i < 300 && !(b.State == EnemyState.Idle && b.Cooldown > 0f); i++) Run(h, t, 1);
            h.C.RaiseBossEvent("b", "go");
            for (var i = 0; i < 200 && b.State != EnemyState.PhaseTransition; i++) Run(h, t, 1);
        }

        [Test, Category("TC_08_11")]
        public void Presentation_StartsAndEndsOnTheJudgedTransitionTicks()
        {
            var h = AiFixtures.H("TC-08-11", World(("b", "P", 11f)), Boss());
            var t = new TransitionPresentationTracker(Lib());
            var b = h.C.Find("b");
            StartTransition(h, t, b);
            var prog = new List<float>();
            Run(h, t, 80, prog);
            var phase = h.Events("boss.phase").Single(e => e.StateAfter == "p2");
            var start = h.Events("presentation.start").Single();
            var end = h.Events("presentation.end").Single();
            var tEnd = h.Events("boss.transitionEnd").Single();
            Assert.AreEqual(phase.SimulationTick, start.SimulationTick, "starts on the transition start tick");
            Assert.AreEqual(tEnd.SimulationTick, end.SimulationTick, "ends on the boss.transitionEnd tick");
            Assert.AreEqual("found", start.Get("animationCue"));
            Assert.AreEqual("found", start.Get("vfxCue"));
            Assert.AreEqual("found", start.Get("cameraCue"));
            Assert.AreEqual("Ended", end.StateAfter);
            for (var i = 1; i < prog.Count; i++) Assert.GreaterOrEqual(prog[i], prog[i - 1], "progress never goes back");
            Assert.That(prog, Has.All.InRange(0f, 1f));
            Assert.AreEqual(1, t.History.Count, "one playback per transition start");
            Assert.IsEmpty(t.Active);
        }

        [Test, Category("TC_08_12")]
        public void Presentation_HitDuringTransition_Continues_PausesWithHitstop()
        {
            var h = AiFixtures.H("TC-08-12", World(("b", "P", 11f)), Boss());
            var t = new TransitionPresentationTracker(Lib());
            var b = h.C.Find("b");
            StartTransition(h, t, b);
            Run(h, t, 10);
            var p = t.Active.Single();
            h.C.TestApplyHit(AiFixtures.Damage(5f), "b"); // hitstop 0.04 s = 2 ticks
            var before = p.Progress;
            Run(h, t, 1);
            Assert.AreEqual(before, p.Progress, 1e-5, "boss in hitstop: the transition timer and the presentation both wait");
            Run(h, t, 3);
            Assert.Greater(p.Progress, before);
            Assert.IsFalse(p.Finished, "a hit does not stop the presentation (EXP-BOSS-03)");
            Run(h, t, 80);
            Assert.AreEqual(h.Events("boss.transitionEnd").Single().SimulationTick, p.EndTick);
            Assert.AreEqual("Ended", p.EndKind);
        }

        [Test, Category("TC_08_10")]
        public void Presentation_DeathDuringTransition_Cancels()
        {
            var h = AiFixtures.H("TC-08-10", World(("b", "P", 11f)), Boss());
            var t = new TransitionPresentationTracker(Lib());
            var b = h.C.Find("b");
            StartTransition(h, t, b);
            Run(h, t, 5);
            h.C.TestApplyHit(AiFixtures.Damage(5000f), "b");
            var deathTick = h.Sim.Tick;
            Run(h, t, 60);
            var c = h.Events("presentation.cancel").Single();
            Assert.AreEqual(h.Events("boss.transitionCancel").Single().SimulationTick, deathTick);
            Assert.AreEqual(deathTick + 1, c.SimulationTick, "stopped on the first frame after the death (the hit came between frames)");
            Assert.AreEqual(0, h.Count("presentation.end"));
        }

        [Test, Category("TC_08_10")]
        public void Presentation_SameTickChain_PreviousEndsAndNextStartsOnThatTick()
        {
            var x = AiFixtures.Phase("x", AiFixtures.Pattern("a", 0f, 2f));
            x.Transitions.Add(AiFixtures.Ev("go", "y"));
            var y = AiFixtures.Phase("y", AiFixtures.Pattern("b", 0f, 2f));
            y.TransitionTime = 0f; // no presentation ids
            y.Transitions.Add(AiFixtures.Ev("go2", "z"));
            var z = AiFixtures.Phase("z", AiFixtures.Pattern("c", 0f, 2f));
            z.TransitionTime = 0.5f;
            z.TransitionAnimation = "anim.roar";
            z.TransitionCamera = "cam.none";
            var h = AiFixtures.H("TC-08-10", World(("b", "C", 11f)), AiFixtures.Boss("C", "x", x, y, z));
            var t = new TransitionPresentationTracker(Lib());
            var b = h.C.Find("b");
            h.C.PlayerHp = 100000f;
            for (var i = 0; i < 300 && !(b.State == EnemyState.Idle && b.Cooldown > 0f); i++) Run(h, t, 1);
            h.C.RaiseBossEvent("b", "go");
            h.C.RaiseBossEvent("b", "go2");
            Run(h, t, 60);
            Assert.AreEqual(2, t.History.Count);
            var py = t.History[0];
            var pz = t.History[1];
            Assert.AreEqual("y", py.Phase);
            Assert.AreEqual("z", pz.Phase);
            Assert.AreEqual(pz.StartTick, py.EndTick, "y ends on the tick z starts");
            Assert.AreEqual("none", h.Events("presentation.start").First().Get("animationCue"), "phase without ids plays nothing");
            Assert.AreEqual("found", h.Events("presentation.start").Last().Get("cameraCue"), "explicit 'no camera change' cue");
            Assert.AreEqual(h.Events("boss.transitionEnd").Last().SimulationTick, pz.EndTick);
        }

        [Test, Category("TC_08_11")]
        public void PresentationMath_ZeroAtStartAndEnd()
        {
            var a = new AnimationCue { kind = AnimationCueKind.Shake, amplitude = 0.3f, frequency = 7f, tintAmount = 0.8f };
            var c = new CameraCue { kind = CameraCueKind.Zoom, fovDelta = -6f };
            foreach (var pr in new[] { 0f, 1f })
            {
                var (off, scale, tint) = PresentationMath.Body(a, 0.123f, pr);
                Assert.AreEqual(0f, off.magnitude, 1e-5);
                Assert.AreEqual(1f, scale, 1e-5);
                Assert.AreEqual(0f, tint, 1e-5);
                Assert.AreEqual(0f, PresentationMath.Camera(c, 0.123f, pr).fov, 1e-5);
            }
            Assert.AreEqual(-6f, PresentationMath.Camera(c, 0.4f, 0.5f).fov, 1e-4, "peak at mid transition");
            var none = new CameraCue { kind = CameraCueKind.None };
            Assert.AreEqual(Vector2.zero, PresentationMath.Camera(none, 0.2f, 0.5f).offset);
        }

        [Test, Category("DATA")]
        public void Validate_MissingIdsAndBadValuesRejected()
        {
            var good = Boss().Boss;
            Assert.IsEmpty(PresentationLibrary.Validate(Lib(), new[] { good }));
            Assert.IsTrue(PresentationLibrary.Validate(null, new[] { good }).Any(s => s.Contains("no PresentationLibrary")));
            var miss = Boss().Boss;
            miss.Phases[1].TransitionVfx = "vfx.nope";
            Assert.IsTrue(PresentationLibrary.Validate(Lib(), new[] { miss }).Any(s => s.Contains("TransitionVfx 'vfx.nope'")));
            var zero = Boss(0f).Boss;
            Assert.IsTrue(PresentationLibrary.Validate(Lib(), new[] { zero }).Any(s => s.Contains("TransitionTime > 0")));
            var dup = Lib();
            dup.cameras.Add(new CameraCue { id = "cam.shake" });
            Assert.IsTrue(PresentationLibrary.Validate(dup, new BossDefinition[0]).Any(s => s.Contains("duplicated")));
            var bad = Lib();
            bad.vfx[0].size = float.NaN;
            Assert.IsTrue(PresentationLibrary.Validate(bad, new BossDefinition[0]).Any(s => s.Contains("size")));
        }

        [Test, Category("DATA")]
        public void Wp04Content_EveryBossPresentationIdResolves()
        {
            var lib = UnityEditor.AssetDatabase.LoadAssetAtPath<PresentationLibrary>("Assets/Corum2/Data/WP04/PresentationLibrary.asset");
            Assert.IsNotNull(lib, "WP-04 presentation library asset");
            var bosses = UnityEditor.AssetDatabase.FindAssets("t:EnemyDefinitionAsset", new[] { "Assets/Corum2/Data/WP04" })
                .Select(g => UnityEditor.AssetDatabase.LoadAssetAtPath<EnemyDefinitionAsset>(UnityEditor.AssetDatabase.GUIDToAssetPath(g)))
                .Where(e => e.ai.type == AiType.Boss).Select(e => e.ToCore().Boss).ToList();
            Assert.AreEqual(2, bosses.Count, "warden and sentinel");
            CollectionAssert.IsEmpty(PresentationLibrary.Validate(lib, bosses));
        }
    }
}
