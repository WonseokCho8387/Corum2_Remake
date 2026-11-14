using System.Collections.Generic;
using System.Linq;
using Corum2.Core;
using Corum2.Core.P1;
using NUnit.Framework;
using static Corum2.Tests.CombatFixtures;

namespace Corum2.Tests
{
    /// <summary>P1-B: damage order BAL-01~12 (pure + through the P0 combat core with the P1 extension).</summary>
    public class P1DamageTests
    {
        sealed class Rig
        {
            public P1Profile P = P1Defaults.Create();
            public P1Progress G;
            public ProgressionSystem Prog;
            public BuffSystem Buffs;
            public P1Effects Fx = new P1Effects();
            public SimHarness H;
            public P1CombatBridge B;

            public Rig(params (string id, EnemyDefinition def, float x)[] enemies)
            {
                G = P1Progress.New(P);
                Prog = new ProgressionSystem(P, G);
                Buffs = new BuffSystem(P);
                var defs = enemies.Select(e => e.def).GroupBy(d => d.Id).Select(g => g.First()).ToList();
                H = new SimHarness("p1-damage", combat: Profile(), combatUnlocks: Unlocks(), enemies: defs,
                    world: World(enemies.Select(e => (e.id, e.def.Id, e.x)).ToArray()));
                B = new P1CombatBridge(P, Prog, Buffs, Fx, H.C, H.Sim.Log);
                H.C.Ext = B;
                H.C.SetPlayerHp(B.MaxHp, "test");
            }

            public EnemyActor E(string id) => H.C.Enemies.First(e => e.Id == id);

            public float Hit(AttackDefinition def, string id)
            {
                var en = E(id);
                var before = en.Hp;
                H.C.TestApplyHit(def, id);
                return before - en.Hp;
            }
        }

        static AttackDefinition Atk(AttackKind k, float d, DamageType t = DamageType.Physical) =>
            new AttackDefinition { Id = "t." + k, Kind = k, TestDamage = d, DamageType = t, Knockback = 1f };

        [Test]
        public void T163_SumBonusesThenBuff()
        {
            var r = new Rig(("e", Bag("e", 100), 3f));
            r.G.Axes["skill.groundBasic.dmg"] = 1;
            r.G.Axes["stat.PhysAtk"] = 2;
            r.Fx.Sword = new SwordState { SpellId = "spell.light_sword", Bonus = 3, Remaining = 10 };
            r.Buffs.Add("BF-PATK", Rarity.Normal); // 8 %
            r.Buffs.Add("BF-PATK", Rarity.Epic);   // 12 %
            // pre = 1 + 1 + 2 + 3 = 7, raw = 7 x 1.20 = 8.4 -> 8
            Assert.AreEqual(8f, r.Hit(Atk(AttackKind.GroundNormal, 1), "e"));
        }

        [Test]
        public void T164_T165_T23_CommonDefenseCurve()
        {
            var p = new DamageProfile();
            Assert.AreEqual(1.0, DamageCalc.DefenseMultiplier(p, 0), 1e-9);
            Assert.AreEqual(0.6667, DamageCalc.DefenseMultiplier(p, 10), 1e-4);
            Assert.AreEqual(0.5, DamageCalc.DefenseMultiplier(p, 20), 1e-9);
            Assert.AreEqual(0.3333, DamageCalc.DefenseMultiplier(p, 40), 1e-4);
            p.DefenseK = 40;
            Assert.AreEqual(0.6667, DamageCalc.DefenseMultiplier(p, 20), 1e-4, "K is editable");
            var armored = Bag("arm", 100);
            armored.Defense = 20;
            var r = new Rig(("a", armored, 3f));
            Assert.AreEqual(5f, r.Hit(Atk(AttackKind.GroundNormal, 10), "a"), "physical halved");
            Assert.AreEqual(5f, r.Hit(Atk(AttackKind.Spell, 10, DamageType.Magic), "a"), "magic uses the same DEF");
            // not a flat -1: DEF1, damage 5 -> floor(5 x 20/21) = 4; damage 1 -> min 1
            var dp = new DamageProfile();
            Assert.AreEqual(4, DamageCalc.PreShield(dp, 5, false, 1, 0));
            Assert.AreEqual(1, DamageCalc.PreShield(dp, 1, false, 1, 0));
        }

        [Test]
        public void T166_DefenseTimesTemporaryReduction()
        {
            var dp = new DamageProfile();
            Assert.AreEqual(0.4, DamageCalc.PreShieldReal(dp, 1, false, 20, 0.2f), 1e-6);
            Assert.AreEqual(0.5 * 0.75, DamageCalc.PreShieldReal(dp, 1, false, 20, 0.6f), 1e-6, "temporary reduction capped at 25 %");
        }

        [Test]
        public void T167_MinOneGuardZeroShieldZero()
        {
            var dp = new DamageProfile();
            Assert.AreEqual(1, DamageCalc.PreShield(dp, 1, false, 10, 0));
            Assert.AreEqual(0, DamageCalc.PreShield(dp, 1, true, 10, 0), "front guard: <1 -> 0, no forced 1");
            Assert.AreEqual(0, DamageCalc.PreShield(dp, 10, false, 0, 0, zeroEvent: true), "parry/invulnerable/clash = 0");
            var shields = new List<ShieldPool> { new ShieldPool { Id = "MagicShield", Remaining = 5 } };
            Assert.AreEqual(0, DamageCalc.Absorb(3, shields));
        }

        [Test]
        public void T168_GuardBeforeDefense()
        {
            var dp = new DamageProfile();
            Assert.AreEqual(1, DamageCalc.PreShield(dp, 20, true, 20, 0), "20 x 0.1 x 0.5 = 1");
            Assert.AreEqual(10, DamageCalc.PreShield(dp, 20, false, 20, 0));
            // through the combat core: front guard on the player
            var r = new Rig(("h", Hitter("h", 20), 11f));
            r.G.Axes["stat.Defense"] = 20;
            r.H.Press(LogicalAction.Guard).StepUntil(() => r.H.Count("guard.block") > 0, 200);
            Assert.AreEqual(1, r.H.Count("guard.block"));
            Assert.AreEqual(r.B.MaxHp - 1, r.H.C.PlayerHp);
            var ev = r.H.Sink.Events.Last(e => e.Type == "p1.damage.in");
            Assert.AreEqual(1, System.Convert.ToInt32(ev.Get("preShield")));
        }

        [Test]
        public void T169_T68_T69_DownedDiveAddsBothCappedBonuses()
        {
            var r = new Rig(("d", Bag("d", 100), 3f));
            r.G.Axes["skill.airDiveDowned.dmg"] = 3;
            r.G.Axes["skill.downAttack.dmg"] = 2;
            Assert.AreEqual(1f, r.Hit(Atk(AttackKind.Dive, 1), "d"), "not downed: no bonus");
            r.E("d").State = EnemyState.Down;
            Assert.AreEqual(6f, r.Hit(Atk(AttackKind.Dive, 1), "d"), "1 + 3 + 2");
            Assert.AreEqual(3f, r.Hit(Atk(AttackKind.DownAttack, 1), "d"), "down attack: its own bonus only");
        }

        [Test]
        public void T170_LifestealSumsOneEventThenFloorsOnce()
        {
            var r = new Rig(("a", Bag("a", 3), 3f), ("b", Bag("b", 5), 3.2f));
            r.Fx.Sword = new SwordState { SpellId = "spell.dark_sword", Rate = 0.25f, Remaining = 10 };
            r.H.C.SetPlayerHp(1, "test");
            var a = new PlayerAttack { Def = Atk(AttackKind.GroundNormal, 10), InstanceId = 999 };
            r.B.OnEnemyHpLost(a, r.E("a"), 0, 3, true);
            r.B.OnEnemyHpLost(a, r.E("b"), 0, 5, true);
            r.B.EndTick();
            Assert.AreEqual(3f, r.H.C.PlayerHp, "floor(8 x 0.25) = 2 (per-enemy floors would give 0 + 1)");
        }

        [Test]
        public void T171_MagicShieldThenArchangel()
        {
            var shields = new List<ShieldPool> { new ShieldPool { Id = "MagicShield", Remaining = 3 }, new ShieldPool { Id = "Archangel", Remaining = 4 } };
            Assert.AreEqual(0, DamageCalc.Absorb(5, shields));
            Assert.AreEqual(0, shields[0].Remaining);
            Assert.AreEqual(2, shields[1].Remaining);
            var fx = new P1Effects { MagicShield = shields[0], ArchangelShield = shields[1] };
            var order = fx.ShieldOrder();
            Assert.AreEqual("Archangel", order[0].Id, "depleted magic shield skipped");
            Assert.AreEqual(1, DamageCalc.Absorb(3, order));
        }

        [Test]
        public void T08_BuffsGoBeyondPermanentCap()
        {
            var r = new Rig(("e", Bag("e", 1000), 3f));
            r.G.Axes["stat.PhysAtk"] = r.P.Character("volker").CapPhysAtk; // 30 (capped)
            var capped = r.Hit(Atk(AttackKind.GroundNormal, 1), "e");
            r.Buffs.Add("BF-PATK", Rarity.Epic);
            Assert.Greater(r.Hit(Atk(AttackKind.GroundNormal, 1), "e"), capped);
            Assert.AreEqual(30f, r.Prog.Rules.Stat(r.G, "PhysAtk"), "the permanent value itself is unchanged");
        }

        [Test]
        public void T21_T64_T65_KoboldHitsAndAllFourHits()
        {
            int Hits(float bonus) { var dmg = 1 + bonus; return (int)System.Math.Ceiling(5 / dmg); }
            Assert.AreEqual(5, Hits(0));
            Assert.AreEqual(3, Hits(1));
            Assert.AreEqual(2, Hits(2));
            Assert.AreEqual(1, Hits(5));
            var r = new Rig(("e", Bag("e", 1000), 3f));
            r.G.Axes["skill.groundBasic.dmg"] = 1;
            r.G.Axes["skill.airBasic.dmg"] = 2;
            float total = 0;
            for (var i = 1; i <= 4; i++) total += r.Hit(Atk(AttackKind.GroundNormal, 1), "e");
            Assert.AreEqual(8f, total, "ground 4-string 4 -> 8 with +1 on every hit");
            Assert.AreEqual(3f, r.Hit(Atk(AttackKind.AirNormal, 1), "e"), "air card is its own axis");
        }

        [Test]
        public void T66_LauncherHeightAndHang()
        {
            var r = new Rig(("e", Bag("e", 1000), 3f));
            var lau = Atk(AttackKind.Launcher, 1);
            lau.LaunchVelocity = 10f;
            r.Hit(lau, "e");
            var v0 = r.E("e").VelY;
            Assert.AreEqual(0f, r.E("e").HangTimer);
            var r2 = new Rig(("e", Bag("e", 1000), 3f));
            r2.G.Axes["skill.launcher.height"] = 1.5f;
            r2.G.Axes["skill.launcher.time"] = 0.5f;
            Assert.AreEqual(1f, r2.Hit(lau, "e"), "launcher damage not changed by the lift card");
            Assert.Greater(r2.E("e").VelY, v0);
            Assert.AreEqual(0.5f, r2.E("e").HangTimer);
            var boss = Bag("boss", 1000);
            boss.Launch = LaunchResponse.Immune;
            var r3 = new Rig(("b", boss, 3f));
            r3.G.Axes["skill.launcher.height"] = 1.5f;
            r3.Hit(lau, "b");
            Assert.AreEqual(EnemyState.Stagger, r3.E("b").State, "immune stays immune");
        }

        [Test]
        public void T67_AirDashDamageOnly()
        {
            var r = new Rig(("e", Bag("e", 1000), 3f));
            r.G.Axes["skill.airDash.dmg"] = 4;
            Assert.AreEqual(5f, r.Hit(Atk(AttackKind.AirDash, 1), "e"));
        }

        [Test]
        public void T22_MissilePermanentSegmentSeparateAndCapped()
        {
            var p = P1Defaults.Create();
            var g = P1Progress.New(p);
            var rules = new GrowthRules(p);
            Assert.AreEqual(3f, rules.MissilePermanentDamage(g));
            g.Axes["mm.dmg"] = 5;
            g.Axes["stat.MagAtk"] = 2;
            Assert.AreEqual(10f, rules.MissilePermanentDamage(g));
            g.Axes["mm.dmg"] = 30;
            Assert.AreEqual(20f, rules.MissilePermanentDamage(g), "per-projectile cap 20 [시험]");
        }
    }
}
