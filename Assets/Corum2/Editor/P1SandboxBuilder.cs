using System.Collections.Generic;
using System.IO;
using System.Linq;
using Corum2.Core;
using Corum2.Core.P1;
using Corum2.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Corum2.EditorTools
{
    /// <summary>
    /// P1 sandbox (test content, EXP-P1-REGION-01): region R1 = maps P1A + P1B (up point), region R2 = map P1C (right
    /// edge of P1A). Origin-scaled enemies with EXP, Volker's P1 test profile, potions and scrolls in the ring, merchant
    /// safe spawns. Player attacks use the origin base damage 1 so permanent bonuses are visible.
    /// </summary>
    public static class P1SandboxBuilder
    {
        const string Dir = "Assets/Corum2/Data/P1";
        const string Wp01 = "Assets/Corum2/Data/WP01";
        const string Wp02 = "Assets/Corum2/Data/WP02";
        public const string ScenePath = "Assets/Corum2/Scenes/P1_Sandbox.unity";
        const string Version = "p1-sandbox-1";

        [MenuItem("Corum2/P1/Build Sandbox")]
        public static void Build()
        {
            Directory.CreateDirectory(Dir);
            var meta = new MetaFields { domainId = "p1", dataVersion = Version, status = "experimental", policyIds = new[] { "EXP-P1-REGION-01", "TEST-P1-01" } };

            // ---- maps
            var a = Map("Map_P1A", "P1A", 0, 90, new Color(0.38f, 0.42f, 0.36f));
            a.spawns = new List<SpawnData> { Sp("Start", 4), Sp("FromB", 41), Sp("FromC", 88, -1), Sp("Field", 54), Sp("Shop", 28) };
            a.solids.Add(new BoxData { min = new Vector2(70, 0), max = new Vector2(71.5f, 1.4f) }); // obstacle for projectiles
            a.points = new List<PointData> { new PointData { id = "P1A-UP", area = new BoxData { min = new Vector2(40, 0), max = new Vector2(42, 2) }, direction = PointDirection.Up, targetMapId = "P1B", targetSpawnId = "FromA" } };
            a.right = new EdgeData { policy = EdgePolicy.Connected, connectionId = "C-P1A-P1C", targetMapId = "P1C", targetSpawnId = "FromA" };
            a.enemies = new List<EnemySpawnData>
            {
                En("k1", "p1Kobold", 12), En("k2", "p1Kobold", 14), En("t1", "p1Thief", 17), En("rk", "p1RedKobold", 22),
                En("d1", "p1Dummy", 55), En("d2", "p1Dummy", 58), En("d3", "p1Dummy", 62), En("lz", "p1Lizard", 66),
            };
            var b = Map("Map_P1B", "P1B", 0, 30, new Color(0.34f, 0.36f, 0.44f));
            b.spawns = new List<SpawnData> { Sp("FromA", 3) };
            b.points = new List<PointData> { new PointData { id = "P1B-DOWN", area = new BoxData { min = new Vector2(2, 0), max = new Vector2(4, 2) }, direction = PointDirection.Down, targetMapId = "P1A", targetSpawnId = "FromB" } };
            b.enemies = new List<EnemySpawnData> { En("b1", "p1Kobold", 15), En("b2", "p1Kobold", 18) };
            var c = Map("Map_P1C", "P1C", 0, 40, new Color(0.44f, 0.36f, 0.32f));
            c.spawns = new List<SpawnData> { Sp("FromA", 2) };
            c.left = new EdgeData { policy = EdgePolicy.Connected, connectionId = "C-P1C-P1A", targetMapId = "P1A", targetSpawnId = "FromC" };
            c.enemies = new List<EnemySpawnData> { En("c1", "p1Goblin", 20), En("c2", "p1Kobold", 25) };
            var world = Save(ScriptableObject.CreateInstance<WorldDefinition>(), "World_P1");
            world.maps = new List<MapDefinition> { a, b, c };
            world.startMapId = "P1A";
            world.startSpawnId = "Start";

            // ---- enemies (origin samples §4.2: kobold HP5/atk1, thief HP7, red kobold HP15/atk2, lizardman HP27/atk2)
            var enemies = new List<EnemyDefinitionAsset>
            {
                Enemy("p1Kobold", 5, 40, 1, new Color(0.55f, 0.75f, 0.35f)),
                Enemy("p1Thief", 7, 50, 1, new Color(0.5f, 0.5f, 0.6f)),
                Enemy("p1RedKobold", 15, 80, 2, new Color(0.85f, 0.3f, 0.25f)),
                Enemy("p1Lizard", 27, 120, 2, new Color(0.3f, 0.6f, 0.55f)),
                Enemy("p1Goblin", 15, 90, 2, new Color(0.6f, 0.5f, 0.2f)),
                Enemy("p1Dummy", 9999, 0, 0, new Color(0.7f, 0.7f, 0.7f)),
            };

            // ---- player combat: origin base damage 1 (§4.2), P0 timings
            var combatSrc = AssetDatabase.LoadAssetAtPath<CombatConfig>(Wp02 + "/CombatConfig.asset");
            var combat = Save(Object.Instantiate(combatSrc), "CombatConfig_P1");
            combat.meta.domainId = "combat.player.p1";
            combat.meta.dataVersion = Version;
            combat.playerMaxHp = 10f;
            foreach (var g in combat.groundNormals) g.testDamage = 1;
            foreach (var g in combat.airNormals) g.testDamage = 1;
            combat.launcher.testDamage = 1;
            combat.airDash.testDamage = 1;
            combat.dive.testDamage = 2;
            combat.downAttack.testDamage = 1;

            // ---- ring: P1 test items are added by the P1 runtime; the start inventory lists them
            var ring = Save(ScriptableObject.CreateInstance<RingConfig>(), "RingConfig_P1");
            ring.meta = new MetaFields { domainId = "ring.p1", dataVersion = Version, status = "experimental", policyIds = new[] { "EXP-P1-ITEM-01" } };
            ring.commands = new List<CommandEntry>();
            ring.magics = new List<MagicEntry>();
            ring.items = new List<ItemEntry>();
            var profile = P1Defaults.Create();
            ring.startInventory = new List<StackEntry>
            {
                new StackEntry { itemId = "p1test.potion.hp", count = 3 }, new StackEntry { itemId = "p1test.potion.mp", count = 3 },
                new StackEntry { itemId = "p1test.potion.mix", count = 1 }, new StackEntry { itemId = "p1test.revive", count = 1 },
            };
            foreach (var s in profile.Spells) ring.startInventory.Add(new StackEntry { itemId = "p1test.scroll." + P1Defaults.Short(s.Id), count = 2 });
            ring.startQuickslots = new[] { "p1test.potion.hp", "p1test.potion.mp", "p1test.scroll.missile", "p1test.scroll.fire_ball" };

            // ---- P1 profile: master test values + sandbox regions
            var p1 = Save(ScriptableObject.CreateInstance<P1ProfileAsset>(), "P1Profile_Sandbox");
            profile.Regions.Add(new RegionDef { Id = "R1", MapIds = { "P1A", "P1B" }, SafeSpawns = { new SafeSpawnDef { MapId = "P1A", X = 30, Y = 0 } } });
            profile.Regions.Add(new RegionDef { Id = "R2", MapIds = { "P1C" }, SafeSpawns = { new SafeSpawnDef { MapId = "P1C", X = 12, Y = 0 } } });
            // sandbox override so the merchant is on screen at once (default and master value 15 %, MER-02)
            profile.Merchant.BaseChance = 1f;
            p1.profile = profile;

            foreach (var o in new Object[] { a, b, c, world, combat, ring, p1 }.Concat(enemies)) EditorUtility.SetDirty(o);
            AssetDatabase.SaveAssets();

            var move = AssetDatabase.LoadAssetAtPath<MovementConfig>(Wp01 + "/MovementConfig.asset");
            var input = AssetDatabase.LoadAssetAtPath<InputProfileConfig>(Wp01 + "/InputProfile.asset");
            var air = AssetDatabase.LoadAssetAtPath<AirUnlockFixture>(Wp01 + "/AirUnlocks_None.asset");
            var cu = AssetDatabase.LoadAssetAtPath<CombatUnlockFixture>(Wp02 + "/CombatUnlocks_None.asset");
            var issues = DataValidator.Validate(world.ToCore(), move.ToCore(), input.ToCore(), air.ToCore());
            issues.AddRange(DataValidator.ValidateCombat(combat.ToCore(), cu.ToCore(), enemies.Select(e => e.ToCore()).ToList(), world.ToCore()));
            foreach (var i in issues) Debug.LogError("[Corum2] " + i);
            foreach (var e in p1.ToCore().Validate()) Debug.LogError("[Corum2] P1: " + e);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var vp = AssetDatabase.LoadAssetAtPath<VolumeProfile>("Assets/Settings/SampleSceneProfile.asset");
            if (vp != null)
            {
                var vol = new GameObject("Global Volume").AddComponent<Volume>();
                vol.isGlobal = true;
                vol.sharedProfile = vp;
            }
            var light = Object.FindFirstObjectByType<Light>();
            if (light != null) light.transform.rotation = Quaternion.Euler(40f, -30f, 0f);
            var host = new GameObject("GameHost").AddComponent<GameHost>();
            host.world = world;
            host.movement = move;
            host.inputProfile = input;
            host.unlocks = air;
            host.combat = combat;
            host.combatUnlocks = cu;
            host.enemies = enemies;
            host.ring = ring;
            host.p1 = p1;
            host.testCaseId = "P1-SANDBOX";
            EditorSceneManager.SaveScene(scene, ScenePath);
            var scenes = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
            scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            AssetDatabase.SaveAssets();
            Debug.Log("[Corum2] P1 sandbox built. Validation issues: " + issues.Count);
        }

        static MapDefinition Map(string asset, string id, float minX, float maxX, Color ground)
        {
            var m = Save(ScriptableObject.CreateInstance<MapDefinition>(), asset);
            m.meta = new MetaFields { domainId = "map." + id, dataVersion = Version, status = "experimental" };
            m.mapId = id;
            m.minX = minX;
            m.maxX = maxX;
            m.cameraMinX = minX + 9;
            m.cameraMaxX = maxX - 9;
            m.cameraY = 2.5f;
            m.cameraFollowSharpness = 10f;
            m.groundColor = ground;
            m.solids = new List<BoxData> { new BoxData { min = new Vector2(minX - 1, -2), max = new Vector2(maxX + 1, 0) } };
            m.left = new EdgeData { policy = EdgePolicy.Blocked };
            m.right = new EdgeData { policy = EdgePolicy.Blocked };
            m.points = new List<PointData>();
            m.enemies = new List<EnemySpawnData>();
            return m;
        }

        static EnemyDefinitionAsset Enemy(string id, float hp, int exp, float atk, Color color)
        {
            var e = Save(ScriptableObject.CreateInstance<EnemyDefinitionAsset>(), "Enemy_" + id);
            e.meta = new MetaFields { domainId = "enemy." + id, dataVersion = Version, status = "experimental", policyIds = new[] { "TEST-P1-01" } };
            e.enemyId = id;
            e.maxHp = hp;
            e.exp = exp;
            e.color = color;
            e.hasAttack = atk > 0;
            e.attack = new AttackData
            {
                id = id + ".atk", kind = AttackKind.EnemyMelee, damageType = DamageType.Physical, startup = 0.45f, active = 0.1f, recovery = 0.4f,
                offsetX = 0.7f, offsetY = 0.9f, width = 1.0f, height = 1.2f, testDamage = atk, knockback = 3f,
            };
            e.attackRange = 1.3f;
            e.attackInterval = 2.2f;
            e.firstAttackDelay = 1.5f;
            return e;
        }

        static SpawnData Sp(string id, float x, int facing = 1) => new SpawnData { id = id, position = new Vector2(x, 0f), facing = facing };

        static EnemySpawnData En(string id, string def, float x) => new EnemySpawnData { instanceId = id, enemyId = def, position = new Vector2(x, 0f), facing = -1 };

        static T Save<T>(T obj, string name) where T : ScriptableObject
        {
            var path = Dir + "/" + name + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null)
            {
                EditorUtility.CopySerialized(obj, existing);
                existing.name = name;
                Object.DestroyImmediate(obj);
                return existing;
            }
            AssetDatabase.CreateAsset(obj, path);
            return obj;
        }
    }
}
