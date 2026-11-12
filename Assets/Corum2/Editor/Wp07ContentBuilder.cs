using System.Collections.Generic;
using System.IO;
using System.Linq;
using Corum2.Core;
using Corum2.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Corum2.EditorTools
{
    /// <summary>
    /// WP-07 test content: P0-06 hit / reaction TCs (TC-06-01..10). Reuses the WP-02 enemies and adds size classes
    /// (medium, large) for the per-enemy stagger / knockback / launch settings. The WP-07 player combat data is a copy of the
    /// WP-02 data whose fourth ground attack is an explicit 3-event test attack (TEST-MULTI-01, R-06-02; not magic content).
    /// All numbers are test data (EXP-ATK-00, TEST-DMG-01).
    /// </summary>
    public static class Wp07ContentBuilder
    {
        const string Dir = "Assets/Corum2/Data/WP07";
        const string Wp02 = "Assets/Corum2/Data/WP02";
        const string Wp01 = "Assets/Corum2/Data/WP01";
        const string Wp04 = "Assets/Corum2/Data/WP04";
        public const string ScenePath = "Assets/Corum2/Scenes/WP07_HitReaction.unity";
        const string Version = "wp07-1";

        [MenuItem("Corum2/WP-07/Build Test Content")]
        public static void Build()
        {
            Directory.CreateDirectory(Dir);
            var cPath = Dir + "/CombatConfig_WP07.asset";
            if (AssetDatabase.LoadAssetAtPath<CombatConfig>(cPath) == null) AssetDatabase.CopyAsset(Wp02 + "/CombatConfig.asset", cPath);
            var combat = AssetDatabase.LoadAssetAtPath<CombatConfig>(cPath);
            combat.meta.domainId = "combat.player.wp07";
            combat.meta.dataVersion = Version;
            combat.meta.policyIds = new[] { "EXP-ATK-00", "TEST-DMG-01", "TEST-MULTI-01" };
            var g4 = combat.groundNormals[3];
            g4.hitEvents = 3; // explicit multi-hit test attack (R-06-02)
            g4.active = 0.24f;
            g4.knockback = 0.5f;
            EditorUtility.SetDirty(combat);

            var medium = Enemy("Enemy_Medium", "medium", 1000f, new Color(0.45f, 0.6f, 0.8f), 0.5f, 0.5f, LaunchResponse.Resist, 1.0f, 2.0f);
            var large = Enemy("Enemy_Large", "large", 2000f, new Color(0.3f, 0.45f, 0.6f), 0.8f, 0.2f, LaunchResponse.Resist, 1.3f, 2.4f);
            // R1 (WP07-F01): knockback below / equal to one tick of deceleration (30 m/s^2 * 0.02 s = 0.6 m/s) must still move.
            var magicWeak = Enemy("Enemy_MagicWeak", "magicWeak", 1000f, new Color(0.7f, 0.5f, 0.8f), 0f, 1f, LaunchResponse.Launchable, 0.8f, 1.6f);
            magicWeak.magicKnockbackScale = 0.1f; // fireball knockback 5 -> 0.5 m/s (< 0.6)
            var magicEqual = Enemy("Enemy_MagicEqual", "magicEqual", 1000f, new Color(0.6f, 0.4f, 0.75f), 0f, 1f, LaunchResponse.Launchable, 0.8f, 1.6f);
            magicEqual.magicKnockbackScale = 0.12f; // 5 -> 0.6 m/s (= one tick of deceleration)
            var physEqual = Enemy("Enemy_PhysEqual", "physEqual", 1000f, new Color(0.5f, 0.6f, 0.5f), 0f, 0.4f, LaunchResponse.Launchable, 0.8f, 1.6f); // 1.5 -> 0.6 m/s

            var map = AssetDatabase.LoadAssetAtPath<MapDefinition>(Dir + "/Map_C07_Hit.asset") ?? Save(ScriptableObject.CreateInstance<MapDefinition>(), "Map_C07_Hit");
            map.meta.domainId = "map.C07";
            map.meta.dataVersion = Version;
            map.meta.relatedRIds = Enumerable.Range(1, 10).Select(i => "R-06-" + i.ToString("00")).ToArray();
            map.mapId = "C07";
            map.minX = 0;
            map.maxX = 520;
            map.cameraMinX = 9;
            map.cameraMaxX = 511;
            map.cameraY = 2.5f;
            map.cameraFollowSharpness = 10f;
            map.groundColor = new Color(0.4f, 0.38f, 0.42f);
            map.solids = new List<BoxData> { new BoxData { min = new Vector2(-1, -2), max = new Vector2(521, 0) } };
            map.spawns = new List<SpawnData>
            {
                Sp("Start", 4), Sp("Z1Overlap", 10), Sp("Z2Three", 40), Sp("Z3Ten", 65),
                Sp("K1Small", 90), Sp("K2Medium", 115), Sp("K3Large", 140), Sp("K4Boss", 165),
                Sp("L1Able", 190), Sp("L2Resist", 215), Sp("L3Immune", 240), Sp("L4Stop", 265),
                Sp("D1Down", 290), Sp("D1Right", 292.2f), Sp("D2Dive", 315), Sp("W1Wake", 340), Sp("W2Magic", 360),
                Sp("K5MagicWeak", 390), Sp("K6MagicEqual", 415), Sp("K7PhysEqual", 440), Sp("W3WakeStart", 465), Sp("W4WakeEnd", 490),
            };
            map.left = new EdgeData { policy = EdgePolicy.Blocked };
            map.right = new EdgeData { policy = EdgePolicy.Blocked };
            map.points = new List<PointData>();
            map.enemies = new List<EnemySpawnData>
            {
                En("o1", "bag", 11f),                                                   // TC-06-01
                En("m1", "bag", 41f), En("m2", "bag", 41f), En("m3", "bag", 41f),       // TC-06-02
            };
            for (var i = 0; i < 10; i++) map.enemies.Add(En("t" + i, "bag", 66f)); // TC-06-03 (1 vs 10 targets)
            map.enemies.Add(En("k1", "bag", 91f));       // TC-06-04/05/06 small
            map.enemies.Add(En("k2", "medium", 116.1f)); // medium
            map.enemies.Add(En("k3", "large", 141.25f)); // large
            map.enemies.Add(En("k4", "boss", 166.3f));   // boss (launch immune, stagger 1, physical knockback 0)
            map.enemies.Add(En("l1", "bag", 191f));      // TC-06-07/08 launchable
            map.enemies.Add(En("l2", "medium", 216.1f)); // TC-06-08 resist
            map.enemies.Add(En("l3", "boss", 241.3f));   // TC-06-08 immune
            map.enemies.Add(En("l4", "bag", 266f));      // TC-06-07 air string interrupted
            map.enemies.Add(En("d1", "bag", 291f));      // TC-06-09 down attack
            map.enemies.Add(En("d2", "bag", 316f));      // TC-06-09 dive on a downed enemy
            map.enemies.Add(En("w1", "bag", 341f));      // TC-06-10 wakeup (physical)
            map.enemies.Add(En("w2", "bag", 361f));      // TC-06-10 wakeup (magic)
            map.enemies.Add(En("mw", "magicWeak", 391f));  // R1 TC-06-06 weak positive magic knockback (0.5 m/s)
            map.enemies.Add(En("me", "magicEqual", 416f)); // R1 TC-06-06 magic knockback equal to one tick of deceleration
            map.enemies.Add(En("pe", "physEqual", 441f));  // R1 TC-06-05 physical knockback equal to one tick of deceleration
            map.enemies.Add(En("w3", "bag", 466f));        // R1 TC-06-10 attack overlapping the Down -> Wakeup tick
            map.enemies.Add(En("w4", "bag", 491f));        // R1 TC-06-10 attack overlapping the last Wakeup -> first Idle tick

            var world = Save(ScriptableObject.CreateInstance<WorldDefinition>(), "World_WP07");
            world.maps = new List<MapDefinition> { map };
            world.startMapId = "C07";
            world.startSpawnId = "Start";
            foreach (var o in new Object[] { map, world, medium, large, magicWeak, magicEqual, physEqual }) EditorUtility.SetDirty(o);
            AssetDatabase.SaveAssets();

            var move = AssetDatabase.LoadAssetAtPath<MovementConfig>(Wp01 + "/MovementConfig.asset");
            var input = AssetDatabase.LoadAssetAtPath<InputProfileConfig>(Wp01 + "/InputProfile.asset");
            var air = AssetDatabase.LoadAssetAtPath<AirUnlockFixture>(Wp01 + "/AirUnlocks_All.asset");
            var cu = AssetDatabase.LoadAssetAtPath<CombatUnlockFixture>(Wp02 + "/CombatUnlocks_None.asset");
            var enemies = new[] { "Enemy_Bag", "Enemy_Weak", "Enemy_BossImmune" }.Select(n => AssetDatabase.LoadAssetAtPath<EnemyDefinitionAsset>(Wp02 + "/" + n + ".asset")).ToList();
            enemies.Add(medium);
            enemies.Add(large);
            enemies.Add(magicWeak);
            enemies.Add(magicEqual);
            enemies.Add(physEqual);
            var issues = DataValidator.Validate(world.ToCore(), move.ToCore(), input.ToCore(), air.ToCore());
            issues.AddRange(DataValidator.ValidateCombat(combat.ToCore(), cu.ToCore(), enemies.Select(e => e.ToCore()).ToList(), world.ToCore()));
            foreach (var i in issues) Debug.LogError("[Corum2] " + i);
            BuildScene(world, move, input, air, combat, cu, enemies);
            Debug.Log("[Corum2] WP-07 content built. Validation issues: " + issues.Count);
        }

        static EnemyDefinitionAsset Enemy(string asset, string id, float hp, Color c, float staggerRes, float kbScale, LaunchResponse launch, float w, float h)
        {
            var e = AssetDatabase.LoadAssetAtPath<EnemyDefinitionAsset>(Dir + "/" + asset + ".asset") ?? Save(ScriptableObject.CreateInstance<EnemyDefinitionAsset>(), asset);
            e.meta.domainId = "enemy." + id;
            e.meta.dataVersion = Version;
            e.meta.policyIds = new[] { "EXP-ATK-00", "TEST-DMG-01" };
            e.enemyId = id;
            e.maxHp = hp;
            e.color = c;
            e.staggerResistance = staggerRes;
            e.physicalKnockbackScale = kbScale;
            e.magicKnockbackScale = 1f;
            e.launch = launch;
            e.launchResistScale = 0.5f;
            e.bodyWidth = w;
            e.bodyHeight = h;
            e.hasAttack = false;
            return e;
        }

        static void BuildScene(WorldDefinition world, MovementConfig move, InputProfileConfig input, AirUnlockFixture air, CombatConfig combat,
            CombatUnlockFixture cu, List<EnemyDefinitionAsset> enemies)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>("Assets/Settings/SampleSceneProfile.asset");
            if (profile != null)
            {
                var vol = new GameObject("Global Volume").AddComponent<Volume>();
                vol.isGlobal = true;
                vol.sharedProfile = profile;
            }
            var cam = Camera.main;
            var data = cam != null ? cam.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>() : null;
            if (data != null) data.renderPostProcessing = true;
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
            host.ring = AssetDatabase.LoadAssetAtPath<RingConfig>("Assets/Corum2/Data/WP05/RingConfig_WP05.asset"); // fireball command = test magic
            host.presentation = AssetDatabase.LoadAssetAtPath<PresentationLibrary>(Wp04 + "/PresentationLibrary.asset");
            EditorSceneManager.SaveScene(scene, ScenePath);
            var scenes = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
            scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        static SpawnData Sp(string id, float x) => new SpawnData { id = id, position = new Vector2(x, 0f) };

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
