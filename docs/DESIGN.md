# DESIGN.md

# Throughput: The Inference Grid
## Game Design Document — v1.0 (Post-Critique Revision, Vertical Slice Spec)

**Genre:** Resource-management / network-automation sim
**Platform:** Browser (Unity 6 LTS, 2D URP, WebGL2), mouse-driven, desktop-first
**Session shape:** Contract-based runs, **10–15 min** each; campaign ≈ 6 hours
**One-line fantasy:** You are the architect of an enterprise AI-inference network. Route the flood. Beat the bill. Then teach the network to run itself.

### Changelog from v0.9 (what the three critiques changed)
- **Latency math re-derived** around a testable invariant (LAT-1); fiber 2× faster, SLO windows widened, Trivial measured to time-to-first-token. The "server-next-to-every-ingress" degenerate optimum is dead.
- **Pressure arrives in minute 6, not minute 35:** servers rescaled down, early demand ×3, scripted first-clog beat in Contract 2, decoy-misroute beat in Contract 1.
- **One demand modulator:** rating no longer scales spawn rate; abandonments *add* breach-ring progress. Scripted demand ramps replace growth rolls in the slice. Rating is a simple EMA, not the 0–255 OpenTTD clone.
- **Authenticity upgrades promoted to core mechanics:** cold starts on all deploys, Gateway TPM caps + 429s, continuous batching inside servers (occupancy scaling), GPU spot market (tokens are fixed-tier priced), Retriever pipeline for RAG, silent quality failures + Verifier, heavy-tailed Complex service times, admission control.
- **"MCP" renamed where it was wrong, spent where it's right:** Act 3 kernel → **Autopilot Core**; the MCP name is reserved for the **MCP Tool Server** (tool-use fan-out), first post-slice milestone.
- **Scope cuts:** freeform rule editor → 6 fixed template cards; balance bots → recorded build orders; replay scrubber → static timeline chart; adaptive audio → 3 stems + quantized one-shots; mid-run saves → cut (short contracts compensate; checkpoint-restore via action-log replay is stretch); Burst/NativeArray/COOP-COEP → plain C#; junctions/VPN bridges/silhouette zoom/pulse-train collapsing → cut or stretch.
- **Housekeeping:** Router and standalone Batcher cut; Batcher rescoped as **Batch Queue** (Batch-API mapping); Class Filter → 4-port **Class Switch**; Firewall → **Guardrail**; domains monochrome until Contract 5; envelope semantics defined; capacity table generated from the node table in CI.

### Design pillars
1. **The topology map IS the profiler.** Every bottleneck — including *latency* and *quality*, not just congestion — must be diagnosable by looking at the map.
2. **Backpressure is the antagonist.** Nothing explodes. Overload backs up, glows, hums wrong, and bleeds money.
3. **The challenge is logical, not spatial.** Conditional routing, capacity math, automation policy — zero belt-tetris.
4. **Every act automates what the player just did by hand** — and moves the furniture when it does.
5. **An optimized network is hypnotic.** Rhythm and quiet harmony are the reward; dissonance and alarms are the punishment.
6. **Borrow the real ops problem, not just the skin.** Cold starts, rate limits, batching curves, spot preemption, silent failures: where a logistics-game mechanic and an inference-native mechanic compete, the inference-native one wins.

---

## 1. Core Simulation Spec

### 1.1 Time & Tick

| Constant | Value | Rationale |
|---|---|---|
| Logic tick | **20 Hz fixed (50 ms)**, accumulator in `Update()` | UPS/FPS decoupling |
| Backgrounded tab | **Sim pauses on `visibilitychange`/focus loss** | Kills catch-up spiral and wake-into-breach; kinder than 4-tick catch-up |
| Render | Display rate; packet positions lerp prev→curr tick | Smooth at any FPS |
| Determinism | Plain C# structs in flat arrays; single seeded `xorshift128`; seed on contract card | Replays, seeded retry, 3-seed medals, headless CI |
| Fast-forward | 1×/2×/4× player-facing; 8×/16× behind a dev flag | Tuning iteration |
| **All goal timers in sim ticks** | "hold for 60 s" = 1,200 ticks | Fast-forward must not change difficulty |
| Player-facing time | 1 grid-hour = 4 s real time (musical bar) | Spawn scheduling & audio quantization |

### 1.2 Grid & Links

- Square grid, 48×27 tiles per contract map; zoom 0.5×–2×. Complexity scales by new maps per contract [STRETCH: multi-region].
- Nodes 1×1 (logic) to 2×2 (servers). Links drawn by port-to-port drag, auto-pathed orthogonally. **Links are independent point-to-point polylines; crossings render overlapped with no interaction.** (Junction auto-insert and VPN bridges are cut; Act 1 maps author no blocked zones.)
- **Link model (shapez BeltPath):** each fiber run is one `PathSegment` owning a sorted `(gapToNext, packetId)` list + head offset. A saturated link costs O(1)/tick and *is* the queue-depth metric.

| Link tier | Speed | Spacing | Cost/tile | Notes |
|---|---|---|---|---|
| Copper fiber | **12 tiles/s** | 0.4 | $2 | 83 ms/tile |
| Optical fiber | **24 tiles/s** | 0.35 | $6 | Unlock C3 |
| Backbone trunk | **48 tiles/s** | 0.3 | $20 | Unlock C8; **max 7 per map** |

**Design invariant LAT-1 (CI-asserted):** *every class achieves full pay on a 12-tile copper route through 2 logic nodes into an unqueued server of its cheapest serving tier.* All SLO windows in §1.6 are derived from this; any tuning change that breaks it fails the build.

**Clog telegraph:** free spacing < threshold ⇒ `clogHeat` ramps 0→1 over 1.0 s; at 0.5 the pulse animation freezes, the link's audio contribution mutes, glow shifts amber→red. Telegraph, then punish.

### 1.3 Packets: Request Classes & Spawning

One-glyph semiotics: shape = complexity tier, color = domain; nodes are large outlined glyphs. Zero labels required; hover shows the request's actual text ("summarize this contract…").

| Class | Glyph | Share (steady) | Served by | Notes |
|---|---|---|---|---|
| **Trivial** | ● | 50% | S, M, L, Cache (API pays poorly — by design, labeled) | **SLO measured to time-to-first-token (serve-start)** — encodes why streaming makes chat latency-tolerant but classify isn't |
| **Standard** | ▲ | 25% | M, L, API, Cache | |
| **Complex** | ■ | 12.5% | L, API | **Heavy-tailed service** (§1.5) |
| **Exotic ★** | ★ | 0→12.5% from **Contract 7** (v0.9 inconsistency resolved) | Pipeline-dependent | Slice sub-type: **★-RAG** (must traverse a Retriever). Post-slice: ★-tool-use (MCP Tool Server fan-out), ★-long-context (sidecar) |
| **Batch ⬢** | ⬢ | 0→12.5% from Contract 5 | Batch Queue → Gateway; or any server off-peak | Deadline (30 s), not latency |

**Domains (color):** all packets **Blue until Contract 5**, where Green (finance) and Magenta (code) arrive *in the same beat* as the Guardrail that makes color matter ("finance must traverse PII redaction"). No noise channel before it's load-bearing.

**Spawn model:** each client owns one edge Ingress with a **scripted rate timeline** `{t, rate}` keyframes (see §7.2) — no emergent growth rolls in the slice [growth rolls: STRETCH]. Spawns quantize to the 4 s grid-hour (Trivials on 16ths, Standards on half-bars, Complex on the bar): legible traffic, musical batching. Errors and 429 retries re-enter at the ingress.

### 1.4 Transfer Protocol & Backpressure (load-bearing, unchanged)

Two-phase push handshake on every node:

```
bool AcceptPacket(source, packetClass)   // may refuse
void HandlePacket(source, packetId)      // commit
```

Backpressure, load balancing, failover, and jam propagation emerge from local refusal. No global flow solver.

- Input queues: **servers default depth 2** (was 6 — a deep hidden queue silently guaranteed floor pay); logic nodes depth 6. **Queue depth is a per-node config.**
- **Admission control (new, a real SRE verb):** per-server toggle — refuse a class when projected wait (queue × current service time) exceeds that class's A. Refusal-as-tool, not just symptom.
- Instant logic nodes (Class Switch, Failover Gate): `instantTransfer`, max chain depth 2/tick.
- Packets are never destroyed by congestion. No hardware explodes.
- **LB reservations propagate exactly one hop** (explicit — deeper propagation is a stealth global solver).

### 1.5 Node Catalog (slice set; all constants in one runtime-loaded JSON table)

| Node | Size | Cost (first) | Upkeep | Behavior |
|---|---|---|---|---|
| **Ingress** | 1×1 | — | — | Spawns client packets; wears SLA/error-budget ring + rating badge |
| **Class Switch** | 1×1 | $100 | $1/min | **4-port switch with a class→port mapping config** (one node per decision point; replaces filter chains = belt-tetris through the back door). Instant. Fills with its config colors in alt-overlay |
| **Failover Gate** | 1×1 | $50 | $1/min | Prefer primary; spill to secondary only on refusal, alternating via XOR bit. Instant |
| **Load Balancer** | 1×1 | $150 | $2/min | ≤4 weighted outputs; least-(queue×weight); advertises max in-flight reservations **one hop** downstream; waiting packets show "Destination full" |
| **Cache** | 1×1 | $400 | $4/min | **Homogeneity model (prefix/semantic caching):** hit rate = f(share of last 50 packets matching modal client+class): 45% at fully homogeneous → 5% mixed firehose. Makes **cache-aware sticky routing** emerge from topology. **One cache check per packet lifetime** (header flag — kills chained-cache degeneracy). Freshness renders as a fill bar. [Clock-decay staleness: cut] |
| **Guardrail** (was Firewall) | 1×1 | $250 | $3/min | PII-redaction/policy model: +1 tick latency; drops malformed/DDoS packets 1/tick; compliance contracts require flagged domains to traverse it |
| **Verifier** (promoted from stretch) | 1×1 | $350 | $4/min | Samples **k%** (configurable 5–50%) of completions passing through; converts silent quality failures into immediate error packets (§1.6). Eval coverage as a slider between "cheap and blind" and "expensive and observable" |
| **Retriever** | 1×1 | $400 | $4/min | Embedding+vector store: ★-RAG packets **must** traverse it before an L server; 0.3 s service, own queue — retrieval latency in the critical path, retriever capacity a separate bottleneck |
| **Model S** | 2×2 | $200 (×1.12ⁿ) | $3/min | **1 slot; Trivial only; 1.0 s** service; accuracy 94% |
| **Model M** | 2×2 | $600 (×1.15ⁿ) | $8/min | 2 slots; Trivial 0.6 s / Standard 1.5 s base; accuracy 97% |
| **Model L** | 2×2 | $2,000 (×1.15ⁿ) | $25/min | 4 slots; Trivial 0.5 / Standard 1.0 / Complex 2.5 / ★-RAG 3.0 s base; accuracy 99% |
| **Batch Queue** (was Batcher) | 1×1 | $300 | $2/min | **Accepts only ⬢ Batch class** (others refused: "No route" chip). Coalesces ≤8 into one envelope; dispatch on full or 2 s timeout. **Envelopes are accepted only by Gateways** and earn a **50% token discount** — the real Batch-API deal: accept deadlines, pay half |
| **API Gateway** | 1×1 | $500 | $5/min + tokens | 1 envelope in flight; round-trip 30 ticks ±10; reload 10 ticks; 99.5% accuracy, all classes. **Provider tier with hard TPM cap** (Tier 1: 400 tok/s; tiers purchased with cumulative spend). Over cap ⇒ packet returns as **429** (amber outline, auto-retry 2 s, latency accrues) — all-API self-limits authentically. Token costs: Trivial 2 / Standard 5 / Complex 12 / Exotic 30 (**input-dominated** — long context is mostly input tokens) / Batch 3, +10/envelope. Tooltip: "Trivial via API pays ≤50% — latency floor" (accepted and labeled) |
| **Control Node T1** [Act 2] | 2×2 | $1,500 | $10/min | Hosts up to 6 **rule template cards** (§5); radius 12 tiles |
| **Long-context Sidecar** [STRETCH] | 1×1 | $800 | $6/min | Extends host M/L to ★-long-context **and reduces host slots by 1** (KV-cache VRAM pressure) — capability costs capacity |
| **MCP Tool Server** [STRETCH — first post-slice milestone] | 1×1 | $900 | $6/min | ★-tool-use packets: mid-service, the serving model **emits 1–3 sub-request packets** to the tool server and back, **holding its slot the whole time** — agentic inference's true load profile; tool servers must sit near L servers or held-slot latency kills you |
| **Autopilot Core** (was "MCP Kernel") [STRETCH] | 3×3 | §6 | $50/min | Hosts Act 3 agents |

*Cut: Router (orphaned, dominated by Failover Gate), standalone Junction, VPN bridge.*

**Continuous batching (the defining curve of the field, now in the server):** on M/L, per-request service time scales **×(1 + 0.15·(occupiedSlots−1))**; aggregate throughput is superlinear (L at 4/4 ≈ 2.75× the tokens/s of 1/4). "Run hot but not full" is the optimization target — the actual daily job of inference capacity engineers. Slot pips on the server glyph light per occupancy: tier legibility and the batching curve share one visual.

**Heavy tails:** Complex and Exotic service rolls are seeded long-tailed — 10% of rolls run 2–3× base. One straggler pinning an L slot mid-spike is an authentic incident; p95 becomes a real metric, not a queueing artifact. Trivial/Standard stay deterministic so Act 1 ratio math survives.

**Cold starts (the single hardest problem in real inference ops, restored):** every server build — manual, blueprint stamp, rule deploy, or spot rental — takes **S: 5 s / M: 20 s / L: 60 s** ("loading weights…" progress ring on the ghost), then a **10 s warm-up at 2× service time**. S is reactively scalable; **L must be provisioned ahead of the 20 s spike warning** — predictive provisioning becomes mechanically necessary, not flavor. Logic nodes build in 1 s. [STRETCH: `keep_warm` rule action — pay idle upkeep for instant capacity = warm pools.]

### 1.6 Latency, Payment, Quality & Rating

**Payment curve (unchanged shape, re-derived numbers).** Payout = `base × TimeFactor(L)`: full pay to A, →50% at B, →12.5% at 2B, floor 12.5%. Shown **in-game as a chart**.

| Class | Base | A (full) | B (half) | Measured at | LAT-1 check (12 tiles + service) |
|---|---|---|---|---|---|
| Trivial | $1.00 | **1.1 s** | 2.5 s | **serve-start (TTFT)** | 1.0 s travel ≤ 1.1 ✓ |
| Standard | $3.00 | **2.6 s** | 5.0 s | completion | 1.0 + 1.5 (M, 1 slot) = 2.5 ✓ |
| Complex | $8.00 | **4.0 s** | 9.0 s | completion | 1.0 + 2.5 (L) = 3.5 ✓ |
| Exotic ★ | $20.00 | **5.0 s** | 10.0 s | completion | 1.0 + 0.3 (Retriever) + 3.0 (L) = 4.3 ✓ |
| Batch ⬢ | $2.50 flat | deadline 30 s | — | delivery | $0 if late; latency-immune otherwise |

**Latency is visible on the map (pillar 1, previously violated):** packets **desaturate as they age past A** and flash at B (shape stays; color channel = age); **payout floaters** ("+$0.40" in amber) at serve; hovering a route shows its steady-state pay %.

**Quality failures are silent (real small-model failure is silent):** on serve, roll node accuracy. A failure **completes normally** and plants **delayed rating damage 20–40 s later** (ticker: *"Acme reports quality issues"*). A **Verifier** in the path samples k% and converts caught failures into immediate **error packets**: red-outlined, respawn instantly at their ingress (fiction: the client retries), normal priority, fresh 8 s deadline; missing it costs −$2 and rating. Caught errors can be escalated to a better tier by routing — FrugalGPT-style cascade routing, deliberately supported. "Route everything to the cheapest model" is now punished *invisibly unless you pay for observability* — the most practitioner-honest mechanic in the game.

**Per-client rating (simplified):** an **EMA of rolling 60 s in-SLO share** (silent failures subtract on their delay). Rating affects **pay multiplier (0.8×–1.1×) and churn risk only — never spawn rate** (the v0.9 triple-homeostat mush is gone; demand is scripted). Rating <50%: random abandonment events — each abandonment **adds ring progress** (drama, not relief). Rating <30% sustained 60 s: churn warning → client churns.

### 1.7 Economy

Three instruments + one meta-resource:

- **Credits ($):** income per delivery; spent on nodes, links, upkeep, tokens, spot rentals.
- **API Tokens — fixed tiered pricing** (posted prices are how tokens actually work): Tier 1 $0.05/tok; provider tiers bought with cumulative spend lower price and raise the TPM cap. **Committed-use discount:** commit to N tok/min for −40%, pay whether used or not — a genuinely interesting bet against your own demand forecast.
- **GPU Spot Market** (the wire-market mechanic moved to the commodity that actually has spot dynamics): rent S/M/L instances by the minute (base S $6 / M $16 / L $50/min), price random-walking 0.5×–1.5× with sine jitter — dip-buying is real gameplay. Spot instances are **preemptible: 30 s eviction warning**, then the node goes dark — a telegraph that interacts with the spike heartbeat for free drama. Half build time (pre-imaged). Unlocks Contract 6.
- **Research Insights:** accrue only while the grid idles healthy (all queues <20% for 60 s ⇒ +1/10 s). Fiction: *slack capacity runs offline evals and fine-tunes* — self-explaining. Spent on side-upgrades (cache +5%, Verifier k-range, S accuracy +1%…).

Cost curves: repeat purchases ×1.15ⁿ (S ×1.12ⁿ); palette ghosts appear at 30% of cost held; stacking bonuses hyperbolically soft-capped. [STRETCH: feeder-share P&L, prestige curves.]

### 1.8 Demand Growth

**Slice: scripted ramps only** (§7.2) — two demand systems fighting made contracts untunable, and goals gated on 1-in-8 RNG rolls (v0.9 Contract 7 was mathematically unreachable). [STRETCH: post-slice growth/shrink rolls layered on top for endless modes, rating-gated as before.]

---

## 2. Pressure & Failure Design

**Doctrine: telegraph → degrade → recover. Never destroy.**

### 2.1 The Error-Budget Ring (the fail state, renamed to the industry term)

Ingress backlog ≥6 ⇒ a radial **error-budget burn gauge** fills over 40 s, draining when backlog drops; **abandonments add progress**. Full ring = breach: lose the client + penalty; 3 breaches fail the contract. Post-run report shows **"error budget consumed"** per client. Spike countdown pauses while any ring >50%, **capped at 60 s cumulative pause per cycle** (closes the hold-a-ring-at-60% freeze exploit). Modes: Standard · Endless/Zen (no rings) · Extreme (irrevocable placement).

**The first ring fills to ~50% in every player's first session** — Contract 2's scripted step guarantees it (§3). That moment is the loop's sales pitch.

### 2.2 Traffic Spikes

Every 120 s, first at 180 s; announced 20 s ahead (banner + ingress pulse + music shift), arrival jittered ±10 s; ×2–×3 on 1–2 clients for 15 s. **The 20 s warning is now load-bearing:** it is less than an L cold start — pre-provisioning or warm capacity, not reaction, is the answer. Spike fiction rotates (launch, viral moment, failover, quarter-end). Demand-as-data: `{class, ingress, begin, spacing, baseAmount, scaling, max}` groups; whole campaign generated from one difficulty scalar per contract. ["Invite traffic early" button: STRETCH.]

### 2.3 DDoS (Contract 5)

Grey jagged malformed packets, 10/s × 10 s at one ingress; a Guardrail shreds them 1/tick. First DDoS is scripted and survivable without one — feel the pain, then buy the cure.

### 2.4 Error & Quality Cascades

Saturated cheap tier → silent failures → delayed rating bleed (ticker tells you) → add a Verifier → caught failures become visible red retries → escalate by routing. The death-spiral is legible and breakable; it teaches the entire cost/quality/observability axis.

### 2.5 Provider Incidents [STRETCH]

Second Gateway provider (different price/latency/TPM) + rare 20 s incidents (round-trip ×5). Failover Gate across two Gateways becomes the textbook multi-provider pattern with zero new mechanics.

### 2.6 Fail/Recover Loop

Contracts are 10–15 min; failure ⇒ instant same-seed retry or reroll. Post-run chart bookmarks the breach moments. [STRETCH: checkpoint-restore — the sim is deterministic and all inputs are logged player actions, so "retry from the 8:00 spike" = replay the action log to that tick at max speed; no state serialization needed. Cut from slice for schedule, not feasibility.]

---

## 3. Progression: Three Acts

~20 contracts as inbox emails with legible tradeoffs; **one headline unlock each**; loose DAG after Contract 8; story ends before peak difficulty; brutal optimization lives in post-campaign "Enterprise RFP" tier.

### Act 1 — "Ops" (Contracts 1–8, manual routing) ~2 h [SLICE]

| # | Contract | Beat | Unlock |
|---|---|---|---|
| 1 | Hello, Latency | **Decoy misroute beat:** the previous architect left a distant, expensive L-server wired to your ingress. First packets take the wrong path, visibly desaturate, land amber floaters. Your first act: cut the link, place an S close. Misroute-burns-money now actually happens | Class Switch |
| 2 | Two Kinds of Users | Standard arrives; **scripted mid-contract step (+0.7 req/s at min 5) guarantees your first clog** — amber link, muted voice, ring to ~50%; fix = one node | Model M |
| 3 | Rush Hour | Survive 3 spikes; first heartbeat | Load Balancer + optical fiber |
| 4 | The Big Ask | Complex arrives; payment chart shown; token tiers + 429s introduced gently | Model L + API Gateway |
| 5 | Batch Night | ⬢ Batch + Green/Magenta domains + scripted DDoS in one beat | Batch Queue + Guardrail |
| 6 | Silent Failures | Quality contract: an all-cheap build passes latency and bleeds rating invisibly until the ticker snitches; spot market opens | Verifier |
| 7 | Grounding | ★-RAG arrives at an awkward ingress; Retriever capacity is a new bottleneck class | Retriever |
| 8 | The Wall | 3 clients, overlapping spikes, hold-rate goal ("6 req/s in-SLO for 1,200 ticks") **at scripted offered rates that actually reach 6+ req/s**; bronze-able by hand, silver realistically wants automation | Backbone trunk + Cache |

**Act 1→2 gate (soft-lock fixed):** the *Provision Control Plane — $5,000* ghost is visible from Contract 7 (own 30% rule). Unlocks on **any** of: 15 manual placements/reconfigures anywhere · reaching Contract 8 · surviving any spike with >3 manual actions. Over-provisioners — the players the game teaches to be good — get the door too. On purchase: full-screen boot, `CONTROL PLANE ONLINE`, the per-node palette collapses into the Blueprint Library.

**What survives the transition (explicit now):** per-node palette dies; **blueprint stamping and node config edits survive** at a small cycle cost (foreshadowing Act 3 override decay). A misfiring rule always has a manual override — this is a builder, not an incremental. "Change Freeze" (Act 2 breather) specifically removes stamping.

### Act 2 — "Infrastructure as Code" (Contracts 9–15) — 9–10 in slice, rest STRETCH

New verbs: save any working cluster as a **Blueprint** (slice: exact-copy stamp; parametrization + base64 share strings STRETCH) and activate **rule template cards** on Control Nodes (§5). Cold starts make the *Spike pre-provision* template mechanically necessary — the act motivates itself. From Contract 10, medals require passing on **3 seeds** (variance sources: spike jitter, heavy tails, spot prices) — hand-tuned static layouts don't generalize; rules do. [STRETCH: Contracts 11–15, region 2, cross-region trunks, MCP Tool Server + ★-tool-use, sidecars, Control Node T2, freeform rule editor.]

### Act 3 — "Autopilot" (Contracts 16–20) [STRETCH]

Pure policy authoring and auditing; see §6. Trigger: control-plane cycle saturation opens *"Install Autopilot Core — $50,000 + 10,000 banked cycles."*

---

## 4. Scoring, Medals & Reports

- **Three competing metrics** per contract: $ spend (amortized) · p95 latency · footprint. Constructed so topping all three is impossible. **Histograms, never leaderboards**; medals only from printed thresholds. (Slice: local-only distribution stub.)
- **Post-run report:** compact triplet vs requirements + **static timeline chart** built from 5 s samples (SLA, queues, cash) with breach markers. [Replay scrubber: cut — it was a marquee feature hiding in a bullet.]
- **Milestone ticker:** 5-line scrolling log; also the narrative channel (quality complaints, spot evictions, Act 3's quiet awakening).
- Endless steady-state scoring (req/kilo-tick + cost-at-infinity: steady/linear/runaway) [STRETCH].

---

## 5. IaC Rule System (Act 2)

### 5.1 Slice scope: six fixed template cards (freeform editor cut)

Each card = a pre-built rule with **2–3 parameter dropdowns** (threshold, target pool/client, blueprint), a live inline signal readout, an enable toggle, and a fire-count. Rendered as YAML-ish text for flavor. No add/remove condition rows, no priorities, no cycle economy, no control radius enforcement beyond T1's 12 tiles, no bucket. The act's feeling — *declare, don't place* — survives; 80% of the UI disappears.

| Template | Sketch |
|---|---|
| **Autoscale on queue** | `when queue_pct(pool.X) > [60] do deploy(bp.Y, near: pool.X), ttl 90s` |
| **Spike pre-provision** | `when spike_incoming do deploy(bp.Y, near: ingress.Z)` — the cold-start answer |
| **Overflow to API** | `when queue_pct(pool.X) > [70] do set_route(switch.S, [Standard+] -> gateway)` — **defaults to Standard+ (Trivial-to-API pays floor; the card says so)** |
| **Spot dip-buyer** | `when spot_price < [0.8×] do rent(spot.[M], ttl 10m)` |
| **Batch consolidation** | `when time_of_hour in lull do set_route(switch.S, Batch -> batch_queue)` |
| **Shed & breaker** | `when errors_per_min(pool.X) > [N] do shed(class, at: ingress)` — reject fast, protect the SLO, spend error budget deliberately |

Rule-deployed nodes wear a **rule badge**; clicking opens the card that placed it.

### 5.2 Full grammar [STRETCH — post-slice], with the budget arithmetic fixed

Three-dropdown rows, live inline signal values, TTL/cooldown/priority. **Evaluations draw from the per-second budget (T1: 8/s, T2: 24/s); actions draw only from the banked bucket (cap 10,000), and Control Nodes ship with a 500-eval starting bucket** — brownout means "you wrote too many rules," never "your rule dared to fire during the spike it exists for." Brownout skips lowest priority first, blinking amber. Burn 1,000 banked evals → 1 Insight. Signals as v0.9 plus `spot_price`, `tpm_used(gateway)`, `verifier_catch_rate`.

---

## 6. Autopilot Endgame (Act 3) [STRETCH — design locked, build post-slice]

Four agents on the Autopilot Core — **Sentinel** (observe/forecast), **Mender** (heal/reroute), **Provisioner** (predictive scale, now with real cold-start lead times to plan around), **Broker** (spot dips, committed-use bets, contract accept/decline). Shared Trust pool 0–10 per agent: 0–2 suggest-only via **Approval Inbox**; 3–6 act with 10 s undo; 7–10 autonomous. Control decay: overrides expire after 10 s unless re-asserted, consuming cycles. **Drift** `0.0002 × trust^1.2`/min mutates a drifted agent's policy; Sentinel flags it; fix = Audit (pause 30 s, 5 Insights, roll back N actions); severe drift spawns rogue internal traffic that must be guardrailed. Win: Contract 20 sustains ≥95% in-SLO at ≥150 req/s for 10 min ending in a ×4 all-ingress finale surge; ≤3 manual interventions silver, 0 gold. Dual prestige: *Sell the Company* (+10% demand/prices) vs *Open-Source the Stack* (+10% efficiency/Insights).

---

## 7. Balance Sheet v1.0

### 7.1 Starting state (Contract 1)
Cash $500 · Tokens 0 (market locked until C4, opens with 200 free) · Palette: Ingress (given), Model S, links · 1 client @ 0.8 req/s, 100% Trivial, ramping to 1.2 · plus the decoy L-server.

### 7.2 Scripted demand ramp (Act 1) — keyframed per client, no RNG gating

| Contract | Clients | Offered req/s (base → peak-of-ramp) | Spike ×  | Mix Tri/Std/Cx/★/⬢ |
|---|---|---|---|---|
| 1 | 1 | 0.8 → 1.2 | — | 100/0/0/0/0 |
| 2 | 1 | 1.5 → 2.2 (scripted step min 5) | — | 70/30/0/0/0 |
| 3 | 2 | 2.5 | ×2 | 65/35/0/0/0 |
| 4 | 2 | 3.0 | ×2.5 | 55/30/15/0/0 |
| 5 | 3 | 4.0 | ×3 | 50/25/12/0/13 |
| 6 | 3 | 4.5 | ×3 | 45/30/12/0/13 |
| 7 | 4 | 5.5 | ×3 | 48/25/12/6/9 |
| 8 | 3 | 8 → 12 peak | ×3 overlapping | 50/25/12.5/6/6.5 |
| 9–10 (Act 2 slice) | 4–6 | 12 → 18 | ×3 | ★→10% |
| 11–20 [STRETCH] | →20, 3 regions | → 150 | ×4 multi-ingress | ★→20% |

Offered rate = what spawns. Rating never scales it. §7.4 is computed against these literal numbers.

### 7.3 Capacity reference — **generated from the node table by the headless sim; CI asserts this table** (the v0.9 slot-math contradiction can't recur)

| Unit | Max sustained |
|---|---|
| Model S | 1.0 Trivial/s |
| Model M | 1.16 Standard/s (2 slots, occupancy-scaled) or 2.9 Trivial/s |
| Model L | 1.10 Complex/s or 2.76 Standard/s (4 slots, occupancy-scaled) |
| API Gateway | ~4.0 req/s enveloped; hard-capped by tier TPM (T1: 400 tok/s) |
| Retriever | 3.3 ★/s |
| Copper fiber | ~30 packets/s |
| Cache (homogeneous feed) | up to +45% Trivial capacity upstream |

### 7.4 Economy sanity (Contract 5, offered 4.0 req/s, ~85% in-SLO)
Gross ≈ 4.0 × $2.1 avg × 60 ≈ **$505/min**. Upkeep (3S+M+L+LB+Guardrail+BQ+GW) ≈ $54/min + tokens ≈ $20/min ⇒ net ≈ **$430/min** ⇒ Model L ($2,000) ≈ 5 min of good play. Target: one meaningful purchase decision every 3–6 min. ✓ (computed at scripted rates — no phantom rating discount).

### 7.5 Curves
Repeat cost ×1.15ⁿ (S ×1.12ⁿ) · unlocks `60 + amount^1.11 × 20`, near-free C1–3 · soft-capped stacking bonuses · spot band 0.5×–1.5× · one difficulty scalar per contract feeds spikes/mix/cadence + global demo multiplier.

---

## 8. Visual & Audio Direction

### 8.1 Look [SLICE unless noted]
Near-black `#101218`, 2% grid lines; packets = HDR-tinted glyph pulses on scrolling-emissive fibers; per-link heat via MaterialPropertyBlock (green→amber→red). Clog trifecta: frozen pulse + muted voice + red heat. **Latency legibility:** age-desaturation past A, flash at B, payout floaters. Slot pips on servers (occupancy + tier in one glyph). Build-progress rings on cold-starting nodes; amber 429 outlines; red error outlines; grey DDoS jags. Error-budget rings readable map-wide. Alt-overlay X-rays configs/queues/rule badges. Named stall chips: "Destination full", "No route", "Throttled", "Rate-limited (429)", "Loading weights". [STRETCH: silhouette zoom weathermap, GIF/replay export.]

### 8.2 Sound [SLICE = 3-system version]
Three crossfaded drone stems (calm/tense/alarm) driven by worst clogHeat + active rings; one pooled set of quantized one-shots snapped to the nearest 16th of the 4 s bar (cap ~8 voices); pager stinger on breach; spike banner shifts stem mix. Success is the absence of alarms. **Browser audio latency tested in week 1.** [STRETCH: per-route voices bound to trunks — the 7-voice cap mirrors the 7-trunk cap exactly.]

---

## 9. Vertical Slice Scope (crisp)

**IN (slice):** 20 Hz deterministic sim; two-phase handshake; gap-list links; clog heat; admission control; cold starts; continuous-batching occupancy; heavy tails; node set = Ingress, Class Switch, Failover Gate, LB, Cache (homogeneity-lite), Guardrail, Verifier, Retriever, S/M/L, Batch Queue, API Gateway (TPM/429), Control Node T1; classes Trivial/Standard/Complex/★-RAG/Batch; payment curve + TTFT Trivial + EMA rating + silent failures + error packets; error-budget rings; spikes + scripted DDoS; token tiers + committed-use; GPU spot market with eviction; Insights (idle accrual); Contracts 1–10; tutorial beats (decoy misroute, scripted first clog); post-run static chart + ticker; blueprints (exact stamp); 6 template cards; full visual kit minus silhouette; 3-stem audio; meta-only saves + export string; recorded-build-order headless CI; 3-seed medals (C10).

**OUT (stretch):** freeform rule editor + cycle economy; Contracts 11–20; Act 3 entirely; MCP Tool Server; sidecars; regions/cross-region trunks; growth rolls; provider #2 + incidents; keep_warm; checkpoint-restore; silhouette zoom; GIF export; online histograms; Daily Grid; feeder shares; success-heat; parametrized/base64 blueprints; invite-traffic button; per-route adaptive audio; prestige.

**Balance validation protocol (rescoped):** three **recorded build orders** per contract (naive / intended / degenerate-all-API), replayed headless on 3 seeds. CI fails if: naive misses bronze, intended misses silver, degenerate golds, any NaN/exception, LAT-1 breaks, or §7.3 drifts from the node table.
