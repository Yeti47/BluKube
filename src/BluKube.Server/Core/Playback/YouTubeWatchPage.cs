using Microsoft.Playwright;
using BluKube.Server.Core.Engine.Browser;

namespace BluKube.Server.Core.Playback;

internal sealed class YouTubeWatchPage(IPage page, string videoId) : IWatchPage
{
    private const int PlaybackRetryDelayMs = 500;
    private const int PlaybackCheckDelayMs = 1200;

    private readonly IPage _page = page;

    public async Task NavigateAsync(CancellationToken ct)
    {
        var url = $"https://www.youtube.com/watch?v={Uri.EscapeDataString(videoId)}";
        await _page.GotoAsync(url,
            new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 45000 });

        await DismissConsentAsync(_page);

        await _page.WaitForFunctionAsync(
            """
            () => {
              const video =
                document.querySelector("video.video-stream:not([aria-hidden='true'])") ??
                document.querySelector("video.video-stream") ??
                document.querySelector("video");
              return !!video;
            }
            """,
            new PageWaitForFunctionOptions { Timeout = 30000 });
    }

    public async Task<WatchSnapshot> ReadStateAsync(CancellationToken ct)
        => (await ReadRawAsync()).ToSnapshot();

    public async Task<WatchSnapshot> EnsurePlayingAsync(CancellationToken ct)
    {
        var page = _page;
        await page.BringToFrontAsync();

        for (var attempt = 0; attempt < 5; attempt++)
        {
            await DismissConsentAsync(page);

            var before = await ReadRawAsync();
            if (!before.Paused && !before.Ended)
            {
                return before.ToSnapshot();
            }

            await page.EvaluateAsync(
                """
                () => {
                  const video =
                    document.querySelector("video.video-stream:not([aria-hidden='true'])") ??
                    document.querySelector("video.video-stream") ??
                    document.querySelector("video");
                  if (!video) return;
                  video.volume = 1;
                }
                """);

            if (await TryPlayDomAsync(page))
            {
                var (progressed, after) = await WaitForProgressAsync(page, before);
                if (progressed) return after.ToSnapshot();
            }

            switch (attempt % 3)
            {
                case 0: await TryClickPlayButtonAsync(page); break;
                case 1: await page.Keyboard.PressAsync("k"); break;
                case 2: await TryClickVideoSurfaceAsync(page); break;
            }

            if (await TryPlayDomAsync(page))
            {
                var (progressed, after) = await WaitForProgressAsync(page, before);
                if (progressed) return after.ToSnapshot();
            }

            await Task.Delay(PlaybackRetryDelayMs, ct);
        }

        throw new InvalidOperationException("Could not start playback after retries.");
    }

    public async Task<WatchSnapshot> PauseAsync(CancellationToken ct)
    {
        await _page.EvaluateAsync(
            """
            () => {
              const video =
                document.querySelector("video.video-stream:not([aria-hidden='true'])") ??
                document.querySelector("video.video-stream") ??
                document.querySelector("video");
              if (!video) return;
              video.pause();
            }
            """);
        return (await ReadRawAsync()).ToSnapshot();
    }

    public async Task<WatchSnapshot> ResumeAsync(CancellationToken ct)
    {
        await _page.EvaluateAsync(
            """
            async () => {
              const video =
                document.querySelector("video.video-stream:not([aria-hidden='true'])") ??
                document.querySelector("video.video-stream") ??
                document.querySelector("video");
              if (!video) return;
              try { await video.play(); } catch { }
            }
            """);
        return (await ReadRawAsync()).ToSnapshot();
    }

    public async Task<WatchSnapshot> SeekToAsync(TimeSpan position, CancellationToken ct)
    {
        await _page.EvaluateAsync(
            $$"""
            () => {
              const video =
                document.querySelector("video.video-stream:not([aria-hidden='true'])") ??
                document.querySelector("video.video-stream") ??
                document.querySelector("video");
              if (!video) return;
              video.currentTime = {{position.TotalSeconds}};
            }
            """);
        return (await ReadRawAsync()).ToSnapshot();
    }

    public async Task<WatchSnapshot> SetVolumeAsync(float volume, CancellationToken ct)
    {
        await _page.EvaluateAsync(
            $$"""
            () => {
              const video =
                document.querySelector("video.video-stream:not([aria-hidden='true'])") ??
                document.querySelector("video.video-stream") ??
                document.querySelector("video");
              if (!video) return;
              video.volume = {{volume}};
            }
            """);
        return (await ReadRawAsync()).ToSnapshot();
    }

    private Task<RawState> ReadRawAsync() =>
        _page.EvaluateAsync<RawState>(
            """
            () => {
              const video =
                document.querySelector("video.video-stream:not([aria-hidden='true'])") ??
                document.querySelector("video.video-stream") ??
                document.querySelector("video");
              if (!video) {
                return {
                  paused: true, ended: false, muted: false,
                  currentTime: 0, duration: 0, volume: 1,
                  readyState: 0, networkState: 0, errorCode: null
                };
              }
              return {
                paused: !!video.paused,
                ended: !!video.ended,
                muted: !!video.muted,
                currentTime: Number(video.currentTime || 0),
                duration: Number.isFinite(video.duration) ? Number(video.duration) : 0,
                volume: Number(video.volume ?? 1),
                readyState: Number(video.readyState || 0),
                networkState: Number(video.networkState || 0),
                errorCode: video.error ? Number(video.error.code) : null
              };
            }
            """);



    private static async Task DismissConsentAsync(IPage page)
    {
        try
        {
            var candidates = new[]
            {
                "button[aria-label*='Accept' i]",
                "button[aria-label*='akzeptieren' i]",
                "button[aria-label*='Tout accepter' i]",
                "button[aria-label*='Aceptar todo' i]",
                "ytd-button-renderer:has-text('Accept all') button",
                "ytd-button-renderer:has-text('Alle akzeptieren') button",
                "tp-yt-paper-button:has-text('Accept all')",
                "tp-yt-paper-button:has-text('Alle akzeptieren')",
                "button:has-text('Accept all')",
                "button:has-text('Alle akzeptieren')",
                "button:has-text('I agree')"
            };

            foreach (var selector in candidates)
            {
                var locator = page.Locator(selector).First;
                try
                {
                    if (await locator.IsVisibleAsync())
                    {
                        await locator.ClickAsync(new LocatorClickOptions { Timeout = 1500 });
                        await page.WaitForTimeoutAsync(300);
                        break;
                    }
                }
                catch (PlaywrightException) { }
            }
        }
        catch (PlaywrightException) { }

        try
        {
            await page.EvaluateAsync(
                """
                () => {
                  const kill = sel => document.querySelectorAll(sel).forEach(el => el.remove());
                  kill('tp-yt-iron-overlay-backdrop');
                  kill('ytd-consent-bump-v2-lightbox');
                  kill('ytd-popup-container tp-yt-paper-dialog');
                  document.documentElement.style.overflow = '';
                  document.body.style.overflow = '';
                }
                """);
        }
        catch (PlaywrightException) { }
    }

    private static async Task<bool> TryPlayDomAsync(IPage page)
    {
        try
        {
            return await page.EvaluateAsync<bool>(
                """
                async () => {
                  const video =
                    document.querySelector("video.video-stream:not([aria-hidden='true'])") ??
                    document.querySelector("video.video-stream") ??
                    document.querySelector("video");
                  if (!video) return false;
                  try { await video.play(); } catch { }
                  return !video.paused;
                }
                """);
        }
        catch (PlaywrightException)
        {
            return false;
        }
    }

    private async Task<(bool Progressed, RawState After)> WaitForProgressAsync(IPage page, RawState before)
    {
        await page.WaitForTimeoutAsync(PlaybackCheckDelayMs);
        var after = await ReadRawAsync();

        var progressed = !after.Paused && !after.Ended &&
            (after.CurrentTime > before.CurrentTime + 0.15 ||
             (after.CurrentTime > 0.15 && after.ReadyState >= 3));
        return (progressed, after);
    }

    private static async Task TryClickPlayButtonAsync(IPage page)
    {
        try
        {
            var playButton = page.Locator(".ytp-play-button").First;
            if (await playButton.IsVisibleAsync())
            {
                await playButton.ClickAsync(new LocatorClickOptions { Timeout = 2000 });
            }
        }
        catch (PlaywrightException) { }
    }

    private static async Task TryClickVideoSurfaceAsync(IPage page)
    {
        try
        {
            var center = await page.EvaluateAsync<VideoCenter?>(
                """
                () => {
                  const video =
                    document.querySelector("video.video-stream:not([aria-hidden='true'])") ??
                    document.querySelector("video.video-stream") ??
                    document.querySelector("video");
                  if (!video) return null;
                  const rect = video.getBoundingClientRect();
                  return { x: rect.left + rect.width / 2, y: rect.top + rect.height / 2 };
                }
                """);

            if (center is not null)
            {
                await page.Mouse.ClickAsync((float)center.X, (float)center.Y);
            }
        }
        catch (PlaywrightException) { }
    }

    private sealed record RawState
    {
        public bool Paused { get; init; }
        public bool Ended { get; init; }
        public bool Muted { get; init; }
        public double CurrentTime { get; init; }
        public double Duration { get; init; }
        public double Volume { get; init; }
        public int ReadyState { get; init; }
        public int NetworkState { get; init; }
        public int? ErrorCode { get; init; }

        public WatchSnapshot ToSnapshot()
            => new(
                Position: TimeSpan.FromSeconds(Math.Max(0, CurrentTime)),
                Duration: TimeSpan.FromSeconds(Math.Max(0, Duration)),
                IsPlaying: !Paused && !Ended,
                IsEnded: Ended,
                Volume: Muted ? 0f : (float)Math.Clamp(Volume, 0d, 1d));
    }

    private sealed record VideoCenter(double X, double Y);
}
