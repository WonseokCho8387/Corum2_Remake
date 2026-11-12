using System.Linq;
using Corum2.Core;
using NUnit.Framework;
using static Corum2.Tests.CombatFixtures;

namespace Corum2.Tests
{
    /// <summary>WP08-F02-R1 regression: when two enemy hits reach the player on the same tick and the first one kills,
    /// the later candidate must not hit the dead player again or publish a second death (GPT WP-06~08 R1 review §1).</summary>
    public class R2DeathTests
    {
        public enum Defense { None, Guard, GuardBreak, Parry }

        static SimHarness Run(int attackers, float hp, Defense def)
        {
            var hitter = Hitter("hitter", 10f, guardBreak: def == Defense.GuardBreak);
            var world = attackers == 2 ? World(("one", "hitter", 11f), ("two", "hitter", 11f)) : World(("one", "hitter", 11f));
            var h = Harness("WP08-F02-R1", world, Defs(hitter));
            h.C.PlayerHp = hp;
            h.StepUntil(() => h.C.Find("one").State == EnemyState.AttackStartup);
            if (def == Defense.Parry) h.Step(12); // RB late enough to fall inside the parry window before contact
            if (def != Defense.None) h.Press(LogicalAction.Guard);
            h.StepUntil(() => h.Count("hit.player") + h.Count("guard.block") + h.Count("guard.parry") > 0);
            return h;
        }

        static int Hits(SimHarness h) => h.Count("hit.player") + h.Count("guard.block") + h.Count("guard.parry");

        [TestCase(Defense.None)]
        [TestCase(Defense.Guard)]
        [TestCase(Defense.GuardBreak)]
        [TestCase(Defense.Parry)]
        public void TwoAttackers_SameTick_Lethal_OneDeath_SecondIgnored(Defense def)
        {
            var h = Run(2, 1f, def);
            Assert.AreEqual(1, h.Count("player.death"), "one death event");
            Assert.AreEqual(1, Hits(h), "only the first candidate is a hit");
            Assert.AreEqual(1, h.Count("hit.player.ignored"), "the second candidate reaches a dead player and is only logged");
            var ig = h.Events("hit.player.ignored").Single();
            Assert.AreEqual("Dead", ig.StateBefore);
            Assert.AreEqual(0f, h.C.PlayerHp);
            Assert.AreEqual(MotorState.Dead, h.P.State);
            var death = h.Events("player.death").Single();
            Assert.AreEqual(ig.SimulationTick, death.SimulationTick, "same tick");
            Assert.AreNotEqual(ig.ActorId, death.TargetId, "the ignored candidate is the other attacker");
            h.Press(LogicalAction.Attack).Step(3);
            Assert.AreEqual(0, h.Count("attack.start"), "no attack after death");
            Assert.AreEqual(1, h.Count("player.death"));
        }

        [TestCase(Defense.None)]
        [TestCase(Defense.Guard)]
        [TestCase(Defense.Parry)]
        public void SingleAttacker_Lethal_OneDeath_NoIgnored(Defense def)
        {
            var h = Run(1, 1f, def);
            Assert.AreEqual(1, h.Count("player.death"));
            Assert.AreEqual(0, h.Count("hit.player.ignored"));
            Assert.AreEqual(MotorState.Dead, h.P.State);
        }

        [Test]
        public void TwoAttackers_SameTick_Hp2Guarded_BothBlocked_NoDeath()
        {
            // Control: guard damage 1 each, HP 2 -> 1 -> 0 kills only on the second hit; still exactly one death.
            var h = Run(2, 2f, Defense.Guard);
            Assert.AreEqual(1, h.Count("player.death"), "second blocked hit is the lethal one");
            Assert.AreEqual(2, h.Count("guard.block"), "both candidates are real hits while the player is alive");
            Assert.AreEqual(0, h.Count("hit.player.ignored"));
            Assert.AreEqual("DefendedHit:Guard", h.Events("player.death").Single().Get("cause"));
        }

        [Test]
        public void TwoAttackers_SameTick_NotLethal_BothHit()
        {
            // Control: the fix does not drop hits on a living player.
            var h = Run(2, 100f, Defense.None);
            Assert.AreEqual(2, h.Count("hit.player"));
            Assert.AreEqual(0, h.Count("hit.player.ignored"));
            Assert.AreEqual(0, h.Count("player.death"));
        }
    }
}
