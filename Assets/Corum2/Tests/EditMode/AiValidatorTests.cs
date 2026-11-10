using System.Collections.Generic;
using System.Linq;
using Corum2.Core;
using NUnit.Framework;

namespace Corum2.Tests
{
    /// <summary>WP-04 AI/boss data checks (spec 11.2: missing phase/target, bad ranges, non-finite values).</summary>
    public class AiValidatorTests
    {
        static List<ValidationIssue> V(EnemyDefinition e) =>
            DataValidator.ValidateCombat(CombatFixtures.Profile(), CombatFixtures.Unlocks(), new List<EnemyDefinition> { e }, null);

        static EnemyDefinition Boss()
        {
            var p1 = AiFixtures.Phase("p1", AiFixtures.Pattern("a", 0f, 2f));
            p1.Transitions.Add(AiFixtures.Hp(0.5f, "p2"));
            return AiFixtures.Boss("B", "p1", p1, AiFixtures.Phase("p2", AiFixtures.Pattern("b", 0f, 2f)));
        }

        [Test, Category("DATA")]
        public void ValidData_Passes()
        {
            Assert.IsEmpty(V(AiFixtures.Chaser()));
            Assert.IsEmpty(V(AiFixtures.Fleer()));
            Assert.IsEmpty(V(AiFixtures.Jumper()));
            Assert.IsEmpty(V(AiFixtures.Ranged()));
            Assert.IsEmpty(V(Boss()));
        }

        [Test, Category("DATA")]
        public void BadData_Rejected_WithSourceAndField()
        {
            var cases = new List<(string, EnemyDefinition)>();
            var e = AiFixtures.Chaser(); e.Ai.MoveSpeed = float.NaN; cases.Add(("MoveSpeed", e));
            e = AiFixtures.Jumper(); e.Ai.JumpMinDistance = 6f; cases.Add(("JumpMinDistance", e));
            e = AiFixtures.Ranged(); e.Ai.Projectile = null; cases.Add(("projectile", e));
            e = AiFixtures.Ranged(); e.Ai.Projectile.Speed = float.PositiveInfinity; cases.Add(("Speed", e));
            e = Boss(); e.Boss.StartPhase = "nope"; cases.Add(("StartPhase", e));
            e = Boss(); e.Boss.Phases[0].Transitions[0].TargetPhase = "nope"; cases.Add(("target phase", e));
            e = Boss(); e.Boss.Phases[0].Transitions[0].Value = 1.5f; cases.Add(("HP ratio", e));
            e = Boss(); e.Boss.Phases[0].Transitions.Add(AiFixtures.Used("missing", 1, "p2")); cases.Add(("not in this phase", e));
            e = Boss(); e.Boss.Phases[1].Patterns.Clear(); cases.Add(("at least one pattern", e));
            e = Boss(); e.Boss.Phases[1].AttackSpeed = 0f; cases.Add(("AttackSpeed", e));
            e = Boss(); e.Boss.Phases[0].Patterns[0].MinDistance = 3f; cases.Add(("MinDistance", e));
            e = Boss(); e.Boss.Phases.Add(AiFixtures.Phase("p1", AiFixtures.Pattern("c", 0f, 2f))); cases.Add(("duplicated", e));
            foreach (var (field, def) in cases)
            {
                var issues = V(def);
                Assert.IsNotEmpty(issues, field);
                Assert.IsTrue(issues.Any(i => i.ToString().Contains(field)), field + ": " + string.Join(" | ", issues));
            }
        }
    }
}
