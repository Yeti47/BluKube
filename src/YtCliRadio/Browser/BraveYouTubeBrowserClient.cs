using Microsoft.Playwright;
using YtCliRadio.Configuration;
using YtCliRadio.Domain;

namespace YtCliRadio.Browser;

public sealed class BraveYouTubeBrowserClient : IYouTubeBrowserClient
{
    private const int PlaybackStartRetries = 5;
    private const int PlaybackCheckDelayMs = 1200;
    private readonly AppOptions _options;
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private IBrowserContext? _context;
    private IPage? _page;
    private XvfbDisplay? _xvfb;
    private PlaybackSnapshot? _lastPlaybackSnapshot;

    public BraveYouTubeBrowserClient(AppOptions options)
    {
        _options = options;
    }

    public async Task<IReadOnlyList<VideoSearchResult>> SearchAsync(
        string query,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        if (limit < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(limit));
        }

        var page = await GetOrCreatePageAsync(cancellationToken);
        await EnsureConsentCookieAsync(page);

        var queryUrl = $"https://www.youtube.com/results?search_query={Uri.EscapeDataString(query)}&hl=en&gl=US";
        await page.GotoAsync(queryUrl, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 30000 });

        var videoItems = page.Locator("ytd-video-renderer");
        await videoItems.First.WaitForAsync(new() { Timeout = 30000 });

        var count = await videoItems.CountAsync();
        var bounded = Math.Min(count, limit);
        var results = new List<VideoSearchResult>(bounded);

        for (var i = 0; i < bounded; i++)
        {
            var item = videoItems.Nth(i);
            var extracted = await item.EvaluateAsync<ExtractedVideoData>(
                """
                node => {
                  const titleEl = node.querySelector('#video-title');
                  const channelEl = node.querySelector('ytd-channel-name a, #channel-name a');
                  const durationEl = node.querySelector('span.ytd-thumbnail-overlay-time-status-renderer');
                  return {
                    title: titleEl?.textContent?.trim() ?? '',
                    channel: channelEl?.textContent?.trim() ?? 'Unknown channel',
                    href: titleEl?.getAttribute('href') ?? '',
                    duration: durationEl?.textContent?.trim() ?? null
                  };
                }
                """);

            var href = extracted?.Href;

            if (string.IsNullOrWhiteSpace(href))
            {
                continue;
            }

            var normalizedUrl = href.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? href
                : $"https://www.youtube.com{href}";

            results.Add(new VideoSearchResult(
                extracted?.Title?.Trim() ?? "Unknown title",
                extracted?.Channel?.Trim() ?? "Unknown channel",
                normalizedUrl,
                extracted?.Duration?.Trim()));
        }

        return results;
    }

    public async Task StartPlaybackAsync(VideoSearchResult selection, CancellationToken cancellationToken)
    {
        var page = await GetOrCreatePageAsync(cancellationToken);
        var playbackUrl = BuildPlaybackUrl(selection.Url);
        await NavigateToVideoAsync(page, playbackUrl);

        if (!await TryEnsurePlayingAsync(cancellationToken))
        {
            var state = _lastPlaybackSnapshot is null
                ? "unavailable"
                : $"paused={_lastPlaybackSnapshot.Paused}, muted={_lastPlaybackSnapshot.Muted}, " +
                  $"currentTime={_lastPlaybackSnapshot.CurrentTime:F2}, readyState={_lastPlaybackSnapshot.ReadyState}, " +
                  $"networkState={_lastPlaybackSnapshot.NetworkState}, ended={_lastPlaybackSnapshot.Ended}, " +
                  $"errorCode={_lastPlaybackSnapshot.ErrorCode?.ToString() ?? "none"}";

            throw new InvalidOperationException(
                "Playback could not be resumed from paused state. " +
                "This can happen when media playback is blocked in the current runtime environment. " +
                $"Last player state: {state}");
        }

        await EnsureUnmutedAsync(page);
    }

    public async Task PauseAsync(CancellationToken cancellationToken)
    {
        var page = await GetOrCreatePageAsync(cancellationToken);
        await page.EvaluateAsync(
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
    }

    public async Task ResumeAsync(CancellationToken cancellationToken)
    {
        await GetOrCreatePageAsync(cancellationToken);
        await TryEnsurePlayingAsync(cancellationToken);
    }

    public async Task<bool> IsPausedAsync(CancellationToken cancellationToken)
    {
        var page = await GetOrCreatePageAsync(cancellationToken);
        var isPaused = await page.EvaluateAsync<bool>(
            """
            () => {
              const video =
                document.querySelector("video.video-stream:not([aria-hidden='true'])") ??
                document.querySelector("video.video-stream") ??
                document.querySelector("video");
              if (!video) return false;
              return !!video.paused;
            }
            """);
        return isPaused;
    }

    public async Task<bool> IsTrackEndedAsync(CancellationToken cancellationToken)
    {
        var page = await GetOrCreatePageAsync(cancellationToken);
        var ended = await page.EvaluateAsync<bool>(
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
        return ended;
    }

    public async ValueTask DisposeAsync()
    {
        if (_page is not null)
        {
            await _page.CloseAsync();
        }

        if (_context is not null)
        {
            await _context.CloseAsync();
        }

        if (_browser is not null)
        {
            await _browser.CloseAsync();
        }

        _playwright?.Dispose();

        if (_xvfb is not null)
        {
            await _xvfb.DisposeAsync();
        }
    }

    private async Task<IPage> GetOrCreatePageAsync(CancellationToken cancellationToken)
    {
        if (_page is not null)
        {
            return _page;
        }

        _playwright ??= await Microsoft.Playwright.Playwright.CreateAsync();
        var bravePath = BravePathResolver.Resolve(_options.BraveExecutablePath);

        // Always run Brave on a private Xvfb display so no window ever appears
        // on the user's real screen, even if their session has DISPLAY set.
        _xvfb ??= await XvfbDisplay.StartAsync(cancellationToken);

        // Inherit the parent's environment so Brave can still find the user's
        // PulseAudio/PipeWire socket (XDG_RUNTIME_DIR, PULSE_SERVER, HOME,
        // DBUS_SESSION_BUS_ADDRESS, ...). Only DISPLAY is overridden so the
        // browser draws into our private Xvfb instead of the host screen.
        var braveEnv = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string key && entry.Value is string value)
            {
                braveEnv[key] = value;
            }
        }
        braveEnv["DISPLAY"] = _xvfb.DisplayValue;
        // On Wayland sessions (e.g. Fedora GNOME) Chromium prefers WAYLAND_DISPLAY
        // over DISPLAY and would otherwise draw into the user's real compositor,
        // bypassing Xvfb entirely. Strip the Wayland hints so X11/Xvfb is used.
        braveEnv.Remove("WAYLAND_DISPLAY");
        braveEnv.Remove("XDG_SESSION_TYPE");

        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            // Brave is launched in HEADED mode intentionally: Brave Shields
            // (ad/cookie-consent blocking, fingerprint protection) — the very
            // reason we use Brave — is degraded or disabled in headless mode,
            // and Chromium-family browsers do not reliably emit audio in
            // headless on Linux. Invisibility is provided by routing the
            // browser to our private Xvfb display via the DISPLAY env var.
            Headless = false,
            ExecutablePath = bravePath,
            Env = braveEnv,
            Args =
            [
                // Force X11 backend so Brave uses our Xvfb display rather than
                // auto-selecting Wayland on a Wayland session.
                "--ozone-platform=x11",
                "--autoplay-policy=no-user-gesture-required",
                "--disable-blink-features=AutomationControlled",
                "--disable-dev-shm-usage",
                "--no-sandbox",
                "--disable-background-media-suspend",
                "--disable-background-timer-throttling",
                "--disable-renderer-backgrounding",
                "--disable-backgrounding-occluded-windows",
                "--disable-features=MediaSessionService,IntensiveWakeUpThrottling,CalculateNativeWinOcclusion"
            ]
        });

        _context = await _browser.NewContextAsync(new BrowserNewContextOptions
        {
            UserAgent =
                "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) " +
                "Chrome/124.0.0.0 Safari/537.36",
            // Force en-US so DOM strings (button labels, consent copy, etc.)
            // remain stable across host locales.
            Locale = "en-US",
            TimezoneId = "Etc/UTC",
            ExtraHTTPHeaders = new Dictionary<string, string>
            {
                ["Accept-Language"] = "en-US,en;q=0.9"
            },
            ViewportSize = new ViewportSize
            {
                Width = 1280,
                Height = 720
            }
        });

        _page = await _context.NewPageAsync();
        await _page.AddInitScriptAsync(
            """
            () => {
              Object.defineProperty(document, 'hidden', { configurable: true, get: () => false });
              Object.defineProperty(document, 'visibilityState', { configurable: true, get: () => 'visible' });
              document.addEventListener('visibilitychange', event => event.stopImmediatePropagation(), true);
              Object.defineProperty(navigator, 'webdriver', { configurable: true, get: () => undefined });
            }
            """);

        cancellationToken.ThrowIfCancellationRequested();
        return _page!;
    }

    private async Task<bool> TryEnsurePlayingAsync(CancellationToken cancellationToken)
    {
        var page = await GetOrCreatePageAsync(cancellationToken);
        await page.BringToFrontAsync();

        for (var attempt = 0; attempt < PlaybackStartRetries; attempt++)
        {
            // Re-dismiss consent overlays each attempt — they sometimes appear
            // after the player has mounted and intercept pointer events.
            await TryDismissConsentAsync(page);

            var before = await GetPlaybackSnapshotAsync(page);
            _lastPlaybackSnapshot = before;
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
                  // Do NOT force-mute here. With
                  // --autoplay-policy=no-user-gesture-required the watch page
                  // can autoplay unmuted, and toggling the mute state later
                  // tends to trip YouTube's "video paused" idle prompt.
                  video.volume = 1;
                }
                """);

            if (await TryPlayViaDomAsync(page) &&
                await WaitForPlaybackProgressAsync(page, before))
            {
                return true;
            }

            if (attempt % 3 == 0)
            {
                await TryClickPlayButtonAsync(page);
            }
            else if (attempt % 3 == 1)
            {
                await TryPressPlayPauseHotkeyAsync(page, "k");
            }
            else
            {
                await TryClickVideoSurfaceAsync(page);
            }

            if (await TryPlayViaDomAsync(page) &&
                await WaitForPlaybackProgressAsync(page, before))
            {
                return true;
            }

            await Task.Delay(500, cancellationToken);
        }

        return false;
    }

    private static async Task NavigateToVideoAsync(IPage page, string url)
    {
        await page.GotoAsync(url, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 45000 });
        await TryDismissConsentAsync(page);
        await page.WaitForFunctionAsync(
            """
            () => {
              const video =
                document.querySelector("video.video-stream:not([aria-hidden='true'])") ??
                document.querySelector("video.video-stream") ??
                document.querySelector("video");
              return !!video;
            }
            """,
            options: new() { Timeout = 30000 });
    }

    private static async Task TryDismissConsentAsync(IPage page)
    {
        try
        {
            // YouTube occasionally shows a consent wall before the player mounts.
            // Try the most common "Accept all" buttons across locales; ignore failures.
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
                    if (await locator.IsVisibleAsync(new() { Timeout = 500 }))
                    {
                        await locator.ClickAsync(new() { Timeout = 1500 });
                        await page.WaitForTimeoutAsync(300);
                        break;
                    }
                }
                catch (PlaywrightException)
                {
                    // Try next selector.
                }
            }
        }
        catch (PlaywrightException)
        {
            // Consent wall click is best-effort.
        }

        // Force-remove any leftover modal backdrop / consent lightbox so it cannot
        // intercept pointer events on the player controls.
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
        catch (PlaywrightException)
        {
            // Best-effort cleanup.
        }
    }

    private static async Task TryClickPlayButtonAsync(IPage page)
    {
        try
        {
            var playButton = page.Locator(".ytp-play-button").First;
            if (await playButton.IsVisibleAsync())
            {
                await playButton.ClickAsync(new() { Timeout = 2000 });
            }
        }
        catch (PlaywrightException)
        {
            // Continue with additional trusted input fallbacks.
        }
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
        catch (PlaywrightException)
        {
            // Continue with keyboard fallback.
        }
    }

    private static async Task TryPressPlayPauseHotkeyAsync(IPage page, string key)
    {
        try
        {
            await page.Keyboard.PressAsync(key);
        }
        catch (PlaywrightException)
        {
            // Continue with DOM play() fallback.
        }
    }

    private static async Task<bool> TryPlayViaDomAsync(IPage page)
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
                  try {
                    await video.play();
                  } catch {
                    // DOM play may reject in autoplay-restricted environments.
                  }
                  return !video.paused;
                }
                """);
        }
        catch (PlaywrightException)
        {
            return false;
        }
    }

    private async Task<bool> WaitForPlaybackProgressAsync(IPage page, PlaybackSnapshot before)
    {
        await page.WaitForTimeoutAsync(PlaybackCheckDelayMs);
        var after = await GetPlaybackSnapshotAsync(page);
        _lastPlaybackSnapshot = after;

        return !after.Paused && !after.Ended &&
               (after.CurrentTime > before.CurrentTime + 0.15 ||
                (after.CurrentTime > 0.15 && after.ReadyState >= 3));
    }

    private static async Task EnsureUnmutedAsync(IPage page)
    {
        try
        {
            var isMuted = await page.EvaluateAsync<bool>(
                """
                () => {
                  const video =
                    document.querySelector("video.video-stream:not([aria-hidden='true'])") ??
                    document.querySelector("video.video-stream") ??
                    document.querySelector("video");
                  if (!video) return false;
                  return !!video.muted;
                }
                """);

            if (!isMuted)
            {
                return;
            }

            var muteButton = page.Locator(".ytp-mute-button").First;
            if (await muteButton.IsVisibleAsync())
            {
                await muteButton.ClickAsync(new() { Timeout = 1500 });
            }

            await page.EvaluateAsync(
                """
                () => {
                  const video =
                    document.querySelector("video.video-stream:not([aria-hidden='true'])") ??
                    document.querySelector("video.video-stream") ??
                    document.querySelector("video");
                  if (!video) return;
                  video.muted = false;
                  video.volume = 1;
                }
                """);
        }
        catch (PlaywrightException)
        {
            // Leave muted state as-is if controls are unavailable.
        }
    }

    private static Task<PlaybackSnapshot> GetPlaybackSnapshotAsync(IPage page) =>
        page.EvaluateAsync<PlaybackSnapshot>(
            """
            () => {
              const video =
                document.querySelector("video.video-stream:not([aria-hidden='true'])") ??
                document.querySelector("video.video-stream") ??
                document.querySelector("video");
              if (!video) {
                return {
                  paused: true,
                  ended: false,
                  muted: false,
                  currentTime: 0,
                  readyState: 0,
                  networkState: 0,
                  errorCode: null
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

    private static string BuildPlaybackUrl(string originalUrl)
    {
        var videoId = ExtractVideoId(originalUrl);
        if (string.IsNullOrWhiteSpace(videoId))
        {
            return originalUrl;
        }

        // The full watch page is far more reliable for headless autoplay than the
        // /embed/ iframe player, which frequently refuses to instantiate <video>
        // (or to leave the paused state) without real user input — even with
        // --autoplay-policy=no-user-gesture-required.
        // hl/gl pin the YouTube UI to en-US so DOM strings stay stable.
        return $"https://www.youtube.com/watch?v={videoId}&hl=en&gl=US";
    }

    private static string? ExtractVideoId(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return null;
        }

        var host = uri.Host.ToLowerInvariant();
        if (host.Contains("youtu.be"))
        {
            return uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        }

        if (!host.Contains("youtube.com"))
        {
            return null;
        }

        var pathSegments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (pathSegments.Length >= 2 &&
            (pathSegments[0].Equals("shorts", StringComparison.OrdinalIgnoreCase) ||
             pathSegments[0].Equals("embed", StringComparison.OrdinalIgnoreCase) ||
             pathSegments[0].Equals("live", StringComparison.OrdinalIgnoreCase)))
        {
            return pathSegments[1];
        }

        var query = uri.Query.TrimStart('?');
        if (string.IsNullOrWhiteSpace(query))
        {
            return null;
        }

        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var keyValue = pair.Split('=', 2);
            if (keyValue.Length == 2 && keyValue[0] == "v")
            {
                return Uri.UnescapeDataString(keyValue[1]);
            }
        }

        return null;
    }

    private static Task EnsureConsentCookieAsync(IPage page) =>
        page.Context.AddCookiesAsync(
        [
            new Cookie
            {
                Name = "CONSENT",
                Value = "YES+cb.20210328-17-p0.en+FX+471",
                Domain = ".youtube.com",
                Path = "/",
                Secure = true
            }
        ]);

    private sealed class ExtractedVideoData
    {
        public string? Title { get; init; }
        public string? Channel { get; init; }
        public string? Href { get; init; }
        public string? Duration { get; init; }
    }

    private sealed class PlaybackSnapshot
    {
        public bool Paused { get; init; }
        public bool Ended { get; init; }
        public bool Muted { get; init; }
        public double CurrentTime { get; init; }
        public int ReadyState { get; init; }
        public int NetworkState { get; init; }
        public int? ErrorCode { get; init; }
    }

    private sealed class VideoCenter
    {
        public double X { get; init; }
        public double Y { get; init; }
    }
}
