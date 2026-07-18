# BUILDSPEC.md — Throughput v2.1 implementation plan

Repo: `C:\Users\ericr\source\repos\Throughput-The-Inference-Grid` · Unity project at `unity/` · all scripts under `unity/Assets/Scripts/{Sim,Game}` + `unity/Assets/Editor`. Current codebase ≈2,280 LOC, verified against source on disk. Target: Phase A playtestable in one session (est. 10–12h; smoke WebGL build mid-session, not only at the end).

---

## 1. File disposition (verified against actual source)

### KEEP UNCHANGED
| File | Why |
|---|---|
| `Sim/XorShift128.cs` | deterministic RNG, reused as-is |
| `Game/UiBuilder.cs` | Canvas/Panel/Label/TextButton/Place helpers — everything the new HUD needs |
| `Editor/BuildScript.cs`, `Editor/ProjectSetup.cs`, `Editor/McpAutoConnect.cs` | build/deploy pipeline works; do not touch |

### ADAPT (keep the skeleton, replace contents)
| File | Keep | Replace |
|---|---|---|
| `Game/GameController.cs` | Awake bootstrap (SpriteFactory.Build, camera, EventSystem, component wiring), fixed-timestep accumulator loop (`_accumulator`/`Tuning.TickDt`/safety cap), `SetSpeed` | delete `StartContract`/`ShowEndCard`/`_cardShowing` modal flow entirely (no modals, no contract cards at boot, no end state — open-ended run); speeds `{0,1,3}` not `{0,1,2,4}`; camera reframed for 24×16 map + 320px left gutter (see §5); add `Application.targetFrameRate` kept; add audio init (§6) |
| `Game/WorldRenderer.cs` | pooling patterns (`_packetPool`+halos → job dots), `LineMaterial`, floater system (`SpawnFloater`/`TickFloaters`), `OnSimTicked` interpolation snapshots, grid drawing | node views (LED-grid buildings replace glyph squares), links section (`SyncLinks` → static L-polyline cables, no ClogHeat), packet age-desaturation logic (dead concept), add heat-overlay quad + rings layer (§5) |
| `Game/HudController.cs` | ticker (`DrainTicker` verbatim), palette affordability pattern, `UiBuilder` layout idioms | full layout rebuild (top bar 6 widgets, left gutter, docked inspect panel, goal chips, contract cards, overlay toggles, undo, contextual chips); `ShowCard`/`HideCard` overlay **deleted** (modal ban) |
| `Game/InputController.cs` | ghost SpriteRenderer + Mode enum + `ScreenToWorldPoint`→tile math + `overUi` check + Esc cancel | **delete** `BeginLinkMode`/`UpdateLink`/`_linkPreview` and **`UpdateIdle` right-click demolish** (v2 forbids right-click); make placement **sticky** (do NOT call `CancelMode()` after `TryPlaceNode` — the current line 104 bug); add tri-state tint, cursor caption, click-to-inspect |
| `Game/GameTheme.cs` | palette, `Hex`, PanelBg, Danger/Warn/Ok | `NodeColor`/`NodeGlyph`/`PacketSprite` replaced by building colors + `JobColor(JobType)` (reuse existing `58C7E1` cyan / `E158B9` purple) + heat gradient stops |
| `Game/SpriteFactory.cs` | `MakeSprite` lambda generator, Circle/Square/Halo/Pixel/Ring | add: `LedGrid(frame)` 3 frames, `FanBlade`, `RackBody`, `PduTraces`, `CracBody`, `UplinkBody`, `FeedBody`, `Smoke` puff, `ArcFlash`. All via existing `MakeSprite`. Budget honestly: this is new art code (~1–1.5h), not reuse |

### DELETE / REPLACE WHOLESALE
| File | Fate |
|---|---|
| `Sim/SimWorld.cs` | rewrite in place as `DcWorld` (keep: 20 Hz Step, `_nodeAt` occupancy grid → `_buildingAt[24,16]`, event lists + `Ticker()`, Cash/Earned pattern, CanPlace/TryPlace shape). Kill: links, packets, backlog/queues/slots, spikes, SLA rings, ChooseOutLink, TimeFactor economy |
| `Sim/SimTypes.cs` | replace: SimPacket/SimLink/PacketState die; keep PayoutEvent/TickerEvent structs; new `Building` class |
| `Sim/Specs.cs` | replace: `NodeSpec`/`Tuning` → `BuildingSpec`/`Balance` (see §3). **Decision: stay a static class, not a ScriptableObject** — the pipeline is code-only/headless; one editable file beats asset plumbing |
| `Sim/ContractData.cs` | delete v1 `ContractSpec.All`; new tiny `ContractScript.cs` (2 hardcoded offers, §4) |

---

## 2. New class list (namespace + responsibility + rough public API)

**`Throughput.Sim` — pure C#, no MonoBehaviours, deterministic:**

```
DcWorld                              // rewrite of SimWorld, same tick-driver contract
  const int GridW=24, GridH=16; const float TickDt=0.05f
  List<Building> Buildings; HeatField Heat; ContractScript Contracts; GoalChips Goals
  List<PayoutEvent> PayoutEvents; List<TickerEvent> TickerEvents   // drained by presentation
  float Cash, Earned, NetPerSec, RevenuePerSec, PowerCostPerSec
  float FeedCapKw, FeedLoadKw, BandwidthCap, BandwidthUsed, BandwidthAccepted
                       // BandwidthUsed = attempted online demand; Accepted = connected subset
  float DemandCyanPf, DemandPurplePf, ServedPf, CapacityPf, QueueDepth
  int Day; float ClockHours; float PricePerKwS; float BreakevenPf
  float SubstationEta                                              // <0 = none ordered
  void Step()                        // order: Clock/Price → Power(trips) → Heat(2Hz) →
                                     //        Demand/Assign → Revenue/Electricity →
                                     //        Contracts → Goals → Timers(substation, boot)
  PlacementCheck CheckPlace(BuildingKind k, int x, int y)
      // returns { Verdict Green|Amber|Red, string Reason, float Cost, float WalletAfter }
      // RED: no ring | occupied/OOB | cash | feed cap.  AMBER: pdu overload | tile>=45° | bw over
  Building TryPlace(BuildingKind k, int x, int y)   // Amber allowed; starts 3s boot
  bool TrySell(int id)               // 100% <=5s after place, else 50%
  bool TryUndo()                     // last placement only, full refund, 10s window
  void ToggleBuilding(int id)        // draw=0, heat=0, revenue=0
  bool BuyUplink()                   // +10 Gbps, $3,000, instant, no gate
  bool OrderSubstation()             // $12,000, 90s, FeedCap += 500 on arrival
  bool RestartHeatShutdown(int id)   // $400 if tile < 60°
  void AcceptContract(int idx); void PassContract(int idx)
  int BuildingIdAt(int x, int y)

Building
  int Id; BuildingKind Kind; int X, Y; int PduId          // -1 = feed-direct (PDU itself)
  BuildingState State  // Booting, Online, HeatShutdown, TrippedDark, NoUplink, ToggledOff
  float BootRemaining, ServedPf, TileTemp; bool ToggledOff, HasPower, NoUplinkFlag; long PlacedTick
  BuildingSpec Spec => Balance.Spec(Kind)

BuildingKind : GridFeed, Uplink, Pdu, CpuRack, GpuRack, Crac   // PhaseB: Chiller, GpuPod

HeatField
  float[,] Temp;                    // 24x16
  void Rebuild(List<Building>)      // called at 2 Hz + on placement/sell/toggle/trip:
      // Temp[t] = 24 + Σ heatStamp − Σ coolStamp, clamp >= 24
      // stamp(d) = amp * max(0, 1 − d/radius); heat amp = heatKw * DegPerHeatKw
      // runaway: building with Temp>=60 gets amp *= 1.20 (cascades via next rebuild)
  float At(int x, int y)

ContractScript                       // hardcoded, no generator
  Offer[] { PicoChat, Nimbus }       // Offer = name, tag, needsGpuOnline, advance,
                                     //   addsPurplePf, rateBonus, deadlineDay, penalty,
                                     //   trigger (GPU unlocked / Day 4), state
  void Step(DcWorld)                 // trigger offers, check completion (gpuOnline>=needs),
                                     //   deadline → penalty + recovery re-offer next day;
                                     //   advance is paid once only; PASS → re-offer next day

GoalChips
  Chip[8] per DESIGN2 §5.4           // Chip = text, predicate(DcWorld), reward, unlockAction
  Chip Current, Next(silhouette)

Balance (static, replaces Tuning)    // every constant from DESIGN2 §6 in ONE file:
  specs table, DegPerHeatKw=1.0, DegPerCoolKw=0.6, thresholds {45,60,75},
  throttles {0.75,0.5}, runaway 1.2, TripSeconds=3, DarkSeconds=8, AmberAt=0.9,
  day=180s, price curve (day1 flat 0.02–0.04, else sine 0.02/0.05/0.08, peak at +90s),
  demand base=4, growth=2/day, purpleShare(day)=min(0.7, 0.15+0.10*(day-1)),
  earnedGpuGate=1200, lifetimeGate=15000, sellFull=5s, undoWindow=10s
```

**Demand assignment (inside `DcWorld.Step`, no global multiplier):** compute per-rack effective capacity = `capacityPf × throttleMult × (online && !noUplink && !toggledOff && !dark)`. Mark newest racks NoUplink while `Σ bw > cap`. Fill order: contract purple pools → eligible GPU racks (their rate × bonus); ambient purple → GPU racks oldest-first; ambient cyan → all racks oldest-first. `QueueDepth = max(0, demand − served)`. Revenue per rack = `rate × served/capacity`; electricity = `Σ draw × price` (powered buildings always draw full watts unless toggled/dark).

**`Throughput.Game` (MonoBehaviours, all AddComponent-wired from GameController as today):**

```
WorldRenderer   // building views: RackBody + LED child (flipbook idx by ServedPf) + Fan
                //   (rotation speed by TileTemp) + screen-space badge anchor
                // heat overlay: 1 quad, Texture2D 24x16 RGBA32 bilinear, SetPixels32 at
                //   2 Hz from HeatField, alpha 0 unless HEAT overlay on or placing rack/CRAC
                // rings: pooled SpriteFactory.Ring, scale=radius*2; while placing: nearest
                //   PDU ring alpha 1.0, others 0.25
                // job dots: reuse packet pool; spawn 1 dot per 2 PF·s served per rack,
                //   lerp uplink→rack L-path over ~1s, cap 256 live; queue pile = dots
                //   parked in rows left of uplink, count = QueueDepth
                // cables: static L-polylines PDU→building, sortingOrder under buildings
                // keep: floaters, arc-flash sprite burst + camera nudge on trip
HudController   // top bar: Cash | NET | Served/Cap PF + queue + breakeven ▲ | kW bar |
                //   Gbps bar | Day+clock+price+arrow  (6 widgets, nothing else)
                // left gutter: <=2 goal chips + <=2 contract cards (5 numbers + ACCEPT/PASS)
                // bottom: 4 build chips + locked silhouettes; overlay toggles HEAT/POWER/NETWORK
                // right: docked InspectPanel (status line, Toggle, Sell) — fixed, never at cursor
                // corners: pause/1x/3x, zoom ±, Undo (all >=44px)
                // contextual chips: "+10 Gbps $3,000" anchored over Uplink at bw>=80%;
                //   "+500 kW $12,000 · 90s" over Feed at load>=80% (WorldToScreenPoint)
                // keep ticker verbatim; hint banner after 2 consecutive red-click attempts
InputController // modes: None | Place(sticky) ; left-click only
                // ghost tint: green/amber/red from CheckPlace; cursor caption TextMesh
                //   (first reason + cost/wallet-after, clamped to viewport); full text
                //   mirrored in strip above toolbar
                // click building → HUD.OpenInspect(id); click empty, no tool → nothing
                // Esc/Z = optional accelerators only
GameController  // loop unchanged; speeds {0,1,3}; no cards; audio: 4 clips via
                //   AudioClip.Create (procedural blips: money, placement, trip, chime),
                //   money tick rate-limited to 8/s
```

---

## 3. WebGL template + build (do early, not last)

- Custom template (or `dist/index.html` post-step): suppress the context menu and install an early capture-phase keyboard guard. Keyboard reaches Unity only while the canvas has focus, so optional `Esc`/`Z` shortcuts work without swallowing browser input elsewhere.
- Known local gotchas: `BuildScript.BuildWebGL` enables Brotli plus Unity's JavaScript `decompressionFallback`, keeping each Cloudflare asset below 25 MiB without requiring a `Content-Encoding` header. **Verify the ESM shield exists before first build:** `C:\Users\ericr\Unity\package.json` must contain `{"type":"commonjs"}` (stale `C:\Users\ericr\package.json` hijacks Unity's Node 12).
- Deploy: push to `main` → GitHub Actions → wrangler 4.107.0 → https://throughput-the-inference-grid.ericrolph.workers.dev.

---

## 4. Contracts + goals data (hardcoded)

```
PicoChat: trigger=GpuUnlocked, needs 1 operational GPU, advance 8000, adds 4 purple PF,
          rate +25% on its pool, deadline Day 3, penalty 1000
Nimbus:   trigger=Day 4, needs 4 operational GPU, advance 8000, adds 20 purple PF,
          rate +50% on its pool, deadline Day 7, penalty 5000
Chips 1–8 exactly per DESIGN2 §5.4 (rewards 0/250/250/unlock/—/1500/500/mastery).
"Earned" = served-work revenue + chip rewards (NOT starting cash, advances, refunds, or penalties).
Final mastery requires both 15000 Earned and Nimbus fulfilled.
An expired active contract fails before fulfillment is evaluated. Failed contracts return the next day with a fresh two-day recovery window and never pay their advance twice.
```

---

## 5. Camera / 720p layout

- Map 24×16 world units; camera orthographic, `orthographicSize = 9.4`, positioned so the map occupies the right ~75% of the frame (x-center = `GridW/2 − 3.2`), leaving a ~320px left UI gutter at 1280 wide. CanvasScaler stays 1600×900 reference.
- Badges: screen-space UI icons (min 20px) positioned via `WorldToScreenPoint`, not world sprites; whole tile = click target.
- Breakeven marker: triangle Image on the Served/Cap bar (a "thin ribbon" is ~2px at 720p — don't).

---

## 6. Ordered implementation sequence (each step has a verify gate)

1. **Gut & compile (0.5h):** delete link/packet/spike/SLA code paths, v1 ContractSpec data, right-click demolish, modal card flow. Gate: project compiles, empty 24×16 grid renders.
2. **Core sim (1.5h):** Building/BuildingSpec/Balance; occupancy; CheckPlace (red rules only) + TryPlace/Sell/Undo/Toggle; power sums + amber flag + trip timer + 8s dark + staged reboot; boot timer. Gate (editor): place 3 CPU in ring → cash falls; overload a PDU deliberately → trip fires at 3s, subtree dark 8s, staged relight.
3. **Money & demand (1.5h):** clock/day/price curve (Day 1 flat); cyan/purple pools + oldest-first assignment + NoUplink marking; revenue/electricity/NET; Earned. Gate: 3 CPU ≈ $6/s gross, NET green through simulated Day 1 peak at 3× speed; 5th CPU rack starves (dark).
4. **Heat (1h):** HeatField stamps + 2 Hz rebuild + runaway; throttle ladder wired into assignment; heat-shutdown + $400 restart. Gate: 3 clustered CPU ≈ 47° (WARM badges); lone GPU hits HOT; CRAC placement cools tint and clears badges.
5. **Purchases + amber ghost (1h):** BuyUplink/OrderSubstation + ETA; CheckPlace amber verdicts with reason strings. Gate: place GPU #2 → bw 11/10 attempted / 7 accepted → newest rack NO UPLINK; buy uplink → all 11 Gbps connects. **→ SMOKE WEBGL BUILD + deploy now (0.75h).**
6. **Input & rings (1.5h):** sticky ghost, tri-state tint, cursor caption + toolbar strip, ring rendering rules, inspect panel, undo button, time controls {0,1,3}. Gate: full mouse-only session in browser build; right-click does nothing anywhere.
7. **Goals + contracts (1.5h):** GoalChips 1–8 with unlock actions + silhouettes; ContractScript (offer/accept/pass/deadline); contextual chips at 80%. Gate: scripted run hits DESIGN2 §6.4 beats within ±40s at 1×.
8. **Aliveness (1.5h):** SpriteFactory additions, LED flipbook, fans, job dots + queue pile, cables, arc-flash, smoke, floaters kept, 4 procedural sounds. Gate: 30-second watch test — can you tell fed/starving/hot/dark racks apart with HUD hidden?
9. **Tune (1h):** play minutes 0–10 at 1×; adjust Balance only (±30% expected). Verify success metrics incl. ≤1 dead 60s window. Final build + deploy + playtest notes (state explicitly: "4 buildings + 2 purchases this slice; Chiller/Pod/events land next session").

For deterministic browser probes, `GameController.DebugCmd("advance:TICKS")` advances up to 200,000 fixed simulation ticks synchronously. Interactive speed remains clamped to the supported `{0,1,3}` range so a large multiplier cannot accumulate an unbounded render-frame backlog.

**Total: 10.5–12h.** Anything from Phase B pulled forward today puts the playtest gate at risk — don't.
