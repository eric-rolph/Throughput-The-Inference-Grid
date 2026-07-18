using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Throughput.Sim;

namespace Throughput.Game
{
    /// All screen-space UI: top bar (6 widgets), left gutter (goals + contracts),
    /// bottom toolbar (build chips + overlays + reason strip), docked inspect
    /// panel, time controls, undo, ticker, contextual buy-chips. No modals.
    public sealed class HudController : MonoBehaviour
    {
        private DcWorld _world;
        private GameController _game;
        private Canvas _canvas;

        private Text _cash, _net, _served, _dayClock, _reasonStrip, _hintBanner;
        private Image _powerFill, _bwFill, _servedFill;
        private Text _powerLabel, _bwLabel;
        private RectTransform _breakevenMark;

        private RectTransform _goalRow1, _goalRow2;
        private Text _goal1Text, _goal2Text;
        private Image _goal1Progress;

        private readonly RectTransform[] _contractCards = new RectTransform[2];
        private readonly OfferState[] _cardState = { (OfferState)(-1), (OfferState)(-1) };

        private readonly List<Button> _buildButtons = new List<Button>();
        private readonly List<Text> _buildLabels = new List<Text>();
        private static readonly BuildingKind[] BuildKinds =
            { BuildingKind.CpuRack, BuildingKind.GpuRack, BuildingKind.Pdu, BuildingKind.Crac };

        private Button _heatToggle, _powerToggle, _netToggle, _undoBtn;
        private Button _uplinkChip, _feedChip;
        private Text _uplinkChipText, _feedChipText;

        private RectTransform _inspectPanel;
        private Text _inspectTitle, _inspectStatus, _inspectStats;
        private Button _toggleBtn, _sellBtn, _restartBtn;
        private int _inspectId = -1;

        private readonly List<Text> _tickerLines = new List<Text>();
        private readonly List<float> _tickerAges = new List<float>();
        private float _hintLife;

        public void Init(GameController game)
        {
            _game = game;
            _canvas = UiBuilder.CreateCanvas("HUD");
            BuildTopBar();
            BuildLeftGutter();
            BuildToolbar();
            BuildInspectPanel();
            BuildCorners();
            BuildTicker();
            BuildContextChips();
        }

        public void BindWorld(DcWorld world) => _world = world;

        // ------------------------------------------------------------ builders

        private void BuildTopBar()
        {
            RectTransform top = UiBuilder.Panel(_canvas.transform, "TopBar", GameTheme.PanelBg);
            UiBuilder.Place(top, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -48), Vector2.zero);

            _cash = UiBuilder.Label(top, "Cash", "$10,000", 24, GameTheme.Ok);
            UiBuilder.Place(_cash.rectTransform, new Vector2(0, 0), new Vector2(0.11f, 1), new Vector2(14, 0), Vector2.zero);

            _net = UiBuilder.Label(top, "Net", "NET +$0.0/s", 18, GameTheme.Ok);
            UiBuilder.Place(_net.rectTransform, new Vector2(0.11f, 0), new Vector2(0.24f, 1), Vector2.zero, Vector2.zero);

            _served = UiBuilder.Label(top, "Served", "0.0 / 0 PF", 16, GameTheme.TextBright);
            UiBuilder.Place(_served.rectTransform, new Vector2(0.24f, 0.45f), new Vector2(0.44f, 1), Vector2.zero, Vector2.zero);
            var servedBarBg = UiBuilder.Panel(top, "ServedBg", new Color(1, 1, 1, 0.08f));
            UiBuilder.Place(servedBarBg, new Vector2(0.24f, 0.12f), new Vector2(0.44f, 0.42f), Vector2.zero, new Vector2(-8, 0));
            _servedFill = UiBuilder.Panel(servedBarBg, "fill", GameTheme.JobCyan).GetComponent<Image>();
            UiBuilder.Place(_servedFill.rectTransform, Vector2.zero, new Vector2(0f, 1f), Vector2.zero, Vector2.zero);
            _breakevenMark = UiBuilder.Panel(servedBarBg, "breakeven", GameTheme.Warn);
            UiBuilder.Place(_breakevenMark, Vector2.zero, new Vector2(0, 1), Vector2.zero, new Vector2(3, 0));

            var pow = MakeBar(top, "Power", 0.46f, 0.62f, GameTheme.Warn, out _powerFill, out _powerLabel);
            var bw = MakeBar(top, "Bw", 0.64f, 0.80f, GameTheme.JobCyan, out _bwFill, out _bwLabel);

            _dayClock = UiBuilder.Label(top, "Clock", "", 16, GameTheme.TextDim, TextAnchor.MiddleRight);
            UiBuilder.Place(_dayClock.rectTransform, new Vector2(0.81f, 0), new Vector2(1, 1), Vector2.zero, new Vector2(-12, 0));
        }

        private RectTransform MakeBar(Transform parent, string name, float x0, float x1, Color fillColor,
                                       out Image fill, out Text label)
        {
            var bg = UiBuilder.Panel(parent, name + "Bg", new Color(1, 1, 1, 0.08f));
            UiBuilder.Place(bg, new Vector2(x0, 0.2f), new Vector2(x1, 0.8f), Vector2.zero, Vector2.zero);
            fill = UiBuilder.Panel(bg, "fill", fillColor).GetComponent<Image>();
            UiBuilder.Place(fill.rectTransform, Vector2.zero, new Vector2(0f, 1f), Vector2.zero, Vector2.zero);
            label = UiBuilder.Label(bg, "label", "", 14, GameTheme.TextBright, TextAnchor.MiddleCenter);
            UiBuilder.Fill(label.rectTransform);
            return bg;
        }

        private void BuildLeftGutter()
        {
            _goalRow1 = UiBuilder.Panel(_canvas.transform, "Goal1", GameTheme.ChipBg);
            UiBuilder.Place(_goalRow1, new Vector2(0, 1), new Vector2(0, 1), new Vector2(10, -110), new Vector2(330, -58));
            _goal1Text = UiBuilder.Label(_goalRow1, "txt", "", 18, GameTheme.TextBright, TextAnchor.MiddleLeft);
            UiBuilder.Place(_goal1Text.rectTransform, Vector2.zero, Vector2.one, new Vector2(12, 0), new Vector2(-8, 0));
            _goal1Progress = UiBuilder.Panel(_goalRow1, "prog", new Color(0.35f, 0.88f, 0.75f, 0.35f)).GetComponent<Image>();
            UiBuilder.Place(_goal1Progress.rectTransform, Vector2.zero, new Vector2(0, 1), Vector2.zero, Vector2.zero);
            _goal1Progress.transform.SetAsFirstSibling();

            _goalRow2 = UiBuilder.Panel(_canvas.transform, "Goal2", new Color(0.06f, 0.08f, 0.12f, 0.7f));
            UiBuilder.Place(_goalRow2, new Vector2(0, 1), new Vector2(0, 1), new Vector2(10, -152), new Vector2(330, -114));
            _goal2Text = UiBuilder.Label(_goalRow2, "txt", "", 15, GameTheme.TextDim, TextAnchor.MiddleLeft);
            UiBuilder.Place(_goal2Text.rectTransform, Vector2.zero, Vector2.one, new Vector2(12, 0), new Vector2(-8, 0));

            for (int i = 0; i < 2; i++)
            {
                _contractCards[i] = UiBuilder.Panel(_canvas.transform, "Contract" + i, GameTheme.PanelBg);
                UiBuilder.Place(_contractCards[i], new Vector2(0, 1), new Vector2(0, 1),
                    new Vector2(10, -352 - i * 210), new Vector2(330, -160 - i * 210));
                _contractCards[i].gameObject.SetActive(false);
            }
        }

        private void RebuildContractCard(int idx)
        {
            RectTransform card = _contractCards[idx];
            foreach (Transform child in card) Destroy(child.gameObject);
            Offer o = _world.Contracts.Offers[idx];
            card.gameObject.SetActive(o.State == OfferState.Offered || o.State == OfferState.Active || o.State == OfferState.Fulfilled);
            if (!card.gameObject.activeSelf) return;

            Text title = UiBuilder.Label(card, "title", $"{o.Name}  [{o.Tag}]", 18,
                o.State == OfferState.Fulfilled ? GameTheme.Ok : GameTheme.JobPurple);
            UiBuilder.Place(title.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(12, -30), new Vector2(-8, -4));

            string advanceLine = o.AdvancePaid ? "$ advance: already paid" : $"$ advance: ${o.Advance:0} now";
            string body =
                $"▦ needs: {o.NeedsGpuOnline} operational GPU rack{(o.NeedsGpuOnline > 1 ? "s" : "")}\n" +
                advanceLine + "\n" +
                $"+ adds: {o.AddsPurplePf:0} PF (GPU jobs)\n" +
                $"^ rate: +{(o.RateBonus - 1f) * 100f:0}% on its jobs\n" +
                $"! by Day {o.DeadlineDay} · penalty ${o.Penalty:0}";
            Text bodyT = UiBuilder.Label(card, "body", body, 14, GameTheme.TextBright, TextAnchor.UpperLeft);
            UiBuilder.Place(bodyT.rectTransform, new Vector2(0, 0), new Vector2(1, 1), new Vector2(12, 40), new Vector2(-8, -32));

            if (o.State == OfferState.Offered)
            {
                Button accept = UiBuilder.TextButton(card, "accept", "ACCEPT", 16,
                    new Color(0.13f, 0.38f, 0.30f, 1f), GameTheme.TextBright,
                    () => { _world.Contracts.Accept(idx, _world); RebuildContractCard(idx); });
                UiBuilder.Place(accept.GetComponent<RectTransform>(), new Vector2(0, 0), new Vector2(0.55f, 0),
                    new Vector2(12, 6), new Vector2(-4, 38));
                Button pass = UiBuilder.TextButton(card, "pass", "PASS", 16,
                    new Color(0.2f, 0.2f, 0.25f, 1f), GameTheme.TextDim,
                    () => { _world.Contracts.Pass(idx, _world); RebuildContractCard(idx); });
                UiBuilder.Place(pass.GetComponent<RectTransform>(), new Vector2(0.58f, 0), new Vector2(1, 0),
                    new Vector2(4, 6), new Vector2(-12, 38));
            }
            else
            {
                string status = o.State == OfferState.Fulfilled ? "✓ CAPACITY ONLINE — bonus flowing"
                    : $"ACTIVE — need {o.NeedsGpuOnline} operational GPU by Day {o.DeadlineDay}";
                Text st = UiBuilder.Label(card, "status", status, 14,
                    o.State == OfferState.Fulfilled ? GameTheme.Ok : GameTheme.Warn, TextAnchor.MiddleLeft);
                UiBuilder.Place(st.rectTransform, new Vector2(0, 0), new Vector2(1, 0), new Vector2(12, 6), new Vector2(-8, 38));
            }
        }

        private void BuildToolbar()
        {
            RectTransform bar = UiBuilder.Panel(_canvas.transform, "Toolbar", GameTheme.PanelBg);
            UiBuilder.Place(bar, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(-430, 8), new Vector2(430, 78));

            for (int i = 0; i < BuildKinds.Length; i++)
            {
                BuildingKind kind = BuildKinds[i];
                BuildingSpec spec = Balance.Spec(kind);
                int idx = i;
                Button b = UiBuilder.TextButton(bar, "build" + i, $"{spec.Name}\n${spec.Cost:0}", 15,
                    GameTheme.ChipBg, GameTheme.TextBright,
                    () => _game.Input.BeginPlacement(kind));
                UiBuilder.Place(b.GetComponent<RectTransform>(),
                    new Vector2(i * 0.155f, 0), new Vector2(i * 0.155f + 0.15f, 1),
                    new Vector2(6, 8), new Vector2(0, -8));
                _buildButtons.Add(b);
                _buildLabels.Add(b.GetComponentInChildren<Text>());
            }

            _heatToggle = MakeToggle(bar, "HEAT", 0.66f, () => { _game.Renderer.ShowHeat = !_game.Renderer.ShowHeat; });
            _powerToggle = MakeToggle(bar, "POWER", 0.77f, () => { _game.Renderer.ShowPower = !_game.Renderer.ShowPower; });
            _netToggle = MakeToggle(bar, "NETWORK", 0.88f, () => { _game.Renderer.ShowNetwork = !_game.Renderer.ShowNetwork; });

            _reasonStrip = UiBuilder.Label(_canvas.transform, "Reason", "", 17, GameTheme.Warn, TextAnchor.MiddleCenter);
            UiBuilder.Place(_reasonStrip.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(-430, 82), new Vector2(430, 108));

            _hintBanner = UiBuilder.Label(_canvas.transform, "Hint", "", 20, GameTheme.Warn, TextAnchor.MiddleCenter);
            UiBuilder.Place(_hintBanner.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-400, 120), new Vector2(400, 160));
        }

        private Button MakeToggle(Transform parent, string label, float x, System.Action onClick)
        {
            Button b = UiBuilder.TextButton(parent, "tog" + label, label, 13,
                new Color(0.08f, 0.10f, 0.16f, 1f), GameTheme.TextDim, onClick);
            UiBuilder.Place(b.GetComponent<RectTransform>(), new Vector2(x, 0.15f), new Vector2(x + 0.10f, 0.85f),
                Vector2.zero, Vector2.zero);
            return b;
        }

        private void BuildInspectPanel()
        {
            _inspectPanel = UiBuilder.Panel(_canvas.transform, "Inspect", GameTheme.PanelBg);
            UiBuilder.Place(_inspectPanel, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-300, -140), new Vector2(-8, 140));

            _inspectTitle = UiBuilder.Label(_inspectPanel, "title", "", 20, GameTheme.TextBright);
            UiBuilder.Place(_inspectTitle.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(12, -34), new Vector2(-30, -6));
            _inspectStatus = UiBuilder.Label(_inspectPanel, "status", "", 15, GameTheme.Warn, TextAnchor.UpperLeft);
            UiBuilder.Place(_inspectStatus.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(12, -80), new Vector2(-8, -38));
            _inspectStats = UiBuilder.Label(_inspectPanel, "stats", "", 14, GameTheme.TextDim, TextAnchor.UpperLeft);
            UiBuilder.Place(_inspectStats.rectTransform, new Vector2(0, 0), new Vector2(1, 1), new Vector2(12, 96), new Vector2(-8, -84));

            _toggleBtn = UiBuilder.TextButton(_inspectPanel, "toggle", "TOGGLE OFF", 15,
                new Color(0.16f, 0.20f, 0.30f, 1f), GameTheme.TextBright, () =>
                {
                    if (_inspectId < 0) return;
                    Building selected = _world.Buildings[_inspectId];
                    if (selected.Kind == BuildingKind.Uplink) _world.BuyUplink();
                    else if (selected.Kind == BuildingKind.GridFeed) _world.OrderSubstation();
                    else _world.ToggleBuilding(_inspectId);
                });
            UiBuilder.Place(_toggleBtn.GetComponent<RectTransform>(), new Vector2(0, 0), new Vector2(0.5f, 0),
                new Vector2(10, 52), new Vector2(-4, 90));

            _sellBtn = UiBuilder.TextButton(_inspectPanel, "sell", "SELL", 15,
                new Color(0.35f, 0.16f, 0.16f, 1f), GameTheme.TextBright, () =>
                { if (_inspectId >= 0 && _world.TrySell(_inspectId)) CloseInspect(); });
            UiBuilder.Place(_sellBtn.GetComponent<RectTransform>(), new Vector2(0.5f, 0), new Vector2(1, 0),
                new Vector2(4, 52), new Vector2(-10, 90));

            _restartBtn = UiBuilder.TextButton(_inspectPanel, "restart", "RESTART $400", 15,
                new Color(0.35f, 0.28f, 0.10f, 1f), GameTheme.TextBright, () =>
                { if (_inspectId >= 0) _world.RestartHeatShutdown(_inspectId); });
            UiBuilder.Place(_restartBtn.GetComponent<RectTransform>(), new Vector2(0, 0), new Vector2(1, 0),
                new Vector2(10, 8), new Vector2(-10, 46));

            Button close = UiBuilder.TextButton(_inspectPanel, "close", "×", 20,
                Color.clear, GameTheme.TextDim, CloseInspect);
            UiBuilder.Place(close.GetComponent<RectTransform>(), new Vector2(1, 1), new Vector2(1, 1),
                new Vector2(-30, -32), new Vector2(-4, -4));

            _inspectPanel.gameObject.SetActive(false);
        }

        private void BuildCorners()
        {
            RectTransform speed = UiBuilder.Panel(_canvas.transform, "Speed", Color.clear);
            UiBuilder.Place(speed, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-438, -110), new Vector2(-8, -54));
            string[] labels = { "II", "1x", "3x" };
            float[] mults = { 0f, 1f, 3f };
            for (int i = 0; i < 3; i++)
            {
                float m = mults[i];
                Button b = UiBuilder.TextButton(speed, "spd" + i, labels[i], 17,
                    new Color(0.12f, 0.16f, 0.24f, 0.95f), GameTheme.TextBright, () => _game.SetSpeed(m));
                UiBuilder.Place(b.GetComponent<RectTransform>(), new Vector2(i / 6f, 0), new Vector2((i + 1) / 6f, 1),
                    new Vector2(2, 0), new Vector2(-2, 0));
            }

            Button zoomOut = UiBuilder.TextButton(speed, "zoomOut", "-", 22,
                new Color(0.12f, 0.16f, 0.24f, 0.95f), GameTheme.TextBright, () => _game.AdjustZoom(0.8f));
            UiBuilder.Place(zoomOut.GetComponent<RectTransform>(), new Vector2(3f / 6f, 0), new Vector2(4f / 6f, 1),
                new Vector2(2, 0), new Vector2(-2, 0));
            Button zoomIn = UiBuilder.TextButton(speed, "zoomIn", "+", 22,
                new Color(0.12f, 0.16f, 0.24f, 0.95f), GameTheme.TextBright, () => _game.AdjustZoom(-0.8f));
            UiBuilder.Place(zoomIn.GetComponent<RectTransform>(), new Vector2(4f / 6f, 0), new Vector2(5f / 6f, 1),
                new Vector2(2, 0), new Vector2(-2, 0));

            _undoBtn = UiBuilder.TextButton(speed, "undo", "UNDO", 15,
                new Color(0.12f, 0.16f, 0.24f, 0.95f), GameTheme.TextBright, () => _world.TryUndo());
            UiBuilder.Place(_undoBtn.GetComponent<RectTransform>(), new Vector2(5f / 6f, 0), new Vector2(1, 1),
                new Vector2(2, 0), new Vector2(-2, 0));
        }

        private void BuildTicker()
        {
            for (int i = 0; i < 5; i++)
            {
                Text t = UiBuilder.Label(_canvas.transform, "tick" + i, "", 14, GameTheme.TextDim);
                UiBuilder.Place(t.rectTransform, new Vector2(0, 0), new Vector2(0.38f, 0),
                    new Vector2(12, 120 + i * 21), new Vector2(0, 141 + i * 21));
                _tickerLines.Add(t);
                _tickerAges.Add(999f);
            }
        }

        private void BuildContextChips()
        {
            _uplinkChip = UiBuilder.TextButton(_canvas.transform, "uplinkChip", "", 14,
                new Color(0.10f, 0.30f, 0.26f, 0.97f), GameTheme.TextBright, () => _world.BuyUplink());
            var urt = _uplinkChip.GetComponent<RectTransform>();
            urt.sizeDelta = new Vector2(210, 40);
            _uplinkChipText = _uplinkChip.GetComponentInChildren<Text>();
            _uplinkChip.gameObject.SetActive(false);

            _feedChip = UiBuilder.TextButton(_canvas.transform, "feedChip", "", 14,
                new Color(0.32f, 0.26f, 0.08f, 0.97f), GameTheme.TextBright, () => _world.OrderSubstation());
            var frt = _feedChip.GetComponent<RectTransform>();
            frt.sizeDelta = new Vector2(230, 40);
            _feedChipText = _feedChip.GetComponentInChildren<Text>();
            _feedChip.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------ runtime

        public void SetModeHint(string s) => _reasonStrip.text = s;

        public void ShowHint(string s)
        {
            _hintBanner.text = s;
            _hintLife = 4f;
        }

        public void OpenInspect(int id)
        {
            _inspectId = id;
            _inspectPanel.gameObject.SetActive(true);
        }

        public void CloseInspect()
        {
            _inspectId = -1;
            _inspectPanel.gameObject.SetActive(false);
        }

        private void Update()
        {
            if (_world == null) return;

            _cash.text = $"${_world.Cash:N0}";
            _cash.color = _world.Cash < 0 ? GameTheme.Danger : GameTheme.Ok;

            float net = _world.NetPerSec;
            _net.text = $"NET {(net >= 0 ? "+" : "−")}${Mathf.Abs(net):0.0}/s";
            _net.color = net >= 0 ? GameTheme.Ok : GameTheme.Danger;

            float demand = _world.DemandCyanPf + _world.DemandPurplePf;
            _served.text = $"{_world.ServedPf:0.0}/{demand:0.0} PF   queue {_world.QueueDepth:0.0}";
            float servedFrac = demand > 0 ? Mathf.Clamp01(_world.ServedPf / demand) : 0f;
            _servedFill.rectTransform.anchorMax = new Vector2(servedFrac, 1f);
            float bkFrac = demand > 0 ? Mathf.Clamp01(_world.BreakevenPf / demand) : 0f;
            _breakevenMark.anchorMin = new Vector2(bkFrac, 0);
            _breakevenMark.anchorMax = new Vector2(bkFrac, 1);

            float powFrac = Mathf.Clamp01(_world.FeedLoadKw / _world.FeedCapKw);
            _powerFill.rectTransform.anchorMax = new Vector2(powFrac, 1f);
            _powerFill.color = powFrac > 0.9f ? GameTheme.Danger : GameTheme.Warn;
            string sub = _world.SubstationEta > 0 ? $"  (+500 in {_world.SubstationEta:0}s)" : "";
            _powerLabel.text = $"{_world.FeedLoadKw:0}/{_world.FeedCapKw:0} kW{sub}";

            float bwFrac = Mathf.Clamp01(_world.BandwidthUsed / _world.BandwidthCap);
            _bwFill.rectTransform.anchorMax = new Vector2(bwFrac, 1f);
            _bwLabel.text = $"{_world.BandwidthUsed:0.0}/{_world.BandwidthCap:0} Gbps";

            _dayClock.text = $"Day {_world.Day}  {FormatClock(_world.ClockHours)}   ${_world.PricePerKwS:0.000}/kWs {(_world.PriceRising ? "▲" : "▼")}";

            UpdateGoals();
            UpdateContracts();
            UpdateBuildButtons();
            UpdateContextChips();
            UpdateInspect();
            DrainTicker();

            if (_hintLife > 0f)
            {
                _hintLife -= Time.deltaTime;
                Color c = _hintBanner.color; c.a = Mathf.Clamp01(_hintLife); _hintBanner.color = c;
                if (_hintLife <= 0f) _hintBanner.text = "";
            }
        }

        private void UpdateGoals()
        {
            GoalChip cur = _world.Goals.Current;
            GoalChip next = _world.Goals.Next;
            bool complete = _world.Goals.IsComplete;
            _goalRow1.gameObject.SetActive(cur != null || complete);
            if (complete)
            {
                _goal1Text.text = "★ GRID MASTERED — NIMBUS DELIVERED";
                _goal1Text.color = GameTheme.Ok;
                _goal1Progress.rectTransform.anchorMax = Vector2.one;
            }
            else if (cur != null)
            {
                _goal1Text.text = cur.Text + (cur.Reward > 0 ? $"  (+${cur.Reward:0})" : "");
                _goal1Text.color = GameTheme.TextBright;
                _goal1Progress.rectTransform.anchorMax = new Vector2(cur.HasProgress ? cur.Progress : 0f, 1f);
            }
            _goalRow2.gameObject.SetActive(!complete && next != null);
            if (next != null) _goal2Text.text = "next: " + next.Text;
        }

        private void UpdateContracts()
        {
            for (int i = 0; i < 2; i++)
            {
                OfferState s = _world.Contracts.Offers[i].State;
                if (s != _cardState[i]) { _cardState[i] = s; RebuildContractCard(i); }
            }
        }

        private void UpdateBuildButtons()
        {
            for (int i = 0; i < BuildKinds.Length; i++)
            {
                BuildingKind kind = BuildKinds[i];
                BuildingSpec spec = Balance.Spec(kind);
                bool locked = (kind == BuildingKind.GpuRack && !_world.GpuUnlocked) ||
                              (kind == BuildingKind.Crac && !_world.CracUnlocked);
                bool affordable = _world.Cash >= spec.Cost;
                bool selected = _game.Renderer.PlacingKind == kind;
                _buildButtons[i].interactable = !locked && affordable;
                _buildButtons[i].GetComponent<Image>().color = selected
                    ? new Color(0.12f, 0.42f, 0.34f, 1f)
                    : GameTheme.ChipBg;
                if (locked)
                {
                    _buildLabels[i].text = kind == BuildingKind.GpuRack
                        ? $"GPU Rack\n🔒 ${Balance.GpuEarnedGate:0} earned" : $"{spec.Name}\n🔒";
                    _buildLabels[i].color = GameTheme.TextDim;
                }
                else
                {
                    _buildLabels[i].text = $"{(selected ? "✕ " : "")}{spec.Name}\n${spec.Cost:0}";
                    _buildLabels[i].color = affordable ? GameTheme.TextBright : GameTheme.TextDim;
                }
            }

            _undoBtn.interactable = _world.CanUndo;
            SetToggleTint(_heatToggle, _game.Renderer.ShowHeat);
            SetToggleTint(_powerToggle, _game.Renderer.ShowPower);
            SetToggleTint(_netToggle, _game.Renderer.ShowNetwork);
        }

        private void SetToggleTint(Button b, bool on)
        {
            b.GetComponent<Image>().color = on ? new Color(0.16f, 0.32f, 0.30f, 1f) : new Color(0.08f, 0.10f, 0.16f, 1f);
            b.GetComponentInChildren<Text>().color = on ? GameTheme.Ok : GameTheme.TextDim;
        }

        private void UpdateContextChips()
        {
            // Uplink upgrade chip at >=80% bandwidth
            bool showUplink = _world.BandwidthUsed >= _world.BandwidthCap * 0.8f;
            _uplinkChip.gameObject.SetActive(showUplink);
            if (showUplink)
            {
                Building up = _world.Buildings[1];
                _uplinkChip.GetComponent<RectTransform>().position =
                    _game.Cam.WorldToScreenPoint(new Vector3(up.X + 2.6f, up.Y + 1.6f, 0));
                _uplinkChipText.text = $"+10 Gbps — ${Balance.UplinkUpgradeCost:0}";
                _uplinkChip.interactable = _world.Cash >= Balance.UplinkUpgradeCost;
            }

            bool showFeed = _world.FeedLoadKw >= _world.FeedCapKw * 0.8f && _world.SubstationEta < 0f;
            _feedChip.gameObject.SetActive(showFeed);
            if (showFeed)
            {
                Building feed = _world.Buildings[0];
                _feedChip.GetComponent<RectTransform>().position =
                    _game.Cam.WorldToScreenPoint(new Vector3(feed.X + 0.5f, feed.Y - 1.2f, 0));
                _feedChipText.text = $"+500 kW — ${Balance.SubstationCost:0} · 90s";
                _feedChip.interactable = _world.Cash >= Balance.SubstationCost;
            }
        }

        private void UpdateInspect()
        {
            if (_inspectId < 0 || !_inspectPanel.gameObject.activeSelf) return;
            Building b = _world.Buildings[_inspectId];
            if (b.Removed) { CloseInspect(); return; }
            BuildingSpec spec = b.Spec;

            _inspectTitle.text = spec.Name;
            _inspectStatus.text = StatusLine(b);
            _inspectStatus.color = b.State == BuildingState.Online && b.HasPower && !b.ToggledOff
                ? GameTheme.Ok : GameTheme.Warn;

            string stats = $"draw {spec.DrawKw:0} kW · tile {b.TileTemp:0}°";
            if (spec.IsRack)
                stats += $"\nserving {b.ServedPf:0.0}/{spec.ComputePf:0} PF · {spec.BandwidthGbps:0} Gbps\nrevenue ${b.RevenueRate:0.00}/s";
            if (spec.PduCapKw > 0)
                stats += $"\nload {_world.PduLoad(b.Id):0}/{spec.PduCapKw:0} kW";
            if (b.Kind == BuildingKind.Uplink)
                stats += $"\nattempted {_world.BandwidthUsed:0}/{_world.BandwidthCap:0} Gbps · accepted {_world.BandwidthAccepted:0}";
            if (b.Kind == BuildingKind.GridFeed)
                stats += $"\nload {_world.FeedLoadKw:0}/{_world.FeedCapKw:0} kW";
            _inspectStats.text = stats;

            if (b.Kind == BuildingKind.Uplink)
            {
                _toggleBtn.GetComponentInChildren<Text>().text = $"BUY +10 Gbps  ${Balance.UplinkUpgradeCost:0}";
                _toggleBtn.interactable = _world.Cash >= Balance.UplinkUpgradeCost;
            }
            else if (b.Kind == BuildingKind.GridFeed)
            {
                _toggleBtn.GetComponentInChildren<Text>().text = _world.SubstationEta > 0f
                    ? $"SUBSTATION  {_world.SubstationEta:0}s"
                    : $"ORDER +500 kW  ${Balance.SubstationCost:0}";
                _toggleBtn.interactable = _world.Cash >= Balance.SubstationCost && _world.SubstationEta < 0f;
            }
            else
            {
                _toggleBtn.GetComponentInChildren<Text>().text = b.ToggledOff ? "POWER ON" : "TOGGLE OFF";
                _toggleBtn.interactable = true;
            }
            bool sellable = !b.PrePlaced;
            _sellBtn.gameObject.SetActive(sellable);
            UiBuilder.Place(_toggleBtn.GetComponent<RectTransform>(), new Vector2(0, 0),
                new Vector2(sellable ? 0.5f : 1f, 0), new Vector2(10, 52),
                new Vector2(sellable ? -4 : -10, 90));
            _restartBtn.gameObject.SetActive(b.State == BuildingState.HeatShutdown);
        }

        private string StatusLine(Building b)
        {
            if (b.ToggledOff) return "Powered down by operator";
            if (b.State == BuildingState.TrippedDark)
                return $"DARK — breaker tripped, {b.DarkRemaining:0.0}s";
            if ((b.Spec.IsRack || b.Kind == BuildingKind.Crac) && !b.HasPower)
                return b.State == BuildingState.Booting
                    ? $"NO POWER — boot paused at {b.BootRemaining:0.0}s"
                    : "NO POWER — supplying PDU unavailable";
            switch (b.State)
            {
                case BuildingState.Booting: return $"Booting — {b.BootRemaining:0.0}s";
                case BuildingState.HeatShutdown: return $"THERMAL SHUTDOWN at {b.TileTemp:0}° — cool below 60°, restart $400";
                default:
                    if (b.NoUplinkFlag) return "NO UPLINK — bandwidth saturated";
                    if (b.TileTemp >= Balance.HotTemp) return $"Throttled −50%: {b.TileTemp:0}° — no cooling in range";
                    if (b.TileTemp >= Balance.WarmTemp) return $"Throttled −25%: {b.TileTemp:0}° — running warm";
                    if (b.Spec.IsRack && b.ServedPf <= 0.01f) return "Starving — no demand reaching it";
                    return "Online";
            }
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
                col.a = Mathf.Clamp01(1.5f - _tickerAges[i] / 10f);
                _tickerLines[i].color = col;
            }
        }

        private static string FormatClock(float hours)
        {
            int h = (int)hours, m = (int)((hours - h) * 60f);
            return $"{h:00}:{m:00}";
        }
    }
}
