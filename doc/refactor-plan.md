# Plan: Server/Client Refactor with TUI Media Player (BluKube)

Refactor the project (working title `yt-cli-radio`, rebranded to **BluKube**) from a single-process Docker container into a **server/client** application. The server (Docker image) owns Brave + Xvfb + Playwright + audio routing and exposes a REST + SSE API. A lightweight self-contained CLI binary runs on the host and presents a Spectre.Console TUI media player with full transport controls (play/pause/stop/next/prev/seek ±10 s/volume). Multiple concurrent playback **sessions** are supported. Auth is a shared-secret token; bind defaults to loopback but is configurable for LAN remote control. .NET Aspire orchestrates dev runs, observability, and Docker Compose publishing.

## Phases

### Phase 0 — Preserve the PoC
1. `git mv` the existing implementation under a top-level `poc/` directory: `src/YtCliRadio` → `poc/src/YtCliRadio`, `tests/YtCliRadio.Tests` → `poc/tests/YtCliRadio.Tests`, `YtCliRadio.slnx` → `poc/YtCliRadio.slnx`, `Dockerfile` → `poc/Dockerfile`. The PoC remains buildable in isolation (`dotnet build poc/YtCliRadio.slnx`) for the duration of the refactor.
2. Leave `README.md` at the repo root but add a top banner: “**Status:** mid-refactor. The legacy PoC lives under [`poc/`](poc/); the new `BluKube` server/client implementation is under construction in `src/`.”
3. The new `BluKube.*` projects are created **alongside** the PoC, not on top of it. Both can coexist on `main` until the new implementation reaches feature parity.
4. Tracking: a `poc/MIGRATION-CHECKLIST.md` lists every behavioural feature the new implementation must reproduce (search, queue, play/pause, autoplay-next, dry-run, Docker audio routing, Xvfb wrapping, consent-cookie handling, etc.). Items get ticked off as they land in the new code; `poc/` is **only** deleted once every item is checked.

*Phase 0 is a single mechanical commit and a checklist file. Nothing else depends on it beyond “happens first.”*

### Phase 1 — Rebrand, new solution, Aspire scaffold

**No shared contracts/DTO library.** The server is the single source of truth for the API surface; it exposes an OpenAPI document (and Swagger UI in dev) via `Microsoft.AspNetCore.OpenApi`. The CLI gets its own DTOs and typed client, **generated from the server's OpenAPI document** via NSwag (`NSwag.MSBuild` build target on the CLI project, output to `src/BluKube.Cli/Generated/`). Generated files are committed so the CLI builds without a running server. SSE event payload shapes are documented alongside the OpenAPI spec and either generated or hand-written in the CLI as small records — they do **not** live in a shared package.

1. **Rebrand**: choose new namespaces, assembly names, project files, solution file, Docker image tag (`BluKube`). The legacy PoC stays untouched under `poc/`.
2. Create `src/BluKube.Server` from scratch as a fresh ASP.NET Core project. **Port** browser/playback logic from `poc/src/YtCliRadio/Browser/*` and `Configuration/AppOptions.cs` into the new structure (don't move — copy and adapt). Server owns its own internal DTO types under `BluKube.Server.Api.Contracts` (not a shared assembly).
3. Create `src/BluKube.Cli` — new console project (Spectre.Console + `System.Net.Http.Json` + `Microsoft.Extensions.ServiceDiscovery`). NSwag MSBuild target generates a typed client + DTOs from the server's `openapi.json` into `Generated/`. Self-contained publish profile for `linux-x64` (later `linux-arm64`, `osx-arm64`, `win-x64`).
4. **Aspire scaffold**:
   - Add `src/BluKube.AppHost` (Aspire app host project, `Aspire.Hosting.AppHost` SDK). Wires up the server as a project resource (with the server's container image for non-dev runs), surfaces the `BLUKUBE_TOKEN` and bind config as resource environment variables, exposes the HTTP endpoint, and (optionally) wires a Brave/Xvfb container probe.
   - Add `src/BluKube.ServiceDefaults` (Aspire shared service defaults: OpenTelemetry tracing/metrics/logs, health checks, service discovery). Referenced by the **server only**; the CLI is a host-side binary, not part of the orchestrated topology, so it doesn't need Aspire defaults.
   - Server registers `builder.AddServiceDefaults()` and `app.MapDefaultEndpoints()` (`/health`, `/alive`).
5. Add a new **`BluKube.slnx`** at the repo root referencing only the new projects (`BluKube.Server`, `BluKube.Cli`, `BluKube.AppHost`, `BluKube.ServiceDefaults`, `BluKube.Server.Tests`, `BluKube.Cli.Tests`, `BluKube.AppHost.Tests`). The legacy `poc/YtCliRadio.slnx` continues to exist for the PoC.
6. Add a new `Dockerfile` at the repo root for the new server image; the legacy `poc/Dockerfile` stays for reference.
7. Add `scripts/regen-cli-client.sh` — convenience wrapper that runs the server briefly to dump a fresh `openapi.json` (or invokes `dotnet run --project src/BluKube.Server -- --emit-openapi <path>`) and triggers the NSwag regeneration.

*Parallel after step 1+2: 3, 4, 5, 6, 7 can advance independently.*

### Phase 2 — Server: extract player engine, add ASP.NET Core host
1. Promote `BraveYouTubeBrowserClient` into a session-scoped `BravePlayerSession` owning one Brave/Xvfb instance + queue + current index + last-known snapshot.
2. Add `IPlayerSession` abstraction (`Search`, `EnqueueResults`, `Play(index)`, `Pause`, `Resume`, `Stop`, `Next`, `Prev`, `SeekRelative(seconds)`, `SetVolume`, `GetState`, `StateChanges` via `IAsyncEnumerable<PlayerEvent>` or `Channel<>`).
3. Add `ISessionManager` — creates/lists/destroys sessions, enforces `MaxSessions` cap (configurable, default 3 — Brave is RAM-heavy), tracks idle timeout for auto-cleanup (configurable).
4. Replace `Program.cs` with ASP.NET Core minimal API host (`WebApplication.CreateBuilder`). Register `ISessionManager` as singleton. Add health endpoint `GET /healthz` (in addition to Aspire's `/health` + `/alive`).
5. Implement endpoints under `/v1/sessions`:
   - `POST /v1/sessions` → create, returns `{ id }`
   - `GET /v1/sessions` → list
   - `DELETE /v1/sessions/{id}` → close (also disposes Brave)
   - `POST /v1/sessions/{id}/search` `{ query, limit }` → `VideoSearchResultDto[]`
   - `POST /v1/sessions/{id}/queue` `{ items }` (replace) / `PATCH` (append/insert)
   - `GET /v1/sessions/{id}/queue`
   - `POST /v1/sessions/{id}/play` `{ index? }`
   - `POST /v1/sessions/{id}/pause`
   - `POST /v1/sessions/{id}/resume`
   - `POST /v1/sessions/{id}/stop`
   - `POST /v1/sessions/{id}/next`, `/prev`
   - `POST /v1/sessions/{id}/seek` `{ deltaSeconds }` (and `{ positionSeconds }` for absolute)
   - `POST /v1/sessions/{id}/volume` `{ value 0..1 }`
   - `GET /v1/sessions/{id}/state` → snapshot
   - `GET /v1/sessions/{id}/events` → **SSE stream** of `PlayerEvent` (state-changed, position-tick, track-ended, error, queue-changed)
6. Position-tick poller: a per-session background `Task` polling Playwright `video.currentTime` every ~500–1000 ms while playing and pushing `position-tick` events into the channel. Pauses polling when paused/stopped.
7. Implement seek/volume in `BravePlayerSession` via JS evaluation on the `<video>` element (`video.currentTime += delta`, `video.volume = v`).
8. Bearer-token auth middleware: token from `BLUKUBE_TOKEN` env var (or `--token` flag); reject all `/v1/*` requests without matching `Authorization: Bearer …` header. Health endpoints stay open.
9. Bind config: `BLUKUBE_BIND` (default `127.0.0.1:8765`); document LAN exposure caveats. Generate token at first start if none provided and print to stdout/stderr (compose-friendly).
10. CORS: locked off by default; opt-in via env var.

*Steps 2-3 block 4-7. Step 8 parallel with 5-7.*

### Phase 3 — CLI: REST/SSE client & Spectre.Console TUI
1. `IServerClient` typed `HttpClient` wrapper covering all endpoints. Token + base URL from `BLUKUBE_SERVER_URL` / `BLUKUBE_TOKEN` (also `--server` / `--token` flags) and a config file at `$XDG_CONFIG_HOME/blukube/config.toml`.
2. SSE consumer: `IAsyncEnumerable<PlayerEvent> SubscribeAsync(sessionId, ct)` using `HttpClient` + manual line-parser (avoid extra deps).
3. Top-level commands using `System.CommandLine` or hand-rolled parser:
   - `blukube play [query]` — interactive: pick session (or auto-create), search, pick track, drop into TUI.
   - `blukube attach [--session <id>]` — drop into TUI for an existing session.
   - `blukube sessions list|new|close <id>` — non-interactive session management.
   - `blukube status` — one-shot snapshot, scriptable.
   - `blukube config set/get` — manage server URL + token.
4. **Login flow**: on any 401 (or missing stored token) the CLI prompts interactively (Spectre `TextPrompt<string>().Secret()`), validates by calling a lightweight authenticated endpoint, stores the token in the config file, and re-prompts on failure. **No proceed without successful login.**
5. **TUI media player** (Spectre.Console `Live` rendering):
   - Header: track title + channel + duration.
   - Progress bar with current / total time, updated from SSE `position-tick`.
   - Status line: state (Playing/Paused/Stopped), volume, queue position (e.g. `3/8`), session ID footer.
   - Queue panel (collapsible): upcoming items.
   - Footer keybindings.
   - Keybindings: `Space` play/pause, `S` stop, `←`/`→` seek -10/+10 s, `Shift+←`/`Shift+→` seek -30/+30 s, `N`/`P` next/prev, `+`/`-` volume, `R` new search, `D` detach (exit TUI, leave server playing), `Q` quit (close session), `?` help overlay.
   - Render loop: merge SSE event stream + keypress task with `Channel<>`; debounce redraws to ~10 fps.
6. Error UX: connection-lost banner with auto-reconnect (exponential backoff) on SSE drop; clear messages for 401/404/409.

*Steps 1-2 block 3-6. TUI (5) is the bulk.*

### Phase 4 — Docker, Aspire orchestration, packaging, distribution
1. Update `Dockerfile`: switch base from `dotnet/runtime:10.0` to `dotnet/aspnet:10.0` for the server stage; expose `8765`; default `ENTRYPOINT` runs the server. Server's `Dockerfile` becomes the artifact Aspire's container resource references.
2. **Aspire deployment via `aspire publish`**: configure the AppHost so `aspire publish --publisher docker-compose` (or the equivalent CLI verb in current Aspire) emits a `docker-compose.yml` that includes the server container, port mapping, the data volume for the token, and the host PulseAudio mounts (declared as resource bindings/volumes on the AppHost). This becomes the canonical compose file shipped in the repo.
3. Token data volume declared in the AppHost as a named volume mounted at `/var/lib/blukube` so token persistence works both under Aspire dev runs and under the published compose.
4. Publish CLI as self-contained single-file binary; add a `dotnet publish` invocation to `scripts/publish-cli.sh`.
5. CI/release (out of scope to wire up now, but plan):
   - GitHub Actions workflow building & pushing server image to GHCR.
   - Same workflow producing CLI binaries as release artifacts (`linux-x64` first; `linux-arm64`, `osx-arm64`, `win-x64` follow).
   - Optional: run `aspire publish` in CI to regenerate the committed compose file deterministically.
6. Update `README.md`: split "Server" and "CLI" sections; document **two run modes**: (a) dev/local via `dotnet run --project src/BluKube.AppHost` (Aspire dashboard with logs, traces, metrics), (b) production via `docker compose up -d` using the Aspire-generated compose file.

*Step 1 parallel with Phase 2. Steps 2-4 after Phase 2 + 3 stabilize.*

### Phase 5 — Testing, hardening, cleanup
1. Server tests: `WebApplicationFactory`-based integration tests with a fake `IPlayerSession` (no real Brave); cover auth, session lifecycle, SSE event delivery.
2. **Aspire orchestration tests** (`BluKube.AppHost.Tests`) using `Aspire.Hosting.Testing.DistributedApplicationTestingBuilder` — boots the AppHost, asserts the server resource reaches `Healthy`, verifies the data-volume token round-trip, and exercises a smoke endpoint. Tagged so CI can run them on machines with Docker available.
3. Player session unit tests for queue/index transitions and seek bounds (no Brave).
4. Optional: a `[Trait("Category","Browser")]` end-to-end test against a real Brave (skipped in CI).
5. CLI tests: render snapshot of TUI components against canned `PlayerStateDto` sequences (Spectre.Console test console); login-prompt flow tested with a stubbed transport returning 401 then 200.
6. **Retire the PoC**: once every item in `poc/MIGRATION-CHECKLIST.md` is checked and all manual smoke tests in *Verification* pass, delete the `poc/` directory and the checklist file in a single dedicated commit. Until then, `poc/` stays buildable on `main`.
7. Cancellation discipline: every endpoint passes `HttpContext.RequestAborted`; sessions own their own `CancellationTokenSource` independent of any one request.
8. Resource cleanup: graceful shutdown drains all sessions; SIGTERM in container disposes Brave/Xvfb cleanly.
9. **Observability sanity-check**: with the AppHost running, confirm OpenTelemetry traces show a span per REST endpoint and that SSE emissions are visible in the Aspire dashboard logs panel.

## Reference files in the PoC (post-Phase-0 paths)

These files live under `poc/` after Phase 0 and serve as the **reference implementation** for the new code. Nothing is moved out of `poc/`; the new code re-implements (or copy-and-adapts) these behaviours and the PoC tree is deleted at the very end.

- [poc/Dockerfile](../poc/Dockerfile) — base + apt deps + Brave install recipe; the new root `Dockerfile` re-uses these layers but switches the runtime base to `dotnet/aspnet:10.0` and changes the entrypoint to the server.
- `poc/src/YtCliRadio/Program.cs` — replaced by a `WebApplication` host in `src/BluKube.Server`.
- `poc/src/YtCliRadio/App/CliApplication.cs` — interactive logic informs the **CLI** TUI; the queue/index action loop is the reference for both the server-side `BravePlayerSession` and the client-side TUI state machine.
- `poc/src/YtCliRadio/Browser/BraveYouTubeBrowserClient.cs` — engine reference for `BravePlayerSession`; existing methods (`SearchAsync`, `StartPlaybackAsync`, `PauseAsync`, `ResumeAsync`, `IsPausedAsync`, `IsTrackEndedAsync`, `TryEnsurePlayingAsync`) port over; **add** `SeekRelativeAsync`, `SeekToAsync`, `SetVolumeAsync`, `GetSnapshotAsync` (extend existing `PlaybackSnapshot`), `StopAsync` (close tab or `video.pause(); video.currentTime = 0`).
- `poc/src/YtCliRadio/Browser/XvfbDisplay.cs` — port unchanged; one Xvfb per session.
- `poc/src/YtCliRadio/Browser/BravePathResolver.cs` — port unchanged.
- `poc/src/YtCliRadio/Configuration/AppOptions.cs` — split into server-side options (bind, token, max sessions, idle timeout, brave path) and client-side options (server URL, token, default session behaviour). Neither inherits from this type directly.
- `poc/src/YtCliRadio/Domain/VideoSearchResult.cs` — re-modelled inside `BluKube.Server`; the CLI's equivalent type is NSwag-generated from OpenAPI, not shared.
- [README.md](../README.md) — full rewrite of run instructions for the server/client model after migration completes.
- `poc/tests/YtCliRadio.Tests/CliApplicationTests.cs` — superseded by server integration tests + CLI TUI tests; deleted with the rest of `poc/` at the end.

## New files created during the refactor

- `src/BluKube.Server/*`, `src/BluKube.Cli/*` (with `Generated/` for the NSwag output), `src/BluKube.AppHost/*`, `src/BluKube.ServiceDefaults/*`
- `tests/BluKube.Server.Tests/*`, `tests/BluKube.Cli.Tests/*`, `tests/BluKube.AppHost.Tests/*`
- New root `BluKube.slnx`, new root `Dockerfile`, generated `docker-compose.yml`
- `scripts/publish-cli.sh`, `scripts/regen-cli-client.sh`
- `poc/MIGRATION-CHECKLIST.md` (created in Phase 0; deleted with `poc/` at the end)

## Verification

1. **Server unit/integration**: `dotnet test tests/BluKube.Server.Tests` — auth (401 without token, 200 with), full session lifecycle, SSE `position-tick` is delivered when playback is mocked-progressing, `MaxSessions` enforcement (409).
2. **Aspire orchestration**: `dotnet test tests/BluKube.AppHost.Tests` — server resource reaches `Healthy`, token persists across an AppHost restart via the data volume.
3. **CLI tests**: `dotnet test tests/BluKube.Cli.Tests` — TUI snapshot rendering for canned state sequences, REST client error mapping, SSE reconnect on drop, login re-prompt on 401.
4. **Manual smoke (host)**: `docker compose up -d` then `blukube play "lofi hip hop"` → login prompt (first run) → search list appears → pick track → audio plays from host speakers → `Space` pauses (audio stops within 1 s) → `→` advances 10 s (progress bar jumps) → `D` exits TUI → `blukube attach` re-enters TUI with audio still playing → `Q` closes session and stops audio.
5. **Manual smoke (concurrency)**: open two terminals, `blukube sessions new` twice, play different tracks in each; both audio streams audible; `docker stats` shows memory grows roughly linearly per session.
6. **Auth/bind verification**: `curl -i http://127.0.0.1:8765/v1/sessions` → 401; `curl -i -H "Authorization: Bearer $BLUKUBE_TOKEN" …` → 200. With default bind, request from another host fails to connect.
7. **Graceful shutdown**: `docker compose down` — server logs show all sessions disposed; no orphan Brave or Xvfb processes inside the container (`docker exec … ps`).
8. **Backwards-compat sanity**: existing `--dry-run` behaviour reproduced via `blukube play --dry-run "synthwave"` (search-only, no session created).

## Decisions captured from interview

- **Brand name**: `BluKube` (rhymes with YouTube, implies simplicity). Bare brand as root namespace (`BluKube.Server`, `BluKube.Cli`).
- **No shared contracts assembly**: server publishes OpenAPI; CLI DTOs + typed client are NSwag-generated and committed. Avoids version-coupling and keeps the client surface honest about being an external consumer.
- **Transport**: REST for commands + SSE for state stream.
- **Sessions**: multiple concurrent supported; one Brave instance per session; one Xvfb per session; capped via `MaxSessions`.
- **CLI distribution**: self-contained single-file binary; `linux-x64` first, others to follow.
- **Controls**: Play, Pause, Stop, Next, Prev, Seek ±10 s (and ±30 s with Shift), Volume.
- **Auth**: Bearer token + bind to `127.0.0.1` by default; LAN exposure opt-in via env var.
- **Deployment**: published Docker image for the server + binary release for the CLI; `docker-compose.yml` (Aspire-generated) provided in the repo for users who clone.
- **Orchestration & observability**: .NET Aspire AppHost for dev runs (dashboard with logs/traces/metrics) and `aspire publish` for the production compose file.
- **Auto-create session on `blukube play`**: yes. Session ID surfaced in TUI footer so the user can `attach` later.
- **Token bootstrap**:
  - Server auto-generates token on first startup if none provided via env var; persists in the container's data volume (`/var/lib/blukube/token`). Token survives container restarts via the Docker volume.
  - Server prints the token to stdout/stderr on every startup (visible in `docker compose logs`).
  - CLI does **not** read the token from any shared file. On first connection (or any 401) the CLI prompts the user interactively (Spectre password prompt) and stores it in `$XDG_CONFIG_HOME/blukube/config.toml`. Cannot proceed without successful login. `blukube config set token` is also available for non-interactive setup.
- **Out of scope (this refactor)**: persistent queues across restarts, web UI, mobile remote, multi-user accounts, HTTPS termination (operator's responsibility behind a reverse proxy if exposed).
