using System.Collections.Generic;
using System.IO;
using System.Linq;
using Corum2.Core;
using Corum2.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Corum2.EditorTools
{
    /// <summary>WP-02 combat test content and scene. All values are experimental test data (EXP-ATK-00, TEST-DMG-01).</summary>
    public static class Wp02ContentBuilder
    {
        const string Dir = "Assets/Corum2/Data/WP02";
        const string Wp01 = "Assets/Corum2/Data/WP01";
        public const string ScenePath = "Assets/Corum2/Scenes/WP02_Combat.unity";
        const string Version = "wp02-1";

        [MenuItem("Corum2/WP-02/Build Test Content")]
        public static void Build()
        {
            Directory.CreateDirectory(Dir);
            var combat = Save(ScriptableObject.CreateInstance<CombatConfig>(), "CombatConfig");
            combat.meta.dataVersion = Version;
            combat.groundNormals = Enumerable.Range(1, 4).Select(i => Atk("ground" + i, AttackKind.GroundNormal, 10)).ToArray();
            combat.launcher = Atk("launcher", AttackKind.Launcher, 12, hitstop: 0.08f, kb: 0f);
            combat.launcher.launchVelocity = 14f;
            combat.airNormals = Enumerable.Range(1, 4).Select(i => Atk("air" + i, AttackKind.AirNormal, 8, 0.7f, 0.9f, 1.2f, 1.8f)).ToArray();
            combat.airDash = Atk("airDash", AttackKind.AirDash, 12, 0.4f, 0.9f, 1.2f, 1.8f, 0.06f, 3f);
            combat.dive = Atk("dive", AttackKind.Dive, 20, 0f, 0.6f, 1f, 1.4f, 0.12f, 2f);
            combat.dive.hitsDowned = true;
            combat.downAttack = Atk("downAttack", AttackKind.DownAttack, 15, 0.9f, 0.3f, 1.4f, 0.8f, 0.06f, 0f);
            combat.downAttack.hitsDowned = true;

            var cuNone = CU("CombatUnlocks_None", false, false);
            CU("CombatUnlocks_All", true, true);

            var bag = Enemy("Enemy_Bag", "bag", 1000f, new Color(0.85f, 0.5f, 0.25f));
            var hitter = Enemy("Enemy_Hitter", "hitter", 1000f, new Color(0.85f, 0.25f, 0.25f));
            Arm(hitter, 10f);
            var armor = Enemy("Enemy_Armor", "armor", 1000f, new Color(0.55f, 0.3f, 0.75f));
            Arm(armor, 15f);
            armor.attack.superArmor = true;
            var boss = Enemy("Enemy_BossImmune", "boss", 3000f, new Color(0.3f, 0.3f, 0.35f));
            boss.launch = LaunchResponse.Immune;
            boss.staggerResistance = 1f;
            boss.physicalKnockbackScale = 0f;
            boss.bodyWidth = 1.4f;
            boss.bodyHeight = 2.6f;
            var weak = Enemy("Enemy_Weak", "weak", 15f, new Color(0.9f, 0.8f, 0.3f));
            var all = new List<EnemyDefinitionAsset> { bag, hitter, armor, boss, weak };

            var map = AssetDatabase.LoadAssetAtPath<MapDefinition>(Dir + "/Map_C01_Arena.asset") ?? Save(ScriptableObject.CreateInstance<MapDefinition>(), "Map_C01_Arena");
            map.meta.domainId = "map.C01";
            map.meta.dataVersion = Version;
            map.mapId = "C01";
            map.minX = 0;
            map.maxX = 70;
            map.cameraMinX = 9;
            map.cameraMaxX = 61;
            map.cameraY = 2.5f;
            map.cameraFollowSharpness = 10f;
            map.groundColor = new Color(0.4f, 0.4f, 0.36f);
            map.solids = new List<BoxData> { new BoxData { min = new Vector2(-1, -2), max = new Vector2(71, 0) } };
            map.spawns = new List<SpawnData>
            {
                Sp("Start", 4), Sp("TestOpenWest", 7), Sp("TestBags", 13), Sp("TestHitter", 28.5f), Sp("TestArmor", 38.5f), Sp("TestBoss", 46.6f), Sp("TestCluster", 55f),
            };
            map.left = new EdgeData { policy = EdgePolicy.Blocked };
            map.right = new EdgeData { policy = EdgePolicy.Blocked };
            map.points = new List<PointData>();
            map.enemies = new List<EnemySpawnData>
            {
                En("bag1", "bag", 14f), En("hitter1", "hitter", 30f), En("armor1", "armor", 39.7f), En("boss1", "boss", 48f),
                En("c1", "weak", 56f), En("c2", "bag", 56f), En("c3", "bag", 56f),
            };

            var world = Save(ScriptableObject.CreateInstance<WorldDefinition>(), "World_WP02");
            world.maps = new List<MapDefinition> { map };
            world.startMapId = "C01";
            world.startSpawnId = "Start";

            foreach (var o in new Object[] { combat, map, world }.Concat(all)) EditorUtility.SetDirty(o);
            AssetDatabase.SaveAssets();

            var move = AssetDatabase.LoadAssetAtPath<MovementConfig>(Wp01 + "/MovementConfig.asset");
            var input = AssetDatabase.LoadAssetAtPath<InputProfileConfig>(Wp01 + "/InputProfile.asset");
            var air = AssetDatabase.LoadAssetAtPath<AirUnlockFixture>(Wp01 + "/AirUnlocks_All.asset");
            var issues = DataValidator.Validate(world.ToCore(), move.ToCore(), input.ToCore(), air.ToCore());
            issues.AddRange(DataValidator.ValidateCombat(combat.ToCore(), cuNone.ToCore(), all.Select(e => e.ToCore()).ToList(), world.ToCore()));
            foreach (var i in issues) Debug.LogError("[Corum2] " + i);

            BuildScene();
            Debug.Log("[Corum2] WP-02 content built. Validation issues: " + issues.Count);
        }

        static void BuildScene()
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
            host.world = AssetDatabase.LoadAssetAtPath<WorldDefinition>(Dir + "/World_WP02.asset");
            host.movement = AssetDatabase.LoadAssetAtPath<MovementConfig>(Wp01 + "/MovementConfig.asset");
            host.inputProfile = AssetDatabase.LoadAssetAtPath<InputProfileConfig>(Wp01 + "/InputProfile.asset");
            host.unlocks = AssetDatabase.LoadAssetAtPath<AirUnlockFixture>(Wp01 + "/AirUnlocks_All.asset");
            host.combat = AssetDatabase.LoadAssetAtPath<CombatConfig>(Dir + "/CombatConfig.asset");
            host.combatUnlocks = AssetDatabase.LoadAssetAtPath<CombatUnlockFixture>(Dir + "/CombatUnlocks_None.asset");
            host.enemies = new[] { "Enemy_Bag", "Enemy_Hitter", "Enemy_Armor", "Enemy_BossImmune", "Enemy_Weak" }
                .Select(n => AssetDatabase.LoadAssetAtPath<EnemyDefinitionAsset>(Dir + "/" + n + ".asset")).ToList();
            EditorSceneManager.SaveScene(scene, ScenePath);
            var scenes = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
            scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        static AttackData Atk(string id, AttackKind k, float d, float ox = 0.8f, float oy = 0.9f, float w = 1f, float h = 1.2f, float hitstop = 0.04f, float kb = 1.5f) =>
            new AttackData { id = id, kind = k, testDamage = d, offsetX = ox, offsetY = oy, width = w, height = h, hitstop = hitstop, knockback = kb, startup = 0.06f, active = 0.08f, recovery = 0.12f };

        static void Arm(EnemyDefinitionAsset e, float d)
        {
            e.hasAttack = true;
            e.attack = new AttackData
            {
                id = e.enemyId + ".attack", kind = AttackKind.EnemyMelee, testDamage = d, startup = 0.3f, active = 0.1f, recovery = 0.3f,
                offsetX = 0.8f, offsetY = 0.9f, width = 1.2f, height = 1.2f, knockback = 4f,
            };
            e.attackRange = 1.6f;
            e.attackInterval = 1.0f;
            e.firstAttackDelay = 0.2f;
        }

        static EnemyDefinitionAsset Enemy(string asset, string id, float hp, Color c)
        {
            var e = Save(ScriptableObject.CreateInstance<EnemyDefinitionAsset>(), asset);
            e.meta.domainId = "enemy." + id;
            e.meta.dataVersion = Version;
            e.enemyId = id;
            e.maxHp = hp;
            e.color = c;
            return e;
        }

        static CombatUnlockFixture CU(string name, bool pierce, bool area)
        {
            var u = Save(ScriptableObject.CreateInstance<CombatUnlockFixture>(), name);
            u.meta.domainId = "combat." + name;
            u.meta.dataVersion = Version;
            u.dashPierce = pierce;
            u.diveArea = area;
            EditorUtility.SetDirty(u);
            return u;
        }

        static SpawnData Sp(string id, float x) => new SpawnData { id = id, position = new Vector2(x, 0f), facing = 1 };
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
