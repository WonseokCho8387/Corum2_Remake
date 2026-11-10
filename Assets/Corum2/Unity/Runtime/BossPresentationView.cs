using System.Collections.Generic;
using Corum2.Core;
using UnityEngine;

namespace Corum2.Unity
{
    /// <summary>
    /// Plays boss transition presentation (R-08-11): procedural body animation, particle VFX and a camera cue, timed by
    /// TransitionPresentationTracker from the judged transition (EXP-PRES-01). Temporary resources (TEST-PRES-01);
    /// nothing here feeds back into judgement.
    ///
    /// WP04-F05 VFX clock: particle systems never run on Unity's own clock. They are advanced with Simulate() by
    ///   - while the transition plays: the change in the playback's elapsed transition time (game time; it does not
    ///     move while the boss is in hitstop), and the emission rate is scaled by the same envelope as the other cues;
    ///   - after the judged end (tail): game time from simulation ticks, not advancing while the boss is in hitstop,
    ///     emission off, until the particle lifetime has passed. A cancel (death) clears the particles at once.
    /// </summary>
    public sealed class BossPresentationView
    {
        sealed class Vfx
        {
            public ParticleSystem Ps;
            public float SimulatedTime;
            public float LastElapsed;
            public bool Started;
            public long LastTick;
            public float TailLeft;
            public float TailElapsed;
            public int ParticlesAtEnd;
            public int MaxBornAfterEnd;
            public int Serial;
            public bool BeginLogged;
            public EnemyActor Enemy;
        }

        readonly Transform _parent;
        readonly CombatView _combat;
        readonly TransitionPresentationTracker _tracker;
        readonly Dictionary<TransitionPresentationTracker.Playback, Vfx> _vfx = new Dictionary<TransitionPresentationTracker.Playback, Vfx>();
        readonly List<Vfx> _tails = new List<Vfx>();
        Material _particleMat;

        public Vector2 CameraOffset { get; private set; }
        public float FovDelta { get; private set; }
        public TransitionPresentationTracker Tracker => _tracker;

        public BossPresentationView(Transform parent, CombatView combat, PresentationLibrary lib)
        {
            _parent = parent;
            _combat = combat;
            _tracker = new TransitionPresentationTracker(lib);
        }

        static void Advance(Vfx v, float dt)
        {
            if (dt < 0f) dt = 0f;
            // The first call must cover a positive interval: a restart with 0 s leaves the time-0 burst unfired and the
            // next call starts after it (found by the R5 Burst test).
            if (!v.Started && dt <= 0f) return;
            v.Ps.Simulate(dt, true, !v.Started, false);
            v.Started = true;
            v.SimulatedTime += dt;
        }

        public void Sync(Simulation sim, float z, float fixedDt)
        {
            foreach (var p in _tracker.Update(sim)) Finish(sim, p, fixedDt);
            StepTails(sim, fixedDt);
            CameraOffset = Vector2.zero;
            FovDelta = 0f;
            TransitionPresentationTracker.Playback camPick = null;
            foreach (var p in _tracker.Active)
            {
                var en = p.Enemy;
                var center = new Vector3(en.Position.X, en.Position.Y + en.Def.BodyHeight * 0.5f, z);
                if (p.Animation != null && _combat.TryGetVisual(en, out var root, out var mat, out var baseColor))
                {
                    var (off, scale, tint) = PresentationMath.Body(p.Animation, p.Elapsed, p.Progress);
                    root.position += new Vector3(off.x, off.y, 0f);
                    root.localScale = Vector3.Scale(root.localScale, new Vector3(scale, scale, scale));
                    if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.Lerp(baseColor, p.Animation.tint, tint));
                }
                var vfxTime = -1f;
                if (p.Vfx != null)
                {
                    if (!_vfx.TryGetValue(p, out var v)) _vfx[p] = v = new Vfx { Ps = MakeParticles(p), Enemy = en, LastTick = sim.Tick };
                    v.Ps.transform.position = center;
                    var em = v.Ps.emission;
                    em.rateOverTimeMultiplier = p.Vfx.rate * PresentationMath.Envelope(p.Progress);
                    Advance(v, p.Elapsed - v.LastElapsed);
                    v.LastElapsed = p.Elapsed;
                    v.LastTick = sim.Tick;
                    vfxTime = v.Started ? v.SimulatedTime : 0f;
                    if (v.Started && !v.BeginLogged)
                    {
                        v.BeginLogged = true;
                        sim.Log.Emit("presentation.vfxBegin", en.Id, "", "", p.VfxId, "FirstVfxStep(EXP-PRES-01)", ("transitionSerial", p.Serial),
                            ("vfxKind", p.Vfx.kind.ToString()), ("burstCount", p.Vfx.kind == VfxCueKind.Burst ? p.Vfx.burstCount : 0),
                            ("particles", v.Ps.particleCount), ("vfxTime", v.SimulatedTime), ("lifetime", p.Vfx.lifetime));
                    }
                }
                if (p.Camera != null && (camPick == null || p.StartTick >= camPick.StartTick)) camPick = p; // EXP-PRES-02
                sim.Log.Emit("presentation.frame", en.Id, "", "", p.Phase, "Render(EXP-PRES-01)", ("transitionSerial", p.Serial),
                    ("elapsedGame", p.Elapsed), ("progress", p.Progress), ("vfxTime", vfxTime), ("bossHitstop", en.Hitstop > 0f),
                    ("bossPausedThisTick", en.HitstopPausedLastStep),
                    ("bossState", en.State.ToString()));
            }
            if (camPick != null)
            {
                var (o, f) = PresentationMath.Camera(camPick.Camera, camPick.Elapsed, camPick.Progress);
                CameraOffset = o;
                FovDelta = f;
            }
        }

        void Finish(Simulation sim, TransitionPresentationTracker.Playback p, float fixedDt)
        {
            if (!_vfx.TryGetValue(p, out var v)) return;
            _vfx.Remove(p);
            if (p.EndKind == "Cancelled")
            {
                v.Ps.Clear(true);
                Kill(v.Ps.gameObject);
                return;
            }
            // WP04-F05-R1: play up to the judged end (the system is non-looping with duration = transition length, so the
            // start burst never repeats), then switch the whole emission module off - continuous rate and bursts - and let
            // only the particles alive at the end fade on game time.
            Advance(v, p.Elapsed - v.LastElapsed);
            v.LastElapsed = p.Elapsed;
            var em = v.Ps.emission;
            em.rateOverTimeMultiplier = 0f;
            em.SetBursts(new ParticleSystem.Burst[0]);
            em.enabled = false;
            v.LastTick = p.EndTick;
            v.TailLeft = p.Vfx.lifetime;
            v.ParticlesAtEnd = v.Ps.particleCount;
            v.Serial = p.Serial;
            _tails.Add(v);
            sim.Log.Emit("presentation.vfxTail", p.Enemy.Id, "", "", "", "EmissionOffAtJudgedEnd:RateAndBursts(EXP-PRES-01)", ("transitionSerial", p.Serial),
                ("vfxTimeAtEnd", v.SimulatedTime), ("elapsedGame", p.Elapsed), ("duration", p.Duration), ("tailSeconds", v.TailLeft),
                ("particlesAtEnd", v.ParticlesAtEnd), ("emissionEnabled", v.Ps.emission.enabled), ("loop", v.Ps.main.loop));
        }

        void StepTails(Simulation sim, float fixedDt)
        {
            for (var i = _tails.Count - 1; i >= 0; i--)
            {
                var v = _tails[i];
                var dt = PresentationMath.TailStep(sim.Tick - v.LastTick, fixedDt, v.Enemy.HitstopPausedLastStep);
                v.LastTick = sim.Tick;
                if (dt > 0f)
                {
                    var step = Mathf.Min(dt, v.TailLeft);
                    Advance(v, step);
                    v.TailElapsed += step;
                    v.MaxBornAfterEnd = Mathf.Max(v.MaxBornAfterEnd, BornAfter(v.Ps, v.TailElapsed));
                }
                v.TailLeft -= dt;
                if (v.TailLeft <= 0f)
                {
                    sim.Log.Emit("presentation.vfxTailEnd", v.Enemy.Id, "", "", "", "TailDone(EXP-PRES-01)", ("transitionSerial", v.Serial),
                        ("particlesAtEnd", v.ParticlesAtEnd), ("bornAfterEnd", v.MaxBornAfterEnd), ("tailSimulated", v.TailElapsed),
                        ("particlesLeft", v.Ps.particleCount));
                    Kill(v.Ps.gameObject);
                    _tails.RemoveAt(i);
                }
            }
        }

        readonly ParticleSystem.Particle[] _buf = new ParticleSystem.Particle[600];

        /// <summary>Particles whose age is below the tail time, i.e. emitted after the judged end (expected 0).</summary>
        int BornAfter(ParticleSystem ps, float tailElapsed)
        {
            var n = ps.GetParticles(_buf);
            var born = 0;
            for (var i = 0; i < n; i++)
                if (_buf[i].startLifetime - _buf[i].remainingLifetime < tailElapsed - 1e-4f) born++;
            return born;
        }

        static void Kill(GameObject go)
        {
            if (Application.isPlaying) Object.Destroy(go);
            else Object.DestroyImmediate(go); // EditMode tests
        }

        Material ParticleMaterial()
        {
            if (_particleMat != null) return _particleMat;
            var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            _particleMat = new Material(sh);
            return _particleMat;
        }

        ParticleSystem MakeParticles(TransitionPresentationTracker.Playback p)
        {
            var c = p.Vfx;
            var go = new GameObject("Vfx_" + p.VfxId + "_" + p.Enemy.Id);
            go.SetActive(false);
            go.transform.SetParent(_parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.playOnAwake = false;
            ps.useAutoRandomSeed = false;
            ps.randomSeed = (uint)(p.Serial * 7919 + p.Enemy.Id.GetHashCode());
            main.duration = Mathf.Max(0.05f, p.Duration); // one cycle = the transition; the start burst fires once (F05-R1)
            main.loop = false;
            main.startLifetime = c.lifetime;
            main.startSpeed = c.speed;
            main.startSize = c.size;
            main.startColor = c.color;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 600;
            var em = ps.emission;
            em.rateOverTime = c.rate;
            if (c.kind == VfxCueKind.Burst && c.burstCount > 0) em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)c.burstCount) });
            var shape = ps.shape;
            shape.shapeType = c.kind == VfxCueKind.Aura ? ParticleSystemShapeType.Circle : ParticleSystemShapeType.Sphere;
            shape.radius = Mathf.Max(0.01f, c.radius);
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(c.color, 0f), new GradientColorKey(c.color, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            var m = new Material(ParticleMaterial());
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
            r.sharedMaterial = m;
            go.SetActive(true);
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); // driven only by Simulate()
            return ps;
        }
    }
}
