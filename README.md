# yt-cli-radio

Interactive YouTube radio CLI using **.NET 10**, **headless Brave**, and Playwright automation.

This project is currently MVP-first and intentionally constrained to browser-native playback in Brave.

## Prerequisites

- .NET SDK 10.0+
- Brave browser installed locally, or provide `--brave-path`
- `Xvfb` on PATH (Fedora: `sudo dnf install xorg-x11-server-Xvfb`,
  Debian/Ubuntu: `sudo apt-get install xvfb`)

> **Why Xvfb?** Brave is launched in headed mode so its Shields
> (ad/cookie-consent blocking) and the Chromium audio pipeline both stay
> fully functional — neither works reliably in headless mode on Linux. The
> app starts its own private Xvfb display, so no window ever appears on your
> real screen even if your session has `DISPLAY` set.

## Run locally

```bash
dotnet restore YtCliRadio.slnx
dotnet run --project src/YtCliRadio -- --query "lofi hip hop" --limit 8
```

Dry-run (search + selection only, does not start Brave or Xvfb):

```bash
dotnet run --project src/YtCliRadio -- --query "synthwave" --dry-run
```

Interactive radio mode controls during playback:

- `Space`: pause/resume
- `N`: next track in current queue
- `R`: start a new search and build a new queue
- `Q`: quit

## CLI options

```text
Usage:
  dotnet run --project src/YtCliRadio -- [options]

Options:
  -q|--query <text>      Search term (default: "lofi hip hop")
  -n|--limit <number>    Number of search results (1-20, default: 8)
     --dry-run           Search only, do not launch playback
     --brave-path <path> Override Brave executable path
  -h|--help              Show help
```

## Docker

Build:

```bash
docker build -t yt-cli-radio .
```

Run (with audio routed to host PulseAudio / PipeWire):

```bash
docker run --rm -it \
  --user $(id -u):$(id -g) \
  -e HOME=/tmp \
  -e XDG_RUNTIME_DIR=/run/user/$(id -u) \
  -e PULSE_SERVER=unix:/run/user/$(id -u)/pulse/native \
  -e PULSE_COOKIE=/tmp/pulse-cookie \
  -v /run/user/$(id -u)/pulse/native:/run/user/$(id -u)/pulse/native \
  -v ~/.config/pulse/cookie:/tmp/pulse-cookie:ro \
  -v /etc/machine-id:/etc/machine-id:ro \
  yt-cli-radio --query "lofi mix"
```

Notes:
- `--user $(id -u):$(id -g)` makes the host PulseAudio socket readable from inside the container.
- `HOME=/tmp` is required because the baked-in `appuser` home is not writable by an arbitrary UID; Brave needs a writable `$HOME` for its profile.
- `XDG_RUNTIME_DIR` is required so the PulseAudio client library can locate its cookie alongside the socket.
- `PULSE_COOKIE` + the cookie bind mount provide the per-user authentication token PulseAudio requires on top of the socket connection. Without it Brave connects but is rejected and silently produces no audio.
- On PipeWire-only systems the same socket path works via `pipewire-pulse`; the cookie is still consulted.
- If you only want to validate search without audio, append `--dry-run` and you can drop the audio mounts/env.

For interactive playback controls in Docker, keep `-it` and omit `--dry-run`.

Troubleshooting:

- If local search times out waiting for YouTube elements, retry once; the app now navigates directly to `/results` and applies a consent cookie to reduce first-load consent interruptions.
- If Docker reports Playwright node permission errors, rebuild the image with `--no-cache` so the updated executable permissions in `/app/.playwright` are applied.
- `Xvfb is required for invisible playback but was not found on PATH` — install it (see Prerequisites).
- No audio: confirm the host PulseAudio/PipeWire socket is reachable. Locally `pactl info` should succeed in the same shell. In Docker, see the audio mount in the Docker section.
## Notes

- MVP keeps playback browser-native in headless Brave only.
- YouTube page structure can change; selectors may need maintenance.
- Ensure usage complies with applicable YouTube terms and local laws.
