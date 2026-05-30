# WebReaper Cloud playground backend

The Tier A live-scrape service: paste a URL, get clean Markdown streamed back
over Server-Sent Events. It is a thin ASP.NET minimal API over the WebReaper
library, deployed separately from the NuGet packages (like `website/`), and is
the seed of WebReaper Cloud's scrape endpoint.

Design: [docs/CLOUD-SCRAPE-PLAYGROUND-PLAN.md](../docs/CLOUD-SCRAPE-PLAYGROUND-PLAN.md)
and the Phase 1 slice [docs/CLOUD-PLAYGROUND-PHASE-1.md](../docs/CLOUD-PLAYGROUND-PHASE-1.md).

## Architecture

```
[playground component, /playground]  --EventSource-->  [Vercel edge route /api/playground/scrape]
                                                          verify Turnstile, per-IP + global rate limit
                                                          proxy SSE through (backend URL stays private,
                                                          inject the shared secret)
                                                              |
                                                              v
                                       [this app, on Fly]  GET /scrape/stream?url=...  -> SSE of ClimbEvent
                                                          require X-Playground-Secret (refuse direct traffic)
                                                          SSRF-guarded handler; concurrency cap; per-job timeout
```

## Run locally

The `#210` client defaults to `http://localhost:5179`, so run on that port:

```bash
ASPNETCORE_URLS=http://localhost:5179 dotnet run --project cloud/WebReaper.PlaygroundApi
# stream a scrape (no gate locally, since no secret is set):
curl -N "http://localhost:5179/scrape/stream?url=https://example.com"
```

With no env set the backend is open (dev mode). It is intentionally NOT in
`WebReaper.sln`, so it stays out of the library CI.

## Deploy (Fly)

Needs a Fly.io account + `flyctl`. Deploy from the **repo root** so the build
context includes the WebReaper library (Fly's context is always the project
root; the Dockerfile copies `WebReaper/`):

```bash
fly launch --no-deploy --config cloud/WebReaper.PlaygroundApi/fly.toml   # first time: set app name + region
fly secrets set PLAYGROUND_BACKEND_SECRET=$(openssl rand -hex 32) \
  --config cloud/WebReaper.PlaygroundApi/fly.toml
fly deploy --config cloud/WebReaper.PlaygroundApi/fly.toml
```

Then set the matching values in the Vercel project (see `website/.env.example`):
`PLAYGROUND_BACKEND_URL` (the Fly app URL), `PLAYGROUND_BACKEND_SECRET` (the same
secret), `TURNSTILE_SECRET_KEY` + `NEXT_PUBLIC_TURNSTILE_SITE_KEY`, and the
Upstash REST vars.

## Environment

| Side | Variable | Purpose |
|---|---|---|
| Fly (backend) | `PLAYGROUND_BACKEND_SECRET` | Shared secret; the backend rejects `/scrape/stream` without a matching `X-Playground-Secret`. Unset => open (dev). |
| Fly (backend) | `PLAYGROUND_ALLOWED_ORIGINS` | Comma-separated CORS allowlist. Unset => any-origin (dev). |
| Vercel (edge) | `PLAYGROUND_BACKEND_URL` | Private Fly URL the edge route proxies to. |
| Vercel (edge) | `PLAYGROUND_BACKEND_SECRET` | Same secret as Fly; injected on every proxied request. |
| Vercel (edge) | `TURNSTILE_SECRET_KEY` | Cloudflare Turnstile server secret. Unset => verification skipped (dev). |
| Vercel (edge) | `UPSTASH_REDIS_REST_URL` / `_TOKEN` | Rate-limit store. Unset => limiting disabled (dev). |

Every variable is a fail-soft seam: unset means "off, dev mode" with a loud
server warning, never a crash. An unconfigured production deploy is therefore
open, so set the secrets before announcing the URL.

## Activation note (client side)

The edge gate is live and self-contained, but it only takes effect once the
playground **client** routes through it. As of `#210` the component points its
`EventSource` straight at the backend (`NEXT_PUBLIC_PLAYGROUND_API`), which
bypasses the gate. To switch the public site onto the gated path, two one-line
client changes belong with that work (kept out of this change to avoid touching
`#210`'s files):

1. Point the `EventSource` at the same-origin route `/api/playground/scrape`
   instead of the public backend origin.
2. Append the Turnstile token as `&cf=<token>` (EventSource is GET-only and
   cannot send headers).

Until then the gate is dormant; the backend secret + CORS allowlist still let
you lock the Fly app down independently.
