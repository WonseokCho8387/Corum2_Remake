using System.Collections.Generic;
using System.Globalization;
using Corum2.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Corum2.Unity
{
    /// <summary>
    /// Combat readability HUD (user play feedback 2026-11-14, EXP-HUD-01; presentation only, no rule reads it):
    /// HP bars over the player and each enemy, a large bar at the top centre for each nearby boss, and pop-ups for
    /// hits, guards and parries drained from CombatSystem.Feedback. Temporary uGUI layout until the UI art pass.
    /// </summary>
    public sealed class CombatHudView
    {
        const float BossRange = 16f;          // m from the player within which a boss gets its top bar (EXP-HUD-01)
        /// <summary>Pop-up life in real seconds (EXP-HUD-01 tuning; tests may lengthen it to capture a still).</summary>
        public static float PopupTime = 0.7f;
        static readonly Color PlayerFill = new Color(0.25f, 0.9f, 0.35f);
        static readonly Color EnemyFill = new Color(0.95f, 0.25f, 0.2f);
        static readonly Color BossFill = new Color(0.85f, 0.15f, 0.3f);
        static readonly Color GuardColor = new Color(0.35f, 0.65f, 1f);
        static readonly Color ParryColor = new Color(1f, 0.85f, 0.2f);

        readonly RectTransform _root;
        readonly Canvas _canvas;
        readonly Font _font;
        readonly Bar _player;
        readonly Dictionary<EnemyActor, Bar> _enemies = new Dictionary<EnemyActor, Bar>();
        readonly List<Bar> _bossBars = new List<Bar>();
        readonly List<Popup> _popups = new List<Popup>();

        sealed class Bar
        {
            public RectTransform Root;
            public RectTransform Fill;
            public Text Label;
        }

        sealed class Popup
        {
            public RectTransform Root;
            public Text Text;
            public Image Burst;
            public Vector3 World;
            public float Age;
            public float Rise;
            public Color Color;
            public Color TextColor;
        }

        public CombatHudView(Canvas canvas)
        {
            _canvas = canvas;
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var go = new GameObject("CombatHud", typeof(RectTransform));
            go.transform.SetParent(canvas.transform, false);
            _root = (RectTransform)go.transform;
            _root.anchorMin = Vector2.zero;
            _root.anchorMax = Vector2.one;
            _root.offsetMin = _root.offsetMax = Vector2.zero;
            _root.SetAsFirstSibling(); // under the ring and death menus
            _player = MakeBar("PlayerHp", new Vector2(70, 8), PlayerFill, false);
        }

        Bar MakeBar(string name, Vector2 size, Color fill, bool label)
        {
            var bg = new GameObject(name, typeof(RectTransform), typeof(Image));
            bg.transform.SetParent(_root, false);
            var rt = (RectTransform)bg.transform;
            rt.sizeDelta = size;
            var img = bg.GetComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0.7f);
            img.raycastTarget = false;
            var f = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            f.transform.SetParent(rt, false);
            var frt = (RectTransform)f.transform;
            frt.anchorMin = new Vector2(0, 0);
            frt.anchorMax = new Vector2(1, 1);
            frt.pivot = new Vector2(0, 0.5f);
            frt.offsetMin = new Vector2(1.5f, 1.5f);
            frt.offsetMax = new Vector2(-1.5f, -1.5f);
            var fi = f.GetComponent<Image>();
            fi.color = fill;
            fi.raycastTarget = false;
            Text t = null;
            if (label)
            {
                var lg = new GameObject("Label", typeof(RectTransform), typeof(Text));
                lg.transform.SetParent(rt, false);
                var lrt = (RectTransform)lg.transform;
                lrt.anchorMin = new Vector2(0, 1);
                lrt.anchorMax = new Vector2(1, 1);
                lrt.pivot = new Vector2(0.5f, 0f);
                lrt.anchoredPosition = new Vector2(0, 2);
                lrt.sizeDelta = new Vector2(0, 26);
                t = lg.GetComponent<Text>();
                t.font = _font;
                t.fontSize = 20;
                t.fontStyle = FontStyle.Bold;
                t.alignment = TextAnchor.LowerCenter;
                t.color = Color.white;
                t.raycastTarget = false;
                lg.AddComponent<Shadow>().effectColor = new Color(0, 0, 0, 0.9f);
            }
            return new Bar { Root = rt, Fill = frt, Label = t };
        }

        static void SetFill(Bar b, float hp, float max)
        {
            var r = max > 0f ? Mathf.Clamp01(hp / max) : 0f;
            b.Fill.anchorMax = new Vector2(r, 1f);
            b.Fill.offsetMax = new Vector2(r > 0f ? -1.5f : 0f, -1.5f);
        }

        Camera UiCamera => _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera;

        bool Place(RectTransform rt, Camera cam, Vector3 world, Vector2 pixelOffset)
        {
            var sp = cam.WorldToScreenPoint(world);
            if (sp.z <= 0f) return false;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_root, sp, UiCamera, out var local)) return false;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = local + pixelOffset;
            return true;
        }

        /// <param name="playerPos">The drawn (interpolated) player feet.</param>
        public void Sync(Simulation sim, Camera cam, Vector3 playerPos, float dt)
        {
            var c = sim.Combat;
            if (c == null || cam == null) return;

            // player
            var pAlive = sim.Player.State != MotorState.Dead;
            _player.Root.gameObject.SetActive(pAlive && Place(_player.Root, cam, playerPos + new Vector3(0, sim.Player.Height + 0.35f, 0), Vector2.zero));
            SetFill(_player, c.PlayerHp, c.PlayerMaxHp);

            // enemies (bosses get the top bar instead of a head bar)
            var alive = new HashSet<EnemyActor>(c.Enemies);
            var stale = new List<EnemyActor>();
            foreach (var kv in _enemies)
                if (!alive.Contains(kv.Key)) stale.Add(kv.Key);
            foreach (var e in stale)
            {
                Object.Destroy(_enemies[e].Root.gameObject);
                _enemies.Remove(e);
            }
            var bosses = new List<EnemyActor>();
            foreach (var e in c.Enemies)
            {
                var show = e.State != EnemyState.Dead && e.State != EnemyState.Gone && e.Hp > 0f;
                if (e.Boss != null)
                {
                    if (show && Mathf.Abs(e.Position.X - sim.Player.Position.X) <= BossRange) bosses.Add(e);
                    continue;
                }
                if (!_enemies.TryGetValue(e, out var bar))
                {
                    bar = MakeBar("EnemyHp_" + e.Id, new Vector2(56, 7), EnemyFill, false);
                    _enemies[e] = bar;
                }
                var h = e.State == EnemyState.Down ? e.Def.BodyHeight * 0.35f : e.Def.BodyHeight;
                var w = new Vector3(e.Position.X, e.Position.Y + h + 0.3f, sim.CurrentMap.CombatPlaneZ);
                bar.Root.gameObject.SetActive(show && Place(bar.Root, cam, w, Vector2.zero));
                SetFill(bar, e.Hp, e.Def.MaxHp);
            }

            // bosses: large bars at the top centre, nearest first, stacked
            bosses.Sort((a, b) => Mathf.Abs(a.Position.X - sim.Player.Position.X).CompareTo(Mathf.Abs(b.Position.X - sim.Player.Position.X)));
            while (_bossBars.Count < bosses.Count)
            {
                var b = MakeBar("BossHp" + _bossBars.Count, new Vector2(620, 20), BossFill, true);
                b.Root.anchorMin = b.Root.anchorMax = new Vector2(0.5f, 1f);
                b.Root.pivot = new Vector2(0.5f, 1f);
                _bossBars.Add(b);
            }
            for (var i = 0; i < _bossBars.Count; i++)
            {
                var b = _bossBars[i];
                if (i >= bosses.Count)
                {
                    b.Root.gameObject.SetActive(false);
                    continue;
                }
                var e = bosses[i];
                b.Root.gameObject.SetActive(true);
                b.Root.anchoredPosition = new Vector2(0, -40 - i * 58);
                SetFill(b, e.Hp, e.Def.MaxHp);
                b.Label.text = string.Format(CultureInfo.InvariantCulture, "{0}  [{1}]   {2:0}/{3:0}", e.Def.Id.ToUpperInvariant(), e.PhaseId, Mathf.Max(0f, e.Hp), e.Def.MaxHp);
            }

            // hit / guard / parry pop-ups
            var z = sim.CurrentMap.CombatPlaneZ;
            foreach (var f in c.Feedback) Spawn(f, z);
            c.Feedback.Clear();
            for (var i = _popups.Count - 1; i >= 0; i--)
            {
                var p = _popups[i];
                p.Age += dt;
                if (p.Age >= PopupTime)
                {
                    Object.Destroy(p.Root.gameObject);
                    _popups.RemoveAt(i);
                    continue;
                }
                var k = p.Age / PopupTime;
                p.Root.gameObject.SetActive(Place(p.Root, cam, p.World, new Vector2(0, p.Rise * k)));
                var col = p.TextColor;
                col.a = 1f - k * k;
                p.Text.color = col;
                if (p.Burst != null)
                {
                    var s = Mathf.Lerp(0.4f, 1.6f, Mathf.Sqrt(Mathf.Clamp01(k * 2.5f)));
                    p.Burst.rectTransform.localScale = new Vector3(s, s, 1f);
                    var bc = p.Color;
                    bc.a = Mathf.Clamp01(0.8f - k * 1.6f);
                    p.Burst.color = bc;
                }
            }
        }

        void Spawn(CombatFeedback f, float z)
        {
            string text;
            Color col;
            int size;
            var burst = false;
            float rise = 40f;
            switch (f.Kind)
            {
                case CombatFeedbackKind.PlayerParry:
                    text = "PARRY!"; col = ParryColor; size = 34; burst = true; rise = 55f; break;
                case CombatFeedbackKind.PlayerGuard:
                    text = f.Amount > 0f ? "GUARD  -" + Num(f.Amount) : "GUARD"; col = GuardColor; size = 26; burst = true; break;
                case CombatFeedbackKind.PlayerHit:
                    text = "-" + Num(f.Amount); col = new Color(1f, 0.25f, 0.25f); size = 30; break;
                case CombatFeedbackKind.EnemyGuard:
                    text = "BLOCK"; col = new Color(0.75f, 0.75f, 0.8f); size = 20; break;
                default:
                    text = Num(f.Amount); col = Color.white; size = 22; break;
            }
            var go = new GameObject("Popup", typeof(RectTransform));
            go.transform.SetParent(_root, false);
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(220, 50);
            Image b = null;
            if (burst)
            {
                // a square flash that grows and fades: blue for a guard, gold (and larger) for a parry
                var bg = new GameObject("Burst", typeof(RectTransform), typeof(Image));
                bg.transform.SetParent(rt, false);
                var brt = (RectTransform)bg.transform;
                var d = f.Kind == CombatFeedbackKind.PlayerParry ? 110f : 70f;
                brt.sizeDelta = new Vector2(d, d);
                brt.localRotation = Quaternion.Euler(0, 0, 45f);
                b = bg.GetComponent<Image>();
                b.raycastTarget = false;
            }
            var tg = new GameObject("Text", typeof(RectTransform), typeof(Text));
            tg.transform.SetParent(rt, false);
            var trt = (RectTransform)tg.transform;
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = trt.offsetMax = Vector2.zero;
            var t = tg.GetComponent<Text>();
            t.font = _font;
            t.text = text;
            t.fontSize = size;
            t.fontStyle = FontStyle.Bold;
            t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.raycastTarget = false;
            tg.AddComponent<Outline>().effectColor = new Color(0, 0, 0, 0.9f);
            var p = new Popup { Root = rt, Text = t, Burst = b, World = new Vector3(f.X, f.Y, z), Rise = rise, Color = col, TextColor = f.Kind == CombatFeedbackKind.PlayerParry ? Color.white : col };
            _popups.Add(p);
        }

        static string Num(float v) => Mathf.RoundToInt(v).ToString(CultureInfo.InvariantCulture);
    }
}
