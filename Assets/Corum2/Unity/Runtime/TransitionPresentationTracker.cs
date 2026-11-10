using System.Collections.Generic;
using Corum2.Core;

namespace Corum2.Unity
{
    /// <summary>
    /// Follows the judged boss transitions and decides presentation playback (EXP-PRES-01). Plain C#, no scene objects,
    /// so it runs in EditMode tests on the simulation alone.
    ///   start  : the tick a transition starts (EnemyActor.TransitionSerial changed)
    ///   progress: (TransitionDuration - TransitionTimer) / TransitionDuration, game time (pauses with the boss's hitstop)
    ///   end    : the tick TransitionTimer reaches 0 (= boss.transitionEnd)
    ///   cancel : the boss dies (= boss.transitionCancel, EXP-BOSS-02)
    /// Hit reactions during the transition do not stop it (EXP-BOSS-03).
    /// </summary>
    public sealed class TransitionPresentationTracker
    {
        public sealed class Playback
        {
            public EnemyActor Enemy;
            public int Serial;
            public string Phase = "";
            public float Duration;
            public string AnimationId = "", VfxId = "", CameraId = "";
            public AnimationCue Animation;
            public VfxCue Vfx;
            public CameraCue Camera;
            public long StartTick;
            public long EndTick = -1;
            public float Elapsed;
            public float Progress;
            public bool Finished;
            public string EndKind = "";
        }

        readonly PresentationLibrary _lib;
        readonly Dictionary<EnemyActor, int> _seen = new Dictionary<EnemyActor, int>();
        public readonly List<Playback> Active = new List<Playback>();
        /// <summary>Every playback ever started, in start order (tests and evidence).</summary>
        public readonly List<Playback> History = new List<Playback>();
        readonly List<Playback> _finished = new List<Playback>();

        public TransitionPresentationTracker(PresentationLibrary lib) => _lib = lib;

        static string Kind(object cue, string id) => string.IsNullOrEmpty(id) ? "none" : cue == null ? "missing" : "found";

        /// <summary>Call once per rendered frame after the simulation steps. Returns playbacks that finished in this call.</summary>
        public List<Playback> Update(Simulation sim)
        {
            _finished.Clear();
            if (sim.Combat == null) return _finished;
            foreach (var en in sim.Combat.Enemies)
            {
                if (en.Boss == null) continue;
                _seen.TryGetValue(en, out var seen);
                if (en.TransitionSerial == seen) continue;
                _seen[en] = en.TransitionSerial;
                // A new transition can only start after the previous one ended (TryTransition runs on a free boss), so an
                // open playback of this boss ended on this tick at the latest.
                var old = Active.Find(x => x.Enemy == en);
                if (old != null)
                {
                    old.Elapsed = old.Duration;
                    old.Progress = 1f;
                    Finish(sim, old, "Ended", "presentation.end", "TransitionEnd:NextTransitionSameTick(EXP-PRES-01)");
                }
                if (en.TransitionSerial - seen > 1)
                    sim.Log.Emit("presentation.skipped", en.Id, "", "", "", "TransitionsWithoutRenderedFrame(EXP-PRES-01)",
                        ("count", en.TransitionSerial - seen - 1));
                var ph = en.Phase;
                var p = new Playback
                {
                    Enemy = en, Serial = en.TransitionSerial, Phase = en.PhaseId, Duration = en.TransitionDuration,
                    AnimationId = ph.TransitionAnimation ?? "", VfxId = ph.TransitionVfx ?? "", CameraId = ph.TransitionCamera ?? "",
                    StartTick = sim.Tick,
                };
                if (_lib != null)
                {
                    p.Animation = _lib.Animation(p.AnimationId);
                    p.Vfx = _lib.Vfx(p.VfxId);
                    p.Camera = _lib.Camera(p.CameraId);
                }
                Active.Add(p);
                History.Add(p);
                sim.Log.Emit("presentation.start", en.Id, "", "", p.Phase, "TransitionPresentationStart(R-08-11, EXP-PRES-01)",
                    ("transitionSerial", p.Serial), ("phase", p.Phase), ("duration", p.Duration),
                    ("animation", p.AnimationId), ("animationCue", Kind(p.Animation, p.AnimationId)), ("animationKind", p.Animation != null ? p.Animation.kind.ToString() : ""),
                    ("vfx", p.VfxId), ("vfxCue", Kind(p.Vfx, p.VfxId)), ("vfxKind", p.Vfx != null ? p.Vfx.kind.ToString() : ""),
                    ("camera", p.CameraId), ("cameraCue", Kind(p.Camera, p.CameraId)), ("cameraKind", p.Camera != null ? p.Camera.kind.ToString() : ""),
                    ("transitionTimer", en.TransitionTimer));
            }
            for (var i = Active.Count - 1; i >= 0; i--)
            {
                var p = Active[i];
                var en = p.Enemy;
                if (en.State == EnemyState.Dead || en.State == EnemyState.Gone)
                {
                    Finish(sim, p, "Cancelled", "presentation.cancel", "TransitionCancel:Death(EXP-BOSS-02, EXP-PRES-01)");
                    continue;
                }
                if (en.TransitionTimer <= 0f)
                {
                    p.Elapsed = p.Duration;
                    p.Progress = 1f;
                    Finish(sim, p, "Ended", "presentation.end", "TransitionEnd(EXP-PRES-01)");
                    continue;
                }
                p.Elapsed = p.Duration - en.TransitionTimer;
                p.Progress = p.Duration > 0f ? UnityEngine.Mathf.Clamp01(p.Elapsed / p.Duration) : 1f;
            }
            return _finished;
        }

        void Finish(Simulation sim, Playback p, string kind, string type, string reason)
        {
            p.Finished = true;
            p.EndKind = kind;
            p.EndTick = sim.Tick;
            Active.Remove(p);
            _finished.Add(p);
            sim.Log.Emit(type, p.Enemy.Id, "", p.Phase, kind, reason, ("transitionSerial", p.Serial), ("phase", p.Phase),
                ("duration", p.Duration), ("elapsedGame", p.Elapsed), ("progress", p.Progress), ("startTick", p.StartTick),
                ("bossState", p.Enemy.State.ToString()));
        }
    }
}
