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
    /// WP-06 test content: P0-04 / P0-05 residual TCs (TC-04-02..09, TC-05-01/03/06) on one map with zones 30 m apart.
    /// Reuses the WP-02 combat data and enemies. The guard boss is a copy of the WP-04 sentinel with guard frequency 1
    /// (test data, EXP-BOSS-05) so every eligible attack meets a guard (TC-04-03, EXP-COMBO-05).
    /// </summary>
    public static class Wp06ContentBuilder
    {
        const string Dir = "Assets/Corum2/Data/WP06";
        const string Wp02 = "Assets/Corum2/Data/WP02";
        const string Wp01 = "Assets/Corum2/Data/WP01";
        const string Wp04 = "Assets/Corum2/Data/WP04";
        public const string ScenePath = "Assets/Corum2/Scenes/WP06_ComboState.unity";
        const string Version = "wp06-1";

        [MenuItem("Corum2/WP-06/Build Test Content")]
        public static void Build()
        {
            Directory.CreateDirectory(Dir);
            var gPath = Dir + "/Enemy_Guardian.asset";
            if (AssetDatabase.LoadAssetAtPath<EnemyDefinitionAsset>(gPath) == null)
                AssetDatabase.CopyAsset(Wp04 + "/Enemy_Sentinel.asset", gPath);
            var guardian = AssetDatabase.LoadAssetAtPath<EnemyDefinitionAsset>(gPath);
            guardian.meta.domainId = "enemy.guardian";
            guardian.meta.dataVersion = Version;
            guardian.meta.policyIds = new[] { "EXP-BOSS-05", "EXP-BOSS-06", "EXP-COMBO-05", "TEST-DMG-01" };
            guardian.meta.relatedRIds = new[] { "R-04-03", "R-08-11" };
            guardian.enemyId = "guardian";
            guardian.maxHp = 3000f;
            guardian.color = new Color(0.3f, 0.55f, 0.45f);
            var p1 = guardian.boss.phases[0];
            p1.guardFrequency = 1f;
            p1.guardTime = 0.6f;
            p1.evadeFrequency = 0f;
            p1.transitions = new List<PhaseTransitionData>(); // stays in its guarding phase
            EditorUtility.SetDirty(guardian);

            // R1 (WP06-F01): the same guardian without guarding, for the guard 1 / 0 comparison of a pierce dash.
            var g0Path = Dir + "/Enemy_GuardianNoGuard.asset";
            if (AssetDatabase.LoadAssetAtPath<EnemyDefinitionAsset>(g0Path) == null) AssetDatabase.CopyAsset(gPath, g0Path);
            var guardian0 = AssetDatabase.LoadAssetAtPath<EnemyDefinitionAsset>(g0Path);
            guardian0.meta.domainId = "enemy.guardianNoGuard";
            guardian0.meta.dataVersion = Version;
            guardian0.meta.policyIds = new[] { "EXP-BOSS-05", "EXP-COMBO-06", "TEST-DMG-01" };
            guardian0.enemyId = "guardianNoGuard";
            guardian0.boss.phases[0].guardFrequency = 0f;
            guardian0.boss.phases[0].transitions = new List<PhaseTransitionData>();
            EditorUtility.SetDirty(guardian0);

            var map = AssetDatabase.LoadAssetAtPath<MapDefinition>(Dir + "/Map_C06_Sweep.asset") ?? Save(ScriptableObject.CreateInstance<MapDefinition>(), "Map_C06_Sweep");
            map.meta.domainId = "map.C06";
            map.meta.dataVersion = Version;
            map.meta.relatedRIds = new[] { "R-04-02", "R-04-03", "R-04-04", "R-04-06", "R-04-07", "R-04-08", "R-05-01", "R-05-03", "R-05-06" };
            map.mapId = "C06";
            map.minX = 0;
            map.maxX = 320;
            map.cameraMinX = 9;
            map.cameraMaxX = 311;
            map.cameraY = 2.5f;
            map.cameraFollowSharpness = 10f;
            map.groundColor = new Color(0.38f, 0.4f, 0.42f);
            map.solids = new List<BoxData> { new BoxData { min = new Vector2(-1, -2), max = new Vector2(321, 0) } };
            map.spawns = new List<SpawnData>
            {
                Sp("Start", 4), Sp("Z1Ten", 12.0f), Sp("Z2Launch", 42f), Sp("Z3Pierce", 74f), Sp("Z4Dive", 102.6f),
                Sp("Z5Guard", 140f), Sp("Z6Dash", 170f), Sp("Z7Lock", 200f),
                Sp("Z8GuardPierce", 230f), Sp("Z9NoGuardPierce", 260f), Sp("Z10Five", 290f),
            };
            map.left = new EdgeData { policy = EdgePolicy.Blocked };
            map.right = new EdgeData { policy = EdgePolicy.Blocked };
            map.points = new List<PointData>();
            map.enemies = new List<EnemySpawnData>();
            for (var i = 0; i < 10; i++) map.enemies.Add(En("t" + i, "bag", 13f)); // TC-04-04: 10 targets at one point
            map.enemies.Add(En("la", "bag", 43f)); // TC-04-02: launched (right)
            map.enemies.Add(En("lb", "bag", 41f)); // TC-04-02: the other enemy on the ground (left)
            map.enemies.Add(En("p1", "bag", 75.4f)); // TC-04-06 pierce line
            map.enemies.Add(En("p2", "bag", 76.2f));
            map.enemies.Add(En("p3", "bag", 77.0f));
            map.enemies.Add(En("d1", "bag", 103.4f)); // TC-04-08 cluster (closest first: U-05 / EXP-ATK-05)
            map.enemies.Add(En("d2", "bag", 104.0f));
            map.enemies.Add(En("d3", "bag", 104.6f));
            map.enemies.Add(En("g1", "guardian", 141.2f)); // TC-04-03 guard cancel
            map.enemies.Add(En("k1", "bag", 171.3f)); // TC-04-07 / TC-05-03 / TC-05-06
            map.enemies.Add(En("m1", "bag", 201.2f)); // TC-05-01 attack motion lock
            map.enemies.Add(En("gp", "guardian", 231.5f)); // R1 WP06-F01: guarding boss first on the pierce line
            map.enemies.Add(En("gq", "bag", 233.5f)); //   then an ordinary target reached on a later tick
            map.enemies.Add(En("np", "guardianNoGuard", 261.5f)); // R1 control: same layout, guard frequency 0
            map.enemies.Add(En("nq", "bag", 263.5f));
            for (var i = 0; i < 5; i++) map.enemies.Add(En("f" + i, "bag", 291f)); // R1 TC-04-04: one attack on 5 targets -> +5

            var world = Save(ScriptableObject.CreateInstance<WorldDefinition>(), "World_WP06");
            world.maps = new List<MapDefinition> { map };
            world.startMapId = "C06";
            world.startSpawnId = "Start";
            foreach (var o in new Object[] { map, world }) EditorUtility.SetDirty(o);
            AssetDatabase.SaveAssets();

            var move = AssetDatabase.LoadAssetAtPath<MovementConfig>(Wp01 + "/MovementConfig.asset");
            var input = AssetDatabase.LoadAssetAtPath<InputProfileConfig>(Wp01 + "/InputProfile.asset");
            var air = AssetDatabase.LoadAssetAtPath<AirUnlockFixture>(Wp01 + "/AirUnlocks_All.asset");
            var combat = AssetDatabase.LoadAssetAtPath<CombatConfig>(Wp02 + "/CombatConfig.asset");
            var cu = AssetDatabase.LoadAssetAtPath<CombatUnlockFixture>(Wp02 + "/CombatUnlocks_None.asset");
            var enemies = Enemies(guardian);
            enemies.Add(guardian0);
            var issues = DataValidator.Validate(world.ToCore(), move.ToCore(), input.ToCore(), air.ToCore());
            issues.AddRange(DataValidator.ValidateCombat(combat.ToCore(), cu.ToCore(), enemies.Select(e => e.ToCore()).ToList(), world.ToCore()));
            foreach (var i in issues) Debug.LogError("[Corum2] " + i);
            BuildScene(world, move, input, air, combat, cu, enemies);
            Debug.Log("[Corum2] WP-06 content built. Validation issues: " + issues.Count);
        }

        static List<EnemyDefinitionAsset> Enemies(EnemyDefinitionAsset guardian)
        {
            var l = new[] { "Enemy_Bag", "Enemy_Hitter" }.Select(n => AssetDatabase.LoadAssetAtPath<EnemyDefinitionAsset>(Wp02 + "/" + n + ".asset")).ToList();
            l.Add(guardian);
            return l;
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
            host.ring = AssetDatabase.LoadAssetAtPath<RingConfig>("Assets/Corum2/Data/WP05/RingConfig_WP05.asset");
            host.presentation = AssetDatabase.LoadAssetAtPath<PresentationLibrary>(Wp04 + "/PresentationLibrary.asset"); // the guardian keeps the sentinel cues
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
