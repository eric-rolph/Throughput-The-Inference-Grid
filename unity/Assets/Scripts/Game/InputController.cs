using UnityEngine;
using UnityEngine.EventSystems;
using Throughput.Sim;

namespace Throughput.Game
{
    /// Left-click-only building: sticky tri-state ghost with live reason,
    /// click-to-inspect. No right-click, no drag (docs/DESIGN2.md §7).
    public sealed class InputController : MonoBehaviour
    {
        private DcWorld _world;
        private GameController _game;
        private Camera _cam;

        private BuildingKind? _placing;
        private SpriteRenderer _ghost, _ghostRing;
        private TextMesh _caption;
        private int _consecutiveRedClicks;

        public void Init(GameController game, Camera cam, Font font)
        {
            _game = game;
            _cam = cam;

            var ghostGo = new GameObject("Ghost");
            _ghost = ghostGo.AddComponent<SpriteRenderer>();
            _ghost.sprite = SpriteFactory.RackBody;
            _ghost.sortingOrder = 10;
            _ghost.enabled = false;
            ghostGo.transform.localScale = Vector3.one * 0.92f;

            var ringGo = new GameObject("GhostRing");
            ringGo.transform.SetParent(ghostGo.transform, false);
            _ghostRing = ringGo.AddComponent<SpriteRenderer>();
            _ghostRing.sprite = SpriteFactory.Ring;
            _ghostRing.sortingOrder = 9;
            _ghostRing.enabled = false;

            var capGo = new GameObject("GhostCaption");
            _caption = capGo.AddComponent<TextMesh>();
            _caption.font = font;
            _caption.characterSize = 0.09f;
            _caption.fontSize = 46;
            _caption.anchor = TextAnchor.LowerCenter;
            capGo.GetComponent<MeshRenderer>().material = font.material;
            capGo.GetComponent<MeshRenderer>().sortingOrder = 11;
            capGo.SetActive(false);
        }

        public void BindWorld(DcWorld world)
        {
            _world = world;
            CancelPlacement();
        }

        public void BeginPlacement(BuildingKind kind)
        {
            if (_placing == kind) { CancelPlacement(); return; }   // chip acts as a toggle
            _placing = kind;
            _ghost.enabled = true;
            _ghostRing.enabled = kind == BuildingKind.Pdu || kind == BuildingKind.Crac;
            if (_ghostRing.enabled)
            {
                float r = Balance.Spec(kind).Radius;
                _ghostRing.transform.localScale = Vector3.one * (r * 2f / 0.92f);
                _ghostRing.color = kind == BuildingKind.Pdu
                    ? new Color(GameTheme.PduRing.r, GameTheme.PduRing.g, GameTheme.PduRing.b, 0.5f)
                    : new Color(GameTheme.CracRing.r, GameTheme.CracRing.g, GameTheme.CracRing.b, 0.5f);
            }
            _caption.gameObject.SetActive(true);
            _game.Renderer.PlacingKind = kind;
            _game.Hud.SetModeHint($"Placing {Balance.Spec(kind).Name} — click a tile · click the chip again to stop");
            _game.Hud.CloseInspect();
        }

        public void CancelPlacement()
        {
            _placing = null;
            if (_ghost != null) _ghost.enabled = false;
            if (_caption != null) _caption.gameObject.SetActive(false);
            if (_game != null && _game.Renderer != null) _game.Renderer.PlacingKind = null;
            if (_game != null && _game.Hud != null) _game.Hud.SetModeHint("");
            _consecutiveRedClicks = 0;
        }

        private void Update()
        {
            if (_world == null) return;
            if (Input.GetKeyDown(KeyCode.Escape)) { CancelPlacement(); return; }
            if (Input.GetKeyDown(KeyCode.Z)) _world.TryUndo();

            Vector3 mouse = _cam.ScreenToWorldPoint(Input.mousePosition);
            int tx = Mathf.FloorToInt(mouse.x), ty = Mathf.FloorToInt(mouse.y);
            bool overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

            if (_placing.HasValue)
                UpdatePlacement(tx, ty, overUi);
            else if (Input.GetMouseButtonDown(0) && !overUi)
            {
                int id = _world.BuildingIdAt(tx, ty);
                if (id >= 0 && !_world.Buildings[id].Removed)
                    _game.Hud.OpenInspect(id);
                else
                    _game.Hud.CloseInspect();
            }
        }

        private void UpdatePlacement(int tx, int ty, bool overUi)
        {
            BuildingKind kind = _placing.Value;
            _game.Renderer.PlacingTile = new Vector2Int(tx, ty);
            _ghost.transform.position = new Vector3(tx + 0.5f, ty + 0.5f, 0f);

            PlacementCheck chk = _world.CheckPlace(kind, tx, ty);
            Color tint = chk.Verdict == Verdict.Green ? new Color(0.35f, 0.95f, 0.7f, 0.55f)
                       : chk.Verdict == Verdict.Amber ? new Color(0.95f, 0.75f, 0.25f, 0.55f)
                       : new Color(0.95f, 0.3f, 0.3f, 0.5f);
            _ghost.color = tint;

            string line = chk.Verdict == Verdict.Green
                ? $"${chk.Cost:0}  (${chk.WalletAfter:0} after)"
                : chk.Reason;
            _caption.text = line;
            _caption.color = chk.Verdict == Verdict.Green ? GameTheme.Ok
                           : chk.Verdict == Verdict.Amber ? GameTheme.Warn : GameTheme.Danger;
            Vector3 capPos = new Vector3(tx + 0.5f, ty + 1.25f, 0f);
            capPos.x = Mathf.Clamp(capPos.x, 2.5f, DcWorld.GridW - 2.5f);
            _caption.transform.position = capPos;

            _game.Hud.SetModeHint(chk.Verdict == Verdict.Green
                ? $"{Balance.Spec(kind).Name}: ${chk.Cost:0} — wallet after ${chk.WalletAfter:0}"
                : $"{Balance.Spec(kind).Name}: {chk.Reason}");

            if (Input.GetMouseButtonDown(0) && !overUi)
            {
                if (chk.Verdict == Verdict.Red)
                {
                    if (++_consecutiveRedClicks >= 2)
                        _game.Hud.ShowHint(chk.Reason);
                }
                else
                {
                    _world.TryPlace(kind, tx, ty);   // sticky: stay armed
                    _consecutiveRedClicks = 0;
                    _game.PlayPlaceSound();
                }
            }
        }
    }
}
