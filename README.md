# Throughput: The Inference Grid

A data-center builder. You're handed an empty, dark server hall with a live grid
feed and a fiber uplink already humming — deploy racks, feed them power, keep them
cool, keep them connected, and watch a river of glowing inference jobs turn into
money. Density is profit. Density is heat. Every tile you click is a tradeoff.

Four interlocking systems: **power** (PDU budgets, breaker trips), **heat**
(radial fields, throttling, thermal shutdown), **bandwidth** (uplink saturation),
and **money** (time-of-day electricity prices vs compute revenue). Left-click
only. No modals. The first breaker trip is the tutorial.

Built with **Unity (WebGL)**. Deployed as a **Cloudflare Worker** with static assets.

## Play

https://throughput-the-inference-grid.ericrolph.workers.dev

## Repo layout

| Path | Purpose |
| --- | --- |
| `unity/` | Unity project (Unity 6000.3 LTS, 2D) |
| `dist/` | Committed WebGL build output — served by the Worker |
| `Tests/SimTests/` | Headless NUnit simulation regressions and deterministic win-path playthrough |
| `wrangler.jsonc` | Cloudflare Worker config (static assets from `dist/`) |
| `.github/workflows/deploy.yml` | Deploys `dist/` to Cloudflare on every push to `main` |
| `docs/DESIGN2.md` | Current game design (v2 — data-center builder) |
| `docs/BUILDSPEC.md` | v2 implementation plan |
| `docs/DESIGN.md` | v1 design (packet routing — superseded after playtest) |

## Deploy pipeline

Every push to `main` runs the headless simulation suite before publishing `dist/`
to Cloudflare Workers via `wrangler-action`. Pull requests run the same suite. The Unity WebGL build is produced locally
(Unity batchmode) into `dist/` and committed; CI stays fast and needs no Unity license.

Local deploy (requires wrangler auth or `CLOUDFLARE_API_TOKEN`):

```sh
npx wrangler deploy
```

## Building the game locally

1. Install Unity 6000.3 LTS with WebGL Build Support.
2. Open `unity/` in the editor, or build headless:

```powershell
Unity.exe -batchmode -quit -projectPath unity -executeMethod BuildScript.BuildWebGL
```

The build lands in `dist/`.

## Headless simulation tests

The core simulation can be tested without launching Unity. The suite includes an
accelerated, deterministic playthrough from the starting grid through Nimbus
fulfillment and the persistent mastery state.

```powershell
dotnet test .\Tests\SimTests\Throughput.SimTests.csproj -c Release
```
