# Migration Checklist

Tracks every behavioural feature the new **BluKube** implementation must
reproduce before the PoC can be retired.

## CLI / Parsing

- [ ] `--query` / `-q` argument (default: `"lofi hip hop"`)
- [ ] `--limit` / `-n` argument (range 1–20, default: 8)
- [ ] `--dry-run` flag (search + display result, no playback, no Brave/Xvfb)
- [ ] `--brave-path` argument (override Brave executable)
- [ ] `--help` / `-h` prints usage
- [ ] Graceful Ctrl+C (returns exit code 130)

## Search

- [ ] YouTube search via Playwright (navigate to `/results?search_query=...`)
- [ ] Consent cookie applied before search to reduce interruptions
- [ ] Parse video renderer elements: title, channel, href, duration
- [ ] Normalize relative hrefs to full `https://www.youtube.com` URLs
- [ ] No results → clear error message, exit code 3

## Queue & Selection

- [ ] Interactive track picker (Spectre.Console `SelectionPrompt`)
- [ ] Non-interactive fallback: picks first result
- [ ] Search query prompt when no `--query` given
- [ ] Queue position tracking (current index / total)
- [ ] Queue ended detection (past last item)

## Playback Controls

- [ ] **Space** → pause / resume toggle
- [ ] **N** → next track in queue
- [ ] **R** → new search + rebuild queue
- [ ] **Q** → quit
- [ ] Auto-advance to next track when current ends (polling loop)
- [ ] Pause feedback message (paused / resumed / still-paused)
- [ ] Seek ±10 seconds (new feature in BluKube)
- [ ] Seek ±30 seconds with Shift (new feature in BluKube)
- [ ] Volume control (new feature in BluKube)
- [ ] Stop (close tab / reset) (new feature in BluKube)
- [ ] Previous track (new feature in BluKube)
- [ ] Progress bar with current/total time (new feature in BluKube TUI)

## Browser / Playwright

- [ ] Brave browser launched in **headed** mode (Shields + audio functional)
- [ ] Playwright Chromium launch with full arg set:
  - `--ozone-platform=x11`
  - `--autoplay-policy=no-user-gesture-required`
  - `--disable-blink-features=AutomationControlled`
  - `--disable-dev-shm-usage`
  - `--no-sandbox`
  - `--disable-background-media-suspend`
  - `--disable-background-timer-throttling`
  - `--disable-renderer-backgrounding`
  - `--disable-backgrounding-occluded-windows`
  - `--disable-features=MediaSessionService,IntensiveWakeUpThrottling,CalculateNativeWinOcclusion`
- [ ] Brave path resolution: explicit → env (`BRAVE_EXECUTABLE_PATH`) → known paths
- [ ] Xvfb display management (private per-session)
- [ ] Xvfb availability check with install instructions
- [ ] Wayland env stripping (`WAYLAND_DISPLAY`, `XDG_SESSION_TYPE` removed)
- [ ] DISPLAY override to Xvfb value
- [ ] Parent environment inherited for PulseAudio/PipeWire

## Playback Engine (JS evaluation)

- [ ] Video element selection: `.video-stream:not([aria-hidden])` → `.video-stream` → `video`
- [ ] `video.pause()` / `video.play()` for pause/resume
- [ ] `video.paused` / `video.ended` polling
- [ ] Unmute after playback start
- [ ] Playback retry: 5 attempts, 1200 ms delay
- [ ] Playback snapshot diagnostics on failure (paused, muted, currentTime, readyState, networkState, ended, errorCode)

## Resource Lifecycle

- [ ] Proper disposal chain: page → context → browser → playwright → xvfb
- [ ] Xvfb process killed on dispose
- [ ] Graceful shutdown on SIGTERM (new: server sessions drained)
- [ ] Per-session CancellationTokenSource (new: server architecture)

## Docker

- [ ] Multi-stage build (SDK → ASP.NET runtime — was `dotnet/runtime`, now `dotnet/aspnet`)
- [ ] Brave + Xvfb + audio libs installed in runtime stage
- [ ] `appuser` created for non-root execution
- [ ] PulseAudio/PipeWire cookie + socket bind-mount support
- [ ] `--dry-run` works without audio mounts
- [ ] Playwright `.playwright/` permissions applied

## Error Handling

- [ ] `ArgumentException` → exit 2 with message
- [ ] `OperationCanceledException` → exit 130
- [ ] `PlaywrightException` → exit 1 with message
- [ ] `TimeoutException` → exit 1 with message
- [ ] `InvalidOperationException` → exit 1 with message

## Session Management (new in BluKube)

- [ ] Create/list/close sessions via REST API
- [ ] `MaxSessions` cap (default 3)
- [ ] Idle timeout auto-cleanup
- [ ] Bearer token auth on all `/v1/*` endpoints
- [ ] Token auto-generation + persistence in data volume
- [ ] SSE event stream (`position-tick`, `state-changed`, `track-ended`, `error`, `queue-changed`)

## CLI / TUI (new in BluKube)

- [ ] `blukube play [query]` — interactive session flow
- [ ] `blukube attach [--session <id>]` — reattach TUI
- [ ] `blukube sessions list|new|close <id>`
- [ ] `blukube status` — one-shot snapshot
- [ ] `blukube config set/get`
- [ ] Login flow on 401 (Spectre password prompt)
- [ ] SSE reconnection with exponential backoff
- [ ] TUI render: header, progress bar, status line, queue panel, footer keybindings
- [ ] TUI keybindings: Space, S, ←/→, Shift+←/Shift+→, N/P, +/- , R, D, Q, ?
- [ ] Self-contained single-file binary publish
