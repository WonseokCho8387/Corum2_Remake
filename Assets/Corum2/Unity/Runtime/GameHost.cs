using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Corum2.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Corum2.Unity
{
    /// <summary>
    /// Unity host for the Core simulation (appendix 3.1): FixedUpdate advances one Core tick,
    /// Update interpolates the visuals between the last two ticks. Core never reads Unity time.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class GameHost : MonoBehaviour
    {
        public static GameHost Instance { get; private set; }

        [Header("Data (Inspector = edit path, spec 11.4)")]
        public WorldDefinition world;
        public MovementConfig movement;
        public InputProfileConfig inputProfile;
        public AirUnlockFixture unlocks;

        [Header("Combat (WP-02; leave empty for movement-only scenes)")]
        public CombatConfig combat;
        public CombatUnlockFixture combatUnlocks;
        public List<EnemyDefinitionAsset> enemies = new List<EnemyDefinitionAsset>();

        [Header("Run")]
        public string testCaseId = "manual";
        [Tooltip("Write a 'tick' log line every fixed step (trajectory logs).")]
        public bool tickSamples = true;
        public bool showHud = true;

        [Header("Camera (EXP-CAM-01)")]
        public float cameraDistance = 14f;
        public float cameraFov = 40f;

        public Simulation Sim { get; private set; }
        public string RunId { get; private set; }
        public string LogPath { get; private set; }
        public string DataSnapshotPath { get; private set; }
        public string ManifestPath { get; private set; }
        public InputCollector Collector => _collector;
        public bool Ready => Sim != null;
        public readonly List<string> ValidationErrors = new List<string>();

        InputTransport _transport;
        InputCollector _collector;
        NdjsonFileSink _sink;
        readonly TickInput _tick = new TickInput();
        WorldView _worldView;
        CombatView _combatView;
        PlayerView _playerView;
        Camera _camera;
        Text _hud;

        Vector2 _prevPos, _currPos;
        float _prevCamX, _currCamX;
        string _shownMap;
        readonly Queue<string> _recentInputs = new Queue<string>();
        float _fpsSmoothed;
        long _viewFrames;
        float _maxViewZError;

        // scripted input
        readonly List<(double at, string cmd)> _script = new List<(double, string)>();
        double _scriptStart;
        int _scriptIndex;
        public bool ScriptRunning => _scriptIndex < _script.Count;

        VirtualDevices _virtual;
        float _savedCaptureDt;
        VirtualDevices Virtual => _virtual ?? (_virtual = CreateVirtual());

        bool _captureDtSaved;

        VirtualDevices CreateVirtual()
        {
            Sim.Log.Emit("marker", "", "", "", "", "VirtualDevicesAdded", ("keyboard", VirtualDevices.KeyboardName), ("gamepad", VirtualDevices.GamepadName));
            return new VirtualDevices();
        }

        void Awake()
        {
            Instance = this;
        }

        void Start()
        {
            var worldData = world.ToCore();
            var move = movement.ToCore();
            var input = inputProfile.ToCore();
            var unlock = unlocks.ToCore();

            foreach (var issue in DataValidator.Validate(worldData, move, input, unlock))
                ValidationErrors.Add(issue.ToString());
            CombatProfile combatProfile = null;
            CombatUnlocks combatUnlock = null;
            var enemyDefs = new List<EnemyDefinition>();
            if (combat != null)
            {
                combatProfile = combat.ToCore();
                combatUnlock = combatUnlocks != null ? combatUnlocks.ToCore() : null;
                foreach (var e in enemies)
                    if (e != null) enemyDefs.Add(e.ToCore());
                foreach (var issue in DataValidator.ValidateCombat(combatProfile, combatUnlock, enemyDefs, worldData))
                    ValidationErrors.Add(issue.ToString());
            }
            BuildHud();
            if (ValidationErrors.Count > 0)
            {
                foreach (var e in ValidationErrors) Debug.LogError("[Corum2] Data validation: " + e);
                enabled = false;
                RefreshHud();
                return;
            }

            RunId = "RUN-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            var dir = RunFiles.RunDirectory();
            LogPath = Path.Combine(dir, RunId + ".ndjson");
            _sink = new NdjsonFileSink(LogPath);
            var extra = new List<ScriptableObject>();
            if (combat != null) extra.Add(combat);
            if (combatUnlocks != null) extra.Add(combatUnlocks);
            foreach (var e in enemies)
                if (e != null) extra.Add(e);
            DataSnapshotPath = RunFiles.WriteDataSnapshot(dir, RunId, world, movement, inputProfile, unlocks, extra);

            var log = new DiagLogger(new SinkTee(_sink, this)) { RunId = RunId, TestCaseId = testCaseId, TickSamples = tickSamples };
            Sim = new Simulation(worldData, move, input, unlock, log, combatProfile, combatUnlock, enemyDefs);
            _transport = new InputTransport();
            _collector = new InputCollector(inputProfile, _transport);

            _worldView = new WorldView(transform);
            _worldView.Build(worldData, world.maps);
            _playerView = new PlayerView(transform, _worldView, move.BodyWidth);
            if (Sim.Combat != null) _combatView = new CombatView(transform, _worldView, enemies);
            _camera = Camera.main;
            if (_camera != null)
            {
                _camera.orthographic = false;
                _camera.fieldOfView = cameraFov;
                _camera.transform.rotation = Quaternion.identity;
            }

            Sim.Start(Time.realtimeSinceStartupAsDouble);
            _currPos = _prevPos = ToV(Sim.Player.Position);
            _currCamX = _prevCamX = Sim.Camera.X;
            ManifestPath = WriteManifest(dir);
            Debug.Log("[Corum2] Run " + RunId + " log: " + LogPath);
        }

        void FixedUpdate()
        {
            if (Sim == null) return;
            _prevPos = _currPos;
            _prevCamX = _currCamX;
            var mapBefore = Sim.CurrentMap.Id;

            _collector.Fill(_tick);
            Sim.Step(_tick, Time.fixedDeltaTime);

            _currPos = ToV(Sim.Player.Position);
            _currCamX = Sim.Camera.X;
            if (Sim.CurrentMap.Id != mapBefore)
            {
                // Immediate screen switch (R-01-03/04): no interpolation across maps.
                _prevPos = _currPos;
                _prevCamX = _currCamX;
            }
        }

        void Update()
        {
            if (Sim == null)
            {
                RefreshHud();
                return;
            }
            RunScript();

            var alpha = Mathf.Clamp01((float)((Time.timeAsDouble - Time.fixedTimeAsDouble) / Time.fixedDeltaTime));
            var map = Sim.CurrentMap;
            if (_shownMap != map.Id)
            {
                _worldView.Show(map.Id);
                _shownMap = map.Id;
            }
            var p = Vector2.Lerp(_prevPos, _currPos, alpha);
            var z = map.CombatPlaneZ;
            _playerView.Apply(new Vector3(p.x, p.y, z), Sim.Player.Height, Sim.Player.Facing, Sim.Player.State);
            // TC-01-01 / TC-UNITY-02: the rendered player must stay on the combat plane.
            if (_combatView != null) _combatView.Sync(Sim.Combat, z);
            _viewFrames++;
            _maxViewZError = Mathf.Max(_maxViewZError, Mathf.Abs(_playerView.Root.position.z - z));
            if (_camera != null)
            {
                var cx = Mathf.Lerp(_prevCamX, _currCamX, alpha);
                _camera.transform.position = new Vector3(cx, Sim.Camera.Y, z - cameraDistance);
            }

            var dt = Time.unscaledDeltaTime;
            if (dt > 0f) _fpsSmoothed = _fpsSmoothed <= 0f ? 1f / dt : Mathf.Lerp(_fpsSmoothed, 1f / dt, 0.05f);
            RefreshHud();
        }

        // ---------------------------------------------------------------- frame capture (video evidence)

        /// <summary>Set by the Editor evidence recorder. Receives one frame per rendered frame while set.</summary>
        public Action<Texture2D> FrameSink;
        public int CaptureWidth = 1280;
        public int CaptureHeight = 800;
        RenderTexture _captureRt;
        Texture2D _captureTex;
        Canvas _hudCanvas;

        void LateUpdate()
        {
            if (FrameSink == null || _camera == null) return;
            if (_captureRt == null || _captureRt.width != CaptureWidth || _captureRt.height != CaptureHeight)
            {
                _captureRt = new RenderTexture(CaptureWidth, CaptureHeight, 24, RenderTextureFormat.ARGB32);
                _captureTex = new Texture2D(CaptureWidth, CaptureHeight, TextureFormat.RGBA32, false);
            }
            // HUD into the camera image: overlay canvases are not drawn by a camera render.
            if (_hudCanvas != null && _hudCanvas.renderMode != RenderMode.ScreenSpaceCamera)
            {
                _hudCanvas.renderMode = RenderMode.ScreenSpaceCamera;
                _hudCanvas.worldCamera = _camera;
                _hudCanvas.planeDistance = 1f;
            }
            var prevTarget = _camera.targetTexture;
            var prevAspect = _camera.aspect;
            _camera.targetTexture = _captureRt;
            _camera.aspect = (float)CaptureWidth / CaptureHeight;
            _camera.Render();
            var prevActive = RenderTexture.active;
            RenderTexture.active = _captureRt;
            _captureTex.ReadPixels(new Rect(0, 0, CaptureWidth, CaptureHeight), 0, 0);
            _captureTex.Apply(false);
            RenderTexture.active = prevActive;
            _camera.targetTexture = prevTarget;
            _camera.aspect = prevAspect;
            FrameSink(_captureTex);
        }

        void OnDestroy()
        {
            _collector?.Dispose();
            _virtual?.Dispose();
            if (Sim != null)
            {
                Sim.Log.Emit("view.planeCheck", "player", "", "", "", "RenderedPlayerDepth",
                    ("frames", _viewFrames), ("maxAbsZError", _maxViewZError), ("planeZ", Sim.CurrentMap.CombatPlaneZ));
                Sim.Log.Emit("run.end", "", "", "", "", "HostDestroyed", ("ticks", Sim.Tick), ("transitions", Sim.TransitionCount));
            }
            _sink?.Dispose();
            if (Instance == this) Instance = null;
        }

        static Vector2 ToV(Vec2 v) => new Vector2(v.X, v.Y);

        // ---------------------------------------------------------------- scripted input

        /// <summary>
        /// Runs a timed input script for evidence runs. Format: "t:cmd;t:cmd" where t is seconds from now and cmd is
        /// dir=Right | press=Jump | release=Jump | tap=Jump | tc=TC-03-02 (sets testCaseId) | mark=text.
        /// </summary>
        public void RunInputScript(string script)
        {
            _script.Clear();
            _scriptIndex = 0;
            // Script times are game seconds, so a recorder that locks frame time keeps the sequence intact.
            _scriptStart = Time.timeAsDouble;
            foreach (var part in script.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var i = part.IndexOf(':');
                if (i < 0) continue;
                _script.Add((double.Parse(part.Substring(0, i).Trim(), CultureInfo.InvariantCulture), part.Substring(i + 1).Trim()));
            }
            _script.Sort((a, b) => a.at.CompareTo(b.at));
            Sim.Log.Emit("script.start", "", "", "", "", "InputScript", ("script", script));
        }

        string _awaitEnemy;
        string _awaitState;

        void RunScript()
        {
            if (_awaitEnemy != null)
            {
                // Script clock is frozen until the awaited enemy state appears (deterministic evidence timing).
                var en = Sim.Combat?.Find(_awaitEnemy);
                if (en == null || en.State.ToString() != _awaitState)
                {
                    _scriptStart += Time.deltaTime;
                    return;
                }
                Sim.Log.Emit("marker", "", _awaitEnemy, "", _awaitState, "AwaitMet", ("enemyState", _awaitState));
                _awaitEnemy = null;
            }
            var now = Time.timeAsDouble - _scriptStart;
            while (_awaitEnemy == null && _scriptIndex < _script.Count && _script[_scriptIndex].at <= now)
            {
                Exec(_script[_scriptIndex].cmd);
                _scriptIndex++;
                if (_scriptIndex == _script.Count) Sim.Log.Emit("script.end", "", "", "", "", "InputScriptDone");
            }
        }

        void Exec(string cmd)
        {
            var eq = cmd.IndexOf('=');
            var verb = eq < 0 ? cmd : cmd.Substring(0, eq);
            var arg = eq < 0 ? "" : cmd.Substring(eq + 1);
            switch (verb)
            {
                case "dir":
                    _collector.InjectDir((Dir8)Enum.Parse(typeof(Dir8), arg));
                    break;
                case "press":
                    _collector.InjectButton((LogicalAction)Enum.Parse(typeof(LogicalAction), arg), InputEdge.Press);
                    break;
                case "release":
                    _collector.InjectButton((LogicalAction)Enum.Parse(typeof(LogicalAction), arg), InputEdge.Release);
                    break;
                case "tap":
                    var a = (LogicalAction)Enum.Parse(typeof(LogicalAction), arg);
                    _collector.InjectButton(a, InputEdge.Press);
                    _collector.InjectButton(a, InputEdge.Release);
                    break;
                case "tc":
                    Sim.Log.TestCaseId = arg;
                    Sim.Log.Emit("marker", "", "", "", "", "TestCase", ("testCaseId", arg));
                    break;
                case "await":
                {
                    // await=<enemyInstanceId>:<EnemyState>
                    var kv = arg.Split(':');
                    _awaitEnemy = kv[0];
                    _awaitState = kv[1];
                    return;
                }
                case "mark":
                    Sim.Log.Emit("marker", "", "", "", "", arg);
                    break;
                case "unlock":
                    // unlock=1,1,0 -> extraJump, airDash, dive (test fixture switch, spec 11.6)
                    var f = arg.Split(',');
                    Sim.Unlocks.ExtraJump = f[0] == "1";
                    Sim.Unlocks.AirDash = f[1] == "1";
                    Sim.Unlocks.Dive = f[2] == "1";
                    Sim.Log.Emit("marker", "", "", "", "", "UnlockFixtureChanged", ("unlocks", arg));
                    break;
                case "spawn":
                    // spawn=<spawnId> on the current map (test setup)
                    var s = Sim.CurrentMap.FindSpawn(arg);
                    if (s != null) Sim.Player.PlaceAt(Sim.CurrentMap, s, "ScriptSpawn");
                    break;
                case "flush":
                    _sink?.Flush();
                    break;
                // Device-level input (virtual keyboard/gamepad through Input System bindings), WP-01-R1 F-03.
                case "kb":
                {
                    var kv = arg.Split(':');
                    Virtual.Key(kv[0], kv[1] == "down");
                    break;
                }
                case "kbtap":
                    Virtual.Key(arg, true);
                    Virtual.Key(arg, false);
                    break;
                case "pad":
                {
                    var xy = arg.Split(',');
                    Virtual.Stick(float.Parse(xy[0], CultureInfo.InvariantCulture), float.Parse(xy[1], CultureInfo.InvariantCulture));
                    break;
                }
                case "padbtn":
                {
                    var kv = arg.Split(':');
                    Virtual.Button(kv[0], kv[1] == "down");
                    break;
                }
                case "padtap":
                    Virtual.Button(arg, true);
                    Virtual.Button(arg, false);
                    break;
                case "framedt":
                    // Test-only: frame length in game seconds (e.g. 0.1 = five fixed ticks per rendered frame).
                    if (!_captureDtSaved)
                    {
                        _savedCaptureDt = Time.captureDeltaTime;
                        _captureDtSaved = true;
                    }
                    Time.captureDeltaTime = arg == "restore" ? _savedCaptureDt : float.Parse(arg, CultureInfo.InvariantCulture);
                    Sim.Log.Emit("marker", "", "", "", "", "FrameDeltaChanged", ("captureDeltaTime", Time.captureDeltaTime));
                    break;
                default:
                    Debug.LogWarning("[Corum2] Unknown script command: " + cmd);
                    break;
            }
        }

        // ---------------------------------------------------------------- HUD / manifest

        sealed class SinkTee : IDiagSink
        {
            readonly IDiagSink _inner;
            readonly GameHost _host;

            public SinkTee(IDiagSink inner, GameHost host)
            {
                _inner = inner;
                _host = host;
            }

            public void Write(DiagEvent e)
            {
                _inner.Write(e);
                if (e.Type == "tick") return;
                if (e.Type == "input" && (e.Reason == "dir" || e.Reason == "release")) return;
                var line = e.SimulationTick + " " + e.Type + " " + e.Reason;
                _host._recentInputs.Enqueue(line.Length > 70 ? line.Substring(0, 70) : line);
                while (_host._recentInputs.Count > 8) _host._recentInputs.Dequeue();
            }
        }

        void BuildHud()
        {
            if (!showHud) return;
            var canvasGo = new GameObject("DebugHud", typeof(Canvas), typeof(CanvasScaler));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _hudCanvas = canvas;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 800);
            var textGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
            textGo.transform.SetParent(canvasGo.transform, false);
            var rt = (RectTransform)textGo.transform;
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(12, -10);
            rt.sizeDelta = new Vector2(1000, 560);
            _hud = textGo.GetComponent<Text>();
            _hud.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _hud.fontSize = 19;
            _hud.color = Color.white;
            _hud.raycastTarget = false;
            var shadow = textGo.AddComponent<Shadow>();
            shadow.effectColor = new Color(0, 0, 0, 0.9f);
        }

        void RefreshHud()
        {
            if (_hud == null) return;
            var sb = new StringBuilder(512);
            if (ValidationErrors.Count > 0)
            {
                sb.AppendLine("<color=#ff6060>DATA VALIDATION FAILED — run stopped</color>");
                foreach (var e in ValidationErrors) sb.AppendLine(e);
                _hud.text = sb.ToString();
                return;
            }
            var p = Sim.Player;
            sb.AppendFormat("<size=30><b>{0}</b>   {1}   tick {2}</size>\n", Sim.Log.TestCaseId, p.State, Sim.Tick);
            sb.Append("experimental build    ").Append(RunId).AppendLine();
            sb.AppendFormat(CultureInfo.InvariantCulture, "map {0}   tick {1}   fps {2:0}   sim {3:0}Hz\n", Sim.CurrentMap.Id, Sim.Tick, _fpsSmoothed, 1f / Time.fixedDeltaTime);
            sb.AppendFormat(CultureInfo.InvariantCulture, "state {0}   grounded {1}   facing {2}   pos {3}   vel ({4:0.00}, {5:0.00})\n",
                p.State, p.Grounded, p.Facing > 0 ? "R" : "L", p.Position, p.VelX, p.VelY);
            sb.AppendFormat("dir {0}   runDir {1}   air {2}\n", p.HeldDir, p.RunDir, p.Air.Describe());
            sb.AppendFormat("unlocks jump={0} dash={1} dive={2}   test case {3}\n", Sim.Unlocks.ExtraJump, Sim.Unlocks.AirDash, Sim.Unlocks.Dive, Sim.Log.TestCaseId);
            var c = Sim.Combat;
            if (c != null)
            {
                sb.AppendFormat(CultureInfo.InvariantCulture, "<size=24><b>HP {0:0}   COMBO {1}   step {2}</b></size>  chain {3}{4}  last end {5} ({6})\n",
                    c.PlayerHp, c.ComboHits, c.ChainSteps, c.ChainId, c.ChainAlive ? " alive" : "", c.LastChainEndReason, c.LastComboHits);
                sb.Append("enemies ").AppendLine(c.DescribeEnemies());
            }
            sb.AppendLine("<color=#aaaaaa>pad: stick move, A jump, X attack, Y special(EXP-INPUT-01) | kb: WASD/arrows, Space, J, K</color>");
            foreach (var l in _recentInputs) sb.AppendLine("<color=#c8e6ff>" + l + "</color>");
            _hud.text = sb.ToString();
        }

        string WriteManifest(string dir)
        {
            var path = Path.Combine(dir, RunId + ".manifest.yaml");
            var dataHash = RunFiles.FileSha256(DataSnapshotPath);
            var sb = new StringBuilder();
            sb.AppendLine("# Run manifest (spec 13.3). Fields Claude fills in the work packet are marked <packet>.");
            sb.AppendLine("run_id: " + RunId);
            sb.AppendLine("spec_id: CORUM2-P0-MASTER");
            sb.AppendLine("spec_version: 1.2.0");
            sb.AppendLine("engine_appendix_version: 1.0.0");
            sb.AppendLine("spec_sha256: <packet>");
            sb.AppendLine("engine_appendix_sha256: <packet>");
            sb.AppendLine("build_id: " + (Application.isEditor ? "editor-playmode" : Application.version));
            sb.AppendLine("source_revision: <packet>");
            sb.AppendLine("source_capture_mode: git");
            sb.AppendLine("engine: Unity");
            sb.AppendLine("engine_version: " + Application.unityVersion);
            sb.AppendLine("platform_os: " + SystemInfo.operatingSystem);
            sb.AppendLine("device_input: InputProfile " + inputProfile.meta.dataVersion + " (EXP-INPUT-01/02/03)");
            sb.AppendLine("display: " + Screen.width + "x" + Screen.height + " @" + Screen.currentResolution.refreshRateRatio.value.ToString("0.##", CultureInfo.InvariantCulture) + "Hz");
            sb.AppendLine("render_fps_observed: see HUD / tick log");
            sb.AppendLine("simulation_tick: " + Time.fixedDeltaTime.ToString("R", CultureInfo.InvariantCulture) + " s (" + (1f / Time.fixedDeltaTime).ToString("0.##", CultureInfo.InvariantCulture) + " Hz game time)");
            sb.AppendLine("time_policy_version: none-approved (U-06); input double tap on raw time EXP-TIME-01");
            sb.AppendLine("data_snapshot: " + Path.GetFileName(DataSnapshotPath) + " sha256 " + dataHash);
            sb.AppendLine("data_version: \"" + Sim.Log.DataVersion + "\"");
            sb.AppendLine("experimental_policies:");
            foreach (var e in Simulation.ExperimentalPolicies) sb.AppendLine("  - \"" + e + "\"");
            sb.AppendLine("scene_map: " + UnityEngine.SceneManagement.SceneManager.GetActiveScene().path + " start " + world.startMapId + "/" + world.startSpawnId);
            sb.AppendLine("save_fixture: none");
            sb.AppendLine("random_seed: not used");
            sb.AppendLine("started_at: " + DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture));
            sb.AppendLine("tester: <packet>");
            sb.AppendLine("commands_or_steps: <packet>");
            sb.AppendLine("log: " + Path.GetFileName(LogPath));
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            return path;
        }
    }
}
