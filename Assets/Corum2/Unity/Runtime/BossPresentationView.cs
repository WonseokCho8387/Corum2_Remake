using System.Collections.Generic;
using Corum2.Core;
using UnityEngine;

namespace Corum2.Unity
{
    /// <summary>
    /// Plays boss transition presentation (R-08-11): procedural body animation, particle VFX and a camera cue, timed by
    /// TransitionPresentationTracker from the judged transition (EXP-PRES-01). Temporary resources (TEST-PRES-01);
    /// nothing here feeds back into judgement.
    /// </summary>
    public sealed class BossPresentationView
    {
        readonly Transform _parent;
        readonly CombatView _combat;
        readonly TransitionPresentationTracker _tracker;
        readonly Dictionary<TransitionPresentationTracker.Playback, ParticleSystem> _particles =
            new Dictionary<TransitionPresentationTracker.Playback, ParticleSystem>();
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

        public void Sync(Simulation sim, float z)
        {
            foreach (var p in _tracker.Update(sim)) StopVfx(p);
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
                if (p.Vfx != null)
                {
                    if (!_particles.TryGetValue(p, out var ps)) _particles[p] = ps = MakeParticles(p);
                    ps.transform.position = center;
                }
                if (p.Camera != null && (camPick == null || p.StartTick >= camPick.StartTick)) camPick = p; // EXP-PRES-02
            }
            if (camPick != null)
            {
                var (o, f) = PresentationMath.Camera(camPick.Camera, camPick.Elapsed, camPick.Progress);
                CameraOffset = o;
                FovDelta = f;
            }
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
            go.transform.SetParent(_parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.useAutoRandomSeed = false;
            ps.randomSeed = (uint)(p.Serial * 7919 + p.Enemy.Id.GetHashCode());
            var main = ps.main;
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
            if (c.kind == VfxCueKind.Aura) shape.rotation = new Vector3(0f, 0f, 0f); // circle faces the camera (XY plane)
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
            ps.Play();
            return ps;
        }

        void StopVfx(TransitionPresentationTracker.Playback p)
        {
            if (!_particles.TryGetValue(p, out var ps)) return;
            _particles.Remove(p);
            if (ps == null) return;
            if (p.EndKind == "Cancelled") ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            else ps.Stop(true, ParticleSystemStopBehavior.StopEmitting); // already emitted particles fade out
            Object.Destroy(ps.gameObject, p.Vfx.lifetime + 0.2f);
        }
    }
}
