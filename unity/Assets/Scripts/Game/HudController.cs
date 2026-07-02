using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Throughput.Sim;

namespace Throughput.Game
{
    /// Top bar, palette, ticker, spike banner, contract overlay cards.
    public sealed class HudController : MonoBehaviour
    {
        private SimWorld _world;
        private GameController _game;

        private Text _cash, _goal, _slo, _breaches, _clock, _spikeBanner, _modeHint;
        private RectTransform _paletteRow;
        private readonly List<Button> _paletteButtons = new List<Button>();
        private readonly List<NodeKind> _paletteKinds = new List<NodeKind>();
        private readonly List<Text> _tickerLines = new List<Text>();
        private readonly List<float> _tickerAges = new List<float>();
        private RectTransform _overlay;
        private Canvas _canvas;

        public void Init(GameController game)
        {
            _game = game;
            _canvas = UiBuilder.CreateCanvas("HUD");

            // ---- top bar
            RectTransform top = UiBuilder.Panel(_canvas.transform, "TopBar", GameTheme.PanelBg);
            UiBuilder.Place(top, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -46), Vector2.zero);

            _cash = UiBuilder.Label(top, "Cash", "$0", 24, GameTheme.Ok);
            UiBuilder.Place(_cash.rectTransform, new Vector2(0, 0), new Vector2(0, 1), new Vector2(16, 0), new Vector2(240, 0));
            _goal = UiBuilder.Label(top, "Goal", "", 18, GameTheme.TextBright, TextAnchor.MiddleCenter);
            UiBuilder.Place(_goal.rectTransform, new Vector2(0.28f, 0), new Vector2(0.72f, 1), Vector2.zero, Vector2.zero);
            _slo = UiBuilder.Label(top, "Slo", "SLO 100%", 18, GameTheme.TextDim);
            UiBuilder.Place(_slo.rectTransform, new Vector2(0.74f, 0), new Vector2(0.85f, 1), Vector2.zero, Vector2.zero);
            _breaches = UiBuilder.Label(top, "Breaches", "", 18, GameTheme.Danger);
            UiBuilder.Place(_breaches.rectTransform, new Vector2(0.85f, 0), new Vector2(0.93f, 1), Vector2.zero, Vector2.zero);
            _clock = UiBuilder.Label(top, "Clock", "0:00", 18, GameTheme.TextDim, TextAnchor.MiddleRight);
            UiBuilder.Place(_clock.rectTransform, new Vector2(0.93f, 0), new Vector2(1, 1), Vector2.zero, new Vector2(-14, 0));

            // ---- speed buttons
            RectTransform speed = UiBuilder.Panel(_canvas.transform, "Speed", Color.clear);
            UiBuilder.Place(speed, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-170, -92), new Vector2(-10, -50));
            string[] speeds = { "II", "1x", "2x", "4x" };
            float[] mults = { 0f, 1f, 2f, 4f };
            for (int i = 0; i < speeds.Length; i++)
            {
                float m = mults[i];
                Button b = UiBuilder.TextButton(speed, "spd" + i, speeds[i], 16,
                    new Color(0.12f, 0.16f, 0.24f, 0.9f), GameTheme.TextBright,
                    () => _game.SetSpeed(m));
                var rt = b.GetComponent<RectTransform>();
                UiBuilder.Place(rt, new Vector2(i / 4f, 0), new Vector2((i + 1) / 4f, 1), new Vector2(2, 0), new Vector2(-2, 0));
            }

            // ---- spike banner
            _spikeBanner = UiBuilder.Label(_canvas.transform, "Spike", "", 26, GameTheme.Warn, TextAnchor.MiddleCenter);
            UiBuilder.Place(_spikeBanner.rectTransform, new Vector2(0.2f, 1), new Vector2(0.8f, 1), new Vector2(0, -100), new Vector2(0, -56));

            // ---- palette (bottom center)
            _paletteRow = UiBuilder.Panel(_canvas.transform, "Palette", GameTheme.PanelBg);
            UiBuilder.Place(_paletteRow, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(-390, 8), new Vector2(390, 66));

            // ---- mode hint
            _modeHint = UiBuilder.Label(_canvas.transform, "Hint", "", 16, GameTheme.TextDim, TextAnchor.MiddleCenter);
            UiBuilder.Place(_modeHint.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(-380, 70), new Vector2(380, 96));

            // ---- ticker (bottom left)
            for (int i = 0; i < 5; i++)
            {
                Text t = UiBuilder.Label(_canvas.transform, "tick" + i, "", 15, GameTheme.TextDim);
                UiBuilder.Place(t.rectTransform, new Vector2(0, 0), new Vector2(0.4f, 0),
                    new Vector2(12, 108 + i * 22), new Vector2(0, 130 + i * 22));
                _tickerLines.Add(t);
                _tickerAges.Add(999f);
            }
        }

        public void BindWorld(SimWorld world)
        {
            _world = world;
            BuildPalette();
            foreach (Text t in _tickerLines) t.text = "";
        }

        private void BuildPalette()
        {
            foreach (Button b in _paletteButtons) Destroy(b.gameObject);
            _paletteButtons.Clear();
            _paletteKinds.Clear();

            NodeKind[] kinds = _world.Contract.Palette;
            int total = kinds.Length + 1; // + link tool
            for (int i = 0; i < kinds.Length; i++)
            {
                NodeKind kind = kinds[i];
                NodeSpec spec = Tuning.Spec(kind);
                Button b = UiBuilder.TextButton(_paletteRow, "pal" + i,
                    $"{spec.Name}\n${spec.Cost:0}", 15,
                    new Color(0.10f, 0.14f, 0.22f, 1f), GameTheme.TextBright,
                    () => _game.Input.BeginPlacement(kind));
                var rt = b.GetComponent<RectTransform>();
                UiBuilder.Place(rt, new Vector2((float)i / total, 0), new Vector2((float)(i + 1) / total, 1),
                    new Vector2(4, 6), new Vector2(-4, -6));
                _paletteButtons.Add(b);
                _paletteKinds.Add(kind);
            }
            Button link = UiBuilder.TextButton(_paletteRow, "palLink",
                "Fiber Link\n$2/tile", 15,
                new Color(0.10f, 0.20f, 0.18f, 1f), GameTheme.Ok,
                () => _game.Input.BeginLinkMode());
            var lrt = link.GetComponent<RectTransform>();
            UiBuilder.Place(lrt, new Vector2((float)kinds.Length / total, 0), new Vector2(1f, 1),
                new Vector2(4, 6), new Vector2(-4, -6));
            _paletteButtons.Add(link);
            _paletteKinds.Add(NodeKind.Ingress); // sentinel, never disabled by cost
        }

        public void SetModeHint(string hint) => _modeHint.text = hint;

        private void Update()
        {
            if (_world == null) return;

            _cash.text = $"${_world.Cash:0}";
            _cash.color = _world.Cash < 0 ? GameTheme.Danger : GameTheme.Ok;

            ContractSpec c = _world.Contract;
            string goal;
            if (c.EarnGoal > 0)
                goal = $"C{c.Number} {c.Title}   —   earned ${_world.Earned:0} / ${c.EarnGoal:0}";
            else
                goal = $"C{c.Number} {c.Title}   —   survive {FormatTime(c.SurviveSeconds)}";
            _goal.text = goal;

            _slo.text = $"SLO {(_world.InSloShare * 100f):0}%";
            _slo.color = _world.InSloShare > 0.85f ? GameTheme.Ok :
                         _world.InSloShare > 0.6f ? GameTheme.Warn : GameTheme.Danger;
            _breaches.text = _world.Breaches > 0 ? $"breach {_world.Breaches}/3" : "";
            _clock.text = FormatTime(_world.Time);

            if (_world.SpikeActive)
            { _spikeBanner.text = "⚡ TRAFFIC SPIKE ⚡"; _spikeBanner.color = GameTheme.Danger; }
            else if (_world.SpikeWarning)
            { _spikeBanner.text = $"spike inbound — {_world.SpikeCountdown:0}s"; _spikeBanner.color = GameTheme.Warn; }
            else _spikeBanner.text = "";

            // Palette affordability
            for (int i = 0; i < _paletteButtons.Count; i++)
            {
                bool affordable = i >= _paletteKinds.Count - 1 ||
                                  _world.Cash >= Tuning.Spec(_paletteKinds[i]).Cost;
                _paletteButtons[i].interactable = affordable;
                var txt = _paletteButtons[i].GetComponentInChildren<Text>();
                if (txt != null) txt.color = affordable ? GameTheme.TextBright : GameTheme.TextDim;
            }

            DrainTicker();
        }

        private void DrainTicker()
        {
            foreach (TickerEvent e in _world.TickerEvents)
            {
                for (int i = _tickerLines.Count - 1; i > 0; i--)
                {
                    _tickerLines[i].text = _tickerLines[i - 1].text;
                    _tickerLines[i].color = _tickerLines[i - 1].color;
                    _tickerAges[i] = _tickerAges[i - 1];
                }
                _tickerLines[0].text = "› " + e.Message;
                _tickerLines[0].color = e.Severity == 2 ? GameTheme.Danger :
                                        e.Severity == 1 ? GameTheme.Warn : GameTheme.TextDim;
                _tickerAges[0] = 0f;
            }
            _world.TickerEvents.Clear();

            for (int i = 0; i < _tickerLines.Count; i++)
            {
                _tickerAges[i] += Time.deltaTime;
                Color col = _tickerLines[i].color;
                col.a = Mathf.Clamp01(1.4f - _tickerAges[i] / 9f);
                _tickerLines[i].color = col;
            }
        }

        private static string FormatTime(float seconds)
        {
            int m = (int)(seconds / 60f), s = (int)(seconds % 60f);
            return $"{m}:{s:00}";
        }

        // ------------------------------------------------------- overlay cards

        public void ShowCard(string title, string body, string buttonLabel, System.Action onClick)
        {
            HideCard();
            _overlay = UiBuilder.Panel(_canvas.transform, "Overlay", new Color(0.02f, 0.03f, 0.05f, 0.88f));
            UiBuilder.Fill(_overlay);

            RectTransform card = UiBuilder.Panel(_overlay, "Card", new Color(0.06f, 0.08f, 0.13f, 0.98f));
            UiBuilder.Place(card, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-380, -270), new Vector2(380, 270));

            Text t = UiBuilder.Label(card, "Title", title, 30, GameTheme.NodeOutline, TextAnchor.MiddleCenter);
            UiBuilder.Place(t.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -70), new Vector2(0, -12));

            Text b = UiBuilder.Label(card, "Body", body, 18, GameTheme.TextBright, TextAnchor.UpperLeft);
            b.horizontalOverflow = HorizontalWrapMode.Wrap;
            UiBuilder.Place(b.rectTransform, new Vector2(0, 0), new Vector2(1, 1), new Vector2(36, 86), new Vector2(-36, -84));

            Button btn = UiBuilder.TextButton(card, "Go", buttonLabel, 22,
                new Color(0.13f, 0.35f, 0.30f, 1f), GameTheme.TextBright, onClick);
            UiBuilder.Place(btn.GetComponent<RectTransform>(), new Vector2(0.5f, 0), new Vector2(0.5f, 0),
                new Vector2(-130, 16), new Vector2(130, 68));
        }

        public void HideCard()
        {
            if (_overlay != null) { Destroy(_overlay.gameObject); _overlay = null; }
        }
    }
}
