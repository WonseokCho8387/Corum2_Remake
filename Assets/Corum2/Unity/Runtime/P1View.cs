using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Corum2.Core;
using Corum2.Core.P1;
using UnityEngine;
using UnityEngine.UI;

namespace Corum2.Unity
{
    /// <summary>
    /// P1 presentation (placeholder uGUI / primitives until the UI art pass; presentation only, no rule reads it):
    /// level / EXP / MP / buffs HUD, growth card screen, merchant shop and questions, map icon for the merchant,
    /// projectiles, fire walls, summons and the merchant body in the world.
    /// </summary>
    public sealed class P1View
    {
        static readonly Color MpFill = new Color(0.3f, 0.55f, 1f);
        static readonly Color ExpFill = new Color(1f, 0.85f, 0.3f);
        readonly Font _font;
        readonly RectTransform _hud;
        readonly Text _status;
        readonly RectTransform _mpFill, _expFill;
        readonly RectTransform _panel;
        readonly Text _panelText;
        readonly RectTransform[] _cards = new RectTransform[3];
        readonly Text[] _cardText = new Text[3];
        readonly Image[] _cardImg = new Image[3];
        readonly RectTransform _icon;
        readonly Transform _worldRoot;
        readonly WorldView _world;
        readonly List<Transform> _proj = new List<Transform>();
        readonly List<Transform> _walls = new List<Transform>();
        Transform _summon, _merchant;
        readonly Material _projMat, _waveMat, _wallMat, _angelMat, _demonMat, _merchantMat, _shieldMat;
        Transform _shield;

        public P1View(Canvas canvas, Transform parent, WorldView world)
        {
            _world = world;
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _hud = Rect("P1Hud", canvas.transform);
            _hud.anchorMin = new Vector2(0, 0);
            _hud.anchorMax = new Vector2(0, 0);
            _hud.pivot = new Vector2(0, 0);
            _hud.anchoredPosition = new Vector2(12, 12);
            _hud.sizeDelta = new Vector2(460, 120);
            Img(_hud, new Color(0, 0, 0, 0.55f));
            _expFill = Bar(_hud, "Exp", new Vector2(10, 92), new Vector2(440, 10), ExpFill);
            _mpFill = Bar(_hud, "Mp", new Vector2(10, 76), new Vector2(440, 10), MpFill);
            _status = Label(_hud, new Vector2(10, 4), new Vector2(440, 68), 15, TextAnchor.UpperLeft);

            _panel = Rect("P1Panel", canvas.transform);
            _panel.anchorMin = _panel.anchorMax = new Vector2(0.5f, 0.5f);
            _panel.sizeDelta = new Vector2(900, 420);
            Img(_panel, new Color(0.05f, 0.05f, 0.1f, 0.88f));
            _panelText = Label(_panel, new Vector2(20, 300), new Vector2(860, 110), 20, TextAnchor.UpperCenter);
            for (var i = 0; i < 3; i++)
            {
                var c = Rect("Card" + i, _panel);
                c.anchorMin = c.anchorMax = new Vector2(0, 0);
                c.pivot = Vector2.zero;
                c.anchoredPosition = new Vector2(30 + i * 285, 30);
                c.sizeDelta = new Vector2(270, 260);
                _cardImg[i] = Img(c, new Color(0.2f, 0.2f, 0.25f, 1));
                _cardText[i] = Label(c, new Vector2(10, 10), new Vector2(250, 240), 17, TextAnchor.MiddleCenter);
                _cards[i] = c;
            }
            _panel.gameObject.SetActive(false);

            _icon = Rect("MerchantIcon", canvas.transform);
            _icon.anchorMin = _icon.anchorMax = new Vector2(1, 1);
            _icon.pivot = new Vector2(1, 1);
            _icon.anchoredPosition = new Vector2(-12, -12);
            _icon.sizeDelta = new Vector2(260, 40);
            Img(_icon, new Color(0.15f, 0.1f, 0.3f, 0.85f));
            Label(_icon, new Vector2(8, 4), new Vector2(244, 32), 16, TextAnchor.MiddleLeft).name = "IconText";
            _icon.gameObject.SetActive(false);

            _worldRoot = new GameObject("P1World").transform;
            _worldRoot.SetParent(parent, false);
            _projMat = world.MakeMaterial(new Color(0.5f, 0.85f, 1f));
            _waveMat = world.MakeMaterial(new Color(1f, 1f, 0.6f));
            _wallMat = world.MakeMaterial(new Color(1f, 0.35f, 0.05f));
            _angelMat = world.MakeMaterial(new Color(1f, 0.95f, 0.7f));
            _demonMat = world.MakeMaterial(new Color(0.45f, 0.05f, 0.4f));
            _merchantMat = world.MakeMaterial(new Color(0.6f, 0.3f, 1f));
            _shieldMat = world.MakeMaterial(new Color(0.4f, 0.8f, 1f));
        }

        RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        static Image Img(RectTransform rt, Color c)
        {
            var img = rt.gameObject.AddComponent<Image>();
            img.color = c;
            img.raycastTarget = false;
            return img;
        }

        RectTransform Bar(RectTransform parent, string name, Vector2 pos, Vector2 size, Color fill)
        {
            var bg = Rect(name, parent);
            bg.anchorMin = bg.anchorMax = Vector2.zero;
            bg.pivot = Vector2.zero;
            bg.anchoredPosition = pos;
            bg.sizeDelta = size;
            Img(bg, new Color(0, 0, 0, 0.8f));
            var f = Rect("Fill", bg);
            f.anchorMin = Vector2.zero;
            f.anchorMax = new Vector2(1, 1);
            f.pivot = new Vector2(0, 0.5f);
            f.offsetMin = f.offsetMax = Vector2.zero;
            Img(f, fill);
            return f;
        }

        Text Label(RectTransform parent, Vector2 pos, Vector2 size, int fontSize, TextAnchor anchor)
        {
            var rt = Rect("Text", parent);
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = Vector2.zero;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var t = rt.gameObject.AddComponent<Text>();
            t.font = _font;
            t.fontSize = fontSize;
            t.alignment = anchor;
            t.color = Color.white;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        static void SetFill(RectTransform f, float v) => f.anchorMax = new Vector2(Mathf.Clamp01(v), 1);

        static string Rar(Rarity r) => r == Rarity.Normal ? "노말" : r == Rarity.Rare ? "레어" : r == Rarity.Epic ? "에픽" : "레전더리";

        static Color RarColor(Rarity r) => r == Rarity.Normal ? new Color(0.25f, 0.25f, 0.3f) : r == Rarity.Rare ? new Color(0.15f, 0.3f, 0.6f)
            : r == Rarity.Epic ? new Color(0.45f, 0.15f, 0.55f) : new Color(0.65f, 0.45f, 0.05f);

        string CardText(P1Runtime rt, string id, out Color bg)
        {
            var fb = rt.P.Fallback(id);
            if (fb != null)
            {
                bg = new Color(0.2f, 0.35f, 0.25f);
                return "<b>보충 보상</b>\n\n" + (rt.P.Item(fb.ItemId)?.Name ?? fb.ItemId) + " x" + fb.Count + "\n\n(EXP-P1-ITEM-01 시험 아이템)";
            }
            var c = rt.P.Card(id);
            if (c == null) { bg = Color.gray; return id; }
            bg = c.Kind == CardKind.Mandatory ? new Color(0.6f, 0.15f, 0.15f) : RarColor(c.Rarity);
            var head = c.Kind == CardKind.Mandatory ? "필수" : c.Permanent ? "영구 · " + Rar(c.Rarity) : "일시 버프 · " + Rar(c.Rarity);
            var label = c.Label;
            if (c.Kind == CardKind.Buff)
            {
                var b = rt.P.Buff(c.BuffId);
                label = b.Label + " +" + (rt.P.BuffRules.Strength(b, c.Rarity) * 100f).ToString("0.#", CultureInfo.InvariantCulture) + "% / " +
                        rt.P.BuffRules.Duration(c.Rarity).ToString("0", CultureInfo.InvariantCulture) + "초";
            }
            var pressure = "";
            if (c.Kind == CardKind.Mandatory)
            {
                var m = rt.Prog.Gen.MandatoryByCard(id);
                pressure = "\n\n거부 " + rt.G.M(m.Id).DeclineCount + "회 (3→2→1)";
            }
            return "<b>" + head + "</b>\n\n" + label + pressure;
        }

        public void Apply(Simulation sim, Camera cam, float z)
        {
            var rt = sim.P1;
            if (rt == null) return;
            _panel.SetAsLastSibling(); // above the debug text and the ring
            var g = rt.G;
            var maxHp = rt.Prog.Rules.MaxHp(g);
            var maxMp = rt.Prog.Rules.MaxMp(g);
            SetFill(_mpFill, maxMp > 0 ? g.CurMp / maxMp : 0);
            var need = g.Level < rt.P.MaxLevel ? rt.P.ExpNeeded(g.Level) : 0;
            SetFill(_expFill, need > 0 ? g.LevelExp / (float)need : 1f);
            var sb = new StringBuilder();
            sb.Append("Lv ").Append(g.Level).Append(g.PendingLevels > 0 ? " (+" + g.PendingLevels + ")" : "")
              .Append("   EXP ").Append(g.LevelExp).Append('/').Append(need)
              .Append("   MP ").Append(g.CurMp.ToString("0", CultureInfo.InvariantCulture)).Append('/').Append(maxMp)
              .Append("   HP ").Append((sim.Combat?.PlayerHp ?? 0).ToString("0", CultureInfo.InvariantCulture)).Append('/').Append(maxHp);
            if (g.Lv50Currency > 0 || g.Level >= rt.P.MaxLevel) sb.Append("   Lv50 재화 ").Append(g.Lv50Currency);
            sb.Append("\n리롤 토큰 ").Append(g.RerollTokens).Append("   습득: ").Append(string.Join(",", Learned(g)));
            var buffs = new List<string>();
            foreach (var b in rt.Buffs.Active) buffs.Add(rt.P.Buff(b.BuffId).Label + " " + b.Remaining.ToString("0", CultureInfo.InvariantCulture) + "s");
            if (rt.Fx.MagicShield != null) buffs.Add("실드 " + rt.Fx.MagicShield.Remaining);
            if (rt.Fx.ArchangelShield != null) buffs.Add("천사 보호막 " + rt.Fx.ArchangelShield.Remaining);
            if (rt.Fx.Sword != null) buffs.Add((rt.Fx.Sword.Light ? "빛의 검 " : "어둠의 검 ") + rt.Fx.Sword.Remaining.ToString("0", CultureInfo.InvariantCulture) + "s");
            if (rt.Magic.Summon != null) buffs.Add((rt.Magic.Summon.Kind == SpellKind.Archangel ? "대천사 " : "악마 ") + rt.Magic.Summon.Remaining.ToString("0", CultureInfo.InvariantCulture) + "s");
            sb.Append("\n").Append(buffs.Count > 0 ? string.Join("  ", buffs) : "버프 없음");
            _status.text = sb.ToString();

            ApplyPanel(rt);
            ApplyIcon(rt, sim);
            ApplyWorld(rt, sim, z);
        }

        static IEnumerable<string> Learned(P1Progress g)
        {
            var l = new List<string>();
            foreach (var x in g.Learned) l.Add(x.StartsWith("spell.") ? x.Substring(6) : x);
            l.Sort();
            return l;
        }

        void ApplyPanel(P1Runtime rt)
        {
            var mer = rt.Merchant;
            if (rt.GrowthOpen)
            {
                _panel.gameObject.SetActive(true);
                var o = rt.G.Offer;
                _panelText.text = "<b>레벨 업! Lv " + o.TargetLevel + "</b>  (남은 성장 " + rt.G.PendingLevels + ")\n← → 선택   A 결정   Y 리롤" +
                                  (o.RerollUsed ? " (사용함)" : " (토큰 " + rt.G.RerollTokens + (rt.G.EquipRerollOption ? " + 장비" : "") + ")");
                for (var i = 0; i < 3; i++)
                {
                    var on = i < o.Cards.Count;
                    _cards[i].gameObject.SetActive(on);
                    if (!on) continue;
                    _cardText[i].text = CardText(rt, o.Cards[i], out var bg);
                    _cardImg[i].color = i == rt.GrowthCursor ? Color.Lerp(bg, Color.white, 0.35f) : bg;
                    _cards[i].anchoredPosition = new Vector2((900 - o.Cards.Count * 285 + 15) * 0.5f + i * 285, 30);
                }
                return;
            }
            if (rt.PendingExit != null)
            {
                ShowMessage("<b>상인이 있는 지역을 떠납니다</b>\n떠나면 상인이 사라집니다. 이동하시겠습니까?\nA 이동   B 취소");
                return;
            }
            if (mer.ShopOpen)
            {
                _panel.gameObject.SetActive(true);
                if (mer.ConfirmLeave)
                {
                    ShowMessage("<b>상점을 떠나면 상인이 사라집니다.</b>\n종료하시겠습니까?\nA 종료   B 취소");
                    return;
                }
                if (mer.PackStock != null)
                {
                    _panelText.text = "<b>카드팩 — 1장 선택</b> (가격 " + mer.Price(mer.PackStock.Kind) + ", 확정 시 결제)\n← → 선택   A 확정   B 취소";
                    for (var i = 0; i < 3; i++)
                    {
                        var on = i < mer.PackCards.Count;
                        _cards[i].gameObject.SetActive(on);
                        if (!on) continue;
                        _cardText[i].text = CardText(rt, mer.PackCards[i], out var bg);
                        _cardImg[i].color = i == mer.Cursor ? Color.Lerp(bg, Color.white, 0.35f) : bg;
                        _cards[i].anchoredPosition = new Vector2(30 + i * 285, 30);
                    }
                    return;
                }
                var sb = new StringBuilder("<b>떠돌이 마법 상인</b>   보유 " + (rt.G.Level >= rt.P.MaxLevel ? "Lv50 재화 " : "EXP ") + rt.Prog.Wallet + "\n↑ ↓ 선택   A 구매   B 나가기\n");
                var st = mer.Here;
                for (var i = 0; i < st.Stock.Count; i++)
                {
                    var s = st.Stock[i];
                    sb.Append(i == mer.Cursor ? "▶ " : "   ").Append(Product(rt, s)).Append("  ").Append(s.Sold ? "(판매됨)" : mer.Price(s.Kind) + " EXP").Append('\n');
                }
                if (mer.LastResult != "") sb.Append("\n").Append(mer.LastResult);
                _panelText.text = sb.ToString();
                for (var i = 0; i < 3; i++) _cards[i].gameObject.SetActive(false);
                return;
            }
            _panel.gameObject.SetActive(false);
        }

        static string Product(P1Runtime rt, MerchantStock s)
        {
            switch (s.Kind)
            {
                case "PermPack": return "영구 성장 카드팩";
                case "TempPack": return "일시 성장 카드팩";
                case "Buff": return "버프: " + rt.P.Buff(s.BuffId).Label + " (" + Rar(s.Rarity) + ")";
                default: return "성장 리롤 토큰";
            }
        }

        void ShowMessage(string text)
        {
            _panel.gameObject.SetActive(true);
            _panelText.text = text;
            for (var i = 0; i < 3; i++) _cards[i].gameObject.SetActive(false);
        }

        void ApplyIcon(P1Runtime rt, Simulation sim)
        {
            var st = rt.Merchant.Here;
            var show = st != null && st.State == RegionMerchant.Present;
            _icon.gameObject.SetActive(show);
            if (!show) return;
            var t = _icon.Find("IconText").GetComponent<Text>();
            t.text = "◆ 상인  " + st.MapId + "  x " + st.X.ToString("0.0", CultureInfo.InvariantCulture) + (rt.Merchant.NearMerchant() ? "  (↑ 대화)" : "");
        }

        Transform Prim(PrimitiveType type, Material m, string name)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(_worldRoot, false);
            go.GetComponent<MeshRenderer>().sharedMaterial = m;
            return go.transform;
        }

        void ApplyWorld(P1Runtime rt, Simulation sim, float z)
        {
            var mg = rt.Magic;
            while (_proj.Count < mg.Projectiles.Count) _proj.Add(Prim(PrimitiveType.Sphere, _projMat, "P1Projectile"));
            for (var i = 0; i < _proj.Count; i++)
            {
                var on = i < mg.Projectiles.Count;
                _proj[i].gameObject.SetActive(on);
                if (!on) continue;
                var p = mg.Projectiles[i];
                _proj[i].position = new Vector3(p.Pos.X, p.Pos.Y, z);
                var d = Mathf.Max(0.15f, p.Radius * 2f) * (p.Kind == SpellKind.FireBall ? 1.6f : 1f);
                _proj[i].localScale = new Vector3(p.Physical ? 0.25f : d, p.Physical ? 1.2f : d, d);
                _proj[i].GetComponent<MeshRenderer>().sharedMaterial = p.Physical ? _waveMat : p.Kind == SpellKind.FireBall ? _wallMat : _projMat;
            }
            while (_walls.Count < mg.Walls.Count) _walls.Add(Prim(PrimitiveType.Cube, _wallMat, "P1FireWall"));
            for (var i = 0; i < _walls.Count; i++)
            {
                var on = i < mg.Walls.Count;
                _walls[i].gameObject.SetActive(on);
                if (!on) continue;
                var w = mg.Walls[i];
                _walls[i].position = new Vector3(w.X, w.Y + w.Height * 0.5f, z);
                _walls[i].localScale = new Vector3(w.Width, w.Height, 0.4f);
            }
            if (mg.Summon != null)
            {
                if (_summon == null) _summon = Prim(PrimitiveType.Sphere, _angelMat, "P1Summon");
                _summon.gameObject.SetActive(true);
                _summon.GetComponent<MeshRenderer>().sharedMaterial = mg.Summon.Kind == SpellKind.Archangel ? _angelMat : _demonMat;
                _summon.position = new Vector3(mg.Summon.Pos.X, mg.Summon.Pos.Y + 0.3f, z);
                _summon.localScale = Vector3.one * 0.7f;
            }
            else if (_summon != null) _summon.gameObject.SetActive(false);
            var st = rt.Merchant.Here;
            var mer = st != null && st.State == RegionMerchant.Present && sim.CurrentMap.Id == st.MapId;
            if (mer)
            {
                if (_merchant == null) _merchant = Prim(PrimitiveType.Capsule, _merchantMat, "P1Merchant");
                _merchant.gameObject.SetActive(true);
                _merchant.position = new Vector3(st.X, st.Y + 0.9f, z);
                _merchant.localScale = new Vector3(0.7f, 0.9f, 0.7f);
            }
            else if (_merchant != null) _merchant.gameObject.SetActive(false);
            var shieldOn = rt.Fx.MagicShield != null || rt.Fx.ArchangelShield != null;
            if (shieldOn)
            {
                if (_shield == null) _shield = Prim(PrimitiveType.Sphere, _shieldMat, "P1Shield");
                _shield.gameObject.SetActive(true);
                _shield.position = new Vector3(sim.Player.Position.X, sim.Player.Position.Y + 0.9f, z + 0.3f);
                _shield.localScale = new Vector3(1.4f, 2.0f, 0.2f);
            }
            else if (_shield != null) _shield.gameObject.SetActive(false);
        }
    }
}
