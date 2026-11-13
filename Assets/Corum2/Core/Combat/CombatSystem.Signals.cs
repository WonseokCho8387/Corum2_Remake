using System.Collections.Generic;
using System.Text;

namespace Corum2.Core
{
    /// <summary>
    /// WP-11A-R1 (TC-UNITY-07, Unity appendix §3): hit candidate collection and external (late) hit signals, diagnostics only.
    /// - hit.candidates: what this tick's resolution collected, before Dodge pairing and application, with its tick, so each
    ///   hit / dodge record can be linked to a candidate (attackInstanceId, targetId, hit event).
    /// - External hit signals model a collision callback that reaches the core on a later tick (for example after the attack
    ///   ended or was cancelled). The core decides from its own state: a signal is accepted only when it names the current attack
    ///   instance, inside its active window, a targetable target that the hitbox overlaps and that the core itself collected this
    ///   tick. A signal never creates, repeats or reorders a hit; it is logged with its verdict and dropped. No undecided priority
    ///   (CF-05/06) is decided here.
    /// </summary>
    public sealed partial class CombatSystem
    {
        public sealed class ExternalHitSignal
        {
            public long AttackInstanceId;
            public string TargetId = "";
            public string Source = "";
            public long ReceivedTick;
        }

        readonly List<ExternalHitSignal> _signals = new List<ExternalHitSignal>();
        /// <summary>Player attack instances that are over, with how they ended (attack.end reason).</summary>
        readonly Dictionary<long, string> _endedAttacks = new Dictionary<long, string>();

        /// <summary>The most recent player attack instance that ended or was cancelled (0 when none).</summary>
        public long LastEndedAttackInstanceId { get; private set; }

        /// <summary>Queues a signal; it is judged at the next hit resolution (the tick it is processed is logged).</summary>
        public void SubmitExternalHitSignal(long attackInstanceId, string targetId, string source)
        {
            var s = new ExternalHitSignal { AttackInstanceId = attackInstanceId, TargetId = targetId ?? "", Source = source ?? "", ReceivedTick = _sim.Tick };
            _signals.Add(s);
            Log.Emit("hit.signal.received", Pl, s.TargetId, "", "", "ExternalSignal(appendix 3, diagnostic)", ("attackInstanceId", attackInstanceId),
                ("source", s.Source), ("receivedTick", s.ReceivedTick), ("currentAttackInstanceId", Attack?.InstanceId ?? 0));
        }

        void NoteAttackOver(PlayerAttack a, string how)
        {
            _endedAttacks[a.InstanceId] = how;
            LastEndedAttackInstanceId = a.InstanceId;
        }

        void LogCandidates(PlayerAttack atk, Aabb? box, int ev)
        {
            if (_pc.Count == 0 && _ec.Count == 0) return;
            var p = new StringBuilder();
            foreach (var (en, e) in _pc)
            {
                if (p.Length > 0) p.Append(',');
                p.Append(atk.InstanceId).Append(':').Append(en.Id).Append(':').Append(e);
            }
            var q = new StringBuilder();
            foreach (var en in _ec)
            {
                if (q.Length > 0) q.Append(',');
                q.Append(en.AttackInstanceId).Append(':').Append(en.Id).Append('>').Append(Pl);
            }
            Log.Emit("hit.candidates", Pl, "", "", "", "Collected(appendix 3)", ("playerCandidates", p.ToString()), ("enemyCandidates", q.ToString()),
                ("attackInstanceId", atk?.InstanceId ?? 0), ("hitEvent", box.HasValue ? ev : -1), ("collectedTick", _sim.Tick));
        }

        void JudgeExternalSignals(PlayerAttack atk, Aabb? box, int ev)
        {
            if (_signals.Count == 0) return;
            foreach (var s in _signals)
            {
                string verdict;
                var accepted = false;
                var en = Find(s.TargetId);
                if (s.AttackInstanceId == 0)
                    verdict = "Rejected:NoAttackInstance";
                else if (atk == null || atk.InstanceId != s.AttackInstanceId)
                    verdict = "Rejected:StaleInstance:" + (_endedAttacks.TryGetValue(s.AttackInstanceId, out var how) ? how : "Unknown");
                else if (!box.HasValue)
                    verdict = "Rejected:NotActive:" + (atk.Elapsed < atk.Def.Startup ? "Startup" : "Recovery");
                else if (en == null)
                    verdict = "Rejected:UnknownTarget";
                else if (!en.Targetable)
                    verdict = "Rejected:TargetNotTargetable:" + en.State;
                else if (!box.Value.Overlaps(en.Body))
                    verdict = "Rejected:NoOverlap";
                else if (_pc.Exists(x => x.en == en))
                {
                    verdict = "Accepted:SameAsCollectedCandidate";
                    accepted = true;
                }
                else if (ev < atk.HitByEvent.Count && atk.HitByEvent[ev].Contains(en.Id))
                    verdict = "Rejected:AlreadyHitThisEvent";
                else
                    verdict = "Rejected:NotCollectedByCore";
                Log.Emit("hit.signal.resolved", Pl, s.TargetId, "", "", verdict, ("attackInstanceId", s.AttackInstanceId), ("source", s.Source),
                    ("receivedTick", s.ReceivedTick), ("judgedTick", _sim.Tick), ("currentAttackInstanceId", atk?.InstanceId ?? 0),
                    ("accepted", accepted), ("appliedAsHit", false));
            }
            _signals.Clear();
        }
    }
}
