using System.Globalization;
using Corum2.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Corum2.Unity
{
    /// <summary>
    /// Test ring and quickslot UI (uGUI). Placeholder visuals for evidence, not the final UI (U-03, U-11).
    /// The ring is centred on the player's screen position (R-02-03) and updates every rendered frame at real time.
    /// </summary>
    public sealed class RingView
    {
        const float Radius = 175f;
        readonly RectTransform _root;
        readonly RectTransform _cursor;
        readonly Image[] _slotBg = new Image[8];
        readonly Text[] _slotText = new Text[8];
        readonly Text _center;
        readonly Text _detail;
        readonly Image _detailBg;
        readonly Text[] _quick = new Text[4];
        readonly Image[] _quickBg = new Image[4];
        readonly Text _quickInfo;
        readonly Font _font;
        /// <summary>True when the ring centre was moved to keep it on screen this frame.</summary>
        public bool Clamped { get; private set; }

        public RingView(Transform canvas)
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _root = Rect("Ring", canvas, Vector2.zero, new Vector2(10, 10));
            for (var i = 0; i < 8; i++)
            {
                var a = Mathf.PI / 2 - i * Mathf.PI / 4;
                var pos = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * Radius;
                _slotBg[i] = Box("Slot" + i, _root, pos, new Vector2(150, 48), new Color(0.08f, 0.1f, 0.16f, 0.82f));
                _slotText[i] = Label(_slotBg[i].rectTransform, 17, TextAnchor.MiddleCenter);
            }
            var cursorBg = Box("Cursor", _root, Vector2.zero, new Vector2(18, 18), new Color(1f, 0.85f, 0.2f, 1f));
            _cursor = cursorBg.rectTransform;
            var centerBg = Box("Center", _root, new Vector2(0, -Radius - 70), new Vector2(420, 62), new Color(0, 0, 0, 0.7f));
            _center = Label(centerBg.rectTransform, 16, TextAnchor.MiddleCenter);
            _detailBg = Box("Detail", _root, new Vector2(0, Radius + 70), new Vector2(460, 62), new Color(0.15f, 0.12f, 0.05f, 0.9f));
            _detail = Label(_detailBg.rectTransform, 16, TextAnchor.MiddleCenter);

            // Quickslot HUD: bottom-left cross (Up, Right, Down, Left).
            var q = Rect("Quickslots", canvas, Vector2.zero, new Vector2(10, 10));
            q.anchorMin = q.anchorMax = new Vector2(1, 0);
            q.anchoredPosition = new Vector2(-240, 95);
            var offsets = new[] { new Vector2(0, 55), new Vector2(150, 0), new Vector2(0, -55), new Vector2(-150, 0) };
            var names = new[] { "U", "R", "D", "L" };
            for (var i = 0; i < 4; i++)
            {
                _quickBg[i] = Box("Quick" + names[i], q, offsets[i], new Vector2(140, 44), new Color(0.05f, 0.08f, 0.12f, 0.8f));
                _quick[i] = Label(_quickBg[i].rectTransform, 15, TextAnchor.MiddleCenter);
            }
            var infoBg = Box("QuickInfo", q, new Vector2(-40, 120), new Vector2(330, 60), new Color(0, 0, 0, 0f));
            _quickInfo = Label(infoBg.rectTransform, 15, TextAnchor.MiddleLeft);
        }

        static RectTransform Rect(string name, Transform parent, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        Image Box(string name, Transform parent, Vector2 pos, Vector2 size, Color c)
        {
            var rt = Rect(name, parent, pos, size);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = c;
            img.raycastTarget = false;
            return img;
        }

        Text Label(RectTransform parent, int size, TextAnchor anchor)
        {
            var rt = Rect("Text", parent, Vector2.zero, Vector2.zero);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(4, 2);
            rt.offsetMax = new Vector2(-4, -2);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = _font;
            t.fontSize = size;
            t.alignment = anchor;
            t.color = Color.white;
            t.supportRichText = true;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        string NameOf(RingProfile p, string id)
        {
            var d = p.FindItem(id);
            return d == null ? id : d.Name;
        }

        /// <summary>viewport = player position in camera viewport coordinates (0..1).</summary>
        public void Apply(Simulation sim, RingProfile p, Vector2 viewport, float timeScale)
        {
            var ring = sim.Ring;
            var inv = sim.Items.Inventory;
            _root.gameObject.SetActive(ring.IsOpen);
            if (ring.IsOpen)
            {
                // Centred on the player, clamped so the whole ring stays on screen (EXP-RING-12, U-03/U-11 layout).
                var mx = (Radius + 90f) / 1280f;
                var my = (Radius + 110f) / 800f;
                var c = new Vector2(Mathf.Clamp(viewport.x, mx, 1f - mx), Mathf.Clamp(viewport.y, my, 1f - my));
                Clamped = c != viewport;
                _root.anchorMin = _root.anchorMax = c;
                _root.anchoredPosition = Vector2.zero;
                for (var i = 0; i < 8; i++)
                {
                    var id = inv.At(ring.Page, i);
                    var selected = i == ring.SelectedSlot;
                    var target = id != null && id == ring.TargetItem && ring.Mode != RingMode.Browse;
                    _slotText[i].text = id == null ? "<color=#666666>-</color>" : NameOf(p, id) + " x" + inv.Count(id);
                    _slotBg[i].color = target ? new Color(0.65f, 0.35f, 0.1f, 0.92f)
                        : selected ? new Color(0.75f, 0.6f, 0.1f, 0.92f) : new Color(0.08f, 0.1f, 0.16f, 0.82f);
                }
                _cursor.anchoredPosition = new Vector2(ring.CursorX, ring.CursorY) * (Radius * 0.62f);
                var mode = ring.Mode == RingMode.Registering ? "REGISTER " + NameOf(p, ring.TargetItem) + ": press a D-pad direction"
                    : ring.Mode == RingMode.ConfirmDiscard ? "DISCARD " + NameOf(p, ring.TargetItem) + "?  A = yes   B = no"
                    : "A use  Y register  X info  B discard  LT/RT page";
                _center.text = "<b>RING  page " + (ring.Page + 1) + "/" + inv.PageCount + "  world x" +
                               timeScale.ToString("0.##", CultureInfo.InvariantCulture) + "</b>\n" + mode +
                               (ring.Feedback.Length > 0 ? "\n<color=#ffb060>" + ring.Feedback + "</color>" : "");
                var showDetail = ring.Mode == RingMode.Detail;
                _detailBg.gameObject.SetActive(showDetail);
                if (showDetail)
                {
                    var d = p.FindItem(ring.TargetItem);
                    _detail.text = d == null ? ring.TargetItem : "<b>" + d.Name + "</b> (" + d.Kind + ")\n" + d.Description;
                }
            }

            var names = new[] { "U", "R", "D", "L" };
            for (var i = 0; i < 4; i++)
            {
                var id = inv.Quickslots[i];
                _quick[i].text = names[i] + ": " + (string.IsNullOrEmpty(id) ? "<color=#666666>empty</color>" : NameOf(p, id) + " x" + inv.Count(id));
            }
            var items = sim.Items;
            var pending = items.Pending;
            _quickInfo.text = "item cooldown " + items.CooldownLeft.ToString("0.00", CultureInfo.InvariantCulture) + "s" +
                              (pending != null ? "\n<color=#ffd060>pending " + NameOf(p, pending.ItemId) + " (" + pending.WaitReason + ")</color>" : "");
        }
    }
}
