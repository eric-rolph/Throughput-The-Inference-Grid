using System.Collections.Generic;
using UnityEngine;
using Throughput.Sim;

namespace Throughput.Game
{
    /// Draws grid, links, nodes, packets and floaters from SimWorld state.
    /// Pools everything; rebuildable when the world resets.
    public sealed class WorldRenderer : MonoBehaviour
    {
        private SimWorld _world;
        private Transform _root;
        private Font _font;

        private readonly List<LineRenderer> _linkLines = new List<LineRenderer>();
        private readonly List<GameObject> _nodeViews = new List<GameObject>();
        private readonly List<TextMesh> _nodeLabels = new List<TextMesh>();
        private readonly List<TextMesh> _nodeStatus = new List<TextMesh>();
        private readonly List<SpriteRenderer> _ringBars = new List<SpriteRenderer>();

        private readonly List<SpriteRenderer> _packetPool = new List<SpriteRenderer>();
        private readonly List<SpriteRenderer> _packetHalos = new List<SpriteRenderer>();

        private struct Floater { public TextMesh Text; public float Life; public Vector3 Vel; }
        private readonly List<Floater> _floaters = new List<Floater>();
        private readonly Stack<TextMesh> _floaterPool = new Stack<TextMesh>();

        // Interpolation snapshots: packet id -> position
        private Vector2[] _prevPos = new Vector2[4096];
        private Vector2[] _currPos = new Vector2[4096];
        private bool[] _hadPrev = new bool[4096];

        public void Init(SimWorld world, Font font)
        {
            _world = world;
            _font = font;
            if (_root != null) Destroy(_root.gameObject);
            _root = new GameObject("WorldRoot").transform;
            _linkLines.Clear(); _nodeViews.Clear(); _nodeLabels.Clear();
            _nodeStatus.Clear(); _ringBars.Clear();
            for (int i = 0; i < _hadPrev.Length; i++) _hadPrev[i] = false;
            BuildGrid();
        }

        private void BuildGrid()
        {
            var grid = new GameObject("Grid");
            grid.transform.SetParent(_root, false);
            for (int x = 0; x <= SimWorld.GridW; x += 4)
                MakeGridLine(grid.transform, new Vector3(x, 0), new Vector3(x, SimWorld.GridH));
            for (int y = 0; y <= SimWorld.GridH; y += 4)
                MakeGridLine(grid.transform, new Vector3(0, y), new Vector3(SimWorld.GridW, y));
        }

        private void MakeGridLine(Transform parent, Vector3 a, Vector3 b)
        {
            var go = new GameObject("gl");
            go.transform.SetParent(parent, false);
            var lr = go.AddComponent<LineRenderer>();
            ConfigureLine(lr, 0.045f, GameTheme.GridLine, 0);
            lr.positionCount = 2;
            lr.SetPosition(0, a);
            lr.SetPosition(1, b);
        }

        private static Material _lineMat;
        public static Material LineMaterial
        {
            get
            {
                if (_lineMat == null) _lineMat = new Material(Shader.Find("Sprites/Default"));
                return _lineMat;
            }
        }

        private void ConfigureLine(LineRenderer lr, float width, Color c, int order)
        {
            lr.material = LineMaterial;
            lr.startWidth = width;
            lr.endWidth = width;
            lr.startColor = c;
            lr.endColor = c;
            lr.sortingOrder = order;
            lr.useWorldSpace = true;
            lr.numCapVertices = 2;
            lr.numCornerVertices = 2;
        }

        /// Called by the loop driver right after each sim tick: snapshot packet positions.
        public void OnSimTicked()
        {
            for (int i = 0; i < _world.Packets.Length; i++)
            {
                SimPacket p = _world.Packets[i];
                if (p.State == PacketState.Free) { _hadPrev[i] = false; continue; }
                Vector2 pos = PacketWorldPos(i, p);
                _prevPos[i] = _hadPrev[i] ? _currPos[i] : pos;
                _currPos[i] = pos;
                _hadPrev[i] = true;
            }
        }

        private Vector2 PacketWorldPos(int id, SimPacket p)
        {
            switch (p.State)
            {
                case PacketState.OnLink:
                    return _world.Links[p.LinkId].PointAt(p.LinkDist);
                case PacketState.Queued:
                case PacketState.InService:
                {
                    SimNode n = _world.Nodes[p.NodeId];
                    return new Vector2(n.CenterX, n.CenterY);
                }
                case PacketState.Backlogged:
                {
                    SimNode n = _world.Nodes[p.NodeId];
                    int idx = n.Backlog.IndexOf(id);
                    return new Vector2(n.CenterX - 0.7f - idx * 0.28f, n.CenterY);
                }
                default:
                    return Vector2.zero;
            }
        }

        /// alpha = interpolation between last two sim ticks.
        public void Render(float alpha)
        {
            SyncLinks();
            SyncNodes();
            SyncPackets(alpha);
            TickFloaters();
            DrainEvents();
        }

        // -------------------------------------------------------------- links

        private void SyncLinks()
        {
            while (_linkLines.Count < _world.Links.Count)
            {
                var go = new GameObject("Link" + _linkLines.Count);
                go.transform.SetParent(_root, false);
                var lr = go.AddComponent<LineRenderer>();
                ConfigureLine(lr, 0.16f, GameTheme.LinkCool, 1);
                _linkLines.Add(lr);
            }
            for (int i = 0; i < _world.Links.Count; i++)
            {
                SimLink link = _world.Links[i];
                LineRenderer lr = _linkLines[i];
                if (link.Removed) { lr.enabled = false; continue; }
                lr.enabled = true;
                if (lr.positionCount != link.Path.Count)
                {
                    lr.positionCount = link.Path.Count;
                    for (int k = 0; k < link.Path.Count; k++)
                        lr.SetPosition(k, new Vector3(link.Path[k].x, link.Path[k].y, 0f));
                }
                Color c = link.ClogHeat < 0.5f
                    ? Color.Lerp(GameTheme.LinkCool, GameTheme.LinkWarm, link.ClogHeat * 2f)
                    : Color.Lerp(GameTheme.LinkWarm, GameTheme.LinkHot, (link.ClogHeat - 0.5f) * 2f);
                float pulse = link.ClogHeat < 0.5f ? 0.75f + 0.25f * Mathf.Sin(UnityEngine.Time.time * 6f) : 1f;
                lr.startColor = c * pulse;
                lr.endColor = c;
            }
        }

        // -------------------------------------------------------------- nodes

        private void SyncNodes()
        {
            while (_nodeViews.Count < _world.Nodes.Count)
                CreateNodeView(_world.Nodes[_nodeViews.Count]);

            for (int i = 0; i < _world.Nodes.Count; i++)
            {
                SimNode n = _world.Nodes[i];
                GameObject view = _nodeViews[i];
                bool removed = n.Churned && n.Kind != NodeKind.Ingress;
                view.SetActive(!removed);
                if (removed) continue;

                var body = view.GetComponent<SpriteRenderer>();
                body.color = GameTheme.NodeColor(n.Kind, n.Active);

                TextMesh status = _nodeStatus[i];
                if (!n.Active && n.ColdStartRemaining < 100000f)
                {
                    status.text = $"loading weights {n.ColdStartRemaining:0}s";
                    status.color = GameTheme.Warn;
                }
                else if (n.Spec.IsServer)
                {
                    status.text = new string('|', n.BusySlots) + new string('.', n.Spec.Slots - n.BusySlots);
                    status.color = n.BusySlots == n.Spec.Slots ? GameTheme.Warn : GameTheme.Ok;
                }
                else if (n.Kind == NodeKind.Ingress)
                {
                    status.text = n.Backlog.Count > 0 ? $"backlog {n.Backlog.Count}" : "";
                    status.color = n.Backlog.Count >= 6 ? GameTheme.Danger : GameTheme.TextDim;
                }
                else status.text = "";

                // Error-budget bar (ingress only)
                SpriteRenderer bar = _ringBars[i];
                if (n.Kind == NodeKind.Ingress && n.RingFill > 0.01f)
                {
                    bar.enabled = true;
                    bar.transform.localScale = new Vector3(1.6f * n.RingFill, 0.14f, 1f);
                    bar.color = Color.Lerp(GameTheme.Warn, GameTheme.Danger, n.RingFill);
                }
                else bar.enabled = false;
            }
        }

        private void CreateNodeView(SimNode n)
        {
            var go = new GameObject("Node_" + GameTheme.NodeGlyph(n.Kind));
            go.transform.SetParent(_root, false);
            float size = n.Spec.Size;
            go.transform.position = new Vector3(n.CenterX, n.CenterY, 0f);

            var body = go.AddComponent<SpriteRenderer>();
            body.sprite = SpriteFactory.Square;
            body.color = GameTheme.NodeColor(n.Kind, n.Active);
            body.sortingOrder = 3;
            go.transform.localScale = new Vector3(size * 0.92f, size * 0.92f, 1f);

            var haloGo = new GameObject("halo");
            haloGo.transform.SetParent(go.transform, false);
            var halo = haloGo.AddComponent<SpriteRenderer>();
            halo.sprite = SpriteFactory.Halo;
            halo.color = new Color(0.3f, 0.65f, 0.9f, 0.16f);
            halo.sortingOrder = 2;
            haloGo.transform.localScale = Vector3.one * 2.4f;

            var labelGo = new GameObject("label");
            labelGo.transform.SetParent(go.transform, false);
            labelGo.transform.localPosition = new Vector3(0f, 0.02f, -0.1f);
            var label = labelGo.AddComponent<TextMesh>();
            label.font = _font;
            label.text = GameTheme.NodeGlyph(n.Kind);
            label.characterSize = 0.1f;
            label.fontSize = 64;
            label.anchor = TextAnchor.MiddleCenter;
            label.color = GameTheme.Background;
            labelGo.GetComponent<MeshRenderer>().material = _font.material;
            labelGo.GetComponent<MeshRenderer>().sortingOrder = 4;
            labelGo.transform.localScale = Vector3.one / Mathf.Max(size * 0.92f, 0.01f);

            var statusGo = new GameObject("status");
            statusGo.transform.SetParent(go.transform, false);
            statusGo.transform.localPosition = new Vector3(0f, -0.72f, -0.1f);
            var status = statusGo.AddComponent<TextMesh>();
            status.font = _font;
            status.characterSize = 0.085f;
            status.fontSize = 48;
            status.anchor = TextAnchor.MiddleCenter;
            status.color = GameTheme.TextDim;
            statusGo.GetComponent<MeshRenderer>().material = _font.material;
            statusGo.GetComponent<MeshRenderer>().sortingOrder = 4;
            statusGo.transform.localScale = Vector3.one / Mathf.Max(size * 0.92f, 0.01f);

            var barGo = new GameObject("ring");
            barGo.transform.SetParent(go.transform, false);
            barGo.transform.localPosition = new Vector3(0f, 0.78f, -0.1f);
            var bar = barGo.AddComponent<SpriteRenderer>();
            bar.sprite = SpriteFactory.Pixel;
            bar.sortingOrder = 5;
            bar.enabled = false;

            _nodeViews.Add(go);
            _nodeLabels.Add(label);
            _nodeStatus.Add(status);
            _ringBars.Add(bar);
        }

        // -------------------------------------------------------------- packets

        private void SyncPackets(float alpha)
        {
            int used = 0;
            for (int i = 0; i < _world.Packets.Length; i++)
            {
                SimPacket p = _world.Packets[i];
                if (p.State == PacketState.Free || !_hadPrev[i]) continue;
                if (p.State == PacketState.InService) continue; // hidden inside server

                SpriteRenderer sr = GetPacketView(used);
                SpriteRenderer haloSr = _packetHalos[used];
                used++;

                Vector2 pos = Vector2.Lerp(_prevPos[i], _currPos[i], alpha);
                sr.transform.position = new Vector3(pos.x, pos.y, 0f);
                sr.sprite = GameTheme.PacketSprite(p.Class);

                // Age desaturation past A (latency legibility)
                ClassSpec cs = Tuning.Classes[(int)p.Class];
                float age = (_world.Tick - p.SpawnTick) * Tuning.TickDt;
                Color c = GameTheme.PacketColor(p.Class);
                if (age > cs.FullPayLatency)
                {
                    float over = Mathf.Clamp01((age - cs.FullPayLatency) / cs.FullPayLatency);
                    c = Color.Lerp(c, new Color(0.55f, 0.55f, 0.55f), over * 0.8f);
                    if (age > cs.HalfPayLatency && Mathf.Repeat(UnityEngine.Time.time, 0.4f) < 0.2f)
                        c = GameTheme.Danger;
                }
                sr.color = c;
                haloSr.transform.position = sr.transform.position;
                haloSr.color = new Color(c.r, c.g, c.b, 0.35f);
            }
            for (int i = used; i < _packetPool.Count; i++)
            {
                _packetPool[i].enabled = false;
                _packetHalos[i].enabled = false;
            }
        }

        private SpriteRenderer GetPacketView(int idx)
        {
            while (_packetPool.Count <= idx)
            {
                var go = new GameObject("pkt");
                go.transform.SetParent(_root, false);
                go.transform.localScale = Vector3.one * 0.34f;
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sortingOrder = 6;
                _packetPool.Add(sr);

                var hgo = new GameObject("pkt_halo");
                hgo.transform.SetParent(_root, false);
                hgo.transform.localScale = Vector3.one * 0.85f;
                var hsr = hgo.AddComponent<SpriteRenderer>();
                hsr.sprite = SpriteFactory.Halo;
                hsr.sortingOrder = 5;
                _packetHalos.Add(hsr);
            }
            _packetPool[idx].enabled = true;
            _packetHalos[idx].enabled = true;
            return _packetPool[idx];
        }

        // -------------------------------------------------------------- floaters & events

        private void DrainEvents()
        {
            foreach (PayoutEvent e in _world.PayoutEvents)
            {
                if (_floaters.Count > 40) break;
                Color c = e.Factor >= 0.999f ? GameTheme.Ok :
                          e.Factor >= 0.5f ? GameTheme.Warn : GameTheme.Danger;
                SpawnFloater($"+${e.Amount:0.00}", new Vector3(e.X, e.Y + 0.6f, 0f), c);
            }
            _world.PayoutEvents.Clear();
        }

        private void SpawnFloater(string text, Vector3 pos, Color color)
        {
            TextMesh tm = _floaterPool.Count > 0 ? _floaterPool.Pop() : CreateFloaterText();
            tm.gameObject.SetActive(true);
            tm.text = text;
            tm.color = color;
            tm.transform.position = pos;
            _floaters.Add(new Floater { Text = tm, Life = 1.1f, Vel = new Vector3(0f, 1.1f, 0f) });
        }

        private TextMesh CreateFloaterText()
        {
            var go = new GameObject("floater");
            go.transform.SetParent(_root, false);
            var tm = go.AddComponent<TextMesh>();
            tm.font = _font;
            tm.characterSize = 0.09f;
            tm.fontSize = 52;
            tm.anchor = TextAnchor.MiddleCenter;
            go.GetComponent<MeshRenderer>().material = _font.material;
            go.GetComponent<MeshRenderer>().sortingOrder = 7;
            return tm;
        }

        private void TickFloaters()
        {
            float dt = UnityEngine.Time.deltaTime;
            for (int i = _floaters.Count - 1; i >= 0; i--)
            {
                Floater f = _floaters[i];
                f.Life -= dt;
                f.Text.transform.position += f.Vel * dt;
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
