using Microsoft.Playwright;
using BluKube.Server.Core.Engine.Browser;

namespace BluKube.Server.Core.Playback;

public sealed class YouTubeWatchPage : IYouTubePage<WatchPageParams>
{
    private const int PlaybackRetryDelayMs = 500;
    private const int PlaybackCheckDelayMs = 1200;

    private readonly IPage _page;
    private readonly WatchPageParams _parameters;

    private YouTubeWatchPage(IPage page, WatchPageParams parameters)
    {
        _page = page;
        _parameters = parameters;
    }

    public static IYouTubePage<WatchPageParams> Create(IPage page, WatchPageParams parameters)
        => new YouTubeWatchPage(page, parameters);

    public async Task NavigateToAsync(CancellationToken ct)
    {
        await _page.GotoAsync(_parameters.VideoUrl,
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

    public Task<PlaybackSnapshot> GetSnapshotAsync(CancellationToken cancellationToken) =>
        _page.EvaluateAsync<PlaybackSnapshot>(
            """
            () => {
              const video =
                document.querySelector("video.video-stream:not([aria-hidden='true'])") ??
                document.querySelector("video.video-stream") ??
                document.querySelector("video");
              if (!video) {
                return {
                  paused: true, ended: false, muted: false,
                  currentTime: 0, readyState: 0, networkState: 0, errorCode: null
                };
              }
              return {
                paused: !!video.paused,
                ended: !!video.ended,
                muted: !!video.muted,
                currentTime: Number(video.currentTime || 0),
                readyState: Number(video.readyState || 0),
                networkState: Number(video.networkState || 0),
                errorCode: video.error ? Number(video.error.code) : null
              };
            }
            """);

    public async Task<bool> TryEnsurePlayingAsync(CancellationToken cancellationToken)
    {
        var page = _page;
        await page.BringToFrontAsync();

        for (var attempt = 0; attempt < 5; attempt++)
        {
            await DismissConsentAsync(page);

            var before = await GetSnapshotAsync(cancellationToken);
            if (!before.Paused && !before.Ended)
            {
                return true;
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

            if (await TryPlayDomAsync(page) && await WaitForProgressAsync(page, before))
            {
                return true;
            }

            switch (attempt % 3)
            {
                case 0: await TryClickPlayButtonAsync(page); break;
                case 1: await page.Keyboard.PressAsync("k"); break;
                case 2: await TryClickVideoSurfaceAsync(page); break;
            }

            if (await TryPlayDomAsync(page) && await WaitForProgressAsync(page, before))
            {
                return true;
            }

            await Task.Delay(PlaybackRetryDelayMs, cancellationToken);
        }

        return false;
    }

    public Task PauseAsync(CancellationToken cancellationToken) =>
        _page.EvaluateAsync(
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

    public Task ResumeAsync(CancellationToken cancellationToken) =>
        _page.EvaluateAsync(
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

    public Task SeekRelativeAsync(double deltaSeconds, CancellationToken cancellationToken) =>
        _page.EvaluateAsync(
            $$"""
            () => {
              const video =
                document.querySelector("video.video-stream:not([aria-hidden='true'])") ??
                document.querySelector("video.video-stream") ??
                document.querySelector("video");
              if (!video) return;
              video.currentTime += {{deltaSeconds}};
            }
            """);

    public Task SeekToAsync(double seconds, CancellationToken cancellationToken) =>
        _page.EvaluateAsync(
            $$"""
            () => {
              const video =
                document.querySelector("video.video-stream:not([aria-hidden='true'])") ??
                document.querySelector("video.video-stream") ??
                document.querySelector("video");
              if (!video) return;
              video.currentTime = {{seconds}};
            }
            """);

    public Task SetVolumeAsync(double volume, CancellationToken cancellationToken) =>
        _page.EvaluateAsync(
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

    public Task<bool> IsEndedAsync(CancellationToken cancellationToken) =>
        _page.EvaluateAsync<bool>(
            """
            () => {
              const video =
                document.querySelector("video.video-stream:not([aria-hidden='true'])") ??
                document.querySelector("video.video-stream") ??
                document.querySelector("video");
              if (!video) return false;
              return !!video.ended;
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

    private async Task<bool> WaitForProgressAsync(IPage page, PlaybackSnapshot before)
    {
        await page.WaitForTimeoutAsync(PlaybackCheckDelayMs);
        var after = await GetSnapshotAsync(CancellationToken.None);

        return !after.Paused && !after.Ended &&
               (after.CurrentTime > before.CurrentTime + 0.15 ||
                (after.CurrentTime > 0.15 && after.ReadyState >= 3));
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

    private sealed record VideoCenter
    {
        public double X { get; init; }
        public double Y { get; init; }
    }
}
