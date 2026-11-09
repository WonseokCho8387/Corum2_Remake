using System.Collections.Generic;
using Corum2.Core;

namespace Corum2.Tests
{
    /// <summary>WP-02 test data. All numbers are test values, not approved tuning.</summary>
    public static class CombatFixtures
    {
        static DataMeta Meta(string id) => new DataMeta { DomainId = id, DataVersion = "test-2", Status = "experimental" };

        static AttackDefinition A(string id, AttackKind k, float d, float ox = 0.8f, float oy = 0.9f, float w = 1.0f, float h = 1.2f,
            float hitstop = 0.04f, float kb = 1.5f) =>
            new AttackDefinition
            {
                Id = id, Kind = k, TestDamage = d, HitboxOffsetX = ox, HitboxOffsetY = oy, HitboxWidth = w, HitboxHeight = h,
                Startup = 0.06f, Active = 0.08f, Recovery = 0.12f, Hitstop = hitstop, Knockback = kb,
            };

        public static CombatProfile Profile(float comboWindow = 0.5f)
        {
            var p = new CombatProfile { Meta = Meta("combat.test"), ComboWindow = comboWindow };
            p.GroundNormals = new[] { A("g1", AttackKind.GroundNormal, 10), A("g2", AttackKind.GroundNormal, 10), A("g3", AttackKind.GroundNormal, 10), A("g4", AttackKind.GroundNormal, 10) };
            p.Launcher = A("launch", AttackKind.Launcher, 12, hitstop: 0.08f, kb: 0f);
            p.Launcher.LaunchVelocity = 14f;
            p.AirNormals = new[]
            {
                A("a1", AttackKind.AirNormal, 8, 0.7f, 0.9f, 1.2f, 1.8f), A("a2", AttackKind.AirNormal, 8, 0.7f, 0.9f, 1.2f, 1.8f),
                A("a3", AttackKind.AirNormal, 8, 0.7f, 0.9f, 1.2f, 1.8f), A("a4", AttackKind.AirNormal, 8, 0.7f, 0.9f, 1.2f, 1.8f),
            };
            p.AirDash = A("dash", AttackKind.AirDash, 12, 0.4f, 0.9f, 1.2f, 1.8f, 0.06f, 3f);
            p.Dive = A("dive", AttackKind.Dive, 20, 0f, 0.6f, 1.0f, 1.4f, 0.12f, 2f);
            p.Dive.HitsDowned = true;
            p.DownAttack = A("down", AttackKind.DownAttack, 15, 0.9f, 0.3f, 1.4f, 0.8f, 0.06f, 0f);
            p.DownAttack.HitsDowned = true;
            return p;
        }

        public static CombatUnlocks Unlocks(bool pierce = false, bool area = false) =>
            new CombatUnlocks { Meta = Meta("combat.unlocks.test"), DashPierce = pierce, DiveArea = area };

        public static EnemyDefinition Bag(string id = "bag", float hp = 1000f) =>
            new EnemyDefinition { Meta = Meta("enemy." + id), Id = id, MaxHp = hp };

        public static EnemyDefinition Hitter(string id = "hitter", float d = 10f, bool unblockable = false, bool guardBreak = false, bool armor = false)
        {
            var e = Bag(id);
            e.Attack = new AttackDefinition
            {
                Id = id + ".atk", Kind = AttackKind.EnemyMelee, TestDamage = d, Startup = 0.3f, Active = 0.1f, Recovery = 0.3f,
                HitboxOffsetX = 0.8f, HitboxOffsetY = 0.9f, HitboxWidth = 1.2f, HitboxHeight = 1.2f, Knockback = 4f,
                Unblockable = unblockable, GuardBreak = guardBreak, SuperArmor = armor,
            };
            e.AttackRange = 1.6f;
            e.AttackInterval = 1.0f;
            e.FirstAttackDelay = 0.2f;
            return e;
        }

        public static List<EnemyDefinition> Defs(params EnemyDefinition[] d) => new List<EnemyDefinition>(d);

        /// <summary>Fixture world T1 with enemies (instance id, definition id, x).</summary>
        public static WorldData World(params (string id, string def, float x)[] enemies)
        {
            var w = Fixtures.World();
            foreach (var (id, def, x) in enemies)
                w.Maps[0].Enemies.Add(new EnemySpawn { InstanceId = id, EnemyId = def, Position = new Vec2(x, 0), Facing = -1 });
            return w;
        }

        public static SimHarness Harness(string tc, WorldData world, List<EnemyDefinition> defs, CombatProfile profile = null,
            CombatUnlocks cu = null, AirUnlocks air = null) =>
            new SimHarness(tc, air ?? Fixtures.Unlocks(true, true, true), world, null, profile ?? Profile(), cu ?? Unlocks(), defs);

        /// <summary>Press attack and step until that attack has ended (and the player is free again).</summary>
        public static SimHarness Hit(this SimHarness h, LogicalAction a = LogicalAction.Attack, int limit = 60)
        {
            h.Press(a).Step();
            return h.StepUntil(() => h.C.Attack == null, limit);
        }
    }
}
