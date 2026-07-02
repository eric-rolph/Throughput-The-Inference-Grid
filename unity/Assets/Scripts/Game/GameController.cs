using UnityEngine;
using UnityEngine.EventSystems;
using Throughput.Sim;

namespace Throughput.Game
{
    /// Scene bootstrap + fixed-timestep loop + contract flow.
    /// The only component the scene needs.
    public sealed class GameController : MonoBehaviour
    {
        public HudController Hud { get; private set; }
        public InputController Input { get; private set; }

        private SimWorld _world;
        private WorldRenderer _renderer;
        private Camera _cam;
        private Font _font;

        private float _accumulator;
        private float _speed = 1f;
        private int _contractIndex;
        private bool _cardShowing;

        private void Awake()
        {
            Application.targetFrameRate = 60;
            SpriteFactory.Build();
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            UiBuilder.UiFont = _font;

            SetupCamera();
            if (FindFirstObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.AddComponent<EventSystem>();
                es.AddComponent<StandaloneInputModule>();
            }

            _renderer = gameObject.AddComponent<WorldRenderer>();
            Hud = gameObject.AddComponent<HudController>();
            Hud.Init(this);
            Input = gameObject.AddComponent<InputController>();
            Input.Init(this, _cam);

            StartContract(0, showIntro: true);
        }

        private void SetupCamera()
        {
            _cam = Camera.main;
            if (_cam == null)
            {
                var go = new GameObject("Main Camera");
                go.tag = "MainCamera";
                _cam = go.AddComponent<Camera>();
            }
            _cam.orthographic = true;
            _cam.orthographicSize = 15.2f;
            _cam.transform.position = new Vector3(SimWorld.GridW / 2f, SimWorld.GridH / 2f - 0.8f, -10f);
            _cam.backgroundColor = GameTheme.Background;
            _cam.clearFlags = CameraClearFlags.SolidColor;
        }

        private void StartContract(int index, bool showIntro)
        {
            _contractIndex = index;
            ContractSpec spec = ContractSpec.All[index];
            _world = new SimWorld(spec);
            _renderer.Init(_world, _font);
            _renderer.OnSimTicked();
            Hud.BindWorld(_world);
            Input.BindWorld(_world);
            _accumulator = 0f;
            _speed = 0f; // paused behind the card

            if (showIntro)
            {
                _cardShowing = true;
                Hud.ShowCard($"CONTRACT {spec.Number} — {spec.Title}", spec.Brief, "ACCEPT CONTRACT", () =>
                {
                    _cardShowing = false;
                    Hud.HideCard();
                    _speed = 1f;
                });
            }
            else _speed = 1f;
        }

        public void SetSpeed(float s)
        {
            if (_cardShowing) return;
            _speed = s;
        }

        private void Update()
        {
            if (_world == null) return;

            if (_world.State == ContractState.Running && _speed > 0f)
            {
                _accumulator += Time.deltaTime * _speed;
                int safety = 0;
                while (_accumulator >= Tuning.TickDt && safety++ < 40)
                {
                    _accumulator -= Tuning.TickDt;
                    _world.Step();
                    _renderer.OnSimTicked();
                }
            }

            float alpha = Mathf.Clamp01(_accumulator / Tuning.TickDt);
            _renderer.Render(_speed > 0f ? alpha : 1f);

            if (_world.State != ContractState.Running && !_cardShowing)
                ShowEndCard();
        }

        private void ShowEndCard()
        {
            _cardShowing = true;
            _speed = 0f;
            ContractSpec spec = _world.Contract;

            if (_world.State == ContractState.Won)
            {
                string stats = $"Earned ${_world.Earned:0}   ·   {_world.Served} served" +
                               $"   ·   SLO {(_world.InSloShare * 100f):0}%   ·   breaches {_world.Breaches}";
                bool more = _contractIndex + 1 < ContractSpec.All.Count;
                Hud.ShowCard("CONTRACT COMPLETE",
                    $"{spec.Title} delivered.\n\n{stats}\n\n" +
                    (more ? "A new contract is waiting in your inbox."
                          : "That's the whole demo grid for now — thanks for playing.\nThe network hums on without you..."),
                    more ? "NEXT CONTRACT" : "FREE PLAY",
                    () =>
                    {
                        _cardShowing = false;
                        Hud.HideCard();
                        if (more) StartContract(_contractIndex + 1, showIntro: true);
                        else StartContract(_contractIndex, showIntro: false);
                    });
            }
            else
            {
                Hud.ShowCard("CONTRACT FAILED",
                    $"Three SLA breaches — the client walked.\n\n" +
                    $"Earned ${_world.Earned:0} · {_world.Served} served · SLO {(_world.InSloShare * 100f):0}%\n\n" +
                    "Same seed, same traffic. Route it better this time.",
                    "RETRY CONTRACT",
                    () =>
                    {
                        _cardShowing = false;
                        Hud.HideCard();
                        StartContract(_contractIndex, showIntro: false);
                    });
            }
        }
    }
}
