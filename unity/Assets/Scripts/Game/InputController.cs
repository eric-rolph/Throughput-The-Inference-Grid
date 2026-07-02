using UnityEngine;
using UnityEngine.EventSystems;
using Throughput.Sim;

namespace Throughput.Game
{
    /// Mouse-driven building: node placement ghosts, link drawing, right-click demolish.
    public sealed class InputController : MonoBehaviour
    {
        private enum Mode { None, Place, LinkFrom, LinkTo }

        private SimWorld _world;
        private GameController _game;
        private Camera _cam;

        private Mode _mode;
        private NodeKind _placeKind;
        private int _linkFromNode = -1;

        private SpriteRenderer _ghost;
        private LineRenderer _linkPreview;

        public void Init(GameController game, Camera cam)
        {
            _game = game;
            _cam = cam;

            var ghostGo = new GameObject("Ghost");
            _ghost = ghostGo.AddComponent<SpriteRenderer>();
            _ghost.sprite = SpriteFactory.Square;
            _ghost.sortingOrder = 8;
            _ghost.enabled = false;

            var lpGo = new GameObject("LinkPreview");
            _linkPreview = lpGo.AddComponent<LineRenderer>();
            _linkPreview.material = WorldRenderer.LineMaterial;
            _linkPreview.startWidth = 0.12f;
            _linkPreview.endWidth = 0.12f;
            _linkPreview.sortingOrder = 8;
            _linkPreview.enabled = false;
        }

        public void BindWorld(SimWorld world)
        {
            _world = world;
            CancelMode();
        }

        public void BeginPlacement(NodeKind kind)
        {
            _mode = Mode.Place;
            _placeKind = kind;
            _ghost.enabled = true;
            float s = Tuning.Spec(kind).Size;
            _ghost.transform.localScale = new Vector3(s * 0.92f, s * 0.92f, 1f);
            _game.Hud.SetModeHint($"Placing {Tuning.Spec(kind).Name} — click a free tile, ESC to cancel");
        }

        public void BeginLinkMode()
        {
            _mode = Mode.LinkFrom;
            _game.Hud.SetModeHint("Fiber: click the SOURCE node (ingress, switch, LB)");
        }

        public void CancelMode()
        {
            _mode = Mode.None;
            _linkFromNode = -1;
            if (_ghost != null) _ghost.enabled = false;
            if (_linkPreview != null) _linkPreview.enabled = false;
            if (_game != null && _game.Hud != null) _game.Hud.SetModeHint("");
        }

        private void Update()
        {
            if (_world == null || _world.State != ContractState.Running) return;
            if (Input.GetKeyDown(KeyCode.Escape)) { CancelMode(); return; }

            Vector3 mouse = _cam.ScreenToWorldPoint(Input.mousePosition);
            int tx = Mathf.FloorToInt(mouse.x), ty = Mathf.FloorToInt(mouse.y);
            bool overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

            switch (_mode)
            {
                case Mode.Place: UpdatePlace(tx, ty, overUi); break;
                case Mode.LinkFrom:
                case Mode.LinkTo: UpdateLink(mouse, tx, ty, overUi); break;
                default: UpdateIdle(tx, ty, overUi); break;
            }
        }

        private void UpdatePlace(int tx, int ty, bool overUi)
        {
            NodeSpec spec = Tuning.Spec(_placeKind);
            // center the footprint on the cursor
            int ax = tx - (spec.Size - 1) / 2, ay = ty - (spec.Size - 1) / 2;
            bool ok = _world.CanPlace(_placeKind, ax, ay);
            _ghost.transform.position = new Vector3(ax + spec.Size * 0.5f, ay + spec.Size * 0.5f, 0f);
            _ghost.color = ok ? new Color(0.3f, 0.9f, 0.7f, 0.45f) : new Color(0.9f, 0.3f, 0.3f, 0.4f);

            if (Input.GetMouseButtonDown(0) && !overUi && ok)
            {
                _world.TryPlaceNode(_placeKind, ax, ay);
                CancelMode();
            }
        }

        private void UpdateLink(Vector3 mouse, int tx, int ty, bool overUi)
        {
            if (_mode == Mode.LinkTo && _linkFromNode >= 0)
            {
                SimNode from = _world.Nodes[_linkFromNode];
                _linkPreview.enabled = true;
                _linkPreview.positionCount = 3;
                _linkPreview.SetPosition(0, new Vector3(from.CenterX, from.CenterY, 0f));
                _linkPreview.SetPosition(1, new Vector3(mouse.x, from.CenterY, 0f));
                _linkPreview.SetPosition(2, new Vector3(mouse.x, mouse.y, 0f));
                _linkPreview.startColor = _linkPreview.endColor = new Color(0.35f, 0.9f, 0.75f, 0.6f);
            }

            if (Input.GetMouseButtonDown(0) && !overUi)
            {
                int nodeId = _world.NodeIdAt(tx, ty);
                if (nodeId < 0) { CancelMode(); return; }
                SimNode n = _world.Nodes[nodeId];
                if (n.Churned && n.Kind != NodeKind.Ingress) { CancelMode(); return; }

                if (_mode == Mode.LinkFrom)
                {
                    if (n.Spec.MaxOutLinks == 0)
                    {
                        _game.Hud.SetModeHint($"{n.Spec.Name} has no uplink ports — pick ingress/switch/LB");
                        return;
                    }
                    _linkFromNode = nodeId;
                    _mode = Mode.LinkTo;
                    _game.Hud.SetModeHint("Fiber: click the TARGET node");
                }
                else
                {
                    SimLink made = _world.TryCreateLink(_linkFromNode, nodeId);
                    if (made == null)
                        _game.Hud.SetModeHint("Can't link (ports full / duplicate / not enough cash)");
                    else
                        CancelMode();
                }
            }
        }

        private void UpdateIdle(int tx, int ty, bool overUi)
        {
            if (overUi) return;

            if (Input.GetMouseButtonDown(1))
            {
                // demolish node under cursor, else nearest link within 0.5 tiles
                int nodeId = _world.NodeIdAt(tx, ty);
                if (nodeId >= 0)
                {
                    _world.TryRemoveNode(nodeId);
                    return;
                }
                Vector2 m = new Vector2(tx + 0.5f, ty + 0.5f);
                int bestLink = -1;
                float bestD = 0.6f;
                foreach (SimLink link in _world.Links)
                {
                    if (link.Removed) continue;
                    for (float d = 0f; d < link.Length; d += 0.5f)
                    {
                        float dist = Vector2.Distance(link.PointAt(d), m);
                        if (dist < bestD) { bestD = dist; bestLink = link.Id; }
                    }
                }
                if (bestLink >= 0) _world.RemoveLink(bestLink);
            }
        }
    }
}
