# IMPLEMENTATION_PLAN.md

# Throughput: The Inference Grid — Unity Implementation Plan
**Unity 6 LTS · 2D URP · WebGL2 · one developer · 10 weeks · code-first**

Guiding rule (from the scope critique): the Unity Editor is used for almost nothing. One scene, programmatic UI, every gameplay constant in runtime-loaded JSON, and a headless sim assembly that runs under plain NUnit — because WebGL build iteration is minutes per cycle and the sim must be testable in milliseconds.

---

## 1. Project Structure

```
Assets/
  Scenes/
    Main.unity                 // the only scene: camera, one Bootstrap GO, URP volume
  Scripts/
    Sim/                       // asmdef: Throughput.Sim — ZERO UnityEngine references
      Core/
        SimWorld.cs            SimClock.cs        XorShift128.cs      SimEvents.cs
      Model/
        PacketBuffer.cs        NodeStateBuffer.cs LinkGraph.cs        PathSegment.cs
        GridModel.cs           PacketClass.cs     NodeType.cs         StallReason.cs
      Systems/
        SpawnScheduler.cs      TransferSystem.cs  ServiceSystem.cs    BuildSystem.cs
        PaymentSystem.cs       QualitySystem.cs   SlaTracker.cs       RingSystem.cs
        EconomySystem.cs       SpotMarket.cs      TokenLedger.cs      InsightSystem.cs
        SpikeDirector.cs       EventDirector.cs   ContractRuntime.cs  RuleEngine.cs
        BlueprintSystem.cs     MetricsRecorder.cs ActionLog.cs
      Data/
        TuningDB.cs            NodeSpec.cs        ClassSpec.cs        ContractSpec.cs
        SpawnGroup.cs          RuleTemplateSpec.cs
    Game/                      // asmdef: Throughput.Game (refs Sim)
      Boot/        Bootstrap.cs  GameLoopDriver.cs  SaveSystem.cs  WebGLBridge.cs
      Input/       InputController.cs  PlacementController.cs  LinkDragController.cs
                   SelectionController.cs  CameraRig.cs
      UI/          HUDController.cs  PalettePanel.cs  InspectorPanel.cs  ContractCard.cs
                   PaymentChartView.cs  ReportScreen.cs  TickerView.cs  MarketPanel.cs
                   RuleCardPanel.cs  BlueprintLibraryPanel.cs  TutorialDirector.cs
      Rendering/   PacketMeshRenderer.cs  FiberMeshBuilder.cs  NodeViewPool.cs
                   RingRenderer.cs  OverlayRenderer.cs  FloaterPool.cs  GhostRenderer.cs
      Audio/       AudioDirector.cs  PlinkPool.cs  MusicClock.cs
    EditorTools/               // asmdef: Throughput.EditorTools (dev speed toggle, JSON reload)
  StreamingAssets/
    tuning/  nodes.json  classes.json  economy.json  contracts/  rule_templates.json
             recorded_builds/           // CI build-order scripts, also loadable in dev builds
  Shaders/   FiberPulse.shader  PacketGlyph.shader  (HDR emissive + scrolling UV mask)
  Art/       glyph_atlas.png  ui_sprites/   // one sprite atlas; baked additive glow sprites
  Audio/     stems/ (calm, tense, alarm .ogg)  plinks/  stinger.ogg
  Plugins/WebGL/  savefile.jslib  visibility.jslib
Tests/
  SimTests/                    // asmdef refs Throughput.Sim only; runs under dotnet NUnit
    DeterminismTests.cs  Lat1InvariantTests.cs  CapacityTableTests.cs
    ContractReplayTests.cs  EconomyTests.cs  BackpressureTests.cs
ci/
  run_headless.ps1             // dotnet test + contract replay matrix (3 builds × 3 seeds)
```

**Prefabs: essentially none.** Node views, packets, UI are constructed in code from `nodes.json` + the atlas. The only serialized Unity content is the scene's camera, URP Global Volume (bloom), and the Bootstrap GameObject.

---

## 2. Core Systems (class-by-class)

### 2.1 Sim assembly (`Throughput.Sim` — pure C#, deterministic, allocation-free after warm-up)

- **`SimWorld`** — owns all state buffers and systems; `Tick()` advances exactly one 50 ms step in a fixed system order (Spawn → Build → Transfer → Service → Quality → Payment → SLA/Rings → Economy → Spike/Events → Rules → Contract → Metrics). Constructor takes `(TuningDB, ContractSpec, ulong seed)`. No statics, no singletons — CI runs many worlds in parallel.
- **`SimClock`** — tick counter, grid-hour phase (tick % 80), helpers for "sim seconds". All gameplay timers live here in ticks.
- **`XorShift128`** — the single RNG; every stochastic call sites through it. `DeterminismTests` runs two worlds with the same seed + action log and asserts identical state hashes every 200 ticks.
- **`PacketBuffer`** — SoA plain arrays (`int[] classId, clientId; float[] spawnTick, serveStartTick; byte[] flags` — cacheChecked, isError, is429, envelopeId…) with a free-list. Capacity pre-allocated (4,096); no per-packet objects, ever.
- **`LinkGraph` / `PathSegment`** — shapez-style gap lists: `(gapToNext, packetId)` per contiguous fiber run + head offset; O(1) advance per tick; exposes `FreeSpacing` (drives clogHeat) and `QueueDepthEquivalent`. Links are independent orthogonal polylines; no junction logic.
- **`TransferSystem`** — the two-phase handshake. `AcceptPacket`/`HandlePacket` dispatch via `switch (nodeType)` into static per-type functions (data-oriented; no virtual per-node objects). Implements: input queues (configurable depth), admission-control projection check, Class Switch port maps, Failover XOR spill, LB least-(queue×weight) with one-hop reservations, Batch Queue coalescing (envelope = one packet row with `envelopeCount`), Gateway TPM metering + 429 flagging, instant-node chain depth 2, stall-reason assignment.
- **`ServiceSystem`** — slot occupancy per server; service duration = `base × (1 + 0.15×(occupied−1)) × warmupFactor`; heavy-tail roll for Complex/★ (10% ⇒ ×2–3 via RNG); Retriever pre-hop requirement for ★-RAG; Cache homogeneity-lite (rolling 50-packet modal-match ratio → hit chance) + once-per-lifetime flag; Guardrail DDoS shredding.
- **`BuildSystem`** — cold-start timers (S 5 s / M 20 s / L 60 s / logic 1 s), warm-up window (2× service, 10 s), ghost→active transitions, spot-rental lifecycle incl. 30 s eviction countdowns. Consumed by rendering for progress rings.
- **`PaymentSystem`** — 4-segment TimeFactor from `classes.json`; Trivial measured at serve-start (TTFT); emits payout events (amount, factor) for floaters; asserts LAT-1 in tests.
- **`QualitySystem`** — accuracy rolls; silent-failure scheduling (delayed rating damage +ticker event at t+20–40 s); Verifier sampling k% → immediate error-packet respawn at ingress with fresh deadline.
- **`SlaTracker`** — per-client EMA of 60 s in-SLO share; pay multiplier 0.8–1.1; abandonment events (feed `RingSystem`); churn warnings/churn.
- **`RingSystem`** — error-budget gauges: fill at backlog ≥6 over 800 ticks, +progress per abandonment, drain on recovery; breach events; spike-pause interaction with 60 s cap.
- **`EconomySystem` / `TokenLedger` / `SpotMarket`** — cash, upkeep amortization, token tier ladder + committed-use contract, spot random-walk pricing + preemption events, Insight accrual (healthy-idle detector).
- **`SpikeDirector` / `EventDirector`** — SpawnGroup summation, 20 s announcements, jitter, pause cap; scripted DDoS, spot evictions, ticker feed.
- **`SpawnScheduler`** — keyframed client rate timelines from `ContractSpec`; grid-hour quantized spawn offsets per class.
- **`ContractRuntime`** — goal evaluation in sim ticks (counts, hold-rate windows), medal thresholds, breach counting, unlock grants.
- **`RuleEngine`** — slice version: evaluates the ≤6 active template-card instances at 1 Hz (each = `{templateId, params, enabled}`); executes `deploy/set_route/set_weight/rent/shed` through the same public command API the player uses. No cycle economy in slice.
- **`BlueprintSystem`** — capture selected region → node+link list; stamp = batch of build commands (pays cost +10%, triggers cold starts).
- **`ActionLog`** — every player/rule command appended as `(tick, command, args)`; enables determinism tests today and checkpoint-restore-by-replay later.
- **`MetricsRecorder`** — 5 s samples (per-client SLA, queue depths, cash, p95 ring buffer) for the post-run static chart; run-summary triplet.
- **`TuningDB`** — loads/validates all JSON at boot; **`CapacityTableTests` regenerates §7.3 from `nodes.json` and fails CI on drift.**

### 2.2 Game assembly

- **`Bootstrap`** — the one scene object: loads `TuningDB` (StreamingAssets via UnityWebRequest on WebGL), constructs `SimWorld`, wires renderers/UI/audio/input, applies meta-save.
- **`GameLoopDriver`** — `Update()` accumulator → `SimWorld.Tick()` at 20 Hz × speed multiplier; exposes `alpha` for render interpolation; subscribes to `WebGLBridge` visibility events to hard-pause.
- **`InputController` / `PlacementController` / `LinkDragController` / `SelectionController`** — mouse-driven placement (ghost preview via `GhostRenderer`, affordability tinting, 30%-cost ghost palette items), orthogonal link drag with live path preview, click-to-inspect. All mutations go through `SimWorld` commands → `ActionLog`.
- **`HUDController`** + panels — **UI Toolkit, built programmatically** (no UXML asset sprawl): top bar (cash/tokens/insights/speed), palette, inspector (per-node config: queue depth, admission toggle, switch port map, LB weights, Verifier k%), contract card with seed, payment-rates chart (`PaymentChartView` draws from `classes.json` — the in-game curve IS the tuning data), market panel, ticker, Act-2 rule cards + blueprint library, report screen (static timeline from `MetricsRecorder`).
- **`TutorialDirector`** — element-highlighting beats for Contracts 1–2 (decoy misroute, first clog); data-driven from `contracts/c01.json` script blocks; no text walls.
- **`SaveSystem`** — meta-only JSON (unlocks, cash carryover, results, settings) via `PlayerPrefs` (IndexedDB-backed on WebGL) + export/import string through `savefile.jslib` download. No mid-run sim serialization.

### 2.3 Rendering (the locked architecture choices)

- **`PacketMeshRenderer`** — **one dynamic mesh**: quad per in-flight packet, rebuilt each frame into pre-allocated `NativeArray`-free plain arrays → `Mesh.SetVertexBufferData(..., DontRecalculateBounds | DontValidateIndices)`; vertex color packs class/domain/age-desaturation/error-flags; `PacketGlyph.shader` samples the glyph atlas + HDR emissive tint. No GameObjects per packet, no pulse-train collapsing (unneeded at slice scale), no compute/indirect (absent on WebGL2).
- **`FiberMeshBuilder`** — chunked static meshes per link tier, UV.x = arclength; `FiberPulse.shader` scrolls an emissive mask by `_Time × linkSpeed` (one global clock — the network beats in unison); per-link `clogHeat` + frozen-pulse flag via **MaterialPropertyBlock**.
- **`NodeViewPool`** — pooled SpriteRenderers from the atlas: outlined glyphs, slot pips (occupancy readout), config fills, build-progress rings (via `RingRenderer` arc mesh, shared with error-budget rings), rule badges, stall chips (world-space UI Toolkit or sprite text).
- **`OverlayRenderer`** — Alt-mode X-ray; `FloaterPool` — payout/penalty text floaters.
- **Glow:** baked additive sprites + HDR emissive as the base look; URP Bloom (HQ filtering OFF) as a quality toggle; **no Light2D**. Linear color space verified in a real browser build in week 1.

### 2.4 Audio

- **`MusicClock`** — quantization grid (16ths over the 4 s bar) from `SimClock` phase; **`AudioDirector`** — 3 looping stems crossfaded by worst clogHeat + active-ring count + spike state; pager stinger; **`PlinkPool`** — pooled one-shots snapped to next 16th, ~8-voice cap, per-class pitch. Week-1 browser test: `AudioSource.PlayScheduled` jitter measured; if unacceptable, plinks degrade to unquantized with clock-aligned triggering.

---

## 3. Milestones (risk-first)

**M0 — "Find the toy" (wk 1–2).** Sim skeleton (`SimWorld/SimClock/PacketBuffer/LinkGraph/SpawnScheduler/ServiceSystem` minimal), one Ingress→S route, `PacketMeshRenderer` + `FiberPulse`, bloom, 3 stems + plinks. **Ship a WebGL build to Cloudflare Pages and verify: linear-space bloom, audio latency, 60 fps, load size.** Exit: watching packets pulse is already hypnotic. *Also stood up in M0: `Throughput.Sim` asmdef purity (CI compiles it under `dotnet` with no Unity refs) + `DeterminismTests`.*

**M1 — Act 1 complete (wk 3–6).** Handshake/backpressure + full slice node catalog (wk 3); payment/TTFT/quality/Verifier/rings/rating (wk 4); economy — tokens/tiers/429, spot market, insights, cold starts — + spikes/DDoS (wk 5); Contracts 1–8 + tutorial beats + report screen + ticker (wk 6). Recorded build orders authored per contract as they land; `ContractReplayTests` green on 3 seeds each. Exit: Contract 8 bronze-able by hand; Contract 2's first clog fires for a fresh player.

**M2 — IaC basic (wk 7–8).** `BlueprintSystem` (exact stamp), Control Node T1, `RuleEngine` + 6 template cards + `RuleCardPanel`, Act 1→2 transition sequence (palette collapse, boot screen), Contracts 9–10, 3-seed medal flow. **Pre-agreed descope trigger:** if M1 slips past wk 6.5, M2 ships Contract 9 only and Contract 10 moves to polish week.

**M3 — Polish & ship (wk 9–10).** Audio pass, alt-overlay completeness, stall chips, saves/export, settings (quiet/potato toggle), balance pass via headless matrix at 16× dev speed, perf/size pass, itch.io + Cloudflare Pages deploy. Exit: CI matrix green (naive bronzes, intended silvers, degenerate-all-API never golds, LAT-1 holds, capacity table matches).

---

## 4. Headless CI (the primary verification loop)

`ci/run_headless.ps1`: `dotnet test Tests/SimTests` → runs the **recorded-build-order matrix** — for each contract × {naive, intended, degenerate} × 3 seeds, `ContractReplayTests` constructs a `SimWorld`, feeds the build script (JSON list of timestamped commands, same schema as `ActionLog`), runs to completion at unbounded speed, and asserts medal band, no NaN/exception, and determinism hash. Recorded scripts are hand-authored (not autonomous bots — that project was bigger than the game) and double as tuning probes: a rescale of `nodes.json` shows its blast radius in one CI run.

---

## 5. WebGL Build Settings (locked)

| Setting | Value |
|---|---|
| Scripting | IL2CPP, **Managed Stripping High** + `link.xml` (Sim assembly, UI Toolkit reflection users), LTO |
| Compression | **Brotli**, served with correct `Content-Encoding` via Cloudflare Pages `_headers` (no decompression fallback) |
| Wasm | WebAssembly 2023 + SIMD on; **exceptions: None**; threads/Burst/COOP-COEP: **off** (embedding portability > unneeded parallelism at slice scale) |
| Memory | Initial heap 256 MB, geometric growth, 2 GB cap; allocation-free tick keeps Boehm GC quiet |
| Size target | **~25 MB Brotli** (realistic URP floor; not chasing 8) — one sprite atlas, .ogg audio (stems DecompressOnLoad small / Streaming if supported), no fonts beyond one UI face + glyph atlas |
| Color/Quality | Linear color space; URP 2D renderer; Bloom HQ-filtering off; no MSAA (glyphs are sprite-AA) |
| Page | Custom template: canvas DPR handling, `visibility.jslib` → pause, `savefile.jslib` → export download; deploy = Cloudflare Pages, itch.io zip as secondary (works because no COOP/COEP) |
| Editor parity | In-editor play mode uses the exact WebGL code paths (no `#if UNITY_EDITOR` sim branches); dev-only 8×/16× speed behind `EditorTools` |

---

## 6. Risk Register (top 5)

1. **Browser audio latency** — probed M0; fallback = unquantized plinks + stems (stems carry the doctrine alone if needed).
2. **UI Toolkit programmatic velocity** — mitigated by panel-per-class structure and zero UXML; worst case, HUD falls back to IMGUI-style minimal panels for the slice.
3. **M2 slip** — pre-agreed descope (Contract 10 → polish week; template cards before blueprint parametrization — cards are the act's soul).
4. **Balance churn** — every constant in JSON + headless matrix at machine speed; no Editor round-trips.
5. **WebGL build size creep** — size budget checked in CI from M0 (build report parsed; fail >30 MB).
