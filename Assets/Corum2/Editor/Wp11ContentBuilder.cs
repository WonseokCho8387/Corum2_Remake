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
    /// WP-11 test content (TEST-AI-01, TEST-BOSS-11): evidence matrix for TC-05-04, TC-08-02, TC-08-05, TC-08-09, TC-08-12 on one map.
    /// Zones are 30 m apart (beyond every track range) so only the zone the player stands in reacts. All numbers are test data.
    /// </summary>
    public static class Wp11ContentBuilder
    {
        const string Dir = "Assets/Corum2/Data/WP11";
        const string Wp01 = "Assets/Corum2/Data/WP01";
        const string Wp02 = "Assets/Corum2/Data/WP02";
        const string Wp04 = "Assets/Corum2/Data/WP04";
        public const string ScenePath = "Assets/Corum2/Scenes/WP11_Evidence.unity";
        const string Version = "wp11-1";

        [MenuItem("Corum2/WP-11/Build Test Content")]
        public static void Build()
        {
            Directory.CreateDirectory(Dir);
            // TC-08-02: a chaser that practically does not walk (0.01 m/s), so the PLAYER enters its range; first attack without delay.
            var post = Enemy("Enemy_Post", "post", 1000f, new Color(0.85f, 0.45f, 0.25f), AiType.Chaser);
            post.ai.moveSpeed = 0.01f; // practically still (validator needs > 0)
            var postWeak = Enemy("Enemy_PostWeak", "postWeak", 12f, new Color(0.95f, 0.75f, 0.35f), AiType.Chaser);
            postWeak.ai.moveSpeed = 0.01f;
            // TC-08-05: a second jumper configuration (farther window, lower and faster jump) next to the WP-04 jumper [3, 5].
            var jfar = Enemy("Enemy_JumperFar", "jumperFar", 60f, new Color(0.3f, 0.65f, 0.25f), AiType.Jumper);
            jfar.ai.moveSpeed = 0.01f; // practically still (validator needs > 0): the jump window alone decides
            jfar.ai.jumpMinDistance = 5f;
            jfar.ai.jumpMaxDistance = 7f;
            jfar.ai.jumpSpeedX = 7f;
            jfar.ai.jumpVelocityY = 6f;
            jfar.ai.jumpCooldown = 2.5f;
            jfar.ai.hasAirAttack = true;
            jfar.ai.airAttack = Atk("jumperFar.air", 8f, 0.06f);
            jfar.ai.airAttackRange = 2.2f;
            var jnear = Enemy("Enemy_JumperStill", "jumperStill", 60f, new Color(0.45f, 0.85f, 0.35f), AiType.Jumper);
            jnear.ai.moveSpeed = 0.01f; // the WP-04 jumper window [3, 5] without walking, so a distance outside the window stays outside
            jnear.ai.jumpMinDistance = 3f;
            jnear.ai.jumpMaxDistance = 5f;
            jnear.ai.jumpSpeedX = 5f;
            jnear.ai.jumpVelocityY = 9f;
            jnear.ai.jumpCooldown = 2.5f;
            jnear.ai.hasAirAttack = true;
            jnear.ai.airAttack = Atk("jumperStill.air", 8f, 0.06f);
            jnear.ai.airAttackRange = 2.2f;
            // TC-08-09: boss A = one phase, boss B = two phases (HP 50 %); boss C = the WP-04 warden (three phases, HP then pattern count).
            var solo = Boss("Enemy_BossSolo", "bossSolo", 100f, new Color(0.4f, 0.3f, 0.3f),
                Phase("a1", false, LaunchResponse.Immune, 0f, "", "", "", SlowMelee("a1.smash")));
            var duo = Boss("Enemy_BossDuo", "bossDuo", 100f, new Color(0.3f, 0.4f, 0.3f),
                Phase("b1", false, LaunchResponse.Immune, 0f, "", "", "", SlowMelee("b1.smash"), new PhaseTransitionData { kind = TransitionKind.HpBelow, value = 0.5f, targetPhase = "b2" }),
                Phase("b2", false, LaunchResponse.Immune, 0.5f, "warden.roar", "vfx.armorGlow", "cam.shakeSmall", SlowMelee("b2.smash")));
            // TC-08-12: one boss whose three phases are the three states the master names: normal (launchable, no armor),
            // super armor, launch immune. Phases change on events only, so each case puts the boss in a state on purpose.
            var gaunt = Boss("Enemy_Gauntlet", "gauntlet", 1000f, new Color(0.45f, 0.35f, 0.2f),
                Phase("g1", false, LaunchResponse.Launchable, 0f, "", "", "", SlowMelee("g1.smash"), new PhaseTransitionData { kind = TransitionKind.Event, eventName = "arm", targetPhase = "g2" }),
                Phase("g2", true, LaunchResponse.Launchable, 0.5f, "warden.roar", "vfx.armorGlow", "cam.shakeSmall", SlowMelee("g2.smash"), new PhaseTransitionData { kind = TransitionKind.Event, eventName = "lift", targetPhase = "g3" }),
                Phase("g3", false, LaunchResponse.Immune, 0.5f, "sentinel.shift", "vfx.blink", "cam.none", SlowMelee("g3.smash")));

            var map = AssetDatabase.LoadAssetAtPath<MapDefinition>(Dir + "/Map_C11_Evidence.asset") ?? Save(ScriptableObject.CreateInstance<MapDefinition>(), "Map_C11_Evidence");
            map.meta.domainId = "map.C11";
            map.meta.dataVersion = Version;
            map.meta.relatedRIds = new[] { "R-05-04", "R-08-02", "R-08-05", "R-08-09", "R-08-12", "R-00-03", "R-00-05", "R-06-01", "R-06-10" };
            map.mapId = "C11";
            map.minX = 0;
            map.maxX = 1100;
            map.cameraMinX = 9;
            map.cameraMaxX = 1091;
            map.cameraY = 2.5f;
            map.cameraFollowSharpness = 10f;
            map.groundColor = new Color(0.38f, 0.4f, 0.37f);
            map.solids = new List<BoxData> { new BoxData { min = new Vector2(-1, -2), max = new Vector2(1101, 0) } };
            map.spawns = new List<SpawnData> { Sp("Start", 4) };
            map.enemies = new List<EnemySpawnData>();
            var x = 20f;
            void Z(string id, params (string inst, string def, float dx)[] es)
            {
                map.spawns.Add(Sp(id, x));
                foreach (var e in es) map.enemies.Add(En(e.inst, e.def, x + e.dx));
                x += 30f;
            }
            // TC-05-04: a WP-02 hitter in reach (keyboard zone, pad zone)
            Z("H5Kb", ("hk", "hitter", 1.2f));
            Z("H5Pad", ("hp", "hitter", 1.2f));
            // TC-08-02: the player walks into a still chaser's range in each enemy state
            Z("A2Ready", ("pr", "post", 4f));       // ready: attacks on the first tick in range
            Z("A2Stagger", ("ps", "post", 4f));     // in range while it reacts to a hit
            Z("A2Down", ("pd", "post", 4f));        // in range while it is launched / down / waking up
            Z("A2Dead", ("px", "postWeak", 4f));    // in range after it died
            // TC-08-05: two jumper configurations, distance inside / outside each window
            Z("J5NearIn", ("jn1", "jumperStill", 4f));   // [3,5]: 4 m -> jump
            Z("J5NearOut", ("jn2", "jumperStill", 6f));  // [3,5]: 6 m -> no jump (does not walk)
            Z("J5FarIn", ("jf1", "jumperFar", 6f));      // [5,7]: 6 m -> jump
            Z("J5FarOut", ("jf2", "jumperFar", 4f));     // [5,7]: 4 m -> no jump
            // TC-08-09: bosses A (1 phase), B (2 phases), C (3 phases, WP-04 warden)
            Z("B9Solo", ("ba", "bossSolo", 1.6f));
            Z("B9Duo", ("bb", "bossDuo", 1.6f));
            Z("B9Warden", ("bc", "warden", 1.6f));
            // TC-08-12: one gauntlet per state x case
            foreach (var st in new[] { "g1", "g2", "g3" })
            foreach (var c in new[] { "Phys", "Magic", "Parry", "Simul", "Launch" })
                Z("G" + st + c, ("x" + st + c, "gauntlet", 1.6f));
            // TC-UNITY-02: walls. Player width 0.6, bag width 0.8.
            BoxData Wall(float minX, float maxX) => new BoxData { min = new Vector2(minX, 0f), max = new Vector2(maxX, 5f) };
            map.solids.Add(Wall(x + 5f, x + 6f));            // run / air dash / dive into a wall on the right
            Z("U2WallMove");
            map.solids.Add(Wall(x - 1.35f, x - 0.35f));      // the hitter on the right knocks the player into a wall 0.05 m behind
            Z("U2WallKnock", ("uk", "hitter", 1.2f));
            map.solids.Add(Wall(x + 2f, x + 3f));            // magic knocks the bag (right edge x + 1.9) into a wall 0.1 m behind it
            Z("U2WallEnemy", ("ub", "bag", 1.5f));
            // TC-UNITY-07: several targets in one motion, a cast cancelled by a hit, a target that is down / waking up
            Z("U7Multi", ("m1", "bag", 1f), ("m2", "bag", 1f), ("m3", "bag", 1f), ("m4", "bag", 1f));
            Z("U7Cancel", ("uc", "hitter", 1.2f));
            Z("U7Wakeup", ("uw", "bag", 1f));
            map.left = new EdgeData { policy = EdgePolicy.Blocked };
            map.right = new EdgeData { policy = EdgePolicy.Blocked };
            map.points = new List<PointData>();

            var world = Save(ScriptableObject.CreateInstance<WorldDefinition>(), "World_WP11");
            world.maps = new List<MapDefinition> { map };
            world.startMapId = "C11";
            world.startSpawnId = "Start";
            var own = new List<EnemyDefinitionAsset> { post, postWeak, jfar, jnear, solo, duo, gaunt };
            foreach (var o in own.Cast<Object>().Concat(new Object[] { map, world })) EditorUtility.SetDirty(o);
            AssetDatabase.SaveAssets();

            var move = AssetDatabase.LoadAssetAtPath<MovementConfig>(Wp01 + "/MovementConfig.asset");
            var input = AssetDatabase.LoadAssetAtPath<InputProfileConfig>(Wp01 + "/InputProfile.asset");
            var air = AssetDatabase.LoadAssetAtPath<AirUnlockFixture>(Wp01 + "/AirUnlocks_All.asset");
            var combat = AssetDatabase.LoadAssetAtPath<CombatConfig>(Wp02 + "/CombatConfig.asset");
            var cu = AssetDatabase.LoadAssetAtPath<CombatUnlockFixture>(Wp02 + "/CombatUnlocks_None.asset");
            var enemies = new List<EnemyDefinitionAsset> { AssetDatabase.LoadAssetAtPath<EnemyDefinitionAsset>(Wp02 + "/Enemy_Hitter.asset"), AssetDatabase.LoadAssetAtPath<EnemyDefinitionAsset>(Wp02 + "/Enemy_Bag.asset"),
                AssetDatabase.LoadAssetAtPath<EnemyDefinitionAsset>(Wp04 + "/Enemy_Warden.asset") };
            enemies.AddRange(own);
            var lib = AssetDatabase.LoadAssetAtPath<PresentationLibrary>(Wp04 + "/PresentationLibrary.asset");
            var issues = DataValidator.Validate(world.ToCore(), move.ToCore(), input.ToCore(), air.ToCore());
            issues.AddRange(DataValidator.ValidateCombat(combat.ToCore(), cu.ToCore(), enemies.Select(e => e.ToCore()).ToList(), world.ToCore()));
            var presIssues = PresentationLibrary.Validate(lib, enemies.Where(e => e.ai.type == AiType.Boss).Select(e => e.ToCore().Boss).ToList());
            foreach (var i in issues) Debug.LogError("[Corum2] " + i);
            foreach (var i in presIssues) Debug.LogError("[Corum2] " + i);
            BuildScene(world, move, input, air, combat, cu, enemies, lib);
            Debug.Log("[Corum2] WP-11 content built. Validation issues: " + (issues.Count + presIssues.Count));
        }

        public const string OpsScenePath = "Assets/Corum2/Scenes/WP11_Ops.unity";

        /// <summary>
        /// WP-11 Part B (TC-OPS-01, TC-UNITY-03): one scene whose unlock / combat fixtures are its own copies, so a variant run changes
        /// exactly one Inspector value without touching any other scene. Baseline: extra jump, air dash and dive unlocked; dash
        /// pierce and dive area off; combo window 0.5 s (the WP-02 value).
        /// </summary>
        [MenuItem("Corum2/WP-11/Build Ops Content")]
        public static void BuildOps()
        {
            Directory.CreateDirectory(Dir);
            var air = Copy<AirUnlockFixture>(Wp01 + "/AirUnlocks_All.asset", "AirUnlocks_Ops");
            air.meta.domainId = "unlock.AirUnlocks_Ops";
            air.meta.dataVersion = Version;
            air.extraJump = true;
            air.airDash = true;
            air.dive = true;
            var cu = Copy<CombatUnlockFixture>(Wp02 + "/CombatUnlocks_None.asset", "CombatUnlocks_Ops");
            cu.meta.domainId = "combat.CombatUnlocks_Ops";
            cu.meta.dataVersion = Version;
            cu.dashPierce = false;
            cu.diveArea = false;
            var combat = Copy<CombatConfig>(Wp02 + "/CombatConfig.asset", "CombatConfig_Ops");
            combat.meta.domainId = "combat.player.ops";
            combat.meta.dataVersion = Version;
            combat.comboWindow = 0.5f;
            foreach (var o in new Object[] { air, cu, combat }) EditorUtility.SetDirty(o);

            var map = AssetDatabase.LoadAssetAtPath<MapDefinition>(Dir + "/Map_C11o_Ops.asset") ?? Save(ScriptableObject.CreateInstance<MapDefinition>(), "Map_C11o_Ops");
            map.meta.domainId = "map.C11o";
            map.meta.dataVersion = Version;
            map.meta.relatedRIds = new[] { "R-03-03", "R-04-06", "R-04-08", "R-04-09" };
            map.mapId = "C11o";
            map.minX = 0;
            map.maxX = 220;
            map.cameraMinX = 9;
            map.cameraMaxX = 211;
            map.cameraY = 2.5f;
            map.cameraFollowSharpness = 10f;
            map.groundColor = new Color(0.36f, 0.38f, 0.42f);
            map.solids = new List<BoxData> { new BoxData { min = new Vector2(-1, -2), max = new Vector2(221, 0) } };
            map.spawns = new List<SpawnData> { Sp("Start", 4) };
            map.enemies = new List<EnemySpawnData>();
            var x = 20f;
            void Z(string id, params (string inst, string def, float dx)[] es)
            {
                map.spawns.Add(Sp(id, x));
                foreach (var e in es) map.enemies.Add(En(e.inst, e.def, x + e.dx));
                x += 30f;
            }
            Z("O1Jump");                                                                     // jump, then a second jump in the air
            Z("O2Dash");                                                                     // air dash, no target
            Z("O3Pierce", ("pa", "bag", 1.4f), ("pb", "bag", 2.2f), ("pc", "bag", 3.0f));    // air dash through three bags in a line
            Z("O4Dive", ("da", "bag", 2.2f), ("db", "bag", 2.2f), ("dc", "bag", 2.2f));      // dive onto three bags at one point
            Z("O5Combo", ("cb", "bag", 1.0f));                                               // second attack 0.4 s after the first ends
            Z("O6Twins", ("ta", "bag", 1.0f), ("tb", "bag", 6.0f));                          // two instances of one definition, only A hit
            map.left = new EdgeData { policy = EdgePolicy.Blocked };
            map.right = new EdgeData { policy = EdgePolicy.Blocked };
            map.points = new List<PointData>();
            var world = Save(ScriptableObject.CreateInstance<WorldDefinition>(), "World_WP11Ops");
            world.maps = new List<MapDefinition> { map };
            world.startMapId = "C11o";
            world.startSpawnId = "Start";
            foreach (var o in new Object[] { map, world }) EditorUtility.SetDirty(o);
            AssetDatabase.SaveAssets();

            var move = AssetDatabase.LoadAssetAtPath<MovementConfig>(Wp01 + "/MovementConfig.asset");
            var input = AssetDatabase.LoadAssetAtPath<InputProfileConfig>(Wp01 + "/InputProfile.asset");
            var enemies = new List<EnemyDefinitionAsset> { AssetDatabase.LoadAssetAtPath<EnemyDefinitionAsset>(Wp02 + "/Enemy_Bag.asset") };
            var issues = DataValidator.Validate(world.ToCore(), move.ToCore(), input.ToCore(), air.ToCore());
            issues.AddRange(DataValidator.ValidateCombat(combat.ToCore(), cu.ToCore(), enemies.Select(e => e.ToCore()).ToList(), world.ToCore()));
            foreach (var i in issues) Debug.LogError("[Corum2] " + i);
            var lib = AssetDatabase.LoadAssetAtPath<PresentationLibrary>(Wp04 + "/PresentationLibrary.asset");
            BuildScene(world, move, input, air, combat, cu, enemies, lib, OpsScenePath);
            Debug.Log("[Corum2] WP-11 ops content built. Validation issues: " + issues.Count);
        }

        public const string R1ScenePath = "Assets/Corum2/Scenes/WP11_R1.unity";

        /// <summary>
        /// WP-11A-R1 (GPT WP-11A review): TC-08-02 entries into a still chaser's range while it is attacking, in its re-attack wait,
        /// staggered or down (the player leaves and comes back during that state); TC-08-12 the gauntlet boss parried in the air in
        /// each state; TC-UNITY-02 moves for the rendered-mesh body check; TC-UNITY-07 late external hit signals.
        /// Longer stagger / down times on two copies of the chaser give the player time to leave and re-enter (test data, TEST-AI-01).
        /// </summary>
        [MenuItem("Corum2/WP-11/Build R1 Content")]
        public static void BuildR1()
        {
            Directory.CreateDirectory(Dir);
            var post = AssetDatabase.LoadAssetAtPath<EnemyDefinitionAsset>(Dir + "/Enemy_Post.asset");
            var postWeak = AssetDatabase.LoadAssetAtPath<EnemyDefinitionAsset>(Dir + "/Enemy_PostWeak.asset");
            var gaunt = AssetDatabase.LoadAssetAtPath<EnemyDefinitionAsset>(Dir + "/Enemy_Gauntlet.asset");
            var postStun = Enemy("Enemy_PostStun", "postStun", 1000f, new Color(0.8f, 0.5f, 0.35f), AiType.Chaser);
            postStun.ai.moveSpeed = 0.01f;
            postStun.staggerTime = 1.5f;
            var postDown = Enemy("Enemy_PostDown", "postDown", 1000f, new Color(0.75f, 0.4f, 0.3f), AiType.Chaser);
            postDown.ai.moveSpeed = 0.01f;
            postDown.downTime = 2.5f;

            var map = AssetDatabase.LoadAssetAtPath<MapDefinition>(Dir + "/Map_C11r_R1.asset") ?? Save(ScriptableObject.CreateInstance<MapDefinition>(), "Map_C11r_R1");
            map.meta.domainId = "map.C11r";
            map.meta.dataVersion = "wp11-r1";
            map.meta.relatedRIds = new[] { "R-08-02", "R-08-12", "R-00-03", "R-00-05", "R-06-01" };
            map.mapId = "C11r";
            map.minX = 0;
            map.maxX = 400;
            map.cameraMinX = 9;
            map.cameraMaxX = 391;
            map.cameraY = 2.5f;
            map.cameraFollowSharpness = 10f;
            map.groundColor = new Color(0.4f, 0.37f, 0.36f);
            map.solids = new List<BoxData> { new BoxData { min = new Vector2(-1, -2), max = new Vector2(401, 0) } };
            map.spawns = new List<SpawnData> { Sp("Start", 4) };
            map.enemies = new List<EnemySpawnData>();
            var x = 20f;
            void Z(string id, params (string inst, string def, float dx)[] es)
            {
                map.spawns.Add(Sp(id, x));
                foreach (var e in es) map.enemies.Add(En(e.inst, e.def, x + e.dx));
                x += 30f;
            }
            // TC-08-02: the player enters range, the chaser starts; the player leaves and re-enters while it is restricted
            Z("A2R1Attack", ("ra", "post", 4f));        // re-enter while its attack is still running
            Z("A2R1Wait", ("rw", "post", 4f));          // re-enter during its re-attack wait (attack interval 1.2 s)
            Z("A2R1Stagger", ("rs", "postStun", 4f));   // re-enter while it is staggered (1.5 s)
            Z("A2R1Down", ("rd", "postDown", 4f));      // re-enter while it is down (2.5 s)
            // TC-08-12: gauntlet boss parried in the air, once per state
            foreach (var st in new[] { "g1", "g2", "g3" }) Z("GAir" + st, ("a" + st, "gauntlet", 1.6f));
            // TC-UNITY-02: run / jump / air dash / dive into a wall; knocked back; enemies walking, knocked back, launched, down, dead
            BoxData Wall(float minX, float maxX) => new BoxData { min = new Vector2(minX, 0f), max = new Vector2(maxX, 5f) };
            map.solids.Add(Wall(x + 5f, x + 6f));
            Z("U2BodyMove");
            Z("U2BodyKnock", ("bk", "hitter", 1.2f));
            Z("U2BodyEnemy", ("bw", "hitter", 5f), ("bl", "bag", 1.2f), ("bx", "postWeak", 2.6f));
            // TC-UNITY-07: late external hit signals for an attack that is starting, active, over, or a cast cancelled by a hit
            Z("U7Late", ("lb", "bag", 1f), ("lf", "bag", 5f));
            Z("U7LateCast", ("lc", "hitter", 1.2f));
            map.left = new EdgeData { policy = EdgePolicy.Blocked };
            map.right = new EdgeData { policy = EdgePolicy.Blocked };
            map.points = new List<PointData>();

            var world = Save(ScriptableObject.CreateInstance<WorldDefinition>(), "World_WP11R1");
            world.maps = new List<MapDefinition> { map };
            world.startMapId = "C11r";
            world.startSpawnId = "Start";
            var own = new List<EnemyDefinitionAsset> { post, postWeak, gaunt, postStun, postDown };
            foreach (var o in own.Cast<Object>().Concat(new Object[] { map, world })) EditorUtility.SetDirty(o);
            AssetDatabase.SaveAssets();

            var move = AssetDatabase.LoadAssetAtPath<MovementConfig>(Wp01 + "/MovementConfig.asset");
            var input = AssetDatabase.LoadAssetAtPath<InputProfileConfig>(Wp01 + "/InputProfile.asset");
            var air = AssetDatabase.LoadAssetAtPath<AirUnlockFixture>(Wp01 + "/AirUnlocks_All.asset");
            var combat = AssetDatabase.LoadAssetAtPath<CombatConfig>(Wp02 + "/CombatConfig.asset");
            var cu = AssetDatabase.LoadAssetAtPath<CombatUnlockFixture>(Wp02 + "/CombatUnlocks_None.asset");
            var enemies = new List<EnemyDefinitionAsset> { AssetDatabase.LoadAssetAtPath<EnemyDefinitionAsset>(Wp02 + "/Enemy_Hitter.asset"),
                AssetDatabase.LoadAssetAtPath<EnemyDefinitionAsset>(Wp02 + "/Enemy_Bag.asset") };
            enemies.AddRange(own);
            var lib = AssetDatabase.LoadAssetAtPath<PresentationLibrary>(Wp04 + "/PresentationLibrary.asset");
            var issues = DataValidator.Validate(world.ToCore(), move.ToCore(), input.ToCore(), air.ToCore());
            issues.AddRange(DataValidator.ValidateCombat(combat.ToCore(), cu.ToCore(), enemies.Select(e => e.ToCore()).ToList(), world.ToCore()));
            var presIssues = PresentationLibrary.Validate(lib, enemies.Where(e => e.ai.type == AiType.Boss).Select(e => e.ToCore().Boss).ToList());
            foreach (var i in issues) Debug.LogError("[Corum2] " + i);
            foreach (var i in presIssues) Debug.LogError("[Corum2] " + i);
            BuildScene(world, move, input, air, combat, cu, enemies, lib, R1ScenePath);
            Debug.Log("[Corum2] WP-11 R1 content built. Validation issues: " + (issues.Count + presIssues.Count));
        }

        static T Copy<T>(string from, string name) where T : ScriptableObject
        {
            var path = Dir + "/" + name + ".asset";
            if (AssetDatabase.LoadAssetAtPath<T>(path) == null) AssetDatabase.CopyAsset(from, path);
            return AssetDatabase.LoadAssetAtPath<T>(path);
        }

        static BossPatternData SlowMelee(string id) => new BossPatternData
        {
            id = id, minDistance = 0f, maxDistance = 2.2f, playerState = PlayerAirCondition.Any, priority = 0,
            attack = Atk(id, 10f, 0.6f, 1.6f, 1.6f, 0.9f, 1.1f), projectile = new ProjectileData(),
        };

        static BossPhaseData Phase(string id, bool armor, LaunchResponse launch, float transitionTime, string anim, string vfx, string cam, BossPatternData pat,
            PhaseTransitionData next = null) => new BossPhaseData
        {
            id = id, attackInterval = 2f, approachSpeed = 2f, retreatSpeed = 2f, preferredMinDistance = 0.8f, preferredMaxDistance = 1.6f,
            superArmor = armor, staggerResistance = 0f, launch = launch, attackSpeed = 1f, transitionTime = transitionTime,
            transitionAnimation = anim, transitionVfx = vfx, transitionCamera = cam, guardFrequency = 0f, evadeFrequency = 0f,
            patterns = new List<BossPatternData> { pat },
            transitions = next == null ? new List<PhaseTransitionData>() : new List<PhaseTransitionData> { next },
        };

        static EnemyDefinitionAsset Boss(string asset, string id, float hp, Color c, params BossPhaseData[] phases)
        {
            var b = Enemy(asset, id, hp, c, AiType.Boss);
            b.hasAttack = false;
            b.bodyWidth = 1.4f;
            b.bodyHeight = 2.6f;
            b.launch = phases[0].launch;
            b.ai.trackRange = 12f;
            b.attackRange = 2f;
            b.firstAttackDelay = 0.5f;
            b.boss = new BossData { startPhase = phases[0].id, phases = phases.ToList() };
            return b;
        }

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
            e.meta.policyIds = new[] { "TEST-AI-01", "TEST-BOSS-11" };
            e.meta.relatedRIds = new[] { "R-08-02", "R-08-05", "R-08-09", "R-08-12" };
            e.enemyId = id;
            e.maxHp = hp;
            e.color = c;
            e.hasAttack = true;
            e.attack = Atk(id + ".atk", 10f, 0.3f);
            e.attackRange = 1.6f;
            e.attackInterval = 1.2f;
            e.firstAttackDelay = 0f;
            e.launch = LaunchResponse.Launchable;
            e.ai = new EnemyAIData { type = type, moveSpeed = 3f, trackRange = 8f };
            e.boss = new BossData();
            return e;
        }

        static void BuildScene(WorldDefinition world, MovementConfig move, InputProfileConfig input, AirUnlockFixture air, CombatConfig combat,
            CombatUnlockFixture cu, List<EnemyDefinitionAsset> enemies, PresentationLibrary lib, string scenePath = ScenePath)
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
            host.presentation = lib;
            EditorSceneManager.SaveScene(scene, scenePath);
            var scenes = EditorBuildSettings.scenes.Where(s => s.path != scenePath).ToList();
            scenes.Add(new EditorBuildSettingsScene(scenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            AssetDatabase.SaveAssets();
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
