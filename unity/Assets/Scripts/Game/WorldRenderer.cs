using System.Collections.Generic;
using UnityEngine;
using Throughput.Sim;

namespace Throughput.Game
{
    /// Draws the hall: floor, heat overlay, rings, buildings (LEDs/fans/badges),
    /// cables, job dots, floaters, arc flashes. Everything pooled & procedural.
    public sealed class WorldRenderer : MonoBehaviour
    {
        private DcWorld _world;
        private Transform _root;
        private Font _font;
        private Camera _cam;

        // Overlay state (set by HUD / input)
        [System.NonSerialized] public bool ShowHeat, ShowPower, ShowNetwork;
        [System.NonSerialized] public BuildingKind? PlacingKind;
        [System.NonSerialized] public Vector2Int PlacingTile;

        private Texture2D _heatTex;
        private SpriteRenderer _heatQuad;
        private float _heatTexClock;

        private sealed class BuildingView
        {
            public GameObject Root;
            public SpriteRenderer Body, Led, Fan, RingSr;
            public TextMesh Badge, Label;
            public float FanAngle;
        }
        private readonly List<BuildingView> _views = new List<BuildingView>();

        private readonly List<LineRenderer> _cables = new List<LineRenderer>();
        private float _cableClock;

        private sealed class JobDot
        {
            public SpriteRenderer Sr;
            public Vector2 From, To;
            public float T, Dur;
            public bool Purple;
        }
        private readonly List<JobDot> _dots = new List<JobDot>();
        private readonly Stack<SpriteRenderer> _dotPool = new Stack<SpriteRenderer>();
        private readonly List<SpriteRenderer> _queuePile = new List<SpriteRenderer>();
        private readonly Dictionary<int, float> _dotAccum = new Dictionary<int, float>();

        private struct Floater { public TextMesh Text; public float Life; }
        private readonly List<Floater> _floaters = new List<Floater>();
        private readonly Stack<TextMesh> _floaterPool = new Stack<TextMesh>();

        private struct Flash { public SpriteRenderer Sr; public float Life; }
        private readonly List<Flash> _flashes = new List<Flash>();

        private static Material _lineMat;
        public static Material LineMaterial
        {
            get
            {
                if (_lineMat == null) _lineMat = new Material(Shader.Find("Sprites/Default"));
                return _lineMat;
            }
        }

        public void Init(DcWorld world, Font font, Camera cam)
        {
            _world = world;
            _font = font;
            _cam = cam;
            if (_root != null) Destroy(_root.gameObject);
            _root = new GameObject("WorldRoot").transform;
            _views.Clear(); _cables.Clear(); _dots.Clear(); _queuePile.Clear(); _dotAccum.Clear();
            BuildFloor();
            BuildHeatOverlay();
        }

        // ------------------------------------------------------------- floor

        private void BuildFloor()
        {
            var floor = new GameObject("Floor");
            floor.transform.SetParent(_root, false);
            var sr = floor.AddComponent<SpriteRenderer>();
            sr.sprite = SpriteFactory.Pixel;
            sr.color = GameTheme.FloorTile;
            sr.sortingOrder = -2;
            floor.transform.position = new Vector3(DcWorld.GridW / 2f, DcWorld.GridH / 2f, 0f);
            floor.transform.localScale = new Vector3(DcWorld.GridW, DcWorld.GridH, 1f);

            var grid = new GameObject("GridLines");
            grid.transform.SetParent(_root, false);
            for (int x = 0; x <= DcWorld.GridW; x++) GridLine(grid.transform, new Vector3(x, 0), new Vector3(x, DcWorld.GridH));
            for (int y = 0; y <= DcWorld.GridH; y++) GridLine(grid.transform, new Vector3(0, y), new Vector3(DcWorld.GridW, y));
        }

        private void GridLine(Transform parent, Vector3 a, Vector3 b)
        {
            var go = new GameObject("gl");
            go.transform.SetParent(parent, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.material = LineMaterial;
            lr.startWidth = lr.endWidth = 0.03f;
            lr.startColor = lr.endColor = GameTheme.GridLine;
            lr.positionCount = 2;
            lr.SetPosition(0, a); lr.SetPosition(1, b);
            lr.sortingOrder = -1;
        }

        private void BuildHeatOverlay()
        {
            _heatTex = new Texture2D(DcWorld.GridW, DcWorld.GridH, TextureFormat.RGBA32, false);
            _heatTex.filterMode = FilterMode.Bilinear;
            _heatTex.wrapMode = TextureWrapMode.Clamp;
            var go = new GameObject("HeatOverlay");
            go.transform.SetParent(_root, false);
            _heatQuad = go.AddComponent<SpriteRenderer>();
            _heatQuad.sprite = Sprite.Create(_heatTex, new Rect(0, 0, DcWorld.GridW, DcWorld.GridH), new Vector2(0.5f, 0.5f), 1f);
            _heatQuad.sortingOrder = 0;
            go.transform.position = new Vector3(DcWorld.GridW / 2f, DcWorld.GridH / 2f, 0f);
        }

        // ------------------------------------------------------------- render

        public void Render(float dt)
        {
            SyncBuildings(dt);
            UpdateHeatOverlay(dt);
            UpdateCables(dt);
            UpdateDots(dt);
            UpdateQueuePile();
            TickFloaters(dt);
            TickFlashes(dt);
            DrainEvents();
        }

        private void SyncBuildings(float dt)
        {
            while (_views.Count < _world.Buildings.Count)
                _views.Add(CreateView(_world.Buildings[_views.Count]));

            bool placingRack = PlacingKind == BuildingKind.CpuRack || PlacingKind == BuildingKind.GpuRack ||
                               PlacingKind == BuildingKind.Crac;

            for (int i = 0; i < _world.Buildings.Count; i++)
            {
                Building b = _world.Buildings[i];
                BuildingView v = _views[i];
                v.Root.SetActive(!b.Removed);
                if (b.Removed) continue;

                BuildingSpec spec = b.Spec;
                bool dark = b.State == BuildingState.TrippedDark;
                bool off = b.ToggledOff;
                bool unpowered = (spec.IsRack || b.Kind == BuildingKind.Crac) && !b.HasPower;

                Color body = GameTheme.BuildingColor(b.Kind);
                if (dark || off || unpowered) body = Color.Lerp(body, Color.black, 0.75f);
                else if (b.State == BuildingState.Booting) body = Color.Lerp(body, Color.black, 0.45f);
                else if (b.State == BuildingState.HeatShutdown) body = Color.Lerp(body, GameTheme.Danger, 0.35f);
                v.Body.color = body;

                // LEDs — servedPF literally
                if (v.Led != null)
                {
                    if (dark || off || unpowered || b.State != BuildingState.Online) v.Led.sprite = SpriteFactory.LedGrid[2];
                    else
                    {
                        float ratio = spec.ComputePf > 0 ? b.ServedPf / spec.ComputePf : 0f;
                        v.Led.sprite = SpriteFactory.LedGrid[ratio > 0.55f ? 0 : ratio > 0.01f ? 1 : 2];
                        // subtle blink
                        v.Led.color = new Color(0.6f, 1f, 0.8f, ratio > 0.01f ? (0.75f + 0.25f * Mathf.Sin(Time.time * 7f + b.Id)) : 0.5f);
                    }
                }

                // Fan spins by temperature
                if (v.Fan != null)
                {
                    float speed = dark || off || unpowered ? 0f : Mathf.Lerp(60f, 900f, Mathf.InverseLerp(24f, 75f, b.TileTemp));
                    v.FanAngle += speed * dt;
                    v.Fan.transform.localRotation = Quaternion.Euler(0, 0, v.FanAngle);
                }

                // Ring (PDU amber / CRAC blue)
                if (v.RingSr != null)
                {
                    bool isPdu = b.Kind == BuildingKind.Pdu;
                    float baseAlpha = isPdu
                        ? (ShowPower ? 0.55f : 0.10f)
                        : (ShowHeat || placingRack ? 0.35f : 0.06f);
                    if (placingRack && isPdu) baseAlpha = 0.30f;
                    if (PlacingKind.HasValue && isPdu)
                    {
                        // nearest covering ring brightens
                        float d = Vector2.Distance(new Vector2(b.X, b.Y), PlacingTile);
                        if (d <= spec.Radius) baseAlpha = 0.85f;
                    }
                    // overload pulse ≥90%
                    if (isPdu && !dark && _world.PduLoad(b.Id) >= spec.PduCapKw * Balance.AmberAt)
                        baseAlpha = 0.5f + 0.4f * Mathf.PingPong(Time.time * 3f, 1f);
                    Color rc = isPdu ? GameTheme.PduRing : GameTheme.CracRing;
                    rc.a = dark || off || unpowered ? 0.03f : baseAlpha;
                    v.RingSr.color = rc;
                }

                // Badge
                string badge = ""; Color bc = GameTheme.TextDim;
                if (dark) { badge = "DARK"; bc = GameTheme.Danger; }
                else if (off) { badge = "OFF"; bc = GameTheme.TextDim; }
                else if (unpowered) { badge = "NO POWER"; bc = GameTheme.Warn; }
                else if (b.State == BuildingState.Booting) { badge = "booting"; bc = GameTheme.TextDim; }
                else if (b.State == BuildingState.HeatShutdown) { badge = "OVERHEAT"; bc = GameTheme.Danger; }
                else if (b.NoUplinkFlag && spec.IsRack) { badge = "NO UPLINK"; bc = GameTheme.JobCyan; }
                else if (spec.IsRack && b.TileTemp >= Balance.HotTemp) { badge = $"{b.TileTemp:0}° -50%"; bc = GameTheme.Danger; }
                else if (spec.IsRack && b.TileTemp >= Balance.WarmTemp) { badge = $"{b.TileTemp:0}° -25%"; bc = GameTheme.Warn; }
                else if (spec.IsRack && b.State == BuildingState.Online && b.ServedPf <= 0.01f) { badge = "starving"; bc = GameTheme.TextDim; }
                v.Badge.text = badge;
                v.Badge.color = bc;
            }
        }

        private BuildingView CreateView(Building b)
        {
            var v = new BuildingView();
            v.Root = new GameObject($"B{b.Id}_{b.Spec.Name}");
            v.Root.transform.SetParent(_root, false);
            v.Root.transform.position = new Vector3(b.X + 0.5f, b.Y + 0.5f, 0f);

            v.Body = v.Root.AddComponent<SpriteRenderer>();
            v.Body.sortingOrder = 3;
            switch (b.Kind)
            {
                case BuildingKind.CpuRack:
                case BuildingKind.GpuRack: v.Body.sprite = SpriteFactory.RackBody; break;
                case BuildingKind.Pdu: v.Body.sprite = SpriteFactory.PduBody; break;
                case BuildingKind.Crac: v.Body.sprite = SpriteFactory.CracBody; break;
                default: v.Body.sprite = SpriteFactory.RackBody; break;
            }
            v.Root.transform.localScale = Vector3.one * 0.92f;

            if (b.Spec.IsRack)
            {
                var ledGo = new GameObject("led");
                ledGo.transform.SetParent(v.Root.transform, false);
                ledGo.transform.localPosition = new Vector3(0, 0, -0.05f);
                v.Led = ledGo.AddComponent<SpriteRenderer>();
                v.Led.sprite = SpriteFactory.LedGrid[2];
                v.Led.sortingOrder = 4;

                var fanGo = new GameObject("fan");
                fanGo.transform.SetParent(v.Root.transform, false);
                fanGo.transform.localPosition = new Vector3(0.28f, -0.28f, -0.05f);
                fanGo.transform.localScale = Vector3.one * 0.3f;
                v.Fan = fanGo.AddComponent<SpriteRenderer>();
                v.Fan.sprite = SpriteFactory.FanBlade;
                v.Fan.color = new Color(0.7f, 0.8f, 0.9f, 0.8f);
                v.Fan.sortingOrder = 4;
            }

            if (b.Kind == BuildingKind.Pdu || b.Kind == BuildingKind.Crac)
            {
                var ringGo = new GameObject("ring");
                ringGo.transform.SetParent(v.Root.transform, false);
                ringGo.transform.localScale = Vector3.one * (b.Spec.Radius * 2f / 0.92f);
                v.RingSr = ringGo.AddComponent<SpriteRenderer>();
                v.RingSr.sprite = SpriteFactory.Ring;
                v.RingSr.sortingOrder = 1;
            }

            if (b.Kind == BuildingKind.Uplink || b.Kind == BuildingKind.GridFeed)
            {
                var chevGo = new GameObject("chev");
                chevGo.transform.SetParent(v.Root.transform, false);
                chevGo.transform.localPosition = new Vector3(0, 0, -0.05f);
                chevGo.transform.localScale = Vector3.one * 0.6f;
                var chev = chevGo.AddComponent<SpriteRenderer>();
                chev.sprite = SpriteFactory.Chevron;
                chev.color = b.Kind == BuildingKind.Uplink ? GameTheme.JobCyan : GameTheme.Warn;
                chev.sortingOrder = 4;
                if (b.Kind == BuildingKind.GridFeed) chevGo.transform.localRotation = Quaternion.Euler(0, 0, -90);
            }

            var labelGo = new GameObject("label");
            labelGo.transform.SetParent(v.Root.transform, false);
            labelGo.transform.localPosition = new Vector3(0f, -0.62f, -0.1f);
            v.Label = labelGo.AddComponent<TextMesh>();
            v.Label.font = _font;
            v.Label.text = ShortName(b.Kind);
            v.Label.characterSize = 0.075f;
            v.Label.fontSize = 40;
            v.Label.anchor = TextAnchor.MiddleCenter;
            v.Label.color = GameTheme.TextDim;
            labelGo.GetComponent<MeshRenderer>().material = _font.material;
            labelGo.GetComponent<MeshRenderer>().sortingOrder = 5;

            var badgeGo = new GameObject("badge");
            badgeGo.transform.SetParent(v.Root.transform, false);
            badgeGo.transform.localPosition = new Vector3(0f, 0.72f, -0.1f);
            v.Badge = badgeGo.AddComponent<TextMesh>();
            v.Badge.font = _font;
            v.Badge.characterSize = 0.085f;
            v.Badge.fontSize = 44;
            v.Badge.anchor = TextAnchor.MiddleCenter;
            badgeGo.GetComponent<MeshRenderer>().material = _font.material;
            badgeGo.GetComponent<MeshRenderer>().sortingOrder = 6;

            return v;
        }

        private static string ShortName(BuildingKind k)
        {
            switch (k)
            {
                case BuildingKind.CpuRack: return "CPU";
                case BuildingKind.GpuRack: return "GPU";
                case BuildingKind.Pdu: return "PDU";
                case BuildingKind.Crac: return "CRAC";
                case BuildingKind.Uplink: return "UPLINK";
                default: return "FEED";
            }
        }

        // ------------------------------------------------------------- overlays

        private void UpdateHeatOverlay(float dt)
        {
            bool placingThermal = PlacingKind == BuildingKind.CpuRack || PlacingKind == BuildingKind.GpuRack ||
                                  PlacingKind == BuildingKind.Crac;
            bool visible = ShowHeat || placingThermal;
            _heatQuad.enabled = visible;
            if (!visible) return;

            _heatTexClock += dt;
            if (_heatTexClock < 0.5f) return;
            _heatTexClock = 0f;

            var px = new Color32[DcWorld.GridW * DcWorld.GridH];
            for (int y = 0; y < DcWorld.GridH; y++)
                for (int x = 0; x < DcWorld.GridW; x++)
                    px[y * DcWorld.GridW + x] = GameTheme.HeatColor(_world.Heat.At(x, y));
            _heatTex.SetPixels32(px);
            _heatTex.Apply();
        }

        private void UpdateCables(float dt)
        {
            _cableClock += dt;
            if (_cableClock < 0.5f) return;
            _cableClock = 0f;

            int needed = 0;
            foreach (Building b in _world.Buildings)
            {
                if (b.Removed) continue;
                bool child = (b.Spec.IsRack || b.Kind == BuildingKind.Crac) && b.PduId >= 0;
                bool pdu = b.Kind == BuildingKind.Pdu;
                if (!child && !pdu) continue;

                while (_cables.Count <= needed)
                {
                    var go = new GameObject("cable");
                    go.transform.SetParent(_root, false);
                    var lr = go.AddComponent<LineRenderer>();
                    lr.material = LineMaterial;
                    lr.startWidth = lr.endWidth = 0.06f;
                    lr.sortingOrder = 2;
                    _cables.Add(lr);
                }
                LineRenderer line = _cables[needed++];
                line.enabled = true;
                Vector3 from, to;
                if (pdu)
                {
                    Building feed = _world.Buildings[0];
                    from = new Vector3(feed.X + 0.5f, feed.Y + 0.5f, 0);
                    to = new Vector3(b.X + 0.5f, b.Y + 0.5f, 0);
                }
                else
                {
                    Building p = _world.Buildings[b.PduId];
                    from = new Vector3(p.X + 0.5f, p.Y + 0.5f, 0);
                    to = new Vector3(b.X + 0.5f, b.Y + 0.5f, 0);
                }
                line.positionCount = 3;
                line.SetPosition(0, from);
                line.SetPosition(1, new Vector3(to.x, from.y, 0));
                line.SetPosition(2, to);
                line.startColor = line.endColor = GameTheme.CableColor;
            }
            for (int i = needed; i < _cables.Count; i++) _cables[i].enabled = false;
        }

        // ------------------------------------------------------------- job dots

        private void UpdateDots(float dt)
        {
            Building uplink = _world.Buildings[1];
            Vector2 src = new Vector2(uplink.X + 0.5f, uplink.Y + 0.5f);

            foreach (Building b in _world.Buildings)
            {
                if (b.Removed || !b.Spec.IsRack || b.ServedPf <= 0.01f) continue;
                float acc;
                _dotAccum.TryGetValue(b.Id, out acc);
                acc += b.ServedPf * 0.5f * dt;   // 1 dot per 2 PF·s
                while (acc >= 1f && _dots.Count < 220)
                {
                    acc -= 1f;
                    bool purple = b.ServedPurplePf > 0f &&
                                  Random.value < b.ServedPurplePf / Mathf.Max(0.01f, b.ServedPf);
                    SpawnDot(src, new Vector2(b.X + 0.5f, b.Y + 0.5f), purple);
                }
                _dotAccum[b.Id] = acc;
            }

            for (int i = _dots.Count - 1; i >= 0; i--)
            {
                JobDot d = _dots[i];
                d.T += dt / d.Dur;
                if (d.T >= 1f)
                {
                    d.Sr.enabled = false;
                    _dotPool.Push(d.Sr);
                    _dots.RemoveAt(i);
                    continue;
                }
                // L-path: horizontal then vertical
                float half = Mathf.Abs(d.To.x - d.From.x) /
                             (Mathf.Abs(d.To.x - d.From.x) + Mathf.Abs(d.To.y - d.From.y) + 0.001f);
                Vector2 pos = d.T < half
                    ? Vector2.Lerp(d.From, new Vector2(d.To.x, d.From.y), half <= 0 ? 1 : d.T / half)
                    : Vector2.Lerp(new Vector2(d.To.x, d.From.y), d.To, (d.T - half) / Mathf.Max(0.001f, 1f - half));
                d.Sr.transform.position = new Vector3(pos.x, pos.y, 0f);
            }
        }

        private void SpawnDot(Vector2 from, Vector2 to, bool purple)
        {
            SpriteRenderer sr = _dotPool.Count > 0 ? _dotPool.Pop() : NewDotSr();
            sr.enabled = true;
            sr.color = purple ? GameTheme.JobPurple : GameTheme.JobCyan;
            _dots.Add(new JobDot { Sr = sr, From = from, To = to, T = 0f, Dur = 0.9f, Purple = purple });
        }

        private SpriteRenderer NewDotSr()
        {
            var go = new GameObject("dot");
            go.transform.SetParent(_root, false);
            go.transform.localScale = Vector3.one * 0.18f;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = SpriteFactory.Circle;
            sr.sortingOrder = 7;
            return sr;
        }

        private void UpdateQueuePile()
        {
            Building uplink = _world.Buildings[1];
            int want = Mathf.Min(Mathf.RoundToInt(_world.QueueDepth), 36);
            while (_queuePile.Count < want)
            {
                var sr = NewDotSr();
                sr.transform.localScale = Vector3.one * 0.15f;
                _queuePile.Add(sr);
            }
            float purpleShare = _world.DemandPurplePf / Mathf.Max(0.01f, _world.DemandCyanPf + _world.DemandPurplePf);
            for (int i = 0; i < _queuePile.Count; i++)
            {
                bool on = i < want;
                _queuePile[i].enabled = on;
                if (!on) continue;
                int col = i % 3, row = i / 3;
                float pulse = 0.7f + 0.3f * Mathf.Sin(Time.time * 4f + i * 0.7f);
                _queuePile[i].transform.position = new Vector3(
                    uplink.X - 0.65f + col * 0.28f, uplink.Y + 1.2f + row * 0.28f, 0f);
                Color c = (i * 37 % 100) / 100f < purpleShare ? GameTheme.JobPurple : GameTheme.JobCyan;
                c.a = pulse;
                _queuePile[i].color = c;
            }
        }

        // ------------------------------------------------------------- fx

        private void DrainEvents()
        {
            foreach (PayoutEvent e in _world.PayoutEvents)
            {
                if (_floaters.Count > 24) break;
                SpawnFloater($"+${e.Amount:0.00}", new Vector3(e.X, e.Y, 0f), GameTheme.Ok);
            }
            _world.PayoutEvents.Clear();

            foreach (TripEvent e in _world.TripEvents)
            {
                var go = new GameObject("flash");
                go.transform.SetParent(_root, false);
                go.transform.position = new Vector3(e.X, e.Y, 0f);
                go.transform.localScale = Vector3.one * 2f;
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = SpriteFactory.ArcFlash;
                sr.color = Color.white;
                sr.sortingOrder = 9;
                _flashes.Add(new Flash { Sr = sr, Life = 0.5f });
                if (_cam != null) StartCoroutine(CameraNudge());
            }
            _world.TripEvents.Clear();
        }

        private System.Collections.IEnumerator CameraNudge()
        {
            Vector3 basePos = _cam.transform.position;
            for (int i = 0; i < 6; i++)
            {
                _cam.transform.position = basePos + (Vector3)(Random.insideUnitCircle * 0.12f);
                yield return null;
            }
            _cam.transform.position = basePos;
        }

        private void TickFlashes(float dt)
        {
            for (int i = _flashes.Count - 1; i >= 0; i--)
            {
                Flash f = _flashes[i];
                f.Life -= dt;
                f.Sr.color = new Color(1f, 1f, 1f, Mathf.Clamp01(f.Life * 2f));
                f.Sr.transform.localScale = Vector3.one * (2f + (0.5f - f.Life) * 3f);
                _flashes[i] = f;
                if (f.Life <= 0f) { Destroy(f.Sr.gameObject); _flashes.RemoveAt(i); }
            }
        }

        public void SpawnFloater(string text, Vector3 pos, Color color)
        {
            TextMesh tm = _floaterPool.Count > 0 ? _floaterPool.Pop() : CreateFloaterText();
            tm.gameObject.SetActive(true);
            tm.text = text;
            tm.color = color;
            tm.transform.position = pos;
            _floaters.Add(new Floater { Text = tm, Life = 1.1f });
        }

        private TextMesh CreateFloaterText()
        {
            var go = new GameObject("floater");
            go.transform.SetParent(_root, false);
            var tm = go.AddComponent<TextMesh>();
            tm.font = _font;
            tm.characterSize = 0.09f;
            tm.fontSize = 48;
            tm.anchor = TextAnchor.MiddleCenter;
            go.GetComponent<MeshRenderer>().material = _font.material;
            go.GetComponent<MeshRenderer>().sortingOrder = 8;
            return tm;
        }

        private void TickFloaters(float dt)
        {
            for (int i = _floaters.Count - 1; i >= 0; i--)
            {
                Floater f = _floaters[i];
                f.Life -= dt;
                f.Text.transform.position += Vector3.up * (1.0f * dt);
                Color c = f.Text.color;
                c.a = Mathf.Clamp01(f.Life);
                f.Text.color = c;
                _floaters[i] = f;
                if (f.Life <= 0f)
                {
                    f.Text.gameObject.SetActive(false);
                    _floaterPool.Push(f.Text);
                    _floaters.RemoveAt(i);
                }
            }
        }
    }
}
