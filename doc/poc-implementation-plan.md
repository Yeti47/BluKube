# YouTube Radio CLI (Docker + Headless Brave) — Feasibility & Implementation Plan (.NET 10)

## Problem statement
Build a dockerized, interactive CLI that provides a “YouTube radio” experience:
- Search YouTube from terminal
- Select a result interactively
- Play audio-only
- Use headless Brave for web interaction and ad-blocking

Target repository: `/home/yeti/source/repos/yt-cli-radio`

The target repo is currently empty, so this is a greenfield implementation.

## Feasibility assessment
This is **feasible** in .NET 10, with important constraints:

1. **Headless browser control**: Feasible via `Microsoft.Playwright` (.NET) launching Brave by executable path in container.
2. **Search UX**: Feasible by automating YouTube search page and parsing result DOM in C#.
3. **Audio-only playback in container**: Feasible but non-trivial. Headless browser audio output in Docker requires careful setup (virtual audio device/sink) while keeping playback browser-native.
4. **Runtime maturity**: .NET 10 may require preview SDK/runtime images depending on release timing; container pinning/version discipline is important.
5. **Reliability risk**: YouTube markup and anti-automation behavior can change; selectors and navigation logic must be resilient.
6. **Policy/legal risk**: Must remain compliant with YouTube Terms of Service and local laws. Avoid bypass behavior and document operational constraints.

Scope decision captured:
- **MVP playback strategy is browser-native audio path in headless Brave only** (no alternative playback backend in MVP).

## Proposed architecture
- **Runtime**: .NET 10 console app (C#)
- **Browser automation**: `Microsoft.Playwright` + Brave binary in Docker
- **Interactive terminal UI**: `Spectre.Console` prompts + key handling for radio controls
- **Playback strategy** (decision gate):
  - **Committed MVP**: browser-mediated playback path only, compatible with “Brave does the heavy lifting”
  - No fallback backend in MVP; failures should be explicit and actionable
- **Container**: Debian/Ubuntu-based image with .NET 10 runtime + Brave + app deps

## Implementation approach
1. **Define scope/constraints**
   - Confirm acceptable playback method boundaries and compliance constraints.
   - Decide minimum viable controls (play/pause/next/quit, queue size, autoplay).

2. **Scaffold project**
   - Initialize .NET 10 solution and console project (C#).
   - Add xUnit test project, analyzers, and reproducible local/container run scripts.

3. **Containerize Brave + app**
   - Build Dockerfile installing .NET runtime, Brave, and runtime deps.
   - Add non-root runtime user and reproducible startup command.

4. **Automation PoC**
   - Launch headless Brave in container.
   - Navigate to YouTube, run query, extract top N result metadata (title, channel, duration, URL).
   - Validate selector robustness and anti-bot handling basics using Playwright (.NET).

5. **Interactive search/selection CLI**
   - Implement prompt flow: query input -> result list -> choose track.
   - Add clear error reporting for no results, navigation failures, and timeouts.

6. **Audio playback pipeline**
   - Implement browser-native audio playback path aligned to Brave-heavy-lifting requirement.
   - Add health checks and explicit failure modes when browser audio pipeline is unavailable (no silent fallback).

7. **Radio mode controls**
   - Add continuous mode (autoplay next item/search).
   - Keyboard controls: pause/resume, skip, stop, new search.

8. **Hardening + docs**
   - Retry/backoff for transient browser failures.
   - README: setup, run, container notes, known limitations, compliance notes, and .NET 10 prerequisites.

## Initial todo list
1. Finalize product constraints and compliance boundaries.
2. Create .NET 10 C# CLI scaffold and scripts.
3. Build Docker image with Brave and runtime dependencies.
4. Implement/verify Brave automation PoC for YouTube search.
5. Implement interactive CLI result selection.
6. Implement audio-only playback path and runtime checks.
7. Add radio controls and queue/autoplay behavior.
8. Add xUnit tests for parser/control flows and document usage.

## Notes and considerations
- Keep scraping logic isolated behind a provider interface so parser updates are localized.
- Prefer observable, explicit errors over silent fallbacks.
- Add a “dry-run search” mode for debugging selectors without invoking playback.
- Keep container startup deterministic and minimal for portability.
- Avoid introducing Node.js as a runtime dependency for the app; keep the runtime and orchestration in .NET 10.
