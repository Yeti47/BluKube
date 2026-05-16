using BluKube.Server.Core.Engine.Display;
using Microsoft.Playwright;

namespace BluKube.Server.Core.Engine.Browser;

public sealed class BraveYouTubeBrowserLauncher : IYouTubeBrowserLauncher
{
    private static readonly string[] KnownBravePaths =
    [
        "/usr/bin/brave-browser",
        "/usr/bin/brave",
        "/opt/brave.com/brave/brave-browser",
    ];

    private readonly string _bravePathOverride;
    private readonly IBraveProfileProvisioner _profiles;

    public BraveYouTubeBrowserLauncher()
        : this(new BraveProfileProvisioner(), "") { }

    public BraveYouTubeBrowserLauncher(string bravePathOverride)
        : this(new BraveProfileProvisioner(), bravePathOverride) { }

    public BraveYouTubeBrowserLauncher(IBraveProfileProvisioner profiles)
        : this(profiles, "") { }

    private BraveYouTubeBrowserLauncher(IBraveProfileProvisioner profiles, string bravePathOverride)
    {
        _profiles = profiles;
        _bravePathOverride = bravePathOverride;
    }

    public async Task<IYouTubeBrowser> LaunchAsync(
        IDisplay display,
        string? pulseSink,
        CancellationToken cancellationToken
    )
    {
        var bravePath = ResolveBravePath();
        var playwright = await Playwright.CreateAsync();
        BraveProfileLease? profile = null;
        IBrowserContext? context = null;

        try
        {
            var braveEnv = InheritAndPatchEnvironment(display, pulseSink);
            profile = await _profiles.CreateAsync(cancellationToken);

            context = await playwright.Chromium.LaunchPersistentContextAsync(
                profile.ProfilePath,
                new BrowserTypeLaunchPersistentContextOptions
                {
                    Headless = false,
                    ExecutablePath = bravePath,
                    Env = braveEnv,
                    IgnoreDefaultArgs =
                    [
                        "--disable-background-networking",
                        "--disable-component-extensions-with-background-pages",
                        "--disable-component-update",
                        "--disable-extensions",
                    ],
                    Args =
                    [
                        "--ozone-platform=x11",
                        "--autoplay-policy=no-user-gesture-required",
                        "--disable-blink-features=AutomationControlled",
                        "--disable-dev-shm-usage",
                        "--no-sandbox",
                        "--disable-background-media-suspend",
                        "--disable-background-timer-throttling",
                        "--disable-renderer-backgrounding",
                        "--disable-backgrounding-occluded-windows",
                        "--disable-features=MediaSessionService,IntensiveWakeUpThrottling,CalculateNativeWinOcclusion",
                    ],
                    UserAgent =
                        "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) "
                        + "Chrome/124.0.0.0 Safari/537.36",
                    Locale = "en-US",
                    TimezoneId = "Etc/UTC",
                    ExtraHTTPHeaders = new Dictionary<string, string>
                    {
                        ["Accept-Language"] = "en-US,en;q=0.9",
                    },
                    ViewportSize = new ViewportSize { Width = 1280, Height = 720 },
                }
            );

            await ApplyConsentCookieAsync(context);

            await ApplyAntiDetectionScriptAsync(context);
            var page = await PrepareSinglePageAsync(context);

            var browser = new BraveYouTubeBrowser(page, context, playwright, profile);
            profile = null;
            context = null;
            return browser;
        }
        catch
        {
            if (context is not null)
            {
                try
                {
                    await context.CloseAsync();
                }
                catch { }
            }
            playwright.Dispose();
            if (profile is not null)
            {
                await profile.DisposeAsync();
            }
            throw;
        }
    }

    public string ResolveBravePath()
    {
        if (!string.IsNullOrWhiteSpace(_bravePathOverride))
        {
            return _bravePathOverride;
        }

        var fromEnv = Environment.GetEnvironmentVariable("BRAVE_EXECUTABLE_PATH");
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            return fromEnv;
        }

        return KnownBravePaths.FirstOrDefault(File.Exists)
            ?? throw new InvalidOperationException(
                "Unable to find Brave executable. Set BRAVE_EXECUTABLE_PATH."
            );
    }

    private static async Task<IPage> PrepareSinglePageAsync(IBrowserContext context)
    {
        var page = context.Pages.FirstOrDefault() ?? await context.NewPageAsync();
        foreach (var extraPage in context.Pages.Where(p => p != page).ToArray())
        {
            try
            {
                await extraPage.CloseAsync(new PageCloseOptions { RunBeforeUnload = false });
            }
            catch { }
        }
        return page;
    }

    private static Dictionary<string, string> InheritAndPatchEnvironment(
        IDisplay display,
        string? pulseSink
    )
    {
        var env = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string key && entry.Value is string value)
            {
                env[key] = value;
            }
        }

        env["DISPLAY"] = display.DisplayValue;
        env.Remove("WAYLAND_DISPLAY");
        env.Remove("XDG_SESSION_TYPE");

        if (!string.IsNullOrWhiteSpace(pulseSink))
        {
            env["PULSE_SINK"] = pulseSink;
        }

        return env;
    }

    private static Task ApplyConsentCookieAsync(IBrowserContext context) =>
        context.AddCookiesAsync([
            ConsentCookie(".youtube.com"),
            ConsentCookie(".google.com"),
            ConsentCookie(".google.de"),
            new Cookie
            {
                Name = "PREF",
                Value = "hl=en&gl=US&f6=40000000",
                Domain = ".youtube.com",
                Path = "/",
                Secure = true,
                SameSite = SameSiteAttribute.Lax,
            },
        ]);

    private static Cookie ConsentCookie(string domain) =>
        new()
        {
            Name = "CONSENT",
            Value = "YES+cb.20210328-17-p0.en+FX+471",
            Domain = domain,
            Path = "/",
            Secure = true,
            SameSite = SameSiteAttribute.None,
        };

    private static async Task ApplyAntiDetectionScriptAsync(IBrowserContext context)
    {
        await context.AddInitScriptAsync(
            """
            () => {
              Object.defineProperty(document, 'hidden', { configurable: true, get: () => false });
              Object.defineProperty(document, 'visibilityState', { configurable: true, get: () => 'visible' });
              document.addEventListener('visibilitychange', event => event.stopImmediatePropagation(), true);
              Object.defineProperty(navigator, 'webdriver', { configurable: true, get: () => undefined });
            }
            """
        );
    }
}
