# Throughput: The Inference Grid — DESIGN2 (v2.1 FINAL, build-ready)

**Version:** 2.1 — data-center-builder pivot, post-critique · **Target:** Unity 2D WebGL, browser, mouse-only, left-click only · **Team:** 1 AI dev, procedural sprites, one-session build slice

**Pitch:** You are handed the keys to an empty, dark server hall with a live grid feed and a fiber uplink already humming. Deploy racks, feed them power, keep them cool, keep them connected — and watch a river of glowing inference jobs turn into money. Density is profit. Density is heat. Every tile you click is a tradeoff.

**What changed since v2.0 (critique integration summary):**
1. **Deadlock removed** — uplink (+10 Gbps) and substation (+500 kW) are purchasable from the start via contextual chips; no gate behind contracts.
2. **Pacing retuned ~3×** — GPU gate at **$1,200 earned** (lands ≈2:15), ambient demand starts **4 PF, +2 PF/day**, so the queue pressure fantasy never inverts.
3. **Day 1 is flattened** — clock pinned (Day 1 starts 06:00), price capped $0.04 on Day 1; full sine from Day 2, when the player has levers.
4. **Contracts carry 5 numbers** including **"adds X PF"**; PicoChat micro-contract at ≈2:30 teaches the verb; Nimbus retuned to be actually winnable.
5. **Starter PDU shrunk to 100 kW / radius 3** — forces PDU #2 as the first real layout decision and guarantees the teaching breaker-trip around minute 5.
6. **Typed jobs (cyan/purple)** — purple is GPU-only; kills CPU-spam dominance, gives demand a face.
7. **Global utilization multiplier killed** — jobs assign per-rack (oldest-fed-first); a rack is fed or starving, HUD shows **Served PF / Capacity PF + queue**.
8. **Ghost is tri-state** (green/amber/red) — power-ring membership and cash are hard gates; PDU overload, heat, and bandwidth are amber warnings you may knowingly eat. This is what lets the breaker trip and NO-UPLINK states exist as teachers.
9. **Scope cut to the realist's line** — Phase A ships 4 placeable buildings + 2 click-purchases (the "7 buildings" pillar completes in Phase B — say so in playtest notes). Heat is a stamped field, not diffusion. 4 sounds max. No save. One hardcoded contract script.

---

## 1. Design Pillars

| v1 complaint | v2.1 answer |
|---|---|
| "Nothing to do" | Verified decision timeline: new verb or real decision at 0:12, 0:40, ~2:15, ~2:30, ~4:00, ~5:30, ~7:30, ~9:00 (see §6.4). Metric: ≤1 sixty-second window with zero meaningful inputs in the first 10 minutes. |
| "Mechanics suck / systems don't interact" | POWER (kW), HEAT (°C from kW), MONEY ($/s), BANDWIDTH (Gbps). Every placement moves at least two. |
| "Contract dialog makes no sense" | Contracts are cards with exactly **5 numbers** (needs / advance / **adds X PF** / rate bonus / deadline+penalty). One click to accept. |
| "Only two build options" | 4 placeable + 2 purchases in session one, each with different power/heat/space/bandwidth math; 3 more buildings Phase B. |
| "Fiber linking doesn't work" | No freehand linking, ever. Radius rings (PDU, CRAC) + facility bandwidth pool. Cables are auto-drawn L-polylines, pure flavor. |
| "No sense of motivation" | Goal chips + contract advances + live **NET $/s** green/bleeding-red + locked silhouette always visible (curiosity gap). |
| Blocking modal tutorial | Zero modals. Goal chips ≤5 words, cursor captions ≤8 words, and the first breaker trip is the power tutorial. |

**Simulation honesty rule:** kW-in/kW-out on a 2-level tree, a stamped radial heat field, circle checks, one bandwidth number. Real-world figures set ratios and flavor only (≈70% peak breakeven, cooling eats ~25% of its rating). No voltage, no CFD, no subnetting, no packet routing.

---

## 2. Core Loop

```
SEE demand piling at the uplink (glowing job queue, cyan + purple)
  → CLICK a building chip → tri-state ghost with live reason + rings
  → PLACE inside power ring, near cooling, with headroom
  → WATCH it boot (LEDs cascade, fan spins) → jobs rain in → NET ticks green
  → HEAT builds, POWER climbs, BANDWIDTH fills, PRICE peaks daily
  → next placement is harder than the last
  → sign a contract / buy uplink / order substation / re-layout
  → repeat, denser
```

Player skill = **layout under multi-constraint pressure**. Dense = profitable = hot = one breaker trip from a dark subtree.

---

## 3. First 60 Seconds (exact beat script — no modals, ever)

Pre-placed at load: **Grid Feed** stub (top edge, 500 kW), **PDU #1** (100 kW, radius-3 yellow ring visible), **Fiber Uplink** (left edge, 10 Gbps) with cyan job dots already piling into a pulsing queue. Toolbar: CPU Rack and PDU enabled + 2 locked silhouettes (GPU Rack, CRAC). HUD after the first tick: Cash $10,000 · NET −$0.20/s · Served 0/4 PF (queue throbbing) · Power 10/500 kW · BW 0/10 Gbps · Day 1, 06:00, price $0.02→.

| Time | Beat |
|---|---|
| **0:00** | Scene opens directly. Goal chip top-left: **"▦ Deploy a rack"**. Queue at the uplink visibly throbs — something is already unresolved. |
| **0:05–0:12** | Player clicks Rack chip → ghost snaps to grid: **green** inside PDU ring, **red** outside with caption *"Needs power — build in ring"*. Cost `$800` + wallet-after at cursor. |
| **0:12** | First click places CPU Rack: scale-pop (1.15→1.0), thunk, dust ring. 3s boot: LED rows blink on, fan spins up. |
| **0:15** | Cyan dots stream uplink → rack. **+$ floaters — best sound in the game.** Demand is 4 PF vs 1 PF capacity: rack is 100% fed, queue still piles (build more!). Goal chip checkmarks, chime, slides away. First money before 0:20. |
| **0:20** | Chip: **"▦ Two more racks (+$250)"**. Tool is sticky — two more clicks. Floor under the cluster visibly warms (orange gradient tiles). |
| **0:40** | Chip: **"❄ Cool the hall (+$250)"**. CRAC chip unlocks with pulse + name flyout. Arming it auto-shows the heat overlay + its blue ring — the dependency is taught by geometry. Cluster is ~47° WARM (−25% badges just appearing); placing the CRAC visibly cools the tint and un-badges the racks. |
| **0:55** | Chip: **"💰 Earn $1,200"** with progress bar (tooltip: *"revenue + rewards — not your starting cash"*), plus locked silhouette beside it: **GPU Rack — unlocks at $1,200 earned**. Three racks + CRAC online, ~$6/s gross, queue still visible. Onboarding complete; nothing ever paused. |

**Adaptive hints (Phase A ships only one):** two invalid placements in a row → one banner naming the rule and pulsing the fixing chip ("Racks need power — place inside a ring"). The 10s-idle nudge and "?" tip-replay library are Phase B. Silent for players who succeed.

---

## 4. The Four Interlocking Systems

### 4.1 Power (2-level tree, exactly)

- **Grid Feed** (pre-built, not placeable): facility cap **500 kW**. HUD bar `total kW / 500`.
- **PDU** (placeable, $2,000): powers buildings within **radius 3**, capacity **100 kW**, draws 5 kW. Every powered building must sit inside some PDU ring (hard gate). PDUs hang off the feed. No PDU chaining, no cable graph.
- **Overload:** at **90%** of PDU or feed rating → amber blink + tick-tick. **>100% for 3s → breaker trip**: white arc-flash, thud, that PDU's subtree dark 8s, then staged reboot (0.5s stagger). Equipment that was thermally shut down returns to that state and still requires its paid restart. One-line toast + camera nudge. The first trip IS the power tutorial. *(Cut: reboot heat spike.)*
- **Overload is reachable on purpose:** the ghost shows **amber** ("PDU at 108% — breaker will trip") but allows placement. Ignore the warning, eat a cheap recoverable trip, learn. That's the lesson plan.
- **Expansion:** when feed ≥80%, a contextual chip appears ON the Grid Feed sprite: **"+500 kW — $12,000 · arrives 90s"**. Lead time is the mid-game bet. (Also reachable anytime by clicking the Feed.)

### 4.2 Heat (the spatial antagonist) — stamped field, no diffusion

- `tileTemp = 24° + Σ heat stamps − Σ cooling stamps` (clamped ≥ 24°). Stamps are radial with linear falloff to 0 at radius 3 (heat) / the cooler's radius (cooling). Whole 24×16 field recomputed at **2 Hz** and on any placement change. No per-tick dynamics to debug; overlapping stamps make clusters hot and sprawl cool — identical player pressure.
- Staged consequences, never instant:
  - **≥45° WARM:** orange tint, fan speeds up, throughput −25% (dots visibly slow).
  - **≥60° HOT:** red tint, throughput −50%, rack's heat stamp +20% (**thermal runaway** — the 2 Hz recompute cascades it to neighbors in visible stages).
  - **≥75° CRITICAL:** smoke particles, rack shuts down; restart = one click, $400. Revenue stops.
- Every throttle names its reason in-place: fixed-size screen-space badge (≥20px), click → *"Rack B3 throttled: 61° — no cooling in range."*
- **CRAC** ($1,500): −100 kW heat, radius 3, draws 25 kW. Cooling that eats a quarter of its rating in power IS the PUE mechanic (PUE readout itself is Phase B).
- *(Cut: heat-shimmer distortion shader. Tint + smoke only.)*

### 4.3 Racks & typed jobs (the money-makers)

- **Jobs are typed:** **cyan** jobs run on any rack; **purple** jobs are **GPU-only**. Purple share of ambient demand grows with days (15% Day 1, +10%/day, cap 70%); contracts add their own purple pools. Purple dots visibly bounce off CPU racks. This makes GPU demand legible and self-limits CPU spam — CPU racks can only ever earn the cyan pool.
- **CPU Rack** ($800): 10 kW, 1 PF cyan, $2/s fed. The starter drug.
- **GPU Rack (air)** ($5,000, unlock @$1,200 earned): 40 kW, 5 PF cyan+purple, $6/s fed. 4× heat density — one CRAC stops being enough.
- **GPU Pod (liquid), Chiller** → **Phase B** (with the $15k-lifetime unlock, chiller-loop requirement, 60s delivery).
- **Job assignment (no global multiplier):** contract pools fill their assigned/eligible racks first, then ambient purple → GPU racks, ambient cyan → all racks, **oldest-placed first**. A rack is **fed** (LEDs bright, earning `rate × servedPF/capacityPF`) or **starving** (dark, still burning full watts — visible bleed on NET). Overbuild and it's your newest rack that sits dark. HUD shows **Served PF / Capacity PF + queue depth**, never an abstract %.
- Powered racks always draw full watts, online or throttled or starving. Toggled-off racks draw 0 (the load-shed verb).
- **GPU failure ticks → Phase B** (with drone repair; first-instance softening per §5.6).

### 4.4 Network (the soft gate)

- Facility bandwidth pool, **10 Gbps** at the Uplink. Each powered online rack attempts bandwidth (CPU 1, GPU 4). Over cap → **newest** racks get a cyan **NO UPLINK** badge, drop to 0 served, dots never arrive. The HUD shows attempted and accepted bandwidth separately.
- **Not a placement blocker:** the ghost goes amber (*"Will exceed uplink — rack will idle"*). The fix is a purchase available **from minute 0**: contextual chip on the Uplink sprite at ≥80% usage — **"+10 Gbps — $3,000"**, instant. No gate, no puzzle, no deadlock.
- Spine Switch / contiguous-pod clauses: STRETCH, cut.

### 4.5 Ghost grammar — why every placement is a decision

Tri-state ghost, failing/warning reason named at the cursor (first reason only; full detail mirrored in a fixed strip above the toolbar):

- **RED (cannot place):** outside every PDU ring (*"Needs power — build in ring"*) · tile occupied/out of bounds · cash < cost · feed would exceed 500 kW (*"Grid feed maxed — order substation"*).
- **AMBER (places, with consequences):** PDU reaches ≥90% (*"PDU at 95% — near breaker limit"*) or exceeds 100 kW (*"PDU at 105% — breaker will trip"*) · tile ≥45° (*"Too hot here — will throttle"*) · bandwidth over cap (*"Uplink saturated — rack will idle"*).
- **GREEN:** all clear. Cost + wallet-after always shown.

Placement forecasts reserve feed, PDU, and bandwidth capacity for every installed building, including equipment that is temporarily off, dark, booting, or heat-shut down. Power cycling therefore cannot be used to construct a grid that exceeds its hard limits when everything returns.

Dense clusters share PDUs and CRACs efficiently but stack heat; sprawl stays cool but multiplies capex and floor. That tension is the game.

---

## 5. Economy & Motivation

### 5.1 Demand & revenue

- Ambient demand: **4 PF at 0:00, +2 PF per game-day** (continuous linear). Contracts add dedicated pools on top. Job particles spawn at the uplink proportional to demand (1 dot per N PF-seconds, pooled/capped — never per job), colored cyan/purple/per-contract.
- Unmet demand piles visibly at the uplink (build more!); dark starving racks = overcapacity (sign contracts!). When money drops, the cause is always a visible thing on the map: hot rack, tripped PDU, NO-UPLINK badge, dark rack.
- **Revenue = Σ per-rack (rate × servedPF/capacityPF × contract bonus)**, ticking; **NET $/s = revenue − electricity**, green above zero, bleeding red below.
- **Breakeven triangle** on the Served/Capacity bar marks the served-PF level where NET = 0 at the current price; it rides up at peak hours.

### 5.2 Electricity (time-varying opex)

- Game day = **3 minutes**. Clock pinned: **elapsed 0:00 = Day 1, 06:00**; evening peak (18:00) lands at +90s into each day → **1:30, 4:30, 7:30, …**
- **Day 1 curve is flattened: $0.02–0.04.** Day 2 onward: full sine **$0.02 night → $0.05 midday → $0.08 peak**, with current price + trend arrow on the HUD (sparkline cut — unreadable at 720p).
- Every powered building pays draw × price continuously. The empty starter grid begins slightly red at −$0.20/s, making the first rack an immediate, visible route back to profit. Later peak-price pressure can be answered with layout, cooling, and load shedding.
- Battery/UPS/Generator: STRETCH, cut.

### 5.3 Contracts (cards, not dialogs — 5 numbers)

At most 2 offered / 2 active (Phase A: hardcoded two-card timeline, no generator). Accepted with one left-click; its jobs and dots carry the owner's color.

```
┌──────────────────────────────────┐
│  PICOCHAT             [inference]│
│  ▦ needs: 1 GPU rack online      │
│  💰 advance: $8,000 now          │
│  📈 adds: +4 PF (GPU jobs)       │
│  ⚡ rate: +25% on its jobs       │
│  ⏱ by Day 3 · penalty $1,000    │
└──────────────────────────────────┘
```

- **PicoChat** — offered the moment GPU Rack unlocks (~2:15–2:30). Funds GPU #1, teaches the contract verb at minute 3, gives the purple pool a face.
- **Nimbus AI** — offered Day 4 (~9:00): **needs 4 operational GPU racks · advance $8,000 · adds +20 PF · rate +50% on its jobs · by Day 7 · penalty $5,000.** The safe tested expansion is `$15,500` beyond the second site, funded by the one-time advance plus earnings.
- Declining: PASS button; the card returns at the next day rollover. No penalty for passing.
- SLA drain bars, training load oscillation, contract generator: **Phase B**. An active deadline miss is checked before fulfillment, charges the penalty, and reoffers a recovery contract next day with a fresh window but no second advance. Failure delays mastery; it never makes the run permanently unwinnable.

### 5.4 Goal chips (max 2 visible, ≤5 words, always one cooking)

| # | Chip | Reward / unlock |
|---|---|---|
| 1 | ▦ Deploy a rack | (first money is the reward) |
| 2 | ▦ Two more racks | +$250 |
| 3 | ❄ Cool the hall | +$250 · unlocks CRAC chip |
| 4 | 💰 Earn $1,200 *(progress bar; "earned" = served-work revenue + rewards, not starting cash or advances)* | unlocks **GPU Rack** |
| 5 | 📄 Sign your first contract *(PicoChat card arrives with it)* | advance $8,000 |
| 6 | ⚡ 100 kW IT online | +$1,500 |
| 7 | ⭐ Hold NET green through a peak | +$500 |
| 8 | 💰 Earn $15,000 lifetime **and fulfill Nimbus** | Phase B: unlocks Chiller + GPU Pod; Phase A: persistent GRID MASTERED state |

Completion = checkmark burst + chime + cash; the next chip and a locked silhouette are always on screen.

### 5.5 Tech unlocks

| Trigger | Unlock | New problem it creates |
|---|---|---|
| $1,200 earned (~2:15) | GPU Rack (air) | 4× heat density; one CRAC stops being enough; purple demand now serviceable |
| — (always available) | Uplink +10 Gbps, Substation +500 kW | Bandwidth and power become spend decisions, never gates |
| $15,000 lifetime | *(Phase B)* Chiller + GPU Pod (liquid, 60s delivery) | 120 kW tiles; chiller loops; feed cap looms |
| *(STRETCH)* Gen3 event | Denser pods at 2× rate | Old-gen rate decay → refresh-vs-milk |

### 5.6 Events

**Phase A ships one systemic event: the breaker trip** (player-caused, guaranteed reachable ~minute 5 via the 100 kW starter PDU + amber-allowed overload).

**Phase B events** (all: three visible stages, one-line reason, one-click recovery, cost is money — never the run):

| Event | Telegraph | Effect | Counter-play |
|---|---|---|---|
| Heat wave (first ~Day 4, 60s) | Forecast bar 30s ahead + actionable prep chip ("Prep: 1 spare CRAC") | **First instance −15%** air cooling, runaway multiplier disabled during it; later waves −30% w/ runaway | Cooling headroom; toggle racks off |
| Grid brownout (first ~Day 3, 20s) | 5s klaxon + HUD flash | Feed cap → **max(60% of cap, player draw × 0.8)** — always bites slightly, never cries wolf | **Shed mode:** during brownout, clicking any rack toggles it directly, one click |
| GPU failure (continuous) | Amber blink, then red | Rack revenue stops | One click = drone repair $200 / 10s |

---

## 6. Numbers (all constants in one `Balance` tuning file; expect ±30% after playtest)

### 6.1 Starting state

| Item | Value |
|---|---|
| Starting cash | $10,000 |
| Grid feed cap | 500 kW |
| Uplink bandwidth | 10 Gbps |
| Pre-placed | Grid Feed, 1 PDU (100 kW, r3), Fiber Uplink |
| Floor | **24 × 16 tiles** |
| Game day | 3 min; Day 1 price $0.02–0.04 flat-ish, Day 2+ $0.02–0.08 sine; peak at +90s/day |
| Ambient demand | 4 PF at 0:00, +2 PF/day continuous; purple share 15% +10%/day (cap 70%) |

### 6.2 Building stats (Phase A)

| Building | Cost | Size | Draw | Heat stamp | Cooling | Compute | BW | Revenue fed | Notes |
|---|---|---|---|---|---|---|---|---|---|
| CPU Rack | $800 | 1×1 | 10 kW | 10 kW | — | 1 PF cyan | 1 Gbps | $2.0/s | starter |
| GPU Rack (air) | $5,000 | 1×1 | 40 kW | 40 kW | — | 5 PF cyan+purple | 4 Gbps | $6.0/s | unlock @$1,200 earned |
| PDU | $2,000 | 1×1 | 5 kW | 2 kW | — | — | — | — | 100 kW cap, radius 3 |
| CRAC | $1,500 | 1×1 | 25 kW | — | −100 kW, r3 | — | — | — | air only |
| Uplink +10 Gbps | $3,000 | — | — | — | — | — | +10 | — | instant; contextual chip @≥80% |
| Substation +500 kW | $12,000 | — | — | — | — | — | — | — | 90s lead; contextual chip @≥80% |
| Heat restart | $400 | — | — | — | — | — | — | — | one click, CRITICAL rack |

Phase B adds: GPU Pod (liquid) $16,000 / 120 kW / 16 PF / 12 Gbps / $20/s (needs chiller loop, 60s delivery) · Chiller+CDU $9,000 / 2×2 / 150 kW draw / −600 kW r6 · drone repair $200/10s.

**Economics sanity:** GPU rack at avg price ($0.05): power $2.0/s vs $6/s revenue → breakeven ≈33% fed; + CRAC share ≈42%; at $0.08 peak ≈ **75%** — peak hours punish slack capacity, matching the real ~70% neocloud breakeven without a spreadsheet.

### 6.3 Capacity walls under 500 kW / 10 Gbps (all cooled + powered)

| Config | IT kW | Overhead | Total | BW | Gross $/s fed | Wall hit |
|---|---|---|---|---|---|---|
| 3 CPU + 1 CRAC + PDU#1 (minute 1) | 30 | 35 | 65 | 3 | $6 | none — easy |
| +GPU #1 (~3:00) | 70 | 35 | 105 | 7 | $12 | PDU#1 at 95% — **amber; one more load risks a trip** |
| +PDU#2, +CRAC, +GPU #2 (~5:30) | 110 | 65 | 175 | 11 | $18 | **bandwidth 11/10** until the uplink upgrade |
| 3 CPU + 4 GPU, 3 CRAC, 4 PDU (Nimbus done) | 190 | 100 | 290 | 19 | $30 | safe four-site win path; growth resumes |
| 3 CPU + 8 GPU (Day 7–8 demand) | 350 | ~145 | ~495 | 35 | $54 | **99% of feed → substation** |

### 6.4 Pacing verification (retuned — decision timeline)

- **0:12–0:55:** place 4 buildings, 3 chips complete. Demand 4 PF vs 3 PF capacity → racks 100% fed, $6/s gross, queue still piles. Electricity ≤ 65 kW × $0.04 = $2.6/s worst case → NET green all of Day 1.
- **~2:15:** earned ≈ $700 revenue + $500 rewards → **$1,200 gate opens** (≈40s tolerance). GPU silhouette resolves; wallet ≈ $7,500.
- **~2:30:** **PicoChat** card. Sign → +$8,000 financing + purple demand → GPU #1, then a funded second-site decision. The advance increases cash but not lifetime earnings. PDU #1 reaches 95% amber; one careless extra load produces a cheap, recoverable breaker lesson.
- **~4:30 (Day 2 peak):** first full peak with GPU online. Breakeven triangle rides up; toggle verb + contract income = survivable, instructive.
- **~5:30:** the Pico advance plus earned revenue funds the full `$11,500` second-site package: **PDU #2 + CRAC + GPU #2 + uplink**. Placement and purchase order still matter: boot the site before upgrading and the new GPU visibly idles at 11/10 bandwidth.
- **~7:30 (Day 3 peak):** hold-NET-green chip resolves or teaches.
- **~9:00 (Day 4):** **Nimbus** card — $8k now against a further `$15,500` safe buildout by Day 7, +20 PF of purple demand. The tested route adds two powered GPU sites and reaches mastery without tripping a breaker; a denser layout remains the riskier alternative.
- Dead-air check: zero-input windows >60s in the first 10 min ≈ 0–1 (was ~5 in v2.0).

---

## 7. Input Spec (left-click only — the browser contract)

**Verbs (complete list):**
1. **Click toolbar chip** → placement mode: chip highlights, cursor becomes ghost.
2. **Move mouse** → ghost snaps to grid; **green/amber/red** with ≤8-word reason; cost + wallet-after at cursor (clamped to screen edges, first reason only; full detail in a fixed strip above the toolbar). While armed: nearest PDU ring bright, other rings 25% alpha; cooling ring + heat overlay auto-shown for racks/CRAC. Never all overlays at full strength simultaneously.
3. **Click valid cell** → place; **tool stays armed** (sticky). Click the highlighted chip again to disarm. Esc also cancels but is never the only way.
4. **Click placed building** → inspect panel **docked to the right edge** (never under the cursor): name, live status line ("Throttled: 61° — no cooling in range"), served/draw/heat, **Toggle On/Off** (load-shed verb), **Sell** (100% refund ≤5s after placement, 50% after).
5. **Click contract card / Grid Feed / Uplink / contextual chips** → accept / buy. Goal chips complete automatically when their visible condition is met. Contextual buy-chips self-advertise at ≥80% usage — buildings-as-buttons is taught, not hidden.
6. **Click empty ground with no tool** → close the inspect panel. Never destructive.
7. **On-screen Undo button** (Z accelerator): reverts last placement, full refund (until next placement or 10s; after that, Sell).
8. Time controls: **⏸ / 1× / 3×**, always visible, ≥44px targets, corner-anchored. Zoom via on-screen ± buttons.

**Forbidden:** right-click (context menu suppressed via WebGL template JS regardless), required drag, double-click, hover-only info, scroll-only zoom. A capture-phase template guard sends keyboard input to Unity only while the canvas is focused; keyboard remains optional accelerators only. Click targets fat: the whole tile is the target, badges ≥20px screen-space.

---

## 8. Readability & Aliveness

**Overlay grammar:** three HUD toggle chips — **HEAT** (24×16 floor-temperature texture quad, blue→orange→red, bilinear), **POWER** (PDU rings + feed load %, amber pulses near limits), **NETWORK** (uplink bar + per-rack BW ticks). Relevant overlay auto-activates while placing. Overlays ARE the tutorial.

**Alive checklist (procedural sprites only):**
- Rack **LED rows = servedPF literally** (bright grid busy, sparse blink starving, dark offline) — 3-frame procedural flipbook.
- Job dots rain uplink → racks (straight/L-shaped lerps, no routing), cyan/purple/contract-colored; purple dots bounce off CPU racks; congestion piles at the uplink; throttled racks receive slowed dots.
- Heat: floor gradient tint per tile; smoke particles at CRITICAL; frost-blue ripple around CRACs. *(No shimmer shader.)*
- Power: amber blink at 90%; **breaker trip = white arc-flash + thud + subtree dark**; staged reboot LEDs.
- Fans: rotating sprite, speed mapped to heat. Cables: dumb L-polylines under buildings, flavor only.
- Money floaters on every payout tick; placement scale-pop + thunk + dust. Hit-pause/camera nudge reserved for breaker trips only — juice never buries validity tints or badges.
- **Audio: exactly 4 procedural one-shots** — money tick (most polished), placement thunk, breaker trip, goal chime. Rate-limited. If time runs out, ship silent; floaters carry the reward feel. Sonification: STRETCH.

**HUD @1280×720 (fits or it doesn't ship):** Top bar, 6 widgets max: **Cash · NET $/s (green/red) · Served/Capacity PF + queue depth (breakeven ▲ marker) · Power kW/cap bar · BW Gbps/cap bar · Day+clock+price+trend arrow.** PUE and sparkline: Phase B. Bottom: build chips (4 + locked silhouettes). Left gutter (~320px, map uses the right ~960px): goal chips (≤2) + contract cards (≤2). Right edge: docked inspect panel. Corners: time controls, zoom ±, Undo.

---

## 9. Scope

### MUST (Phase A — this session, gate = playtestable)
24×16 floor; pre-placed Feed/PDU/Uplink · 4 placeables (CPU, GPU, PDU, CRAC) + 2 purchases (uplink, substation w/ 90s timer) · 2-level power tree, 90% amber, 100%-for-3s trip, 8s dark + staged reboot · stamped heat field @2 Hz, 45/60/75 ladder, badges w/ reasons, texture-quad overlay · bandwidth pool + NO UPLINK + contextual chips · typed demand (cyan/purple), per-rack oldest-first assignment, queue depth · revenue + electricity day-curve (Day 1 flattened) + NET + breakeven marker · tri-state ghost w/ reasons + rings + sticky + cost-at-cursor · goal chips 1–8 · hardcoded PicoChat + Nimbus cards (5 numbers, PASS/refresh, deadline check — no generator, no SLA bars, no spikes) · inspect panel (status/toggle/sell/purchase) · 1-deep undo · pause/1×/3× + zoom · job dots from v1 packet pool + LED flipbook + fan sprite + floaters + scale-pop · 4 sounds · WebGL template: right-click suppressed, keyboard capture scoped to the focused canvas · two-invalid-placements hint banner.

### Phase B (next session)
Chiller + GPU Pod + delivery queue + $15k unlock · GPU failures + drone repair · brownout (shed mode, draw-scaled cap) + heat wave (softened first instance, prep chip) + forecast bar · contract generator, 2/2 slots, SLA bars, training spikes · PUE readout · price sparkline · boot LED cascade polish · idle-nudge + "?" replay · star chip (PUE ≤1.35).

### CUT (do not touch)
Heat diffusion dynamics · shimmer shaders · UPS/Generator/Battery · Spine Switch · Gen3 decay · Maintenance Bay · sonification · drag-to-paint · technician walkers · second map · demand charges · save system.

### Success metrics, next playtest
- First money < 60s; something placed by 0:20.
- **≤1 sixty-second window with zero meaningful player inputs in the first 10 minutes.**
- Player survives minute 5 having hit and *understood* one crunch (can name it).
- "What were you optimizing?" gets a spatial answer.
- Zero "how do I…" questions a chip or caption should have answered.
- At minute 10 the player has an expansion plan they chose (Nimbus/substation), not one they were told.

### Anti-scope
No modal tutorials. No right-click or required drag. No fake electrical/network fidelity — abstract honestly. No idle-waiting states. No upgrades that retroactively solve layout. No UI advertising unbuilt systems (no Chiller tooltip until Phase B ships it). No RNG-gated unlocks. No punishing left click — everything undoable or refundable. No new system before the previous one's goal-chip beat.
