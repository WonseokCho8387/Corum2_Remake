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
    /// WP-04 enemy AI / boss test content (TEST-AI-01): one long arena with zones spaced beyond the tracking range so each
    /// zone's enemies wait until the player comes near. All numbers are test data.
    /// </summary>
    public static class Wp04ContentBuilder
    {
        const string Dir = "Assets/Corum2/Data/WP04";
        const string Wp02 = "Assets/Corum2/Data/WP02";
        const string Wp01 = "Assets/Corum2/Data/WP01";
        const string Wp03 = "Assets/Corum2/Data/WP03";
        public const string ScenePath = "Assets/Corum2/Scenes/WP04_AI.unity";
        const string Version = "wp04-1";

        [MenuItem("Corum2/WP-04/Build Test Content")]
        public static void Build()
        {
            Directory.CreateDirectory(Dir);
            var chaser = Enemy("Enemy_Chaser", "chaser", 60f, new Color(0.85f, 0.3f, 0.25f), AiType.Chaser);
            var fleer = Enemy("Enemy_Fleer", "fleer", 60f, new Color(0.3f, 0.7f, 0.85f), AiType.Fleer);
            fleer.ai.fleeSpeed = 4f;
            fleer.ai.fleeTime = 0.9f;
            fleer.ai.reapproachAfterFlee = true;
            var holder = Enemy("Enemy_FleerHold", "fleerHold", 60f, new Color(0.2f, 0.45f, 0.6f), AiType.Fleer);
            holder.ai.fleeSpeed = 4f;
            holder.ai.fleeTime = 0.9f;
            holder.ai.reapproachAfterFlee = false;
            var jumper = Enemy("Enemy_Jumper", "jumper", 60f, new Color(0.4f, 0.8f, 0.3f), AiType.Jumper);
            jumper.ai.jumpMinDistance = 3f;
            jumper.ai.jumpMaxDistance = 5f;
            jumper.ai.jumpSpeedX = 5f;
            jumper.ai.jumpVelocityY = 9f;
            jumper.ai.jumpCooldown = 2.5f;
            jumper.ai.hasAirAttack = true;
            jumper.ai.airAttack = Atk("jumper.air", 8f, 0.06f);
            jumper.ai.airAttackRange = 2.2f;
            var ranged = Enemy("Enemy_Ranged", "ranged", 60f, new Color(0.85f, 0.75f, 0.2f), AiType.Ranged);
            ranged.attack = Atk("ranged.shot", 6f, 0.25f);
            ranged.attackRange = 6f;
            ranged.attackInterval = 1.6f;
            ranged.ai.hasProjectile = true;
            ranged.ai.projectile = new ProjectileData { speed = 9f, maxDistance = 9f };
            ranged.ai.retreatSpeed = 3f;
            ranged.ai.retreatTime = 0.7f;
            var boss = Boss();

            var all = new List<EnemyDefinitionAsset> { chaser, fleer, holder, jumper, ranged, boss };
            var map = AssetDatabase.LoadAssetAtPath<MapDefinition>(Dir + "/Map_C04_AI.asset");
            if (map == null)
            {
                map = ScriptableObject.CreateInstance<MapDefinition>();
                AssetDatabase.CreateAsset(map, Dir + "/Map_C04_AI.asset");
            }
            map.meta.domainId = "map.C04";
            map.meta.dataVersion = Version;
            map.mapId = "C04";
            map.minX = 0;
            map.maxX = 260;
            map.cameraMinX = 9;
            map.cameraMaxX = 251;
            map.cameraY = 2.5f;
            map.cameraFollowSharpness = 10f;
            map.groundColor = new Color(0.38f, 0.4f, 0.36f);
            map.solids = new List<BoxData> { new BoxData { min = new Vector2(-1, -2), max = new Vector2(261, 0) } };
            // Zones 30+ m apart (track range 8, boss 12) so only the zone the player is in reacts.
            map.spawns = new List<SpawnData>
            {
                Sp("Start", 4), Sp("Z1Ten", 12), Sp("Z2Chaser", 49), Sp("Z3Fleer", 79), Sp("Z4Hold", 109), Sp("Z5Jumper", 139),
                Sp("Z6Ranged", 167), Sp("Z6Mid", 173), Sp("Z6In", 175.5f), Sp("Z7Boss", 207), Sp("Z7Near", 214.5f),
            };
            map.left = new EdgeData { policy = EdgePolicy.Blocked };
            map.right = new EdgeData { policy = EdgePolicy.Blocked };
            map.points = new List<PointData>();
            map.enemies = new List<EnemySpawnData>();
            for (var i = 0; i < 10; i++) map.enemies.Add(En("ten" + i, "chaser", 21f));
            map.enemies.Add(En("ch", "chaser", 56f));
            map.enemies.Add(En("fl", "fleer", 85f));
            map.enemies.Add(En("fh", "fleerHold", 115f));
            map.enemies.Add(En("jp", "jumper", 143f));
            map.enemies.Add(En("rg", "ranged", 180f));
            map.enemies.Add(En("boss", "warden", 216f));
            EditorUtility.SetDirty(map);

            var world = AssetDatabase.LoadAssetAtPath<WorldDefinition>(Dir + "/World_WP04.asset");
            if (world == null)
            {
                world = ScriptableObject.CreateInstance<WorldDefinition>();
                AssetDatabase.CreateAsset(world, Dir + "/World_WP04.asset");
            }
            world.maps = new List<MapDefinition> { map };
            world.startMapId = "C04";
            world.startSpawnId = "Start";
            EditorUtility.SetDirty(world);
            foreach (var o in all) EditorUtility.SetDirty(o);
            AssetDatabase.SaveAssets();

            var combat = AssetDatabase.LoadAssetAtPath<CombatConfig>(Wp02 + "/CombatConfig.asset");
            var cu = AssetDatabase.LoadAssetAtPath<CombatUnlockFixture>(Wp02 + "/CombatUnlocks_None.asset");
            var issues = DataValidator.ValidateCombat(combat.ToCore(), cu.ToCore(), all.Select(e => e.ToCore()).ToList(), world.ToCore());
            foreach (var i in issues) Debug.LogError("[Corum2] " + i);
            BuildScene(all);
            Debug.Log("[Corum2] WP-04 content built. Validation issues: " + issues.Count);
        }

        static EnemyDefinitionAsset Boss()
        {
            var b = Enemy("Enemy_Warden", "warden", 200f, new Color(0.35f, 0.3f, 0.45f), AiType.Boss);
            b.hasAttack = false;
            b.bodyWidth = 1.4f;
            b.bodyHeight = 2.6f;
            b.launch = LaunchResponse.Immune;
            b.ai.trackRange = 12f;
            b.attackRange = 2f;
            b.firstAttackDelay = 0.5f;
            var p1 = new BossPhaseData
            {
                id = "p1", attackInterval = 1.2f, approachSpeed = 2f, retreatSpeed = 2f, preferredMinDistance = 0.8f, preferredMaxDistance = 1.6f,
                staggerResistance = 0f, launch = LaunchResponse.Immune, attackSpeed = 1f, transitionTime = 0f,
                patterns = new List<BossPatternData>
                {
                    Pat("slam", 0f, 2.2f, PlayerAirCondition.Grounded, 0, Atk("warden.slam", 10f, 0.35f, 1.6f, 1.6f, 0.9f, 1.1f)),
                    Pat("shot", 3f, 9f, PlayerAirCondition.Any, 0, Atk("warden.shot", 8f, 0.3f), new ProjectileData { speed = 9f, maxDistance = 10f, spawnOffsetY = 1.4f }),
                    Pat("antiair", 0f, 3f, PlayerAirCondition.Airborne, 2, Atk("warden.antiair", 9f, 0.15f, 1.2f, 2.4f, 1.2f)),
                },
                transitions = new List<PhaseTransitionData> { new PhaseTransitionData { kind = TransitionKind.HpBelow, value = 0.6f, targetPhase = "p2" } },
            };
            var p2 = new BossPhaseData
            {
                id = "p2", attackInterval = 0.9f, approachSpeed = 3f, retreatSpeed = 2f, preferredMinDistance = 0.8f, preferredMaxDistance = 1.6f,
                superArmor = true, staggerResistance = 0.5f, launch = LaunchResponse.Immune, attackSpeed = 1.5f, transitionTime = 1f,
                transitionAnimation = "warden.roar", transitionVfx = "vfx.armorGlow", transitionCamera = "cam.shakeSmall",
                patterns = new List<BossPatternData>
                {
                    Pat("crush", 0f, 2.2f, PlayerAirCondition.Any, 0, Atk("warden.crush", 12f, 0.35f, 1.6f, 1.6f, 0.9f, 1.1f)),
                    Pat("shot2", 3f, 9f, PlayerAirCondition.Any, 0, Atk("warden.shot2", 8f, 0.3f), new ProjectileData { speed = 11f, maxDistance = 10f, spawnOffsetY = 1.4f }),
                },
                transitions = new List<PhaseTransitionData> { new PhaseTransitionData { kind = TransitionKind.PatternUsed, patternId = "crush", count = 2, targetPhase = "p3" } },
            };
            var p3 = new BossPhaseData
            {
                id = "p3", attackInterval = 0.6f, approachSpeed = 3.5f, retreatSpeed = 2f, preferredMinDistance = 0.8f, preferredMaxDistance = 1.6f,
                jumpInterval = 2f, jumpSpeedX = 5f, jumpVelocityY = 8f, superArmor = false, staggerResistance = 0f, launch = LaunchResponse.Immune,
                attackSpeed = 2f, transitionTime = 1f, transitionAnimation = "warden.rage", transitionVfx = "vfx.redAura", transitionCamera = "cam.zoomIn",
                patterns = new List<BossPatternData>
                {
                    new BossPatternData { id = "rush", attack = Atk("warden.rush", 14f, 0.3f, 1.8f, 1.6f, 0.9f, 1.1f), minDistance = 0f, maxDistance = 2.4f, special = true },
                },
                transitions = new List<PhaseTransitionData> { new PhaseTransitionData { kind = TransitionKind.Event, eventName = "calm", targetPhase = "p1" } },
            };
            b.boss = new BossData { startPhase = "p1", phases = new List<BossPhaseData> { p1, p2, p3 } };
            return b;
        }

        static BossPatternData Pat(string id, float min, float max, PlayerAirCondition air, int prio, AttackData a, ProjectileData proj = null) =>
            new BossPatternData { id = id, attack = a, minDistance = min, maxDistance = max, playerState = air, priority = prio, hasProjectile = proj != null, projectile = proj ?? new ProjectileData() };

        static AttackData Atk(string id, float d, float startup, float w = 1.2f, float h = 1.2f, float oy = 0.9f, float ox = 0.8f) => new AttackData
        {
            id = id, kind = AttackKind.EnemyMelee, testDamage = d, startup = startup, active = 0.1f, recovery = 0.3f,
            offsetX = ox, offsetY = oy, width = w, height = h, knockback = 4f,
        };

        static EnemyDefinitionAsset Enemy(string asset, string id, float hp, Color c, AiType type)
        {
            var path = Dir + "/" + asset + ".asset";
            var e = AssetDatabase.LoadAssetAtPath<EnemyDefinitionAsset>(path);
            if (e == null)
            {
                e = ScriptableObject.CreateInstance<EnemyDefinitionAsset>();
                AssetDatabase.CreateAsset(e, path);
            }
            e.meta.domainId = "enemy." + id;
            e.meta.dataVersion = Version;
            e.meta.policyIds = new[] { "TEST-AI-01" };
            e.meta.relatedRIds = new[] { "R-08-01", "R-08-02", "R-08-03", "R-08-04", "R-08-05", "R-08-06", "R-08-07", "R-08-08", "R-08-09", "R-08-10", "R-08-11", "R-08-12" };
            e.enemyId = id;
            e.maxHp = hp;
            e.color = c;
            e.hasAttack = true;
            e.attack = Atk(id + ".atk", 10f, 0.3f);
            e.attackRange = 1.6f;
            e.attackInterval = 1.2f;
            e.firstAttackDelay = 0f;
            e.ai = new EnemyAIData { type = type, moveSpeed = 3f, trackRange = 8f };
            e.boss = new BossData();
            return e;
        }

        static SpawnData Sp(string id, float x) => new SpawnData { id = id, position = new Vector2(x, 0f), facing = 1 };
        static EnemySpawnData En(string id, string def, float x) => new EnemySpawnData { instanceId = id, enemyId = def, position = new Vector2(x, 0f), facing = -1 };

        static void BuildScene(List<EnemyDefinitionAsset> enemies)
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
            host.world = AssetDatabase.LoadAssetAtPath<WorldDefinition>(Dir + "/World_WP04.asset");
            host.movement = AssetDatabase.LoadAssetAtPath<MovementConfig>(Wp01 + "/MovementConfig.asset");
            host.inputProfile = AssetDatabase.LoadAssetAtPath<InputProfileConfig>(Wp01 + "/InputProfile.asset");
            host.unlocks = AssetDatabase.LoadAssetAtPath<AirUnlockFixture>(Wp01 + "/AirUnlocks_All.asset");
            host.combat = AssetDatabase.LoadAssetAtPath<CombatConfig>(Wp02 + "/CombatConfig.asset");
            host.combatUnlocks = AssetDatabase.LoadAssetAtPath<CombatUnlockFixture>(Wp02 + "/CombatUnlocks_None.asset");
            host.enemies = enemies;
            host.ring = AssetDatabase.LoadAssetAtPath<RingConfig>(Wp03 + "/RingConfig.asset");
            EditorSceneManager.SaveScene(scene, ScenePath);
            var scenes = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
            scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
