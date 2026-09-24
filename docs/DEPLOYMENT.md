# Deploying the Virtual Coffee Machine

This project ships as a Docker image and is hosted on **Render's free tier**.
Everything needed for a one-click deploy is already in the repo: the
multi-stage [`Dockerfile`](../Dockerfile) and the [`render.yaml`](../render.yaml)
Blueprint.

**Contract that must never drift:** the app listens on **port 8080**
(`ASPNETCORE_URLS=http://+:8080` in the Dockerfile) and answers **`GET /healthz`**
with `200` (implemented in `src/CoffeeMachine.Api/Program.cs`). Both values are
baked into `render.yaml` and the Dockerfile `HEALTHCHECK`.

## Prerequisites

- The repo pushed to GitHub with `main` up to date (`git push origin main`).
- A free [Render](https://render.com) account (GitHub sign-in is fastest).

## Option A — Blueprint deploy (recommended, one-click)

The `render.yaml` at the repo root is a Render *Blueprint*: it describes the
service (Docker runtime, port, health check, env vars, free plan) so you never
have to click through the service form.

1. Push everything to GitHub `main`.
2. Render dashboard → **New +** → **Blueprint**.
3. Connect GitHub → select the `virtual-coffee-machine` repo.
4. Render reads `render.yaml` and shows the service it will create — review,
   then **Apply** (a.k.a. *Create Resources*).
5. Wait for the first build (~5–10 min: NuGet restore + Release publish of the
   Blazor WASM bundle). Follow along under the service's **Events/Logs** tab.
6. Grab the live URL from the top of the service page — for this repo it is
   **`https://virtual-coffee-machine.onrender.com`** — and smoke-test it:

   ```bash
   curl -i https://<your-app>.onrender.com/healthz   # expect HTTP/2 200
   ```

   The **Swagger UI is live in production** at `https://<your-app>.onrender.com/swagger`
   — the API is deliberately left explorable so reviewers can try the REST
   endpoints on the hosted demo without cloning the repo.

No manual configuration is needed beyond clicking Apply: the Dockerfile bakes
in the port (`ASPNETCORE_URLS=http://+:8080`) **and** the SQLite path
(`DB_PATH=/data/coffeemachine.db`, a writable dir owned by the container's
non-root `app` user). You only touch env vars if you deliberately want to
override them.

What `render.yaml` pins down (in case you edit it later):

| Field | Value | Why |
|---|---|---|
| `type` / `runtime` | `web` / `docker` | Builds `./Dockerfile`; no native .NET toolchain on Render needed |
| `branch` | `main` | Matches the CI/CD flow; PRs never auto-deploy |
| `region` / `plan` | `oregon` / `free` | Free tier, US-West |
| `autoDeploy` | `true` | Every push to `main` triggers a rebuild + deploy |
| `pullRequestPreviewsEnabled` | `false` | Keeps the single free service slot |
| `healthCheckPath` | `/healthz` | Render considers the deploy live only after this returns 200 |
| `PORT` | `8080` | Pins the Docker service's listener port to match `ASPNETCORE_URLS` |
| `ASPNETCORE_ENVIRONMENT` | `Production` | Production logging/config behavior |
| `Logging__LogLevel__Microsoft.EntityFrameworkCore` | `Warning` | Keeps EF Core's per-request SQL chatter out of the Render logs |

Environment variables set by the **Dockerfile** (so they need no Blueprint entry):

| Variable | Value | Why |
|---|---|---|
| `ASPNETCORE_URLS` | `http://+:8080` | The canonical listener; matches `PORT` above |
| `DB_PATH` | `/data/coffeemachine.db` | Writable dir owned by the non-root `app` user — the app would fail with SQLite error 14 if it defaulted to `/app/coffeemachine.db` |

## Option B — Manual Web Service (fallback, no Blueprint)

Use this only if you can't (or don't want to) use the Blueprint.

1. Render dashboard → **New +** → **Web Service**.
2. Connect the repo; choose runtime **Docker** → *Build from Dockerfile*
   (path `./Dockerfile` — it is at the repo root).
3. Set the fields that the Blueprint would have set for you:
   - **Branch:** `main`
   - **Instance type:** `Free`
   - **Region:** `Oregon`
   - **Health Check Path:** `/healthz`
   - **Environment variables:**
     - `PORT` = `8080` *(critical — the container only listens on 8080; without this Render's default $PORT is 10000 and the health check will fail)*
     - `ASPNETCORE_ENVIRONMENT` = `Production`
     - *(No `DB_PATH` needed — the Dockerfile already sets it to a writable
       `/data` location. Setting it by hand to something under `/app` would
       break startup for the non-root `app` user.)*
   - **Auto-Deploy:** `Yes`
4. **Create Web Service** and wait out the first build as in Option A.

Verify locally before deploying if anything is misbehaving:

```bash
docker build -t coffee-machine .
docker run --rm -p 8080:8080 coffee-machine
# in a second terminal:
curl -i http://localhost:8080/healthz   # expect 200
```

## Free-tier notes (read before sharing the link)

- **Sleep / cold start.** With no inbound HTTP traffic the service sleeps after
  ~15 minutes. The next request cold-starts it: expect **~30–90 s** before the
  first paint (container boot + .NET + WASM download).
- **Keep-warm.** Register the live URL with a free [UptimeRobot](https://uptimerobot.com)
  monitor (or any cron job) pinging every 5–10 min (the root page or `/healthz`
  both count). That keeps the app awake 24/7.
- **Hero GIF in the README.** The demo GIF is the fallback for the cold-start
  gap — reviewers see the UX instantly even if the instance is asleep.
- **Ephemeral disk.** The SQLite ledger/state database lives on the container's
  filesystem (`/data/coffeemachine.db`) and is **ephemeral on the free plan**:
  it resets on every redeploy **and after each sleep/wake cycle**, so visitor
  machines and the ledger start fresh (by design — each visitor gets a fresh
  machine anyway). Don't treat hosted data as durable.

## Tear down / redeploy

- **Redeploy:** push to `main` (auto-deploy handles it), or dashboard → service
  → **Manual Deploy** → *Deploy latest commit*.
- **Rollback:** **Manual Deploy** → pick a previous commit from the list.
- **Pause:** service → **Settings** → *Suspend Service* (frees the slot, keeps config).
- **Delete:** service → **Settings** → *Danger Zone* → *Delete Service*; then
  also delete the entry under **Blueprints** and (optionally) revoke Render's
  access in GitHub → Settings → Applications.
- **Recreate:** just re-run Option A — the Blueprint file stays in the repo.

## Troubleshooting (quick hits)

- **Health check never passes:** confirm the app returns 200 on `/healthz`
  locally (see Option B docker command); confirm `PORT=8080` is set.
- **Build fails on Render:** open the service's **Logs** during build — most
  failures are NuGet restore/network hiccups; **Manual Deploy → Clear build
  cache & deploy** usually fixes stale-layer issues.
- **Requests time out during cold start:** normal on the free tier; retry
  after ~60 s or set up the keep-warm ping.
