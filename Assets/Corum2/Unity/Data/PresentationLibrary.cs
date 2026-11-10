using System;
using System.Collections.Generic;
using Corum2.Core;
using UnityEngine;

namespace Corum2.Unity
{
    public enum AnimationCueKind
    {
        /// <summary>Body scale pulses (roar).</summary>
        Pulse,
        /// <summary>Body shakes sideways (rage).</summary>
        Shake,
        /// <summary>Body tint blinks on and off (shift).</summary>
        Flicker,
        /// <summary>Body rises and settles.</summary>
        Rise,
    }

    public enum VfxCueKind
    {
        /// <summary>Continuous particles around the body for the whole transition.</summary>
        Aura,
        /// <summary>One burst at the start, then a light trickle.</summary>
        Burst,
    }

    public enum CameraCueKind
    {
        /// <summary>Explicit "no camera change" cue.</summary>
        None,
        Shake,
        Zoom,
    }

    /// <summary>Temporary procedural body animation (EXP-PRES-01). Final rigged animation is U-11.</summary>
    [Serializable]
    public sealed class AnimationCue
    {
        public string id = "";
        public AnimationCueKind kind;
        public float amplitude = 0.15f;
        public float frequency = 6f;
        public Color tint = Color.white;
        [Range(0f, 1f)] public float tintAmount = 0.5f;
    }

    [Serializable]
    public sealed class VfxCue
    {
        public string id = "";
        public VfxCueKind kind;
        public Color color = Color.white;
        [Tooltip("Particles per second (Aura) or after the burst (Burst).")] public float rate = 40f;
        public int burstCount = 30;
        public float size = 0.25f;
        public float speed = 2f;
        public float radius = 1f;
        public float lifetime = 0.6f;
    }

    [Serializable]
    public sealed class CameraCue
    {
        public string id = "";
        public CameraCueKind kind;
        public float amplitude = 0.12f;
        public float frequency = 18f;
        [Tooltip("Field of view change at the peak (negative = zoom in).")] public float fovDelta = -6f;
    }

    /// <summary>
    /// Boss transition presentation cues looked up by the ids in BossPhase (R-08-11 "전환 애니메이션·VFX·카메라").
    /// The cues are temporary procedural resources (TEST-PRES-01); playback timing follows the judged transition
    /// (EXP-PRES-01). Presentation only: nothing here changes judgement.
    /// </summary>
    [CreateAssetMenu(menuName = "Corum2/Presentation Library", fileName = "PresentationLibrary")]
    public sealed class PresentationLibrary : ScriptableObject
    {
        public MetaFields meta = new MetaFields { domainId = "presentation", relatedRIds = new[] { "R-08-11" } };
        public List<AnimationCue> animations = new List<AnimationCue>();
        public List<VfxCue> vfx = new List<VfxCue>();
        public List<CameraCue> cameras = new List<CameraCue>();

        public AnimationCue Animation(string id) => string.IsNullOrEmpty(id) ? null : animations.Find(x => x.id == id);
        public VfxCue Vfx(string id) => string.IsNullOrEmpty(id) ? null : vfx.Find(x => x.id == id);
        public CameraCue Camera(string id) => string.IsNullOrEmpty(id) ? null : cameras.Find(x => x.id == id);

        static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

        /// <summary>Library checks plus every boss phase id must resolve; ids need a transition time above 0.</summary>
        public static List<string> Validate(PresentationLibrary lib, IEnumerable<BossDefinition> bosses, string libName = "PresentationLibrary")
        {
            var issues = new List<string>();
            if (lib != null)
            {
                Unique(issues, libName, "animation", lib.animations.ConvertAll(a => a.id));
                Unique(issues, libName, "vfx", lib.vfx.ConvertAll(a => a.id));
                Unique(issues, libName, "camera", lib.cameras.ConvertAll(a => a.id));
                foreach (var a in lib.animations)
                {
                    if (!Finite(a.amplitude) || a.amplitude < 0f) issues.Add(libName + " animation " + a.id + ": amplitude must be finite and >= 0");
                    if (!Finite(a.frequency) || a.frequency < 0f) issues.Add(libName + " animation " + a.id + ": frequency must be finite and >= 0");
                }
                foreach (var v in lib.vfx)
                {
                    if (!Finite(v.rate) || v.rate < 0f) issues.Add(libName + " vfx " + v.id + ": rate must be finite and >= 0");
                    if (!Finite(v.size) || v.size <= 0f) issues.Add(libName + " vfx " + v.id + ": size must be finite and > 0");
                    if (!Finite(v.lifetime) || v.lifetime <= 0f) issues.Add(libName + " vfx " + v.id + ": lifetime must be finite and > 0");
                    if (!Finite(v.radius) || v.radius < 0f) issues.Add(libName + " vfx " + v.id + ": radius must be finite and >= 0");
                    if (!Finite(v.speed) || v.speed < 0f) issues.Add(libName + " vfx " + v.id + ": speed must be finite and >= 0");
                    if (v.burstCount < 0) issues.Add(libName + " vfx " + v.id + ": burstCount must be >= 0");
                }
                foreach (var c in lib.cameras)
                {
                    if (!Finite(c.amplitude) || c.amplitude < 0f) issues.Add(libName + " camera " + c.id + ": amplitude must be finite and >= 0");
                    if (!Finite(c.frequency) || c.frequency < 0f) issues.Add(libName + " camera " + c.id + ": frequency must be finite and >= 0");
                    if (!Finite(c.fovDelta) || Mathf.Abs(c.fovDelta) > 30f) issues.Add(libName + " camera " + c.id + ": fovDelta must be finite and within +-30");
                }
            }
            foreach (var b in bosses)
            {
                if (b == null) continue;
                foreach (var ph in b.Phases)
                {
                    var any = !string.IsNullOrEmpty(ph.TransitionAnimation) || !string.IsNullOrEmpty(ph.TransitionVfx) || !string.IsNullOrEmpty(ph.TransitionCamera);
                    if (!any) continue;
                    var src = "boss phase " + ph.Id;
                    if (lib == null)
                    {
                        issues.Add(src + ": presentation ids set but no PresentationLibrary assigned");
                        continue;
                    }
                    if (!(ph.TransitionTime > 0f)) issues.Add(src + ": presentation ids need TransitionTime > 0");
                    if (!string.IsNullOrEmpty(ph.TransitionAnimation) && lib.Animation(ph.TransitionAnimation) == null)
                        issues.Add(src + ": TransitionAnimation '" + ph.TransitionAnimation + "' not in " + libName);
                    if (!string.IsNullOrEmpty(ph.TransitionVfx) && lib.Vfx(ph.TransitionVfx) == null)
                        issues.Add(src + ": TransitionVfx '" + ph.TransitionVfx + "' not in " + libName);
                    if (!string.IsNullOrEmpty(ph.TransitionCamera) && lib.Camera(ph.TransitionCamera) == null)
                        issues.Add(src + ": TransitionCamera '" + ph.TransitionCamera + "' not in " + libName);
                }
            }
            return issues;
        }

        static void Unique(List<string> issues, string lib, string kind, List<string> ids)
        {
            var seen = new HashSet<string>();
            foreach (var id in ids)
                if (string.IsNullOrEmpty(id) || !seen.Add(id)) issues.Add(lib + " " + kind + " id empty or duplicated: '" + id + "'");
        }
    }

    /// <summary>Pure cue math (testable). Everything is scaled by Envelope(progress), which is 0 at the transition start
    /// and end, so the visible effect begins and finishes with the judged transition.</summary>
    public static class PresentationMath
    {
        public static float Envelope(float progress) => Mathf.Sin(Mathf.PI * Mathf.Clamp01(progress));

        /// <summary>Body offset, scale multiplier and tint weight for an animation cue at game time t into the transition.</summary>
        public static (Vector2 offset, float scale, float tint) Body(AnimationCue a, float t, float progress)
        {
            if (a == null) return (Vector2.zero, 1f, 0f);
            var env = Envelope(progress);
            var w = Mathf.Sin(2f * Mathf.PI * a.frequency * t);
            switch (a.kind)
            {
                case AnimationCueKind.Pulse: return (Vector2.zero, 1f + a.amplitude * env * Mathf.Abs(w), a.tintAmount * env);
                case AnimationCueKind.Shake: return (new Vector2(a.amplitude * env * w, 0f), 1f, a.tintAmount * env);
                case AnimationCueKind.Flicker: return (Vector2.zero, 1f, w >= 0f ? a.tintAmount * env : 0f);
                case AnimationCueKind.Rise: return (new Vector2(0f, a.amplitude * env), 1f, a.tintAmount * env);
            }
            return (Vector2.zero, 1f, 0f);
        }

        /// <summary>Camera offset and field-of-view delta for a camera cue.</summary>
        public static (Vector2 offset, float fov) Camera(CameraCue c, float t, float progress)
        {
            if (c == null) return (Vector2.zero, 0f);
            var env = Envelope(progress);
            switch (c.kind)
            {
                case CameraCueKind.Shake:
                    return (new Vector2(Mathf.Sin(2f * Mathf.PI * c.frequency * t), Mathf.Sin(2f * Mathf.PI * c.frequency * 1.37f * t + 1f)) * (c.amplitude * env), 0f);
                case CameraCueKind.Zoom:
                    return (Vector2.zero, c.fovDelta * env);
            }
            return (Vector2.zero, 0f);
        }
    }
}
