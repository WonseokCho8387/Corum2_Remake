using System.Collections.Generic;

namespace Corum2.Core
{
    /// <summary>Load-time checks for P0-08 AI and boss data (spec 11.2). Failures stop the run; nothing is patched.</summary>
    public static class AiValidator
    {
        static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

        static void NonNeg(List<ValidationIssue> issues, string src, string name, float v)
        {
            if (!Finite(v) || v < 0f) issues.Add(new ValidationIssue(src, name + " must be a finite number >= 0 (got " + v + ")"));
        }

        static void Pos(List<ValidationIssue> issues, string src, string name, float v)
        {
            if (!Finite(v) || !(v > 0f)) issues.Add(new ValidationIssue(src, name + " must be a finite number > 0 (got " + v + ")"));
        }

        static void Projectile(List<ValidationIssue> issues, string src, ProjectileDefinition p)
        {
            if (p == null) { issues.Add(new ValidationIssue(src, "projectile missing")); return; }
            Pos(issues, src, "Projectile.Speed", p.Speed);
            Pos(issues, src, "Projectile.MaxDistance", p.MaxDistance);
            Pos(issues, src, "Projectile.Width", p.Width);
            Pos(issues, src, "Projectile.Height", p.Height);
            if (!Finite(p.SpawnOffsetX) || !Finite(p.SpawnOffsetY)) issues.Add(new ValidationIssue(src, "Projectile spawn offset must be finite"));
        }

        public static void Validate(List<ValidationIssue> issues, EnemyDefinition e, System.Action<List<ValidationIssue>, string, AttackDefinition> attack)
        {
            var ai = e.Ai;
            if (ai == null || ai.Type == AiType.Fixture) return;
            var src = "Enemy " + e.Id + " AI";
            Pos(issues, src, "TrackRange", ai.TrackRange);
            Pos(issues, src, "AttackRange", e.AttackRange);
            NonNeg(issues, src, "AttackInterval", e.AttackInterval);
            NonNeg(issues, src, "FirstAttackDelay", e.FirstAttackDelay);
            if (ai.Type != AiType.Boss && e.Attack == null) issues.Add(new ValidationIssue(src, ai.Type + " needs an attack"));
            switch (ai.Type)
            {
                case AiType.Chaser:
                    Pos(issues, src, "MoveSpeed", ai.MoveSpeed);
                    break;
                case AiType.Fleer:
                    Pos(issues, src, "MoveSpeed", ai.MoveSpeed);
                    Pos(issues, src, "FleeSpeed", ai.FleeSpeed);
                    Pos(issues, src, "FleeTime", ai.FleeTime);
                    break;
                case AiType.Jumper:
                    Pos(issues, src, "MoveSpeed", ai.MoveSpeed);
                    NonNeg(issues, src, "JumpMinDistance", ai.JumpMinDistance);
                    Pos(issues, src, "JumpMaxDistance", ai.JumpMaxDistance);
                    if (Finite(ai.JumpMinDistance) && Finite(ai.JumpMaxDistance) && ai.JumpMinDistance > ai.JumpMaxDistance)
                        issues.Add(new ValidationIssue(src, "JumpMinDistance must be <= JumpMaxDistance"));
                    Pos(issues, src, "JumpSpeedX", ai.JumpSpeedX);
                    Pos(issues, src, "JumpVelocityY", ai.JumpVelocityY);
                    NonNeg(issues, src, "JumpCooldown", ai.JumpCooldown);
                    if (ai.AirAttack != null)
                    {
                        attack(issues, src + " air attack", ai.AirAttack);
                        Pos(issues, src, "AirAttackRange", ai.AirAttackRange);
                    }
                    break;
                case AiType.Ranged:
                    Projectile(issues, src, ai.Projectile);
                    Pos(issues, src, "RetreatSpeed", ai.RetreatSpeed);
                    NonNeg(issues, src, "RetreatTime", ai.RetreatTime);
                    break;
                case AiType.Boss:
                    ValidateBoss(issues, e, attack);
                    break;
            }
        }

        static void ValidateBoss(List<ValidationIssue> issues, EnemyDefinition e, System.Action<List<ValidationIssue>, string, AttackDefinition> attack)
        {
            var src = "Boss " + e.Id;
            var b = e.Boss;
            if (b == null || b.Phases.Count == 0) { issues.Add(new ValidationIssue(src, "boss needs at least one phase (R-08-09)")); return; }
            var ids = new HashSet<string>();
            foreach (var ph in b.Phases)
                if (string.IsNullOrEmpty(ph.Id) || !ids.Add(ph.Id)) issues.Add(new ValidationIssue(src, "phase id empty or duplicated: '" + ph.Id + "'"));
            if (b.Find(b.StartPhase) == null) issues.Add(new ValidationIssue(src, "StartPhase '" + b.StartPhase + "' not found"));
            foreach (var ph in b.Phases)
            {
                var ps = src + " phase " + ph.Id;
                if (ph.Patterns.Count == 0) issues.Add(new ValidationIssue(ps, "phase needs at least one pattern"));
                NonNeg(issues, ps, "AttackInterval", ph.AttackInterval);
                NonNeg(issues, ps, "ApproachSpeed", ph.ApproachSpeed);
                NonNeg(issues, ps, "RetreatSpeed", ph.RetreatSpeed);
                NonNeg(issues, ps, "PreferredMinDistance", ph.PreferredMinDistance);
                NonNeg(issues, ps, "PreferredMaxDistance", ph.PreferredMaxDistance);
                if (Finite(ph.PreferredMinDistance) && Finite(ph.PreferredMaxDistance) && ph.PreferredMinDistance > ph.PreferredMaxDistance)
                    issues.Add(new ValidationIssue(ps, "PreferredMinDistance must be <= PreferredMaxDistance"));
                NonNeg(issues, ps, "JumpInterval", ph.JumpInterval);
                if (ph.JumpInterval > 0f)
                {
                    Pos(issues, ps, "JumpSpeedX", ph.JumpSpeedX);
                    Pos(issues, ps, "JumpVelocityY", ph.JumpVelocityY);
                }
                if (!Finite(ph.StaggerResistance) || ph.StaggerResistance < 0f || ph.StaggerResistance > 1f)
                    issues.Add(new ValidationIssue(ps, "StaggerResistance must be in [0,1]"));
                Pos(issues, ps, "AttackSpeed", ph.AttackSpeed);
                NonNeg(issues, ps, "TransitionTime", ph.TransitionTime);
                var pids = new HashSet<string>();
                foreach (var pt in ph.Patterns)
                {
                    var pts = ps + " pattern " + pt.Id;
                    if (string.IsNullOrEmpty(pt.Id) || !pids.Add(pt.Id)) issues.Add(new ValidationIssue(ps, "pattern id empty or duplicated: '" + pt.Id + "'"));
                    if (pt.Attack == null) issues.Add(new ValidationIssue(pts, "attack missing"));
                    else attack(issues, pts, pt.Attack);
                    NonNeg(issues, pts, "MinDistance", pt.MinDistance);
                    NonNeg(issues, pts, "MaxDistance", pt.MaxDistance);
                    if (Finite(pt.MinDistance) && Finite(pt.MaxDistance) && pt.MinDistance > pt.MaxDistance)
                        issues.Add(new ValidationIssue(pts, "MinDistance must be <= MaxDistance"));
                    if (pt.Projectile != null) Projectile(issues, pts, pt.Projectile);
                }
                foreach (var t in ph.Transitions)
                {
                    var ts = ps + " transition " + t.Kind;
                    if (b.Find(t.TargetPhase) == null) issues.Add(new ValidationIssue(ts, "target phase '" + t.TargetPhase + "' not found"));
                    else if (t.TargetPhase == ph.Id) issues.Add(new ValidationIssue(ts, "transition to the same phase"));
                    switch (t.Kind)
                    {
                        case TransitionKind.HpBelow:
                            if (!Finite(t.Value) || !(t.Value > 0f && t.Value < 1f)) issues.Add(new ValidationIssue(ts, "HP ratio must be in (0,1)"));
                            break;
                        case TransitionKind.PatternUsed:
                            if (!pids.Contains(t.PatternId)) issues.Add(new ValidationIssue(ts, "pattern '" + t.PatternId + "' is not in this phase"));
                            if (t.Count < 1) issues.Add(new ValidationIssue(ts, "Count must be >= 1"));
                            break;
                        case TransitionKind.Event:
                            if (string.IsNullOrEmpty(t.EventName)) issues.Add(new ValidationIssue(ts, "EventName missing"));
                            break;
                    }
                }
            }
        }
    }
}
