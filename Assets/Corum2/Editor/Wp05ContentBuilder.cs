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
    /// WP-05 test content: magic commands (R-02-02), shared magic data for command and scroll (R-02-10) and a revive item
    /// for the death flow (R-06-19). Reuses the WP-02 arena, enemies and combat data. All numbers and command shapes are
    /// test data (TEST-MAGIC-01); conflicts with other inputs are CF-01 (EXP-CMD-01..06).
    /// </summary>
    public static class Wp05ContentBuilder
    {
        const string Dir = "Assets/Corum2/Data/WP05";
        const string Wp02 = "Assets/Corum2/Data/WP02";
        const string Wp01 = "Assets/Corum2/Data/WP01";
        public const string ScenePath = "Assets/Corum2/Scenes/WP05_MagicDeath.unity";
        const string Version = "wp05-1";

        [MenuItem("Corum2/WP-05/Build Test Content")]
        public static void Build()
        {
            Directory.CreateDirectory(Dir);
            var ring = AssetDatabase.LoadAssetAtPath<RingConfig>(Dir + "/RingConfig_WP05.asset");
            if (ring == null)
            {
                ring = ScriptableObject.CreateInstance<RingConfig>();
                AssetDatabase.CreateAsset(ring, Dir + "/RingConfig_WP05.asset");
            }
            ring.meta.domainId = "ring.config.wp05";
            ring.meta.dataVersion = Version;
            ring.meta.policyIds = new[] { "EXP-RING-01", "EXP-RING-02", "EXP-ITEM-02", "TEST-ITEM-01", "TEST-MAGIC-01", "EXP-CMD-01", "EXP-CMD-02", "EXP-DEATH-05" };
            ring.meta.relatedRIds = new[] { "R-02-02", "R-02-06", "R-02-10", "R-06-19" };
            ring.slowScale = 0.2f;
            ring.slotsPerPage = 8;
            ring.cursorDeadzone = 0.35f;
            ring.magics = new List<MagicEntry>
            {
                new MagicEntry { id = "fire", effect = MagicEffect.Attack, power = 30f, castTime = 0.2f, recovery = 0.25f, active = 0.1f,
                    hitboxOffsetX = 1.4f, hitboxOffsetY = 0.9f, hitboxWidth = 2.2f, hitboxHeight = 1.6f, knockback = 5f, hitstop = 0.05f },
                new MagicEntry { id = "cure", effect = MagicEffect.Heal, power = 25f, castTime = 0.25f, recovery = 0.2f },
                new MagicEntry { id = "might", effect = MagicEffect.Buff, power = 1.5f, castTime = 0.15f, recovery = 0.15f, buffDuration = 4f },
            };
            ring.commands = new List<CommandEntry>
            {
                Cmd("fireball", "fire", LogicalAction.Attack, Dir8.Down, Dir8.DownRight, Dir8.Right),
                Cmd("cure", "cure", LogicalAction.Guard, Dir8.Down, Dir8.DownLeft, Dir8.Left),
                Cmd("might", "might", LogicalAction.Jump, Dir8.Down, Dir8.DownLeft, Dir8.Left),
            };
            ring.commandTakesButton = true;
            ring.minCommandDirections = 3;
            ring.items = new List<ItemEntry>
            {
                Item("potion", "Potion", "Restores 20 HP.", ItemKind.Consumable, ItemEffectType.Heal, 20f, 1f),
                Item("scroll", "Fire Scroll", "Casts 'fire' once - the same magic data as the fireball command (R-02-10).", ItemKind.MagicScroll, ItemEffectType.Magic, 0f, 1f, "fire"),
                Item("feather", "Phoenix Feather", "Revive item. Offered only when you die (EXP-DEATH-05).", ItemKind.Consumable, ItemEffectType.Revive, 0f, 0f),
            };
            ring.startInventory = new List<StackEntry> { St("potion", 2), St("scroll", 2), St("feather", 1) };
            ring.startQuickslots = new[] { "", "", "scroll", "feather" }; // D-pad down = scroll, D-pad left = feather (refused: EXP-DEATH-05)
            EditorUtility.SetDirty(ring);
            AssetDatabase.SaveAssets();

            var issues = RingValidator.Validate(ring.ToCore());
            foreach (var i in issues) Debug.LogError("[Corum2] " + i);
            BuildScene(ring);
            Debug.Log("[Corum2] WP-05 content built. Ring validation issues: " + issues.Count);
        }

        static CommandEntry Cmd(string id, string magic, LogicalAction button, params Dir8[] dirs) =>
            new CommandEntry { id = id, magicId = magic, button = button, window = 0.6f, directions = dirs.ToList() };

        static ItemEntry Item(string id, string name, string desc, ItemKind k, ItemEffectType e, float amount, float cd, string magic = "") =>
            new ItemEntry { id = id, displayName = name, description = desc, kind = k, effect = e, amount = amount, cooldown = cd, magicId = magic };

        static StackEntry St(string id, int n) => new StackEntry { itemId = id, count = n };

        static void BuildScene(RingConfig ring)
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
            host.world = AssetDatabase.LoadAssetAtPath<WorldDefinition>(Wp02 + "/World_WP02.asset");
            host.movement = AssetDatabase.LoadAssetAtPath<MovementConfig>(Wp01 + "/MovementConfig.asset");
            host.inputProfile = AssetDatabase.LoadAssetAtPath<InputProfileConfig>(Wp01 + "/InputProfile.asset");
            host.unlocks = AssetDatabase.LoadAssetAtPath<AirUnlockFixture>(Wp01 + "/AirUnlocks_All.asset");
            host.combat = AssetDatabase.LoadAssetAtPath<CombatConfig>(Wp02 + "/CombatConfig.asset");
            host.combatUnlocks = AssetDatabase.LoadAssetAtPath<CombatUnlockFixture>(Wp02 + "/CombatUnlocks_None.asset");
            host.enemies = new[] { "Enemy_Bag", "Enemy_Hitter", "Enemy_Armor", "Enemy_BossImmune", "Enemy_Weak" }
                .Select(n => AssetDatabase.LoadAssetAtPath<EnemyDefinitionAsset>(Wp02 + "/" + n + ".asset")).ToList();
            host.ring = ring;
            EditorSceneManager.SaveScene(scene, ScenePath);
            var scenes = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
            scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
