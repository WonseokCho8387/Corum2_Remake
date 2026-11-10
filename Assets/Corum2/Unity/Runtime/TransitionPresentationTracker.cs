using System.Collections.Generic;
using Corum2.Core;

namespace Corum2.Unity
{
    /// <summary>
    /// Follows the judged boss transitions and decides presentation playback (EXP-PRES-01). Plain C#, no scene objects,
    /// so it runs in EditMode tests on the simulation alone.
    ///
    /// WP04-F04: the lifecycle comes from the Core transition event queue (CombatSystem.DrainTransitionEvents), in
    /// simulation order, so the judged start / end / cancel ticks are kept as they were. What the screen did is recorded
    /// separately: the tick of the first rendered update that shows a playback (ShownTick) and the update that observed
    /// its end (ObservedEndTick). A transition that started and ended between two rendered updates is never shown and is
    /// logged as presentation.skipped with its judged ticks.
    ///
    ///   progress: (duration - TransitionTimer) / duration at the rendered update, game time (pauses with the boss's
    ///             hitstop); 1 once the judged end is consumed.
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
            /// <summary>Judged transition start tick (Core event).</summary>
            public long StartTick;
            /// <summary>Judged end / cancel tick (Core event); -1 while running.</summary>
            public long EndTick = -1;
            /// <summary>Simulation tick of the first rendered update that showed it; -1 = never shown.</summary>
            public long ShownTick = -1;
            /// <summary>Simulation tick of the rendered update that consumed the end.</summary>
            public long ObservedEndTick = -1;
            public float Elapsed;
            public float Progress;
            public bool Finished;
            /// <summary>Ended / Cancelled / Skipped (never rendered).</summary>
            public string EndKind = "";
        }

        readonly PresentationLibrary _lib;
        readonly List<TransitionEvent> _events = new List<TransitionEvent>();
        public readonly List<Playback> Active = new List<Playback>();
        /// <summary>Every playback, in judged start order (tests and evidence).</summary>
        public readonly List<Playback> History = new List<Playback>();
        readonly List<Playback> _finished = new List<Playback>();
        int _dropped;

        public TransitionPresentationTracker(PresentationLibrary lib) => _lib = lib;

        static string Kind(object cue, string id) => string.IsNullOrEmpty(id) ? "none" : cue == null ? "missing" : "found";

        /// <summary>Call once per rendered update after the simulation steps. Returns playbacks finished in this call
        /// (ended, cancelled or skipped).</summary>
        public List<Playback> Update(Simulation sim)
        {
            _finished.Clear();
            if (sim.Combat == null) return _finished;
            if (sim.Combat.DroppedTransitionEvents != _dropped)
            {
                sim.Log.Emit("presentation.dropped", "", "", "", "", "TransitionEventQueueOverflow(EXP-PRES-01)",
                    ("count", sim.Combat.DroppedTransitionEvents - _dropped));
                _dropped = sim.Combat.DroppedTransitionEvents;
            }
            _events.Clear();
            sim.Combat.DrainTransitionEvents(_events);
            foreach (var ev in _events)
            {
                if (ev.Kind == TransitionEventKind.Start)
                {
                    var ph = ev.Enemy.Boss?.Find(ev.Phase);
                    var p = new Playback
                    {
                        Enemy = ev.Enemy, Serial = ev.Serial, Phase = ev.Phase, Duration = ev.Duration, StartTick = ev.Tick,
                        AnimationId = ph?.TransitionAnimation ?? "", VfxId = ph?.TransitionVfx ?? "", CameraId = ph?.TransitionCamera ?? "",
                    };
                    if (_lib != null)
                    {
                        p.Animation = _lib.Animation(p.AnimationId);
                        p.Vfx = _lib.Vfx(p.VfxId);
                        p.Camera = _lib.Camera(p.CameraId);
                    }
                    Active.Add(p);
                    History.Add(p);
                    continue;
                }
                var q = Active.Find(x => x.Enemy == ev.Enemy && x.Serial == ev.Serial);
                if (q == null)
                {
                    sim.Log.Emit("presentation.orphanEnd", ev.Enemy.Id, "", "", ev.Kind.ToString(), "NoPlaybackForSerial(EXP-PRES-01)",
                        ("transitionSerial", ev.Serial), ("judgedTick", ev.Tick));
                    continue;
                }
                q.EndTick = ev.Tick;
                q.ObservedEndTick = sim.Tick;
                q.Elapsed = ev.Kind == TransitionEventKind.End ? q.Duration : q.Elapsed;
                q.Progress = ev.Kind == TransitionEventKind.End ? 1f : q.Progress;
                if (q.ShownTick < 0) Finish(sim, q, "Skipped", "presentation.skipped", "EndedBeforeAnyRenderedUpdate(EXP-PRES-01)");
                else if (ev.Kind == TransitionEventKind.End) Finish(sim, q, "Ended", "presentation.end", "TransitionEnd(EXP-PRES-01)");
                else Finish(sim, q, "Cancelled", "presentation.cancel", "TransitionCancel:Death(EXP-BOSS-02, EXP-PRES-01)");
            }
            foreach (var p in Active)
            {
                var en = p.Enemy;
                p.Elapsed = p.Duration - en.TransitionTimer;
                if (p.Elapsed < 0f) p.Elapsed = 0f;
                p.Progress = p.Duration > 0f ? UnityEngine.Mathf.Clamp01(p.Elapsed / p.Duration) : 1f;
                if (p.ShownTick >= 0) continue;
                p.ShownTick = sim.Tick;
                sim.Log.Emit("presentation.start", en.Id, "", "", p.Phase, "TransitionPresentationShown(R-08-11, EXP-PRES-01)",
                    ("transitionSerial", p.Serial), ("phase", p.Phase), ("duration", p.Duration),
                    ("judgedStartTick", p.StartTick), ("shownTick", p.ShownTick), ("displayDelayTicks", p.ShownTick - p.StartTick),
                    ("progressAtShow", p.Progress),
                    ("animation", p.AnimationId), ("animationCue", Kind(p.Animation, p.AnimationId)), ("animationKind", p.Animation != null ? p.Animation.kind.ToString() : ""),
                    ("vfx", p.VfxId), ("vfxCue", Kind(p.Vfx, p.VfxId)), ("vfxKind", p.Vfx != null ? p.Vfx.kind.ToString() : ""),
                    ("camera", p.CameraId), ("cameraCue", Kind(p.Camera, p.CameraId)), ("cameraKind", p.Camera != null ? p.Camera.kind.ToString() : ""));
            }
            return _finished;
        }

        void Finish(Simulation sim, Playback p, string kind, string type, string reason)
        {
            p.Finished = true;
            p.EndKind = kind;
            Active.Remove(p);
            _finished.Add(p);
            sim.Log.Emit(type, p.Enemy.Id, "", p.Phase, kind, reason, ("transitionSerial", p.Serial), ("phase", p.Phase),
                ("duration", p.Duration), ("judgedStartTick", p.StartTick), ("judgedEndTick", p.EndTick),
                ("shownTick", p.ShownTick), ("observedTick", p.ObservedEndTick), ("observeDelayTicks", p.ObservedEndTick - p.EndTick),
                ("elapsedGame", p.Elapsed), ("progress", p.Progress), ("bossState", p.Enemy.State.ToString()));
        }
    }
}
