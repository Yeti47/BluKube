using BluKube.Server.Core.Engine.Display;
using Microsoft.Playwright;

namespace BluKube.Server.Core.Engine.Browser;

public sealed class BraveYouTubeBrowserLauncher : IYouTubeBrowserLauncher
{
    private static readonly string[] KnownBravePaths =
    [
        "/usr/bin/brave-browser",
        "/usr/bin/brave",
        "/opt/brave.com/brave/brave-browser"
    ];

    private readonly string _bravePathOverride;

    public BraveYouTubeBrowserLauncher(string bravePathOverride = "")
    {
        _bravePathOverride = bravePathOverride;
    }

    public async Task<IYouTubeBrowser> LaunchAsync(IDisplay display, CancellationToken cancellationToken)
    {
        var bravePath = ResolveBravePath();
        var playwright = await Playwright.CreateAsync();

        var braveEnv = InheritAndPatchEnvironment(display);

        var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = false,
            ExecutablePath = bravePath,
            Env = braveEnv,
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
                "--disable-features=MediaSessionService,IntensiveWakeUpThrottling,CalculateNativeWinOcclusion"
            ]
        });

        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            UserAgent = "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) " +
                        "Chrome/124.0.0.0 Safari/537.36",
            Locale = "en-US",
            TimezoneId = "Etc/UTC",
            ExtraHTTPHeaders = new Dictionary<string, string>
            {
                ["Accept-Language"] = "en-US,en;q=0.9"
            },
            ViewportSize = new ViewportSize { Width = 1280, Height = 720 }
        });

        await ApplyConsentCookieAsync(context);

        var page = await context.NewPageAsync();
        await ApplyAntiDetectionScriptAsync(page);

        return new BraveYouTubeBrowser(page, browser, context, playwright, display);
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
                "Unable to find Brave executable. Set BRAVE_EXECUTABLE_PATH.");
    }

    private static Dictionary<string, string> InheritAndPatchEnvironment(IDisplay display)
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

        return env;
    }

    private static Task ApplyConsentCookieAsync(IBrowserContext context) =>
        context.AddCookiesAsync(
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

    private static async Task ApplyAntiDetectionScriptAsync(IPage page)
    {
        await page.AddInitScriptAsync(
            """
            () => {
              Object.defineProperty(document, 'hidden', { configurable: true, get: () => false });
              Object.defineProperty(document, 'visibilityState', { configurable: true, get: () => 'visible' });
              document.addEventListener('visibilitychange', event => event.stopImmediatePropagation(), true);
              Object.defineProperty(navigator, 'webdriver', { configurable: true, get: () => undefined });
            }
            """);
    }
}
