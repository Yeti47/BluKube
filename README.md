# BluKube

BluKube is a self-hosted YouTube audio player. It lets you run the playback workload on a server and control it from a terminal or browser UI.

Use it when you want a lightweight, private radio-like player without keeping a full YouTube browser tab open on the client machine.

Playback is driven by Brave on the server side, including Brave Shields for built-in ad-blocking.

BluKube includes a Docker-first server, a native terminal client, and a Blazor web client.

## Components

### `BluKube.Server`

ASP.NET Core server with:

- SignalR hub at `/hubs/session`
- health endpoints at `/health` and `/alive`
- session management and browser lifecycle
- ad-blocking during playback via Brave Shields
- bearer-token authentication

By default the server binds to `127.0.0.1:8765` outside development. In the container it is configured to bind to `0.0.0.0:8765`.

### `BluKube.Tui`

A Spectre.Console terminal UI that connects to the server, renders the live player/search experience, and stores config in:

- `$XDG_CONFIG_HOME/blukube/config.json`, or
- `~/.config/blukube/config.json`

### `BluKube.Web`

A Blazor Server client that renders the shared TUI output inside xterm.js and stores config in browser `localStorage`.

On first load it prompts for:

- server URL
- auth token

After that it reconnects directly into the terminal view until you log out / clear the saved config.

## How It Works

```text
YouTube in Brave -> PulseAudio capture -> Opus stream -> BluKube client
                       ^                         |
                       |                         v
                 Playwright automation <- SignalR commands
```

More concretely:

1. A client connects to `BluKube.Server`.
2. The server creates an isolated browser session.
3. Search/play/pause/seek/volume commands go to the session hub.
4. The server automates YouTube in Brave with Shields enabled for ad-blocking.
5. Audio is captured from the browser session and streamed to the client.
6. State snapshots are streamed alongside audio so the UI stays live.

## Releases

If you want to use BluKube without building it yourself, start with the [GitHub releases page](https://github.com/Yeti47/yt-cli-radio/releases).

Releases provide:

- pre-built Docker images for the server components
- a pre-built TUI binary
- setup and usage notes for the packaged artifacts

## Quick Start From Source

### 1. Start the server

The easiest path is Docker Compose:

```bash
docker compose build
BLUKUBE_TOKEN=replace-me docker compose up -d
```

The server listens on port `8765` by default.

If you do not provide `BLUKUBE_TOKEN`, the server generates a random token on first startup and persists it to `/var/lib/blukube/token` inside the container.

To inspect that generated token:

```bash
docker exec blukube cat /var/lib/blukube/token
```

### 2. Connect with the TUI

Build a local release binary:

```bash
dotnet publish src/BluKube.Tui/BluKube.Tui.csproj -c Release -o publish/tui
```

Run it:

```bash
./publish/tui/blukube
```

On first run, the TUI prompts for:

- `Server URL` (typically `http://127.0.0.1:8765`)
- `Auth token`

You can inspect or clear saved TUI config with:

```bash
./publish/tui/blukube config --show
./publish/tui/blukube config --clear
```

### 3. Connect with the web client

Run the web client locally:

```bash
dotnet run --project src/BluKube.Web/BluKube.Web.csproj
```

Then open the URL shown by ASP.NET Core, enter:

- the BluKube server URL
- the auth token

The web client stores both values in browser `localStorage`.

## Docker Images

The repository contains two Dockerfiles:

- [Dockerfile](Dockerfile) for `BluKube.Server`
- [Dockerfile.web](Dockerfile.web) for `BluKube.Web`

Build them locally with:

```bash
docker build -t blukube-server .
docker build -f Dockerfile.web -t blukube-web .
```

Run them with:

```bash
docker run --rm -p 8765:8765 -e BLUKUBE_TOKEN=replace-me blukube-server
docker run --rm -p 8080:8080 blukube-web
```

The web client container serves only the Blazor client. You still need a running BluKube server and must enter its URL/token in the app.

## Local Development

Useful commands for the current tree:

```bash
dotnet build BluKube.slnx
set +H && dotnet test BluKube.slnx --filter 'FullyQualifiedName!~BraveMediaPlayerIntegrationTests'
dotnet publish src/BluKube.Tui/BluKube.Tui.csproj -c Release -o publish/tui
docker compose build && BLUKUBE_TOKEN=replace-me docker compose up -d
```

If you want only the server build:

```bash
dotnet build src/BluKube.Server/BluKube.Server.csproj
```

## Authentication

BluKube uses a single bearer token shared by clients.

The server resolves that token in this order:

1. `BLUKUBE_TOKEN`
2. `BLUKUBE_TOKEN_FILE`
3. generated token persisted to the configured token file

Clients do not manage accounts or sessions independently. They simply store the server URL and bearer token locally.

## Project Layout

- [src/BluKube.Server](src/BluKube.Server) - server, browser automation, audio capture, session hub
- [src/BluKube.Tui](src/BluKube.Tui) - terminal client
- [src/BluKube.Web](src/BluKube.Web) - Blazor web client
- [src/BluKube.Contracts](src/BluKube.Contracts) - shared wire contracts
- [src/BluKube.Client.Core](src/BluKube.Client.Core) - shared client connection/audio code
- [src/BluKube.Tui.Rendering](src/BluKube.Tui.Rendering) - shared TUI rendering and controller logic

## Notes

- The server is designed to be Docker-first.
- The web client is a remote UI, not a replacement for the server.
- YouTube behavior changes over time; selectors and player automation may need maintenance.
- This project and its authors are not affiliated with, endorsed by, or associated with YouTube.
- Use BluKube only in compliance with YouTube's terms of service.
