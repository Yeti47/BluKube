# Plan: Server/Client Refactor with TUI Media Player (BluKube)

Refactor the project (working title `yt-cli-radio`, rebranded to **BluKube**) from a single-process Docker container into a **server/client** application. The server (Docker image) owns Brave + Xvfb + Playwright and exposes a **SignalR hub** for real-time interaction. Audio is captured from Brave inside the container and streamed to the TUI for client-side playback. A lightweight self-contained TUI binary runs on the host and presents a Spectre.Console TUI media player with full transport controls (play/pause/stop/next/prev/seek ±10 s/volume). Multiple concurrent playback **sessions** are supported. Auth is a shared-secret token; bind defaults to loopback but is configurable for LAN remote control.

Development is **Docker-first**: the server always runs in a container. VS Code attaches the debugger to the running container. The TUI runs natively on the host.

## Phases

### Phase 0 — Preserve the PoC *(done)*
1. `git mv` the existing implementation under a top-level `poc/` directory.
2. Leave `README.md` at the repo root with mid-refactor status banner.
3. Both `poc/` and new `src/` coexist on `main` until feature parity.
4. `poc/MIGRATION-CHECKLIST.md` tracks every behavioural feature to reproduce.

### Phase 1 — Rebrand, new solution, docker-first scaffold
1. **Rebrand**: namespaces, assemblies, solution file, Docker image tag (`BluKube`).
2. Create `src/BluKube.Server` — fresh ASP.NET Core project. Core abstractions under `Core/Search/` and `Core/Playback/`.
3. Create `src/BluKube.Tui` — console project (Spectre.Console + SignalR client).
4. Add `BluKube.slnx` at repo root (Server, Tui, Server.Tests, Tui.Tests).
5. Root `Dockerfile` (multi-stage, `dotnet/aspnet:10.0` base, Brave+Xvfb+audio libs).
6. `docker-compose.yml` — single service, port 8765, token volume.
7. Server `Program.cs` — plain ASP.NET Core with `/health` `/alive` endpoints. No Aspire.

### Phase 2 — Core abstractions & browser engine
1. **Domain types** under `Core/Search/`: `MediaItem`, `IMediaSearch`.
2. **Domain types** under `Core/Playback/`: `PlayerState`, `PlayerEvent` (discriminated union via inheritance), `IMediaPlayer`, `IPlayerSession`, `ISessionManager`.
3. `IMediaPlayer` — low-level: Play, Pause, Resume, Stop, SeekRelative, SeekTo, SetVolume, GetState, `IAsyncEnumerable<PlayerEvent> Events(ct)`.
4. `IPlayerSession` — session-scoped: wraps `IMediaPlayer` + queue management (Enqueue, Insert, Remove, index tracking, auto-advance). Exposes same transport ops + `IAsyncEnumerable<PlayerEvent> Events(ct)`.
5. `ISessionManager` — creates/lists/destroys sessions. Enforces `MaxSessions` cap (default 3). Tracks idle timeout for auto-cleanup.
6. Port browser infrastructure from PoC: `XvfbDisplay`, `BravePathResolver`.
7. Implement `BraveMediaPlayer : IMediaPlayer, IMediaSearch` — wraps Playwright/Brave. Both interfaces implemented on the same class via composition of the underlying browser (they share the same Brave/Playwright instance but are separate concerns).
8. Implement `PlayerSession : IPlayerSession` — wraps `IMediaPlayer` + queue list. Position-tick poller (background task polling `video.currentTime` every ~500ms). Auto-advance on track end.
9. Implement `SessionManager : ISessionManager` — concurrent dictionary registry with cap enforcement.

### Phase 3 — Server: SignalR hub + REST endpoints
1. **SignalR `SessionHub`** — the primary interactive surface. Hub methods map 1:1 to `IPlayerSession` operations:
   - `CreateSession()` → `{ sessionId }`
   - `JoinSession(sessionId)` → joins a SignalR group; caller receives `PlayerEvent` stream
   - `LeaveSession(sessionId)`
   - `Search(sessionId, query, limit)` → `MediaItem[]`
   - `Play(sessionId, index?)`, `Pause`, `Resume`, `Stop`, `Next`, `Prev`
   - `SeekRelative(sessionId, deltaSeconds)`, `SeekTo(sessionId, seconds)`
   - `SetVolume(sessionId, volume)`
   - `GetState(sessionId)` → `PlayerState`
   - `SetQueue(sessionId, items, replace)`, `GetQueue(sessionId)` → `MediaItem[]`
2. **Event streaming**: hub method `JoinSession` subscribes the caller to `IPlayerSession.Events(ct)` and forwards each event via `Clients.Caller.SendAsync("Event", event)`. The SignalR group = the session.
3. **Thin REST endpoints** for one-shot non-interactive operations:
   - `GET /v1/sessions` — list sessions
   - `POST /v1/sessions` — create session
   - `DELETE /v1/sessions/{id}` — close session
   - `GET /v1/sessions/{id}/state` — snapshot (scriptable)
4. Bearer-token auth middleware: token from `BLUKUBE_TOKEN` env var. Applied to both SignalR hub and REST `/v1/*`. Health endpoints open.
5. Bind config: `BLUKUBE_BIND` (default `127.0.0.1:8765`). Generate token at first start if none provided, persist in `/var/lib/blukube/token`.
6. CORS: locked off by default; opt-in via env var.

### Phase 4 — TUI: SignalR client & Spectre.Console TUI
1. SignalR client using `Microsoft.AspNetCore.SignalR.Client`. Connection lifecycle: connect → login → create/join session.
2. Top-level commands:
   - `blukube play [query]` — pick/auto-create session, search, pick track, drop into TUI.
   - `blukube attach [--session <id>]` — reattach TUI for existing session.
   - `blukube sessions list|new|close <id>` — REST calls to thin endpoints.
   - `blukube status` — one-shot snapshot via REST.
   - `blukube config set/get` — manage server URL + token.
3. Login flow: on connect failure (401) TUI prompts for token via Spectre password prompt, stores in `$XDG_CONFIG_HOME/blukube/config.toml`.
4. TUI media player (Spectre.Console `Live` rendering):
   - Header: track title + channel + duration.
   - Progress bar (current / total time) updated from `PlayerEvent.PositionTick`.
   - Status line: state, volume, queue position, session ID.
   - Queue panel (collapsible): upcoming items.
   - Footer keybindings.
   - Keybindings: `Space` play/pause, `S` stop, `←`/`→` seek ±10 s, `Shift+←`/`Shift+→` seek ±30 s, `N`/`P` next/prev, `+`/`-` volume, `R` new search, `D` detach, `Q` quit, `?` help.
   - Render loop: merge SignalR event stream + keypress task; debounce to ~10 fps.
5. SignalR auto-reconnect with exponential backoff (SignalR client built-in).

### Phase 5 — Docker, packaging, distribution
1. Root `Dockerfile`: multi-stage, `dotnet/aspnet:10.0` runtime, Brave+Xvfb+audio libs. HEALTHCHECK on `/health`.
2. `docker-compose.yml`: canonical dev+prod setup (no PulseAudio host mounts — audio is client-side).
3. Token data volume `/var/lib/blukube` for persistence across container restarts.
4. TUI: self-contained single-file binary via `dotnet publish`; `scripts/publish-tui.sh`.
5. CI/release (future):
   - GitHub Actions: build & push server image to GHCR.
   - Same workflow: produce TUI binaries (`linux-x64` first; `linux-arm64`, `osx-arm64`, `win-x64` later).
6. README: document server (docker compose) and TUI (native binary) run modes.

### Phase 6 — Testing, hardening, cleanup
1. Server tests: integration tests with fake `IPlayerSession` + `WebApplicationFactory`. SignalR hub tests via `Microsoft.AspNetCore.SignalR.Client` in test.
2. Player session unit tests: queue transitions, index bounds, seek bounds.
3. TUI tests: TUI snapshot rendering, SignalR reconnect, login re-prompt.
4. Cancellation discipline: sessions own `CancellationTokenSource`; hub methods pass `Context.ConnectionAborted`.
5. Graceful shutdown: SIGTERM drains all sessions, disposes Brave/Xvfb.
6. **Retire the PoC**: once `MIGRATION-CHECKLIST.md` is fully checked and smoke tests pass, delete `poc/`.

## Architecture summary

```
┌─────────────────────────────────────────────┐
│  Docker container (blukube)                 │
│  ┌───────────────────────────────────────┐  │
│  │  BluKube.Server (ASP.NET Core)        │  │
│  │  ┌─────────────────────────────────┐  │  │
│  │  │  SessionHub (SignalR)           │  │  │
│  │  │  REST /v1/* (thin endpoints)    │  │  │
│  │  │  ISessionManager                │  │  │
│  │  │    └─ PlayerSession             │  │  │
│  │  │         └─ BraveMediaPlayer     │  │  │
│  │  │              ├─ IMediaPlayer    │  │  │
│  │  │              ├─ IMediaSearch    │  │  │
│  │  │              ├─ XvfbDisplay     │  │  │
│  │  │              └─ Brave/Playwright│  │  │
│  │  └─────────────────────────────────┘  │  │
│  └───────────────────────────────────────┘  │
│              port 8765                        │
└─────────────────────────────────────────────┘
         │ SignalR (WebSocket)
         │ REST (one-shots)
         ▼
┌─────────────────────────────────────────────┐
│  Host machine                               │
│  ┌───────────────────────────────────────┐  │
│  │  BluKube.Tui (self-contained binary)  │  │
│  │  ┌─────────────────────────────────┐  │  │
│  │  │  Spectre.Console TUI            │  │  │
│  │  │  SignalR client connection      │  │  │
│  │  │  Config: $XDG_CONFIG_HOME       │  │  │
│  │  └─────────────────────────────────┘  │  │
│  └───────────────────────────────────────┘  │
└─────────────────────────────────────────────┘
```

## Decisions captured

- **Brand name**: `BluKube`. Root namespace: `BluKube.Server`, `BluKube.Tui`.
- **No shared contracts assembly**: server owns types; TUI references server types via SignalR (no NSwag needed for interactive path). Thin REST endpoints may use NSwag for one-shot ops.
- **Transport**: SignalR hub for interactive commands + event streaming. REST for one-shot status/list operations.
- **Events**: `IAsyncEnumerable<PlayerEvent>` backed by `Channel<T>` on the server, streamed via SignalR to the TUI.
- **Audio**: Client-side. Server captures Brave audio (ffmpeg + PulseAudio virtual sink), streams to TUI for local playback. No host audio mounts in Docker.
- **Sessions**: multiple concurrent; one Brave + Xvfb per session; capped via `MaxSessions`.
- **Development**: Docker-first. Server always runs in container. VS Code attaches debugger to container. TUI runs on host.
- **TUI distribution**: self-contained single-file binary; `linux-x64` first.
- **Controls**: Play, Pause, Stop, Next, Prev, Seek ±10 s / ±30 s (Shift), Volume.
- **Auth**: Bearer token + bind to `127.0.0.1` by default; LAN exposure opt-in.
- **Deployment**: Docker image for server + binary release for TUI; `docker-compose.yml` in repo.
- **Token bootstrap**: Server auto-generates token on first startup, persists in `/var/lib/blukube/token`. TUI prompts on first connect/401, stores in `$XDG_CONFIG_HOME/blukube/config.toml`.
- **Abstractions**: Feature-based (`Core/Search/`, `Core/Playback/`). `IMediaPlayer` and `IMediaSearch` are separate interfaces — implementations compose both.
- **Duration**: `TimeSpan` (non-nullable) on `MediaItem`.
- **Out of scope**: persistent queues across restarts, web UI, mobile remote, multi-user accounts, HTTPS.
