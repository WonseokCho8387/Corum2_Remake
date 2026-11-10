using System.Collections.Generic;
using Corum2.Core;
using UnityEngine;

namespace Corum2.Unity
{
    /// <summary>Block-out enemies and debug hitboxes (presentation only; judgement lives in Core).</summary>
    public sealed class CombatView
    {
        readonly Transform _parent;
        readonly WorldView _world;
        readonly Dictionary<EnemyActor, (Transform root, Material mat)> _enemies = new Dictionary<EnemyActor, (Transform, Material)>();
        readonly Dictionary<string, Color> _colors = new Dictionary<string, Color>();
        readonly Transform _playerBox;
        readonly List<Transform> _enemyBoxes = new List<Transform>();
        readonly Material _playerBoxMat;
        readonly Material _enemyBoxMat;
        readonly Material _projectileMat;
        readonly List<Transform> _projectiles = new List<Transform>();
        readonly Dictionary<EnemyActor, Color> _lastColor = new Dictionary<EnemyActor, Color>();

        /// <summary>The block-out body of an enemy as placed by the last Sync (for presentation cues layered on top).</summary>
        public bool TryGetVisual(EnemyActor e, out Transform root, out Material mat, out Color color)
        {
            root = null;
            mat = null;
            color = Color.white;
            if (!_enemies.TryGetValue(e, out var v) || !v.root.gameObject.activeSelf) return false;
            root = v.root;
            mat = v.mat;
            _lastColor.TryGetValue(e, out color);
            return true;
        }

        public CombatView(Transform parent, WorldView world, IEnumerable<EnemyDefinitionAsset> defs)
        {
            _parent = parent;
            _world = world;
            foreach (var d in defs)
                if (d != null) _colors[d.enemyId] = d.color;
            _playerBoxMat = world.MakeMaterial(new Color(1f, 0.15f, 0.15f));
            _enemyBoxMat = world.MakeMaterial(new Color(1f, 0.6f, 0.1f));
            _playerBox = Box("PlayerHitbox", _playerBoxMat);
            _projectileMat = world.MakeMaterial(new Color(1f, 0.3f, 0.9f));
        }

        Transform Box(string name, Material m)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            if (Application.isPlaying) Object.Destroy(go.GetComponent<Collider>());
            else Object.DestroyImmediate(go.GetComponent<Collider>()); // EditMode tests
            go.transform.SetParent(_parent, false);
            go.GetComponent<MeshRenderer>().sharedMaterial = m;
            go.SetActive(false);
            return go.transform;
        }

        static void Place(Transform t, Aabb b, float z)
        {
            t.gameObject.SetActive(true);
            t.position = new Vector3(b.CenterX, b.CenterY, z - 0.45f); // in front of bodies so it stays visible
            t.localScale = new Vector3(b.Width, b.Height, 0.05f);
        }

        public void Sync(CombatSystem c, float z)
        {
            // enemies
            var alive = new HashSet<EnemyActor>(c.Enemies);
            var stale = new List<EnemyActor>();
            foreach (var kv in _enemies)
                if (!alive.Contains(kv.Key)) stale.Add(kv.Key);
            foreach (var e in stale)
            {
                Object.Destroy(_enemies[e].root.gameObject);
                _enemies.Remove(e);
            }
            var boxIndex = 0;
            foreach (var e in c.Enemies)
            {
                if (!_enemies.TryGetValue(e, out var v))
                {
                    var mat = _world.MakeMaterial(_colors.TryGetValue(e.Def.Id, out var col) ? col : Color.red);
                    var t = Box("Enemy_" + e.Id, mat);
                    v = (t, mat);
                    _enemies[e] = v;
                }
                var visible = e.State != EnemyState.Gone && !(e.State == EnemyState.Dead && e.Vanishing && Mathf.Repeat(Time.time * 10f, 1f) > 0.5f);
                v.root.gameObject.SetActive(visible);
                var h = e.State == EnemyState.Down || e.State == EnemyState.Dead ? e.Def.BodyHeight * 0.35f : e.Def.BodyHeight;
                v.root.position = new Vector3(e.Position.X, e.Position.Y + h * 0.5f, z);
                v.root.localScale = new Vector3(e.Def.BodyWidth, h, e.Def.BodyWidth);
                var baseCol = _colors.TryGetValue(e.Def.Id, out var bc) ? bc : Color.red;
                var stateCol = StateTint(e.State, baseCol);
                if (v.mat.HasProperty("_BaseColor")) v.mat.SetColor("_BaseColor", stateCol);
                _lastColor[e] = stateCol;

                if (e.State == EnemyState.AttackActive && e.CurAttack != null && !e.AttackSpent)
                {
                    while (_enemyBoxes.Count <= boxIndex) _enemyBoxes.Add(Box("EnemyHitbox", _enemyBoxMat));
                    var a = e.CurAttack;
                    Place(_enemyBoxes[boxIndex++], Aabb.FromCenterSize(e.Position.X + e.Facing * a.HitboxOffsetX, e.Position.Y + a.HitboxOffsetY, a.HitboxWidth, a.HitboxHeight), z);
                }
            }
            for (var i = boxIndex; i < _enemyBoxes.Count; i++) _enemyBoxes[i].gameObject.SetActive(false);

            var pi = 0;
            foreach (var pr in c.Projectiles)
            {
                while (_projectiles.Count <= pi) _projectiles.Add(Box("Projectile", _projectileMat));
                var t = _projectiles[pi++];
                t.gameObject.SetActive(true);
                t.position = new Vector3(pr.Position.X, pr.Position.Y, z);
                t.localScale = new Vector3(pr.Def.Width, pr.Def.Height, pr.Def.Width);
            }
            for (var i = pi; i < _projectiles.Count; i++) _projectiles[i].gameObject.SetActive(false);

            var pb = c.ActivePlayerHitbox();
            if (pb.HasValue) Place(_playerBox, pb.Value, z);
            else _playerBox.gameObject.SetActive(false);
        }

        static Color StateTint(EnemyState s, Color b)
        {
            switch (s)
            {
                case EnemyState.AttackStartup: return Color.Lerp(b, Color.yellow, 0.5f);
                case EnemyState.AttackActive: return Color.yellow;
                case EnemyState.Stagger: return Color.Lerp(b, Color.white, 0.6f);
                case EnemyState.Launched: return Color.Lerp(b, Color.cyan, 0.5f);
                case EnemyState.Down: return Color.Lerp(b, Color.black, 0.5f);
                case EnemyState.Wakeup: return Color.Lerp(b, Color.magenta, 0.6f);
                case EnemyState.DodgeReact: return Color.green;
                case EnemyState.Dead: return Color.gray;
                case EnemyState.PhaseTransition: return Color.Lerp(b, new Color(0.6f, 0.2f, 1f), 0.7f);
                case EnemyState.Guard: return Color.Lerp(b, new Color(0.2f, 0.4f, 1f), 0.7f);
                case EnemyState.Evade: return Color.Lerp(b, new Color(0.3f, 1f, 0.8f), 0.6f);
                default: return b;
            }
        }
    }
}
