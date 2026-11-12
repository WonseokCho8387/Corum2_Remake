using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
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

        [Header("Ring / quickslots (WP-03; leave empty for scenes without items)")]
        public RingConfig ring;

        [Header("Boss transition presentation (R-08-11, EXP-PRES-01; needed when a boss phase has presentation ids)")]
        public PresentationLibrary presentation;

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
        /// <summary>Events the ring router forwarded to the game (ring scenes only); drained by FixedUpdate.</summary>
        InputTransport _gameTransport;
        readonly TickInput _uiTick = new TickInput();
        readonly List<InputEvent> _routed = new List<InputEvent>();
        readonly List<StickSample> _samples = new List<StickSample>();
        RingProfile _ringProfile;
        RingView _ringView;
        float _appliedTimeScale = 1f;
        InputCollector _collector;
        NdjsonFileSink _sink;
        readonly TickInput _tick = new TickInput();
        WorldView _worldView;
        CombatView _combatView;
        DeathView _deathView;
        BossPresentationView _presentationView;
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
        // WP09-M01: rendered player frames per input direction fed to the simulation, and while airborne.
        readonly long[] _dirFrames = new long[9];
        readonly float[] _dirMaxZ = new float[9];
        long _airFrames;
        float _airMaxZ;

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
            var v = new VirtualDevices();
            AnnotateManifest("input_devices", DescribeDevices());
            AnnotateManifest("input_devices_note", "scripted input goes through " + VirtualDevices.KeyboardName + "/" +
                VirtualDevices.GamepadName + " (Input System bindings); no physical device or Steam Deck was used by the script");
            return v;
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
                var bosses = new List<BossDefinition>();
                foreach (var d in enemyDefs)
                    if (d.Ai != null && d.Ai.Type == AiType.Boss && d.Boss != null) bosses.Add(d.Boss);
                foreach (var issue in PresentationLibrary.Validate(presentation, bosses)) ValidationErrors.Add(issue);
            }
            if (ring != null)
            {
                _ringProfile = ring.ToCore();
                foreach (var issue in RingValidator.Validate(_ringProfile)) ValidationErrors.Add(issue.ToString());
                if (combat == null) ValidationErrors.Add("RingConfig: ring scenes need combat data (player HP for heal items)");
            }
            BuildHud();
            if (ValidationErrors.Count > 0)
            {
                foreach (var e in ValidationErrors) Debug.LogError("[Corum2] Data validation: " + e);
                enabled = false;
                RefreshHud();
                return;
            }

            // M01-R1-B: one clock read gives both the run id and started_at; rewrites never change them.
            var startedAt = DateTimeOffset.Now;
            RunId = "RUN-" + startedAt.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            _manifest = new RunManifest(RunId, startedAt);
            var dir = RunFiles.RunDirectory();
            LogPath = Path.Combine(dir, RunId + ".ndjson");
            _sink = new NdjsonFileSink(LogPath);
            var extra = new List<ScriptableObject>();
            if (combat != null) extra.Add(combat);
            if (combatUnlocks != null) extra.Add(combatUnlocks);
            foreach (var e in enemies)
                if (e != null) extra.Add(e);
            if (ring != null) extra.Add(ring);
            if (presentation != null) extra.Add(presentation);
            DataSnapshotPath = RunFiles.WriteDataSnapshot(dir, RunId, world, movement, inputProfile, unlocks, extra);

            var log = new DiagLogger(new SinkTee(_sink, this)) { RunId = RunId, TestCaseId = testCaseId, TickSamples = tickSamples };
            Sim = new Simulation(worldData, move, input, unlock, log, combatProfile, combatUnlock, enemyDefs, _ringProfile);
            _transport = new InputTransport();
            if (Sim.Router != null) _gameTransport = new InputTransport();
            _collector = new InputCollector(inputProfile, _transport);

            _worldView = new WorldView(transform);
            _worldView.Build(worldData, world.maps);
            _playerView = new PlayerView(transform, _worldView, move.BodyWidth);
            if (Sim.Combat != null)
            {
                _combatView = new CombatView(transform, _worldView, enemies);
                _presentationView = new BossPresentationView(transform, _combatView, presentation);
            }
            if (Sim.Ring != null)
            {
                if (_hudCanvas == null) BuildCanvas();
                _ringView = new RingView(_hudCanvas.transform);
            }
            if (Sim.Death != null)
            {
                if (_hudCanvas == null) BuildCanvas();
                _deathView = new DeathView(_hudCanvas.transform);
            }
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

            if (_gameTransport == null) _collector.Fill(_tick);
            else
            {
                // Ring scenes: the game only sees what the router forwarded; the stick is the ring cursor while open.
                _gameTransport.Drain(_tick);
                _tick.Dir = Sim.Router.GameDir;
                var open = Sim.Ring.IsOpen;
                _tick.StickX = open ? 0f : _collector.Stick.x;
                _tick.StickY = open ? 0f : _collector.Stick.y;
                _tick.Device = _collector.LastDevice;
                _tick.RealTime = Time.realtimeSinceStartupAsDouble;
            }
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
            RouteUi();

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
            if (_presentationView != null) _presentationView.Sync(Sim, z, Time.fixedDeltaTime);
            _viewFrames++;
            var pz = Mathf.Abs(_playerView.Root.position.z - z);
            _maxViewZError = Mathf.Max(_maxViewZError, pz);
            var di = (int)_tick.Dir;
            if (di >= 0 && di < 9) { _dirFrames[di]++; _dirMaxZ[di] = Mathf.Max(_dirMaxZ[di], pz); }
            if (!Sim.Player.Grounded) { _airFrames++; _airMaxZ = Mathf.Max(_airMaxZ, pz); }
            if (_camera != null)
            {
                var cx = Mathf.Lerp(_prevCamX, _currCamX, alpha);
                // EXP-PRES-01: the camera cue only offsets the view; the combat plane z and judgement are unchanged.
                var co = _presentationView != null ? _presentationView.CameraOffset : Vector2.zero;
                _camera.transform.position = new Vector3(cx + co.x, Sim.Camera.Y + co.y, z - cameraDistance);
                _camera.fieldOfView = cameraFov + (_presentationView != null ? _presentationView.FovDelta : 0f);
            }

            if (_ringView != null && _camera != null)
            {
                var vp = _camera.WorldToViewportPoint(new Vector3(p.x, p.y + Sim.Player.Height * 0.5f, z));
                _ringView.Apply(Sim, _ringProfile, new Vector2(vp.x, vp.y), Time.timeScale);
            }

            _deathView?.Apply(Sim);

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

        /// <summary>
        /// Ring scenes: routes this frame's input at real time (ring / quickslot / game), feeds the ring cursor and sets
        /// the world time scale (R-02-03, EXP-RING-01). Runs every rendered frame, so the ring keeps normal speed.
        /// </summary>
        void RouteUi()
        {
            if (_gameTransport == null) return;
            var now = Time.realtimeSinceStartupAsDouble;
            _transport.Drain(_uiTick);
            _collector.DrainSamples(_samples);
            _routed.Clear();
            // WP03-F02: stick samples and presses are replayed in the order they happened.
            Sim.RouteFrame(_uiTick.Events, _samples, _routed, now);
            foreach (var e in _routed) _gameTransport.Enqueue(e);
            var want = Sim.Ring.IsOpen ? _ringProfile.SlowScale : 1f;
            if (!Mathf.Approximately(want, _appliedTimeScale))
            {
                Time.timeScale = want;
                Sim.Log.RealTime = now;
                Sim.Log.Emit("time.scale", "", "", _appliedTimeScale.ToString("0.###", CultureInfo.InvariantCulture),
                    want.ToString("0.###", CultureInfo.InvariantCulture), Sim.Ring.IsOpen ? "RingOpen(R-02-03, EXP-RING-01)" : "RingClosed(R-02-03)",
                    ("timeScale", want), ("fixedDeltaTime", Time.fixedDeltaTime), ("unscaledTime", Time.unscaledTimeAsDouble),
                    ("gameTime", Sim.GameTime), ("ringOpenCount", Sim.Ring.OpenCount));
                _appliedTimeScale = want;
            }
        }

        void OnDestroy()
        {
            if (_gameTransport != null) Time.timeScale = 1f;
            _collector?.Dispose();
            _virtual?.Dispose();
            if (Sim != null)
            {
                Sim.Log.Emit("view.planeCheck", "player", "", "", "", "RenderedPlayerDepth",
                    ("frames", _viewFrames), ("maxAbsZError", _maxViewZError), ("planeZ", Sim.CurrentMap.CombatPlaneZ),
                    ("dirFrames", DirTable(_dirFrames)), ("dirMaxAbsZError", DirTable(_dirMaxZ)), ("airFrames", _airFrames), ("airMaxAbsZError", _airMaxZ));
                if (_combatView != null)
                {
                    var cv = _combatView;
                    Sim.Log.Emit("view.planeCheck", "enemies", "", "", "", "RenderedEnemyDepth(WP09-M01)",
                        ("samples", cv.EnemySamples), ("bossSamples", cv.BossSamples), ("bossMovingSamples", cv.BossMovingSamples),
                        ("firstSamples", cv.FirstSamples), ("airborneSamples", cv.AirborneSamples), ("teleportSamples", cv.TeleportSamples),
                        ("groundMovingSamples", cv.GroundMovingSamples), ("groundStillSamples", cv.GroundStillSamples),
                        ("propelledSamples", cv.PropelledSamples), ("teleportDx", CombatView.TeleportDx),
                        ("maxAbsZError", cv.MaxEnemyZError), ("maxAbsZFirst", cv.MaxZFirst), ("maxAbsZAirborne", cv.MaxZAirborne),
                        ("maxAbsZTeleport", cv.MaxZTeleport), ("maxAbsZGroundMoving", cv.MaxZGroundMoving), ("maxAbsZGroundStill", cv.MaxZGroundStill),
                        ("planeZ", Sim.CurrentMap.CombatPlaneZ));
                }
                Sim.Log.Emit("run.end", "", "", "", "", "HostDestroyed", ("ticks", Sim.Tick), ("transitions", Sim.TransitionCount));
            }
            _sink?.Dispose();
            if (Instance == this) Instance = null;
        }

        static Vector2 ToV(Vec2 v) => new Vector2(v.X, v.Y);

        static string DirTable<T>(T[] values)
        {
            var parts = new List<string>();
            for (var i = 0; i < values.Length; i++)
                parts.Add(((Dir8)i) + ":" + string.Format(CultureInfo.InvariantCulture, "{0}", values[i]));
            return string.Join(",", parts);
        }

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
            // Stable: equal times keep their written order (e.g. spawn before await).
            var ordered = _script.Select((x, i) => (x, i)).OrderBy(p => p.x.at).ThenBy(p => p.i).Select(p => p.x).ToList();
            _script.Clear();
            _script.AddRange(ordered);
            Sim.Log.Emit("script.start", "", "", "", "", "InputScript", ("script", script));

            // WP02-M01: the command list goes next to the run files and the manifest names it.
            var cmdPath = Path.Combine(Path.GetDirectoryName(LogPath), RunId + ".commands.txt");
            File.WriteAllText(cmdPath, "# GameHost.RunInputScript input (game seconds:command), started at game time " +
                _scriptStart.ToString("R", CultureInfo.InvariantCulture) + "\n" + string.Join("\n", script.Split(';')) + "\n", new UTF8Encoding(false));
            _tester = "Claude Code (automated: GameHost.RunInputScript)";
            _commands = Path.GetFileName(cmdPath);
            _commandsHash = RunFiles.FileSha256(cmdPath);
            AnnotateManifest("input_devices", DescribeDevices());
        }

        string _tester = "manual (Editor Play mode)";
        string _commands = "manual play; every input is in the log (input.* events)";
        string _commandsHash;
        RunManifest _manifest;
        readonly List<(string key, object value)> _manifestExtra = new List<(string, object)>();

        /// <summary>Adds or replaces a manifest field and rewrites the manifest (evidence recorder, script start).
        /// Values are typed: string, bool, number, null, a list or a <see cref="YamlMap"/>.</summary>
        public void AnnotateManifest(string key, object value)
        {
            _manifestExtra.RemoveAll(x => x.key == key);
            _manifestExtra.Add((key, value));
            if (ManifestPath != null) WriteManifest(Path.GetDirectoryName(ManifestPath));
        }

        static List<YamlMap> DescribeDevices()
        {
            var list = new List<YamlMap>();
            foreach (var d in UnityEngine.InputSystem.InputSystem.devices)
                list.Add(new YamlMap().Add("name", d.name).Add("layout", d.layout).Add("virtual", !d.native));
            return list;
        }

        string _awaitEnemy;
        string _awaitState;
        double _awaitLimit;

        void RunScript()
        {
            if (_awaitEnemy != null)
            {
                // Script clock is frozen until the awaited enemy state appears (deterministic evidence timing).
                var en = Sim.Combat?.Find(_awaitEnemy);
                if (en == null || en.State.ToString() != _awaitState)
                {
                    _awaitLimit -= Time.deltaTime;
                    if (_awaitLimit > 0)
                    {
                        _scriptStart += Time.deltaTime;
                        return;
                    }
                    Sim.Log.Emit("marker", "", _awaitEnemy, "", en != null ? en.State.ToString() : "missing", "AwaitTimeout", ("awaited", _awaitState));
                    _awaitEnemy = null;
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
                    // await=<enemyInstanceId>:<EnemyState>[:<timeoutSeconds, default 5>]
                    var kv = arg.Split(':');
                    _awaitEnemy = kv[0];
                    _awaitState = kv[1];
                    _awaitLimit = kv.Length > 2 ? double.Parse(kv[2], CultureInfo.InvariantCulture) : 5.0;
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
                case "cunlock":
                    // cunlock=1,0 -> dash pierce, dive area (combat unlock test fixture switch, spec 11.6)
                    var cf = arg.Split(',');
                    Sim.Combat.Unlocks.DashPierce = cf[0] == "1";
                    Sim.Combat.Unlocks.DiveArea = cf[1] == "1";
                    Sim.Log.Emit("marker", "", "", "", "", "CombatUnlockFixtureChanged", ("combatUnlocks", arg));
                    break;
                case "bossevent":
                {
                    // bossevent=<enemyInstanceId|*>:<eventName> (R-08-10 event condition, test trigger)
                    var kv = arg.Split(':');
                    Sim.Combat?.RaiseBossEvent(kv[0], kv[1]);
                    break;
                }
                case "hp":
                    // hp=<value>: test-only player HP set (evidence setup), logged
                    if (Sim.Combat != null)
                    {
                        var before = Sim.Combat.PlayerHp;
                        Sim.Combat.PlayerHp = float.Parse(arg, CultureInfo.InvariantCulture);
                        Sim.Log.Emit("marker", "player", "", "", "", "TestSetPlayerHp", ("hpBefore", before), ("hpAfter", Sim.Combat.PlayerHp));
                    }
                    break;
                case "ehp":
                {
                    // ehp=<enemyInstanceId>:<hp>: test-only enemy HP set (evidence setup, e.g. to reach a boss HP phase), logged
                    var kv = arg.Split(':');
                    var en = Sim.Combat?.Find(kv[0]);
                    if (en != null)
                    {
                        var before = en.Hp;
                        en.Hp = float.Parse(kv[1], CultureInfo.InvariantCulture);
                        Sim.Log.Emit("marker", "", en.Id, "", en.State.ToString(), "TestSetEnemyHp", ("hpBefore", before), ("hpAfter", en.Hp), ("maxHp", en.Def.MaxHp));
                    }
                    break;
                }
                case "item":
                {
                    // item=<itemId>:<count>: test-only inventory set (evidence setup), logged
                    var kv = arg.Split(':');
                    var inv = Sim.Items?.Inventory;
                    if (inv != null)
                    {
                        var before = inv.Count(kv[0]);
                        inv.TestSetCount(kv[0], int.Parse(kv[1], CultureInfo.InvariantCulture));
                        Sim.Log.Emit("marker", "player", kv[0], "", "", "TestSetItemCount", ("countBefore", before), ("countAfter", inv.Count(kv[0])),
                            ("inventory", inv.Describe()));
                    }
                    break;
                }
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
                case "padsame":
                {
                    // padsame=x,y|x,y|...: separate stick samples queued with one shared device time (WP05-F01 R2 evidence).
                    var parts = arg.Split('|');
                    var v = new Vector2[parts.Length];
                    for (var i = 0; i < parts.Length; i++)
                    {
                        var xy = parts[i].Split(',');
                        v[i] = new Vector2(float.Parse(xy[0], CultureInfo.InvariantCulture), float.Parse(xy[1], CultureInfo.InvariantCulture));
                    }
                    var t = Virtual.SticksSameTime(v);
                    Sim.Log.Emit("test.padsame", "script", "", "", "", "SeparateSamplesSameDeviceTime(WP05-F01 R2)",
                        ("samples", arg), ("deviceTime", t));
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
            BuildCanvas();
            BuildHudText();
        }

        void BuildCanvas()
        {
            var canvasGo = new GameObject("DebugHud", typeof(Canvas), typeof(CanvasScaler));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _hudCanvas = canvas;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 800);
        }

        void BuildHudText()
        {
            var textGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
            textGo.transform.SetParent(_hudCanvas.transform, false);
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
                var cast = c.Attack?.Magic != null ? "casting " + c.Attack.Magic.Id + " (" + c.Attack.MagicSource + ")" : "-";
                var buff = c.BuffMultiplier != 1f
                    ? string.Format(CultureInfo.InvariantCulture, "buff {0} x{1:0.##} {2:0.0}s", c.BuffMagicId, c.BuffMultiplier, c.BuffUntil - Sim.GameTime)
                    : "no buff";
                var hist = Sim.Magic != null ? Sim.Magic.Describe(Sim.GameTime) : "";
                sb.AppendFormat("<color=#ffe28a>magic: {0}   {1}   dirs [{2}]{3}</color>\n", cast, buff, hist, c.ReviveInvulnerable ? "   REVIVE-INVULN" : "");
            }
            sb.AppendLine("<color=#aaaaaa>pad: stick move, A jump, X attack, Y special(EXP-INPUT-01) | kb: WASD/arrows, Space, J, K</color>");
            foreach (var l in _recentInputs) sb.AppendLine("<color=#c8e6ff>" + l + "</color>");
            _hud.text = sb.ToString();
        }

        string WriteManifest(string dir)
        {
            var path = Path.Combine(dir, RunId + ".manifest.yaml");
            var m = _manifest;
            if (m.Revision == 0)
            {
                m.Comment("Run manifest, master spec 13.3 field names. Typed YAML (WP02-M01-R1-A); run_id/started_at fixed at run creation,");
                m.Comment("updated_at/manifest_revision change on each rewrite (input script, recorder, device changes) (WP02-M01-R1-B).");
            }
            var git = _source ?? (_source = SourceCapture.Take(dir, RunId)).Value;
            var spec = SpecHash(SpecFile);
            var appendix = SpecHash(AppendixFile);
            m.Set("spec_id", "CORUM2-P0-MASTER");
            m.Set("spec_version", "1.2.0");
            m.Set("spec_file", SpecFile);
            m.Set("spec_sha256", spec.hash);
            m.Set("engine_appendix_version", "1.0.0");
            m.Set("engine_appendix_file", AppendixFile);
            m.Set("engine_appendix_sha256", appendix.hash);
            if (spec.hash == null || appendix.hash == null) m.Set("spec_hash_note", ((spec.note ?? "") + " " + (appendix.note ?? "")).Trim());
            m.Set("build_id", Application.isEditor ? "editor-playmode" : Application.version);
            m.Set("source_revision", git.rev);
            m.Set("source_capture_mode", "git");
            m.Set("working_tree_dirty", git.dirty);
            m.Set("dirty_diff_artifact", git.DirtyArtifact());
            m.Set("source_snapshot_artifact", null);
            m.Set("source_file_hash_manifest", git.FileList());
            if (git.note != null) m.Set("source_note", git.note);
            m.Set("engine", "Unity");
            m.Set("engine_version", Application.unityVersion);
            m.Set("platform_os", SystemInfo.operatingSystem);
            m.Set("device_input", "InputProfile " + inputProfile.meta.dataVersion + " (EXP-INPUT-01/02/03)");
            m.Set("display", new YamlMap().Add("width", Screen.width).Add("height", Screen.height)
                .Add("refresh_hz", Math.Round(Screen.currentResolution.refreshRateRatio.value, 2)));
            m.Set("render_fps_observed", null);
            m.Set("render_fps_note", "not measured by the manifest; recorder runs lock frame time to game time (see capture)");
            m.Set("simulation_tick", new YamlMap().Add("seconds", (double)Time.fixedDeltaTime).Add("hz", Math.Round(1.0 / Time.fixedDeltaTime, 3))
                .Add("clock", "game time"));
            m.Set("time_policy_version", "none-approved (U-06); input double tap on raw time EXP-TIME-01");
            m.Set("data_snapshot", Path.GetFileName(DataSnapshotPath));
            m.Set("data_snapshot_sha256", RunFiles.FileSha256(DataSnapshotPath));
            m.Set("data_version", Sim.Log.DataVersion);
            m.Set("experimental_policies", Simulation.ExperimentalPolicies);
            m.Set("scene_map", new YamlMap().Add("scene", UnityEngine.SceneManagement.SceneManager.GetActiveScene().path)
                .Add("start_map", world.startMapId).Add("start_spawn", world.startSpawnId));
            m.Set("save_fixture", null);
            m.Set("save_fixture_note", "no save data; every run starts from the scene's initial state");
            m.Set("random_seed", null);
            m.Set("random_seed_note", "no random numbers are used");
            m.Set("tester", _tester);
            m.Set("commands_or_steps", _commands);
            m.Set("commands_sha256", _commandsHash);
            foreach (var (k, v) in _manifestExtra) m.Set(k, v);
            m.Set("log", Path.GetFileName(LogPath));
            File.WriteAllText(path, m.Render(DateTimeOffset.Now), new UTF8Encoding(false));
            return path;
        }

        const string SpecFile = "CORUM2_P0_Master_Spec_v1.2.md";
        const string AppendixFile = "CORUM2_Unity_Implementation_Appendix_v1.0.md";

        /// <summary>Spec documents live in the sibling analysis folder (D:\ProjectR\Corum2\docs\GPT).</summary>
        static (string hash, string note) SpecHash(string file)
        {
            var p = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Corum2", "docs", "GPT", file));
            return File.Exists(p) ? (RunFiles.FileSha256(p), null) : (null, p.Replace('\\', '/') + " not found");
        }

        SourceCapture? _source;

        /// <summary>
        /// Source state at run start (master 13.3, WP02-M02): git revision/tree, dirty state, the per-file
        /// path/size/SHA-256 list of the source scope, and for a dirty tree the patch against HEAD plus copies of
        /// untracked files. Taken once, at the first manifest write.
        /// </summary>
        struct SourceCapture
        {
            public static readonly string[] Scope = { "Assets", "Packages", "ProjectSettings", "Tools" };
            /// <summary>Untracked files outside the scope above this size are not copied (recorded as skipped).
            /// Files inside the scope are always copied (WP02-M03).</summary>
            const long OutOfScopeCopyLimit = 16L * 1024 * 1024;

            public string rev, tree, note;
            public bool? dirty;
            public List<string> status;
            public string listFile, listSha;
            public int entries;
            public long totalBytes;
            public List<string> missing;
            public string patchFile, patchSha;
            public string untrackedDir;
            public List<string> untrackedCopied, untrackedSkipped, requiredUntracked, incomplete;

            public static SourceCapture Take(string runDir, string runId)
            {
                var c = new SourceCapture { missing = new List<string>() };
                var root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                var rev = Git(root, "rev-parse HEAD");
                List<string> paths;
                if (rev == null)
                {
                    c.note = "git not available; repository state unknown; file list made by walking the scope folders";
                    paths = SourceFileList.Walk(root, Scope);
                }
                else
                {
                    c.rev = rev.Trim();
                    c.tree = Git(root, "rev-parse HEAD^{tree}")?.Trim();
                    var status = Git(root, "status --porcelain");
                    if (status == null) c.note = "git status failed; dirty state unknown";
                    else
                    {
                        c.status = status.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).ToList();
                        c.dirty = c.status.Count > 0;
                    }
                    var ls = Git(root, "ls-files -z -c -o --exclude-standard -- " + string.Join(" ", Scope));
                    paths = ls == null ? SourceFileList.Walk(root, Scope) : ls.Split('\0').Where(x => x.Length > 0).ToList();
                }

                var files = SourceFileList.Hash(root, paths, c.missing);
                if (c.rev != null && files.Count > 0)
                {
                    // Git's id for the same content after its line-ending normalisation; links each row to a commit tree.
                    var blobs = Git(root, "hash-object --stdin-paths", string.Join("\n", files.Select(f => f.Path)) + "\n");
                    var ids = blobs?.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
                    if (ids != null && ids.Count == files.Count)
                        for (var i = 0; i < files.Count; i++) files[i].GitBlob = ids[i];
                }
                c.listFile = runId + ".source_files.tsv";
                var header = new List<KeyValuePair<string, string>>
                {
                    new KeyValuePair<string, string>("run_id", runId),
                    new KeyValuePair<string, string>("source_revision", c.rev ?? "unknown"),
                    new KeyValuePair<string, string>("scope", string.Join(",", Scope) + " (git tracked + untracked not ignored)"),
                    new KeyValuePair<string, string>("bytes_basis", "working tree file bytes read at run start"),
                    new KeyValuePair<string, string>("git_blob", "git hash-object of the same file (after git line-ending normalisation)"),
                    new KeyValuePair<string, string>("generated_at", RunManifest.FormatTime(DateTimeOffset.Now)),
                };
                var listPath = Path.Combine(runDir, c.listFile);
                File.WriteAllText(listPath, SourceFileList.ToTsv(files, header), new UTF8Encoding(false));
                c.listSha = RunFiles.FileSha256(listPath);
                c.entries = files.Count;
                c.totalBytes = SourceFileList.TotalBytes(files);

                if (c.dirty == true)
                {
                    c.patchFile = runId + ".dirty.patch";
                    var patch = GitBytes(root, "diff HEAD --binary");
                    var patchPath = Path.Combine(runDir, c.patchFile);
                    File.WriteAllBytes(patchPath, patch ?? new byte[0]);
                    c.patchSha = RunFiles.FileSha256(patchPath);
                    c.untrackedDir = runId + ".untracked";
                    c.untrackedCopied = new List<string>();
                    c.untrackedSkipped = new List<string>();
                    var rows = files.ToDictionary(f => f.Path, StringComparer.Ordinal);
                    var copyMatches = new Dictionary<string, bool>(StringComparer.Ordinal);
                    var untracked = Git(root, "ls-files -z -o --exclude-standard");
                    foreach (var rel in (untracked ?? "").Split('\0').Where(x => x.Length > 0))
                    {
                        var src = Path.Combine(root, rel);
                        if (!File.Exists(src)) continue;
                        var inScope = DirtyCapture.InScope(rel, Scope);
                        if (!inScope && new FileInfo(src).Length > OutOfScopeCopyLimit) { c.untrackedSkipped.Add(rel); continue; }
                        var dst = Path.Combine(runDir, c.untrackedDir, rel);
                        Directory.CreateDirectory(Path.GetDirectoryName(dst));
                        File.Copy(src, dst, true);
                        c.untrackedCopied.Add(rel);
                        if (inScope)
                        {
                            // The copy must hold the bytes the list recorded (the file could change between the two reads).
                            var bytes = File.ReadAllBytes(dst);
                            copyMatches[rel] = rows.TryGetValue(rel, out var row) && row.Size == bytes.LongLength &&
                                row.Sha256 == SourceFileList.Sha256Hex(bytes);
                        }
                    }
                    c.requiredUntracked = DirtyCapture.RequiredUntracked(c.status, files.Select(f => f.Path), Scope);
                    c.incomplete = DirtyCapture.IncompleteReasons(patch != null, c.requiredUntracked,
                        c.untrackedCopied, copyMatches);
                    if (untracked == null) c.incomplete.Add("git ls-files for untracked files failed");
                }
                return c;
            }

            public object FileList()
            {
                var map = new YamlMap().Add("file", listFile).Add("sha256", listSha).Add("format", "tsv: " + SourceFileList.Columns.Replace('\t', ' '))
                    .Add("entries", entries).Add("total_bytes", totalBytes).Add("scope", Scope.ToList())
                    .Add("bytes_basis", "working tree file bytes read at run start").Add("git_tree", tree);
                if (missing.Count > 0) map.Add("missing_paths", missing);
                return map;
            }

            public object DirtyArtifact()
            {
                if (dirty != true) return null;
                return new YamlMap().Add("status", status).Add("patch", patchFile).Add("patch_sha256", patchSha)
                    .Add("patch_basis", "git diff HEAD --binary at run start (tracked changes)")
                    .Add("untracked_dir", untrackedDir).Add("untracked_files", untrackedCopied)
                    .Add("untracked_skipped", untrackedSkipped)
                    .Add("untracked_skipped_note", "only untracked files outside the source scope larger than 16 MiB are skipped")
                    .Add("required_untracked", requiredUntracked)
                    .Add("capture_complete", incomplete.Count == 0)
                    .Add("incomplete_reasons", incomplete)
                    .Add("note", "untracked files are copied as they were at run start; copies inside the scope are checked against their list rows");
            }
        }

        /// <summary>Raw git output bytes (patches may hold non-UTF-8 text); null on failure.</summary>
        static byte[] GitBytes(string root, string args)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("git", "-c core.quotepath=off -C \"" + root + "\" " + args)
                {
                    UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
                };
                using (var p = System.Diagnostics.Process.Start(psi))
                using (var ms = new MemoryStream())
                {
                    var errTask = p.StandardError.ReadToEndAsync();
                    p.StandardOutput.BaseStream.CopyTo(ms);
                    p.WaitForExit(20000);
                    return p.HasExited && p.ExitCode == 0 ? ms.ToArray() : null;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        static string Git(string root, string args, string stdin = null)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("git", "-c core.quotepath=off -C \"" + root + "\" " + args)
                {
                    UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
                    RedirectStandardInput = stdin != null, StandardOutputEncoding = new UTF8Encoding(false),
                };
                using (var p = System.Diagnostics.Process.Start(psi))
                {
                    var outTask = p.StandardOutput.ReadToEndAsync();
                    var errTask = p.StandardError.ReadToEndAsync();
                    if (stdin != null)
                    {
                        p.StandardInput.Write(stdin);
                        p.StandardInput.Close();
                    }
                    p.WaitForExit(20000);
                    var o = outTask.Result;
                    return p.HasExited && p.ExitCode == 0 ? o : null;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
