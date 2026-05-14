#!/usr/bin/env bash
# Start a per-container PulseAudio daemon for the blukube user, then exec the server.
# We use `--start` (idempotent) so multiple invocations are safe.
set -e

# Ensure the runtime dir exists and is owned correctly (tmpfs may have wiped it).
mkdir -p "${XDG_RUNTIME_DIR:-/run/user/$(id -u)}"
chmod 700 "${XDG_RUNTIME_DIR:-/run/user/$(id -u)}" || true

# Start PulseAudio in the background. --exit-idle-time=-1 keeps it alive
# even when no clients are attached, so per-session sinks survive client churn.
pulseaudio --start --exit-idle-time=-1 --log-target=stderr || {
  echo "warning: pulseaudio failed to start; audio streaming will be unavailable" >&2
}

exec dotnet BluKube.Server.dll "$@"
