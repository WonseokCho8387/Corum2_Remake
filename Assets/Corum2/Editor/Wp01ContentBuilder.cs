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
    /// <summary>
    /// Creates the WP-01 test data assets and scene reproducibly. All values are experimental test content,
    /// not approved game data. Re-running overwrites the WP-01 assets.
    /// </summary>
    public static class Wp01ContentBuilder
    {
        const string DataDir = "Assets/Corum2/Data/WP01";
        const string SceneDir = "Assets/Corum2/Scenes";
        public const string ScenePath = SceneDir + "/WP01_MapMovement.unity";
        const string Version = "wp01-r1";

        [MenuItem("Corum2/WP-01/Build Test Content")]
        public static void Build()
        {
            Directory.CreateDirectory(DataDir);
            Directory.CreateDirectory(SceneDir);

            var movement = Save(ScriptableObject.CreateInstance<MovementConfig>(), "MovementConfig");
            movement.meta.dataVersion = Version;
            var input = Save(ScriptableObject.CreateInstance<InputProfileConfig>(), "InputProfile");
            input.meta.dataVersion = Version;
            input.buttons = DefaultButtons();
            var unlockAll = Unlocks("AirUnlocks_All", true, true, true);
            Unlocks("AirUnlocks_None", false, false, false);

            var a = Map("Map_M01_FieldA", "M01", 0, 80, 9, 71, new Color(0.45f, 0.40f, 0.33f));
            a.solids = new List<BoxData>
            {
                Box(-1, -2, 81, 0),        // ground; x 0..22 is open floor for walk/run/jump/roll checks
                Box(26, 0, 28, 1f),         // 1 m step block (jump over)
                Box(40, 1.2f, 42, 7f),      // low passage: 1.2 m gap, wall above (slide only, R-03-04)
            };
            a.spawns = new List<SpawnData>
            {
                Spawn("Start", 4, 1), Spawn("FromRight", 78, -1), Spawn("FromAbove", 56, 1),
                Spawn("TestPassageWest", 38.5f, 1), Spawn("TestOpen", 12, 1), Spawn("TestPassageSlide", 37.55f, 1),
            };
            a.left = new EdgeData { policy = EdgePolicy.Blocked };
            a.right = new EdgeData { policy = EdgePolicy.Connected, connectionId = "C-M01-M02", targetMapId = "M02", targetSpawnId = "FromLeft" };
            a.points = new List<PointData> { Point("P-M01-UP", 55, 57, PointDirection.Up, "M03", "FromBelow") };

            var b = Map("Map_M02_FieldB", "M02", 0, 40, 9, 31, new Color(0.33f, 0.42f, 0.36f));
            b.solids = new List<BoxData>
            {
                Box(-1, -2, 41, 0),
                Box(10, 2f, 14, 2.5f),      // reachable with a basic jump
                Box(20, 3.6f, 26, 4.1f),    // needs the extra jump
            };
            b.spawns = new List<SpawnData> { Spawn("FromLeft", 2, 1) };
            b.left = new EdgeData { policy = EdgePolicy.Connected, connectionId = "C-M02-M01", targetMapId = "M01", targetSpawnId = "FromRight" };
            b.right = new EdgeData { policy = EdgePolicy.Blocked };

            var c = Map("Map_M03_UpperC", "M03", 0, 24, 9, 15, new Color(0.38f, 0.36f, 0.48f));
            c.solids = new List<BoxData> { Box(-1, -2, 25, 0) };
            c.spawns = new List<SpawnData> { Spawn("FromBelow", 12, 1) };
            c.left = new EdgeData { policy = EdgePolicy.Blocked };
            c.right = new EdgeData { policy = EdgePolicy.Blocked };
            c.points = new List<PointData> { Point("P-M03-DOWN", 11, 13, PointDirection.Down, "M01", "FromAbove") };

            var world = Save(ScriptableObject.CreateInstance<WorldDefinition>(), "World_WP01");
            world.maps = new List<MapDefinition> { a, b, c };
            world.startMapId = "M01";
            world.startSpawnId = "Start";

            foreach (var o in new Object[] { movement, input, a, b, c, world }) EditorUtility.SetDirty(o);
            AssetDatabase.SaveAssets();
            // Validate before the scene switch: NewScene unloads unreferenced in-memory objects.
            var issues = Validate(world, movement, input, unlockAll);

            BuildScene();
            Debug.Log("[Corum2] WP-01 content built. Validation issues: " + issues.Count);
        }

        [MenuItem("Corum2/WP-01/Validate Data")]
        public static void ValidateMenu()
        {
            var world = AssetDatabase.LoadAssetAtPath<WorldDefinition>(DataDir + "/World_WP01.asset");
            var move = AssetDatabase.LoadAssetAtPath<MovementConfig>(DataDir + "/MovementConfig.asset");
            var input = AssetDatabase.LoadAssetAtPath<InputProfileConfig>(DataDir + "/InputProfile.asset");
            var unlocks = AssetDatabase.LoadAssetAtPath<AirUnlockFixture>(DataDir + "/AirUnlocks_All.asset");
            var issues = Validate(world, move, input, unlocks);
            Debug.Log("[Corum2] Validation issues: " + issues.Count + (issues.Count > 0 ? "\n" + string.Join("\n", issues) : ""));
        }

        static List<ValidationIssue> Validate(WorldDefinition w, MovementConfig m, InputProfileConfig i, AirUnlockFixture u)
        {
            var issues = DataValidator.Validate(w.ToCore(), m.ToCore(), i.ToCore(), u.ToCore());
            foreach (var issue in issues) Debug.LogError("[Corum2] " + issue);
            return issues;
        }

        static void BuildScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var world = AssetDatabase.LoadAssetAtPath<WorldDefinition>(DataDir + "/World_WP01.asset");
            var movement = AssetDatabase.LoadAssetAtPath<MovementConfig>(DataDir + "/MovementConfig.asset");
            var input = AssetDatabase.LoadAssetAtPath<InputProfileConfig>(DataDir + "/InputProfile.asset");
            var unlocks = AssetDatabase.LoadAssetAtPath<AirUnlockFixture>(DataDir + "/AirUnlocks_All.asset");

            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>("Assets/Settings/SampleSceneProfile.asset");
            if (profile != null)
            {
                var volGo = new GameObject("Global Volume");
                var vol = volGo.AddComponent<Volume>();
                vol.isGlobal = true;
                vol.sharedProfile = profile;
            }
            var cam = Camera.main;
            if (cam != null)
            {
                var data = cam.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
                if (data != null) data.renderPostProcessing = true;
            }
            var light = Object.FindFirstObjectByType<Light>();
            if (light != null) light.transform.rotation = Quaternion.Euler(40f, -30f, 0f);

            var hostGo = new GameObject("GameHost");
            var host = hostGo.AddComponent<GameHost>();
            host.world = world;
            host.movement = movement;
            host.inputProfile = input;
            host.unlocks = unlocks;

            EditorSceneManager.SaveScene(scene, ScenePath);
            var scenes = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        static List<ActionBinding> DefaultButtons() => new List<ActionBinding>
        {
            Bind(LogicalAction.Jump, "", "<Gamepad>/buttonSouth", "<Keyboard>/space"),
            Bind(LogicalAction.Attack, "", "<Gamepad>/buttonWest", "<Keyboard>/j"),
            Bind(LogicalAction.SpecialAttack, "EXP-INPUT-01", "<Gamepad>/buttonNorth", "<Keyboard>/k"),
            Bind(LogicalAction.Guard, "", "<Gamepad>/rightShoulder", "<Keyboard>/l"),
            Bind(LogicalAction.RingToggle, "", "<Gamepad>/leftShoulder", "<Keyboard>/q"),
            Bind(LogicalAction.QuickUp, "EXP-INPUT-02", "<Gamepad>/dpad/up", "<Keyboard>/1"),
            Bind(LogicalAction.QuickRight, "EXP-INPUT-02", "<Gamepad>/dpad/right", "<Keyboard>/2"),
            Bind(LogicalAction.QuickDown, "EXP-INPUT-02", "<Gamepad>/dpad/down", "<Keyboard>/3"),
            Bind(LogicalAction.QuickLeft, "EXP-INPUT-02", "<Gamepad>/dpad/left", "<Keyboard>/4"),
        };

        static ActionBinding Bind(LogicalAction a, string policy, params string[] paths) =>
            new ActionBinding { action = a, policy = string.IsNullOrEmpty(policy) ? "keyboard key EXP-INPUT-02" : policy, paths = paths };

        static T Save<T>(T obj, string name) where T : ScriptableObject
        {
            var path = DataDir + "/" + name + ".asset";
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

        static AirUnlockFixture Unlocks(string name, bool jump, bool dash, bool dive)
        {
            var u = Save(ScriptableObject.CreateInstance<AirUnlockFixture>(), name);
            u.meta.domainId = "unlock." + name;
            u.meta.dataVersion = Version;
            u.extraJump = jump;
            u.airDash = dash;
            u.dive = dive;
            EditorUtility.SetDirty(u);
            return u;
        }

        static MapDefinition Map(string asset, string id, float minX, float maxX, float camMin, float camMax, Color color)
        {
            var m = Save(ScriptableObject.CreateInstance<MapDefinition>(), asset);
            m.meta.domainId = "map." + id;
            m.meta.dataVersion = Version;
            m.meta.policyIds = new[] { "EXP-MAP-01", "EXP-MAP-02", "EXP-MAP-03", "EXP-CAM-01" };
            m.mapId = id;
            m.minX = minX;
            m.maxX = maxX;
            m.killY = -10f;
            m.combatPlaneZ = 0f;
            m.cameraMinX = camMin;
            m.cameraMaxX = camMax;
            m.cameraY = 2.5f;
            m.cameraFollowSharpness = 10f;
            m.groundColor = color;
            return m;
        }

        static BoxData Box(float x0, float y0, float x1, float y1) => new BoxData { min = new Vector2(x0, y0), max = new Vector2(x1, y1) };
        static SpawnData Spawn(string id, float x, int facing) => new SpawnData { id = id, position = new Vector2(x, 0f), facing = facing };

        static PointData Point(string id, float x0, float x1, PointDirection d, string map, string spawn) =>
            new PointData { id = id, area = Box(x0, 0, x1, 2f), direction = d, targetMapId = map, targetSpawnId = spawn };
    }
}
