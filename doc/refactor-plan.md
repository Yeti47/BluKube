# BluKube Refactor Status

The original `yt-cli-radio` proof of concept has been retired. BluKube is now a server/client application: the Docker server owns Brave, Xvfb, PulseAudio capture, and Playwright automation; the native TUI connects over SignalR, streams Opus audio to local playback, and renders the current session state.

Development is Docker-first for the server and native for the TUI. The canonical local flow is `docker compose up -d` for `BluKube.Server`, then `./publish/tui/blukube play ...` for the client.

## Current Shape

### Server

1. `src/BluKube.Server` is an ASP.NET Core app with `/health`, `/alive`, REST session endpoints, and SignalR hub `/hubs/session`.
2. `SessionManager` owns active sessions, enforces `MaxSessions`, reaps idle sessions, and disposes engine resources.
3. Each session owns its own Xvfb display, PulseAudio null sink, Brave/Playwright context, disposable Brave profile, and `BrowserSession` state machine.
4. Brave profiles are isolated per session under `/var/lib/blukube/brave-profiles/sessions/<guid>`, seeded from warmed Brave filter/component state, stripped of restore/cache state, and deleted on session close.
5. Brave Shields are enforced through managed policy in `docker/brave-policies.json`; the launcher avoids Playwright defaults that disable Brave component extensions and component updates.
6. Audio is captured from each session's PulseAudio monitor, encoded as Opus, and streamed from the hub.

### TUI

1. `src/BluKube.Tui` is a Spectre.Console CLI with `play` and `config` commands.
2. `play` creates one fresh server session, optionally searches YouTube and prompts for a track, then enters the live player view.
3. Starting the client creates a session; leaving the client closes it. There is no attach/resume command.
4. The TUI consumes `StreamStates` for playback state and `StreamAudio` for Opus packets, decoded through Concentus and played locally through OpenAL.

### SignalR Contract

`SessionHub` methods operate on the session attached to the current connection:

- `CreateSession()` returns the new session id.
- `CloseSession(Guid id)` disposes that session.
- `Search(string query, int limit)` returns `SearchResultsState`.
- `Play(string videoId)`, `Pause()`, `Resume()`, `SeekTo(TimeSpan)`, and `SetVolume(float)` return `PlaybackState` or `ErrorState`.
- `GetState()` returns the current `SessionState`.
- `StreamStates(CancellationToken)` yields state snapshots.
- `StreamAudio(CancellationToken)` yields Opus packets.

### REST Contract

REST remains a thin inspection/admin surface:

- `GET /v1/sessions`
- `POST /v1/sessions`
- `DELETE /v1/sessions/{id}`
- `GET /v1/sessions/{id}/state`

## Validation

Use these commands for the current tree:

- `dotnet build BluKube.slnx`
- `set +H && dotnet test BluKube.slnx --filter 'FullyQualifiedName!~BraveMediaPlayerIntegrationTests'`
- `dotnet publish src/BluKube.Tui/BluKube.Tui.csproj -c Release -o publish/tui`
- `docker compose build && BLUKUBE_TOKEN=<token> docker compose up -d`

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
│  │  │    └─ BrowserSession            │  │  │
│  │  │         ├─ BraveMediaPlayer     │  │  │
│  │  │         └─ PulseAudio sink      │  │  │
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
- **Contracts**: shared wire contracts live in `BluKube.Contracts`; server and TUI both reference them.
- **Transport**: SignalR hub for interactive commands plus state/audio streaming. REST remains a thin inspection/admin surface.
- **Events**: `BrowserSession` fans out `SessionState` snapshots via channels; audio is a separate Opus packet stream.
- **Audio**: client-side playback. Server captures Brave audio with a per-session PulseAudio null sink, encodes Opus, and streams to the TUI. No host audio mounts in Docker.
- **Sessions**: multiple concurrent server sessions are supported, but each TUI run owns exactly one session and closes it on exit.
- **Development**: Docker-first. Server always runs in container. VS Code attaches debugger to container. TUI runs on host.
- **TUI distribution**: self-contained single-file binary; `linux-x64` first.
- **Controls**: Play/Pause, Seek ±10 s / ±30 s (Shift), Volume, Quit.
- **Auth**: Bearer token + bind to `127.0.0.1` by default; LAN exposure opt-in.
- **Deployment**: Docker image for server + binary release for TUI; `docker-compose.yml` in repo.
- **Token bootstrap**: Server auto-generates token on first startup, persists in `/var/lib/blukube/token`. TUI prompts on first connect/401, stores in `$XDG_CONFIG_HOME/blukube/config.toml`.
- **Abstractions**: engine/browser/display/audio infrastructure under `Core/Engine`; domain player/search under `Core/Domain`; session lifecycle under `Core/Session`.
- **Duration**: `TimeSpan` (non-nullable) on `MediaItem`.
- **Out of scope**: persistent queues across restarts, web UI, mobile remote, multi-user accounts, HTTPS.
