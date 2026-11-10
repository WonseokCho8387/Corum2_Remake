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
                    vfxTime = v.SimulatedTime;
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
            // play up to the judged end, then turn emission off and let the tail run on game time
            var em = v.Ps.emission;
            em.rateOverTimeMultiplier = 0f;
            Advance(v, p.Elapsed - v.LastElapsed);
            v.LastElapsed = p.Elapsed;
            v.LastTick = p.EndTick;
            v.TailLeft = p.Vfx.lifetime;
            _tails.Add(v);
            sim.Log.Emit("presentation.vfxTail", p.Enemy.Id, "", "", "", "EmissionStoppedAtJudgedEnd(EXP-PRES-01)", ("transitionSerial", p.Serial),
                ("vfxTimeAtEnd", v.SimulatedTime), ("elapsedGame", p.Elapsed), ("tailSeconds", v.TailLeft));
        }

        void StepTails(Simulation sim, float fixedDt)
        {
            for (var i = _tails.Count - 1; i >= 0; i--)
            {
                var v = _tails[i];
                var dt = PresentationMath.TailStep(sim.Tick - v.LastTick, fixedDt, v.Enemy.HitstopPausedLastStep);
                v.LastTick = sim.Tick;
                if (dt > 0f) Advance(v, Mathf.Min(dt, v.TailLeft));
                v.TailLeft -= dt;
                if (v.TailLeft <= 0f)
                {
                    Kill(v.Ps.gameObject);
                    _tails.RemoveAt(i);
                }
            }
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
            main.duration = Mathf.Max(0.1f, p.Duration);
            main.loop = true;
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
