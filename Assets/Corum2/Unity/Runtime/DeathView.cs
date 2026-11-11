using Corum2.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Corum2.Unity
{
    /// <summary>
    /// R-06-19 death prompt and game-over menu (temporary uGUI layout; presentation only - the flow lives in Core
    /// DeathFlow). TC-06-19a: UI branch. The return itself (TC-06-19b) is BLOCKED_SPEC (U-08) and only shown as such.
    /// </summary>
    public sealed class DeathView
    {
        readonly GameObject _root;
        readonly Text _title;
        readonly Text[] _rows = new Text[3];
        readonly Text _note;

        public DeathView(Transform canvas)
        {
            _root = new GameObject("DeathView", typeof(RectTransform), typeof(Image));
            _root.transform.SetParent(canvas, false);
            var rt = (RectTransform)_root.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(620, 330);
            rt.anchoredPosition = new Vector2(0, -40);
            var img = _root.GetComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0.72f);
            img.raycastTarget = false;
            _title = Label("Title", new Vector2(0, 120), 34, FontStyle.Bold);
            for (var i = 0; i < 3; i++) _rows[i] = Label("Row" + i, new Vector2(0, 40 - i * 52), 28, FontStyle.Normal);
            _note = Label("Note", new Vector2(0, -135), 17, FontStyle.Italic);
            _root.SetActive(false);
        }

        Text Label(string name, Vector2 pos, int size, FontStyle style)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(_root.transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(600, 48);
            rt.anchoredPosition = pos;
            var t = go.GetComponent<Text>();
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.fontSize = size;
            t.fontStyle = style;
            t.alignment = TextAnchor.MiddleCenter;
            t.color = Color.white;
            t.raycastTarget = false;
            return t;
        }

        static readonly string[] PromptLabels = { "Use revive item", "Decline" };
        static readonly string[] MenuLabels = { "Restart from checkpoint", "Select save to load", "Return to lobby" };

        public void Apply(Simulation sim)
        {
            var d = sim.Death;
            var show = d != null && (d.Stage == DeathStage.RevivePrompt || d.Stage == DeathStage.GameOver || d.Stage == DeathStage.Motion);
            _root.SetActive(show);
            if (!show) return;
            switch (d.Stage)
            {
                case DeathStage.Motion:
                    _title.text = "<color=#ff7070>YOU DIED</color>";
                    for (var i = 0; i < 3; i++) _rows[i].text = "";
                    _note.text = string.Format("death motion {0:0.0}s (EXP-DEATH-01)", Mathf.Max(0f, d.Timer));
                    break;
                case DeathStage.RevivePrompt:
                    _title.text = "Use a revive item? (" + d.ReviveItemCount() + " owned)";
                    for (var i = 0; i < 3; i++) _rows[i].text = i < 2 ? Row(PromptLabels[i], i == d.Cursor) : "";
                    _note.text = "A confirm   B decline   (R-06-19, EXP-DEATH-03/04)";
                    break;
                case DeathStage.GameOver:
                    _title.text = "<color=#ff7070>GAME OVER</color>";
                    for (var i = 0; i < 3; i++) _rows[i].text = Row(MenuLabels[i], i == d.Cursor);
                    _note.text = d.Message.Length > 0 ? "<color=#ffd27a>" + d.Message + "</color>" : "A select   (return: TC-06-19b BLOCKED_SPEC, U-08)";
                    break;
            }
        }

        static string Row(string label, bool on) => on ? "<color=#ffe066>> " + label + " <</color>" : "<color=#d0d0d0>" + label + "</color>";
    }
}
