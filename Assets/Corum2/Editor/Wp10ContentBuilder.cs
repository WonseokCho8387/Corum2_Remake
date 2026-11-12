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
    /// WP-10 test content: integration scenarios TC-INT-01, 03, 04, 05, 06 (master 13.6) on one map. Reuses the WP-02 player data
    /// and targets and the WP-04 AI enemies and bosses unchanged; only the placement is new.
    /// </summary>
    public static class Wp10ContentBuilder
    {
        const string Dir = "Assets/Corum2/Data/WP10";
        const string Wp02 = "Assets/Corum2/Data/WP02";
        const string Wp01 = "Assets/Corum2/Data/WP01";
        const string Wp04 = "Assets/Corum2/Data/WP04";
        public const string ScenePath = "Assets/Corum2/Scenes/WP10_Integration.unity";
        const string Version = "wp10-1";

        [MenuItem("Corum2/WP-10/Build Test Content")]
        public static void Build()
        {
            Directory.CreateDirectory(Dir);
            // TC-INT-04 magic case: a warden copy whose super-armor phase (p2) attacks at half speed, so a 0.2 s cast can land inside
            // its crush startup. Same rules and armor; only the phase attack speed is test data.
            var slowPath = Dir + "/Enemy_WardenSlowP2.asset";
            if (AssetDatabase.LoadAssetAtPath<EnemyDefinitionAsset>(slowPath) == null) AssetDatabase.CopyAsset(Wp04 + "/Enemy_Warden.asset", slowPath);
            var slow = AssetDatabase.LoadAssetAtPath<EnemyDefinitionAsset>(slowPath);
            slow.meta.domainId = "enemy.wardenSlowP2";
            slow.meta.dataVersion = Version;
            slow.enemyId = "wardenSlowP2";
            slow.boss.phases[1].attackSpeed = 0.5f;
            EditorUtility.SetDirty(slow);
            // R2 TC-INT-04 weak magic (GPT WP-10 review §2): the same slow-p2 warden with a weak but valid magic knockback scale (0.1).
            var weakPath = Dir + "/Enemy_WardenWeakP2.asset";
            if (AssetDatabase.LoadAssetAtPath<EnemyDefinitionAsset>(weakPath) == null) AssetDatabase.CopyAsset(slowPath, weakPath);
            var weak = AssetDatabase.LoadAssetAtPath<EnemyDefinitionAsset>(weakPath);
            weak.meta.domainId = "enemy.wardenWeakP2";
            weak.meta.dataVersion = Version;
            weak.enemyId = "wardenWeakP2";
            weak.boss.phases[1].attackSpeed = 0.5f;
            weak.magicKnockbackScale = 0.1f;
            EditorUtility.SetDirty(weak);
            var map = AssetDatabase.LoadAssetAtPath<MapDefinition>(Dir + "/Map_C10_Integration.asset") ?? Save(ScriptableObject.CreateInstance<MapDefinition>(), "Map_C10_Integration");
            map.meta.domainId = "map.C10";
            map.meta.dataVersion = Version;
            map.meta.relatedRIds = new[] { "R-04-01", "R-04-04", "R-04-10", "R-06-01", "R-03-07", "R-03-09", "R-04-07", "R-05-03", "R-06-11", "R-06-12", "R-06-15",
                "R-08-12", "R-08-01", "R-08-02", "R-06-14", "R-07-03", "R-08-09", "R-08-10", "R-08-11" };
            map.mapId = "C10";
            map.minX = 0;
            map.maxX = 300;
            map.cameraMinX = 9;
            map.cameraMaxX = 291;
            map.cameraY = 2.5f;
            map.cameraFollowSharpness = 10f;
            map.groundColor = new Color(0.4f, 0.38f, 0.36f);
            map.solids = new List<BoxData> { new BoxData { min = new Vector2(-1, -2), max = new Vector2(301, 0) } };
            map.spawns = new List<SpawnData> { Sp("Start", 4) };
            map.enemies = new List<EnemySpawnData>();
            void Z(string id, float x, params (string inst, string def, float dx)[] es)
            {
                map.spawns.Add(Sp(id, x));
                map.spawns.Add(Sp(id + "Far", x - 6f));
                foreach (var e in es) map.enemies.Add(En(e.inst, e.def, x + e.dx));
            }
            // TC-INT-01: 10 targets at one point: 5 bags, 3 weak (die during the chain), 2 launch-immune bosses
            Z("I1Mix", 12, ("m0", "bag", 1f), ("m1", "bag", 1f), ("m2", "bag", 1f), ("m3", "bag", 1f), ("m4", "bag", 1f),
                ("w0", "weak", 1f), ("w1", "weak", 1f), ("w2", "weak", 1f), ("i0", "boss", 1f), ("i1", "boss", 1f));
            Z("I3Air", 45, ("a0", "bag", 3.5f));                                      // TC-INT-03 (landing attack target)
            // TC-INT-04 boss phase super armor (p2): one warden per case (p2 moves on to p3 after its pattern is used)
            Z("I4Armor", 80, ("wb", "warden", 1.6f));
            Z("I4ArmorB", 97, ("wb3", "wardenSlowP2", 1.6f));
            map.spawns.Add(Sp("I4ArmorBCast", 96f)); // 2.6 m from wb3: just outside crush range (2.2); the boss walks in while the cast runs
            Z("I4ArmorC", 114, ("wb4", "warden", 1.6f));
            Z("I5Mixed", 140, ("ch", "chaser", 2.5f), ("fl", "fleer", -2.5f), ("jp", "jumper", 4f), ("rg", "ranged", -5f)); // TC-INT-05
            Z("I6Bosses", 190, ("wb2", "warden", 4f), ("sn", "sentinel", -4f));     // TC-INT-06 two bosses, separate transitions
            map.spawns.Add(Sp("I6SnRange", 179f)); // R2 TC-INT-06: 7 m left of the sentinel's start (its s2 pattern needs 4..9 m)
            Z("I4ArmorD", 260, ("wb5", "wardenWeakP2", 1.6f));                     // R2 TC-INT-04 weak magic on the p2 super armor
            map.spawns.Add(Sp("I4ArmorDCast", 259f));
            map.left = new EdgeData { policy = EdgePolicy.Blocked };
            map.right = new EdgeData { policy = EdgePolicy.Blocked };
            map.points = new List<PointData>();

            var world = Save(ScriptableObject.CreateInstance<WorldDefinition>(), "World_WP10");
            world.maps = new List<MapDefinition> { map };
            world.startMapId = "C10";
            world.startSpawnId = "Start";
            foreach (var o in new Object[] { map, world }) EditorUtility.SetDirty(o);
            AssetDatabase.SaveAssets();

            var move = AssetDatabase.LoadAssetAtPath<MovementConfig>(Wp01 + "/MovementConfig.asset");
            var input = AssetDatabase.LoadAssetAtPath<InputProfileConfig>(Wp01 + "/InputProfile.asset");
            var air = AssetDatabase.LoadAssetAtPath<AirUnlockFixture>(Wp01 + "/AirUnlocks_All.asset");
            var combat = AssetDatabase.LoadAssetAtPath<CombatConfig>(Wp02 + "/CombatConfig.asset");
            var cu = AssetDatabase.LoadAssetAtPath<CombatUnlockFixture>(Wp02 + "/CombatUnlocks_None.asset");
            var enemies = new[] { "Enemy_Bag", "Enemy_Weak", "Enemy_BossImmune" }.Select(n => AssetDatabase.LoadAssetAtPath<EnemyDefinitionAsset>(Wp02 + "/" + n + ".asset")).ToList();
            enemies.AddRange(new[] { "Enemy_Chaser", "Enemy_Fleer", "Enemy_Jumper", "Enemy_Ranged", "Enemy_Warden", "Enemy_Sentinel" }
                .Select(n => AssetDatabase.LoadAssetAtPath<EnemyDefinitionAsset>(Wp04 + "/" + n + ".asset")));
            enemies.Add(slow);
            enemies.Add(weak);
            var issues = DataValidator.Validate(world.ToCore(), move.ToCore(), input.ToCore(), air.ToCore());
            issues.AddRange(DataValidator.ValidateCombat(combat.ToCore(), cu.ToCore(), enemies.Select(e => e.ToCore()).ToList(), world.ToCore()));
            foreach (var i in issues) Debug.LogError("[Corum2] " + i);
            BuildScene(world, move, input, air, combat, cu, enemies);
            Debug.Log("[Corum2] WP-10 content built. Validation issues: " + issues.Count);
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
            AssetDatabase.SaveAssets();
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
