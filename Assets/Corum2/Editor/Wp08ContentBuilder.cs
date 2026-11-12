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
    /// WP-08 test content: P0-06 TC-06-11..18 and P0-07 guard / parry TC-07-01..07 (+ TC-OPS-02). Reuses the WP-02 player data
    /// and enemies; adds copies of the timed hitter with one attack property changed (D = 9 / 50, unblockable, guard break,
    /// startup 0.6 / 0.8 s for the roll and air-guard timing). All numbers are test data (EXP-ATK-00, TEST-DMG-01).
    /// </summary>
    public static class Wp08ContentBuilder
    {
        const string Dir = "Assets/Corum2/Data/WP08";
        const string Wp02 = "Assets/Corum2/Data/WP02";
        const string Wp01 = "Assets/Corum2/Data/WP01";
        const string Wp04 = "Assets/Corum2/Data/WP04";
        public const string ScenePath = "Assets/Corum2/Scenes/WP08_Defense.unity";
        const string Version = "wp08-1";

        [MenuItem("Corum2/WP-08/Build Test Content")]
        public static void Build()
        {
            Directory.CreateDirectory(Dir);
            var v = new List<EnemyDefinitionAsset>
            {
                Hitter("Enemy_Hit9", "hit9", a => a.testDamage = 9f),
                Hitter("Enemy_Hit50", "hit50", a => a.testDamage = 50f),
                Hitter("Enemy_Unblockable", "unbl", a => a.unblockable = true),
                Hitter("Enemy_GuardBreak", "brk", a => a.guardBreak = true),
                Hitter("Enemy_Slow6", "slow6", a => a.startup = 0.6f),
                Hitter("Enemy_Slow8", "slow8", a => a.startup = 0.8f),
                Hitter("Enemy_ArmorSlow", "armorSlow", a => a.startup = 0.6f, "Enemy_Armor"), // super armor startup long enough to land magic first (TC-06-12)
            };
            // R1 (WP08-F01): the same armored slow attacker with a weak positive magic knockback scale (fireball 5 -> 0.5 m/s)
            var armorWeak = Hitter("Enemy_ArmorSlowWeakMagic", "armorWeak", a => a.startup = 0.6f, "Enemy_Armor");
            armorWeak.magicKnockbackScale = 0.1f;
            v.Add(armorWeak);

            var map = AssetDatabase.LoadAssetAtPath<MapDefinition>(Dir + "/Map_C08_Defense.asset") ?? Save(ScriptableObject.CreateInstance<MapDefinition>(), "Map_C08_Defense");
            map.meta.domainId = "map.C08";
            map.meta.dataVersion = Version;
            map.meta.relatedRIds = new[] { "R-06-11", "R-06-12", "R-06-13", "R-06-14", "R-06-15", "R-06-16", "R-06-17", "R-06-18", "R-07-01", "R-07-02", "R-07-03", "R-07-04", "R-07-05", "R-07-06", "R-07-07" };
            map.mapId = "C08";
            map.minX = 0;
            map.maxX = 760;
            map.cameraMinX = 9;
            map.cameraMaxX = 751;
            map.cameraY = 2.5f;
            map.cameraFollowSharpness = 10f;
            map.groundColor = new Color(0.36f, 0.4f, 0.38f);
            map.solids = new List<BoxData> { new BoxData { min = new Vector2(-1, -2), max = new Vector2(761, 0) } };
            map.spawns = new List<SpawnData> { Sp("Start", 4) };
            map.enemies = new List<EnemySpawnData>();
            void Z(string id, float x, params (string inst, string def, float dx)[] es)
            {
                map.spawns.Add(Sp(id, x));
                map.spawns.Add(Sp(id + "Far", x - 5f)); // out of every attack range: the next in-range spawn starts the attack on the next tick
                foreach (var e in es) map.enemies.Add(En(e.inst, e.def, x + e.dx));
            }
            Z("Z1Armor", 10, ("a1", "armor", 1.2f));                                   // TC-06-11
            Z("Z2ArmorMagic", 35, ("a2", "armorSlow", 1.2f));                              // TC-06-12
            Z("Z3Roll", 60, ("r1", "slow6", 1.2f));                                    // TC-06-13
            map.spawns.Add(Sp("Z2Near", 35.5f)); // R1: a2 is pushed 0.47 m by the magic (knockback fix); the armor-again segment starts closer
            map.spawns.Add(Sp("Z3Ra", 59.9f));
            map.spawns.Add(Sp("Z3Rb", 59.0f));
            map.spawns.Add(Sp("Z3Rc", 58.0f));
            Z("Z4Multi", 90, ("x1", "hitter", 1.2f), ("x2", "hitter", 1.2f), ("x3", "hitter", -1.2f)); // TC-06-14
            Z("Z5Dodge", 120, ("dd1", "hitter", 1.2f));                                // TC-06-15
            Z("Z6DodgeMagic", 145, ("dm1", "hitter", 1.2f));                           // TC-06-16 magic vs normal
            Z("Z7DodgeArmor", 170, ("da1", "armor", 1.2f));                            // TC-06-16 normal vs super armor
            Z("Z8Death", 195, ("w1", "weak", 1.2f));                                   // TC-06-17
            Z("Z9Link", 220, ("c1", "weak", 1.0f), ("c2", "bag", 1.0f), ("c3", "bag", 1.0f)); // TC-06-18
            Z("Z10Front", 250, ("fl", "slow8", -1.2f), ("fr", "hitter", 1.2f));       // TC-07-01: front hits first, rear later
            Z("Z10Back", 266, ("bl", "hitter", -1.2f));                                // TC-07-01: rear attacker, then turn to it
            Z("Z11D9", 280, ("g9", "hit9", 1.2f));                                     // TC-07-02 / TC-OPS-02
            Z("Z12D10", 305, ("g10", "hitter", 1.2f));
            Z("Z13D50", 330, ("g50", "hit50", 1.2f));
            Z("Z14Cont", 355, ("cg", "hitter", 1.2f));                                 // TC-07-03 consecutive guard
            Z("Z15AirGuard", 380, ("ag", "slow8", 1.2f));                              // TC-07-03 air guard
            Z("Z16AirParry", 405, ("ap", "slow8", 1.2f));                              // TC-07-03 air parry
            Z("Z17Parry", 430, ("pp", "hitter", 1.2f));                                // TC-07-05
            Z("Z18Unbl", 455, ("tu", "unbl", 1.2f));                                   // TC-07-06
            Z("Z19Break", 480, ("tb", "brk", 1.2f));
            Z("Z20Normal", 505, ("tn", "hitter", 1.2f));
            Z("Z21Cancel", 530, ("kb", "bag", 1.2f));                                  // TC-07-07 allowed cancels
            Z("Z22Stun", 555, ("hs", "hitter", 1.2f));                                 // TC-07-07 RB during hitstun
            for (var i = 0; i < 4; i++) Z("Z23Window" + i, 580 + 10 * i, ("pw" + i, "hitter", 1.2f)); // TC-07-04: one enemy per offset (a parry pushes its enemy away)
            Z("Z24ArmorWeak", 650, ("aw", "armorWeak", 1.2f));                        // R1 TC-06-12 weak magic on super armor
            Z("Z25LethalGuard", 675, ("lg", "hitter", 1.2f));                          // R1 WP08-F02 lethal front guard damage
            Z("Z26LethalBreak", 700, ("lk", "brk", 1.2f));                             // R1 WP08-F02 lethal guard-break damage
            Z("Z27LethalParry", 725, ("lp", "hitter", 1.2f));                          // R1 WP08-F02 lethal parry damage (EXP-PARRY-02)
            map.left = new EdgeData { policy = EdgePolicy.Blocked };
            map.right = new EdgeData { policy = EdgePolicy.Blocked };
            map.points = new List<PointData>();

            var world = Save(ScriptableObject.CreateInstance<WorldDefinition>(), "World_WP08");
            world.maps = new List<MapDefinition> { map };
            world.startMapId = "C08";
            world.startSpawnId = "Start";
            foreach (var o in new Object[] { map, world }.Concat(v)) EditorUtility.SetDirty(o);
            AssetDatabase.SaveAssets();

            var move = AssetDatabase.LoadAssetAtPath<MovementConfig>(Wp01 + "/MovementConfig.asset");
            var input = AssetDatabase.LoadAssetAtPath<InputProfileConfig>(Wp01 + "/InputProfile.asset");
            var air = AssetDatabase.LoadAssetAtPath<AirUnlockFixture>(Wp01 + "/AirUnlocks_All.asset");
            var combat = AssetDatabase.LoadAssetAtPath<CombatConfig>(Wp02 + "/CombatConfig.asset");
            var cu = AssetDatabase.LoadAssetAtPath<CombatUnlockFixture>(Wp02 + "/CombatUnlocks_None.asset");
            var enemies = new[] { "Enemy_Bag", "Enemy_Hitter", "Enemy_Armor", "Enemy_Weak" }.Select(n => AssetDatabase.LoadAssetAtPath<EnemyDefinitionAsset>(Wp02 + "/" + n + ".asset")).ToList();
            enemies.AddRange(v);
            var issues = DataValidator.Validate(world.ToCore(), move.ToCore(), input.ToCore(), air.ToCore());
            issues.AddRange(DataValidator.ValidateCombat(combat.ToCore(), cu.ToCore(), enemies.Select(e => e.ToCore()).ToList(), world.ToCore()));
            foreach (var i in issues) Debug.LogError("[Corum2] " + i);
            BuildScene(world, move, input, air, combat, cu, enemies);
            Debug.Log("[Corum2] WP-08 content built. Validation issues: " + issues.Count);
        }

        /// <summary>A copy of the WP-02 timed hitter with one attack property changed (test data).</summary>
        static EnemyDefinitionAsset Hitter(string asset, string id, System.Action<AttackData> change, string src = "Enemy_Hitter")
        {
            var path = Dir + "/" + asset + ".asset";
            if (AssetDatabase.LoadAssetAtPath<EnemyDefinitionAsset>(path) == null) AssetDatabase.CopyAsset(Wp02 + "/" + src + ".asset", path);
            var e = AssetDatabase.LoadAssetAtPath<EnemyDefinitionAsset>(path);
            e.meta.domainId = "enemy." + id;
            e.meta.dataVersion = Version;
            e.meta.policyIds = new[] { "EXP-ATK-00", "TEST-DMG-01" };
            e.meta.relatedRIds = new[] { "R-06-13", "R-07-01", "R-07-02", "R-07-03", "R-07-04", "R-07-05", "R-07-06" };
            e.enemyId = id;
            e.attack.id = id + ".attack";
            change(e.attack);
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
