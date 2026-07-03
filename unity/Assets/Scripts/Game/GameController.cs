using UnityEngine;
using UnityEngine.EventSystems;
using Throughput.Sim;

namespace Throughput.Game
{
    /// Scene bootstrap + fixed-timestep loop + audio. Open-ended run, no modals.
    public sealed class GameController : MonoBehaviour
    {
        public HudController Hud { get; private set; }
        public InputController Input { get; private set; }
        public WorldRenderer Renderer { get; private set; }
        public Camera Cam { get; private set; }

        private DcWorld _world;
        private Font _font;
        private float _accumulator;
        private float _speed = 1f;

        private AudioSource _audio;
        private AudioClip _moneyClip, _tripClip, _chimeClip, _placeClip;
        private float _lastMoneySound;

        private void Awake()
        {
            Application.targetFrameRate = 60;
#if UNITY_WEBGL && !UNITY_EDITOR
            WebGLInput.captureAllKeyboardInput = false;
#endif
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

            SetupAudio();

            _world = new DcWorld();
            Renderer = gameObject.AddComponent<WorldRenderer>();
            Renderer.Init(_world, _font, Cam);
            Hud = gameObject.AddComponent<HudController>();
            Hud.Init(this);
            Hud.BindWorld(_world);
            Input = gameObject.AddComponent<InputController>();
            Input.Init(this, Cam, _font);
            Input.BindWorld(_world);
        }

        private void SetupCamera()
        {
            Cam = Camera.main;
            if (Cam == null)
            {
                var go = new GameObject("Main Camera");
                go.tag = "MainCamera";
                Cam = go.AddComponent<Camera>();
            }
            Cam.orthographic = true;
            Cam.orthographicSize = 9.4f;
            // Map on the right ~75%, left gutter for goals/contracts.
            Cam.transform.position = new Vector3(DcWorld.GridW / 2f - 3.2f, DcWorld.GridH / 2f - 0.4f, -10f);
            Cam.backgroundColor = GameTheme.Background;
            Cam.clearFlags = CameraClearFlags.SolidColor;
        }

        private void SetupAudio()
        {
            _audio = gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _moneyClip = MakeTone(880f, 0.07f, fade: true);
            _chimeClip = MakeChime();
            _tripClip = MakeNoise(0.35f);
            _placeClip = MakeTone(220f, 0.09f, fade: true);
        }

        private static AudioClip MakeTone(float freq, float dur, bool fade)
        {
            int rate = 22050, n = (int)(rate * dur);
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / rate;
                float env = fade ? 1f - (float)i / n : 1f;
                data[i] = Mathf.Sin(2f * Mathf.PI * freq * t) * env * 0.25f;
            }
            var clip = AudioClip.Create("tone", n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static AudioClip MakeChime()
        {
            int rate = 22050;
            float dur = 0.4f;
            int n = (int)(rate * dur);
            var data = new float[n];
            float[] notes = { 660f, 880f, 1100f };
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / rate;
                int note = Mathf.Min(2, (int)(t / 0.13f));
                float env = 1f - (t % 0.13f) / 0.15f;
                data[i] = Mathf.Sin(2f * Mathf.PI * notes[note] * t) * Mathf.Clamp01(env) * 0.22f;
            }
            var clip = AudioClip.Create("chime", n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static AudioClip MakeNoise(float dur)
        {
            int rate = 22050, n = (int)(rate * dur);
            var data = new float[n];
            var rng = new System.Random(7);
            for (int i = 0; i < n; i++)
            {
                float env = 1f - (float)i / n;
                data[i] = ((float)rng.NextDouble() * 2f - 1f) * env * env * 0.4f;
            }
            var clip = AudioClip.Create("noise", n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        public void PlayPlaceSound() => _audio.PlayOneShot(_placeClip, 0.8f);

        public void SetSpeed(float s) => _speed = s;

        private void Update()
        {
            if (_world == null) return;

            if (_speed > 0f)
            {
                _accumulator += Time.deltaTime * _speed;
                int safety = 0;
                while (_accumulator >= Balance.TickDt && safety++ < 60)
                {
                    _accumulator -= Balance.TickDt;
                    _world.Step();
                }
            }

            // Audio reads events first; the renderer consumes & clears them after.
            DrainAudioEvents();
            Renderer.Render(Time.deltaTime);
        }

        private void DrainAudioEvents()
        {
            foreach (ChimeEvent e in _world.ChimeEvents)
                _audio.PlayOneShot(_chimeClip, 0.9f);
            _world.ChimeEvents.Clear();

            foreach (TripEvent e in _world.TripEvents)
                _audio.PlayOneShot(_tripClip, 1f);

            // Money tick: rate-limited.
            if (_world.PayoutEvents.Count > 0 && Time.time - _lastMoneySound > 0.125f)
            {
                _audio.PlayOneShot(_moneyClip, 0.35f);
                _lastMoneySound = Time.time;
            }
        }
    }
}
