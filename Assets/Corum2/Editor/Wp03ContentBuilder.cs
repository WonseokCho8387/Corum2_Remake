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
    /// WP-03 ring/quickslot test content: RingConfig with test items (TEST-ITEM-01), the extra ring bindings on the shared
    /// input profile (B, LT, RT; keyboard keys EXP-INPUT-04) and the WP03_Ring scene on the WP-02 arena map.
    /// </summary>
    public static class Wp03ContentBuilder
    {
        const string Dir = "Assets/Corum2/Data/WP03";
        const string Wp02 = "Assets/Corum2/Data/WP02";
        const string Wp01 = "Assets/Corum2/Data/WP01";
        public const string ScenePath = "Assets/Corum2/Scenes/WP03_Ring.unity";
        const string Version = "wp03-1";

        [MenuItem("Corum2/WP-03/Build Test Content")]
        public static void Build()
        {
            Directory.CreateDirectory(Dir);
            var ring = AssetDatabase.LoadAssetAtPath<RingConfig>(Dir + "/RingConfig.asset");
            if (ring == null)
            {
                ring = ScriptableObject.CreateInstance<RingConfig>();
                AssetDatabase.CreateAsset(ring, Dir + "/RingConfig.asset");
            }
            ring.meta.dataVersion = Version;
            ring.slowScale = 0.2f;
            ring.slotsPerPage = 8;
            ring.cursorDeadzone = 0.35f;
            ring.magics = new List<MagicEntry> { new MagicEntry { id = "fire", power = 30f, castTime = 0.2f } };
            ring.items = new List<ItemEntry>
            {
                Item("potion", "Potion", "Restores 20 HP.", ItemKind.Consumable, ItemEffectType.Heal, 20f, 1f),
                Item("hipotion", "Hi-Potion", "Restores 50 HP.", ItemKind.Consumable, ItemEffectType.Heal, 50f, 1f),
                Item("herb", "Herb", "Restores 5 HP.", ItemKind.Consumable, ItemEffectType.Heal, 5f, 0.5f),
                Item("scroll", "Fire Scroll", "Casts the test magic 'fire' once (same data as the command).", ItemKind.MagicScroll, ItemEffectType.Magic, 0f, 1f, "fire"),
                Item("key", "Old Key", "Quest item. Cannot be discarded.", ItemKind.Quest, ItemEffectType.None, 0f, 0f),
                Item("bread", "Bread", "Restores 10 HP.", ItemKind.Consumable, ItemEffectType.Heal, 10f, 1f),
                Item("water", "Water", "Restores 8 HP.", ItemKind.Consumable, ItemEffectType.Heal, 8f, 0.5f),
                Item("apple", "Apple", "Restores 3 HP.", ItemKind.Consumable, ItemEffectType.Heal, 3f, 0.5f),
                Item("elixir", "Elixir", "Restores 100 HP.", ItemKind.Consumable, ItemEffectType.Heal, 100f, 2f),
                Item("meat", "Meat", "Restores 30 HP.", ItemKind.Consumable, ItemEffectType.Heal, 30f, 1f),
            };
            // Page 1: U potion, UR hipotion, R herb, DR scroll, D key, DL bread, L water, UL apple. Page 2: U elixir, UR meat.
            ring.startInventory = new List<StackEntry>
            {
                St("potion", 3), St("hipotion", 1), St("herb", 2), St("scroll", 2), St("key", 1),
                St("bread", 2), St("water", 1), St("apple", 3), St("elixir", 1), St("meat", 1),
            };
            ring.startQuickslots = new[] { "", "", "", "" };
            EditorUtility.SetDirty(ring);

            var input = AssetDatabase.LoadAssetAtPath<InputProfileConfig>(Wp01 + "/InputProfile.asset");
            AddBinding(input, LogicalAction.ButtonB, "EXP-INPUT-04", "<Gamepad>/buttonEast", "<Keyboard>/e");
            AddBinding(input, LogicalAction.PagePrev, "EXP-INPUT-04", "<Gamepad>/leftTrigger", "<Keyboard>/z");
            AddBinding(input, LogicalAction.PageNext, "EXP-INPUT-04", "<Gamepad>/rightTrigger", "<Keyboard>/c");
            input.meta.dataVersion = Version;
            if (!input.meta.policyIds.Contains("EXP-INPUT-04")) input.meta.policyIds = input.meta.policyIds.Concat(new[] { "EXP-INPUT-04" }).ToArray();
            EditorUtility.SetDirty(input);
            AssetDatabase.SaveAssets();

            var issues = RingValidator.Validate(ring.ToCore());
            foreach (var i in issues) Debug.LogError("[Corum2] " + i);
            BuildScene();
            Debug.Log("[Corum2] WP-03 content built. Ring validation issues: " + issues.Count);
        }

        static ItemEntry Item(string id, string name, string desc, ItemKind k, ItemEffectType e, float amount, float cd, string magic = "") =>
            new ItemEntry { id = id, displayName = name, description = desc, kind = k, effect = e, amount = amount, cooldown = cd, magicId = magic };

        static StackEntry St(string id, int n) => new StackEntry { itemId = id, count = n };

        static void AddBinding(InputProfileConfig input, LogicalAction a, string policy, params string[] paths)
        {
            input.buttons.RemoveAll(b => b.action == a);
            input.buttons.Add(new ActionBinding { action = a, policy = policy, paths = paths });
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
            host.world = AssetDatabase.LoadAssetAtPath<WorldDefinition>(Wp02 + "/World_WP02.asset");
            host.movement = AssetDatabase.LoadAssetAtPath<MovementConfig>(Wp01 + "/MovementConfig.asset");
            host.inputProfile = AssetDatabase.LoadAssetAtPath<InputProfileConfig>(Wp01 + "/InputProfile.asset");
            host.unlocks = AssetDatabase.LoadAssetAtPath<AirUnlockFixture>(Wp01 + "/AirUnlocks_All.asset");
            host.combat = AssetDatabase.LoadAssetAtPath<CombatConfig>(Wp02 + "/CombatConfig.asset");
            host.combatUnlocks = AssetDatabase.LoadAssetAtPath<CombatUnlockFixture>(Wp02 + "/CombatUnlocks_None.asset");
            host.enemies = new[] { "Enemy_Bag", "Enemy_Hitter", "Enemy_Armor", "Enemy_BossImmune", "Enemy_Weak" }
                .Select(n => AssetDatabase.LoadAssetAtPath<EnemyDefinitionAsset>(Wp02 + "/" + n + ".asset")).ToList();
            host.ring = AssetDatabase.LoadAssetAtPath<RingConfig>(Dir + "/RingConfig.asset");
            EditorSceneManager.SaveScene(scene, ScenePath);
            var scenes = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
            scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
