# Throughput: The Inference Grid

A resource-management sim about architecting an enterprise AI-inference network.
Route torrents of request packets through routers, load balancers, caches, and model
servers — balancing compute cycles, API-token spend, bandwidth, and latency SLAs.
Manual routing gives way to Infrastructure-as-Code automation, and finally to a fully
autonomous, self-healing grid.

Built with **Unity (WebGL)**. Deployed as a **Cloudflare Worker** with static assets.

## Play

https://throughput-the-inference-grid.ericrolph.workers.dev

## Repo layout

| Path | Purpose |
| --- | --- |
| `unity/` | Unity project (Unity 6000.3 LTS, 2D URP) |
| `dist/` | Committed WebGL build output — served by the Worker |
| `wrangler.jsonc` | Cloudflare Worker config (static assets from `dist/`) |
| `.github/workflows/deploy.yml` | Deploys `dist/` to Cloudflare on every push to `main` |
| `docs/` | Game design document & implementation plan |

## Deploy pipeline

Every push to `main` runs the GitHub Actions workflow, which publishes `dist/` to
Cloudflare Workers via `wrangler-action`. The Unity WebGL build is produced locally
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
