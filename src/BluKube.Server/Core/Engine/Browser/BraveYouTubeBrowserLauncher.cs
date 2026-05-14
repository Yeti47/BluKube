using BluKube.Server.Core.Engine.Display;
using Microsoft.Playwright;

namespace BluKube.Server.Core.Engine.Browser;

public sealed class BraveYouTubeBrowserLauncher : IYouTubeBrowserLauncher
{
    private const string DefaultProfileRoot = "/var/lib/blukube/brave-profiles";
    private static readonly string[] DefaultProfileSeeds =
    [
        "/var/lib/blukube/brave-profile",
        "/var/lib/blukube/brave-warm",
        "/var/lib/blukube/brave-profile-seed"
    ];

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

    public async Task<IYouTubeBrowser> LaunchAsync(IDisplay display, string? pulseSink, CancellationToken cancellationToken)
    {
        var bravePath = ResolveBravePath();
        var playwright = await Playwright.CreateAsync();

        var braveEnv = InheritAndPatchEnvironment(display, pulseSink);
        var profilePath = CreateProfilePath();
        SeedProfile(profilePath);

        var context = await playwright.Chromium.LaunchPersistentContextAsync(profilePath, new BrowserTypeLaunchPersistentContextOptions
        {
            Headless = false,
            ExecutablePath = bravePath,
            Env = braveEnv,
            IgnoreDefaultArgs =
            [
                "--disable-background-networking",
                "--disable-component-extensions-with-background-pages",
                "--disable-component-update",
                "--disable-extensions"
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
                "--disable-features=MediaSessionService,IntensiveWakeUpThrottling,CalculateNativeWinOcclusion"
            ],
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

        await ApplyAntiDetectionScriptAsync(context);
        var page = await PrepareSinglePageAsync(context);

        return new BraveYouTubeBrowser(page, context, playwright, profilePath);
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

    private static string CreateProfilePath()
    {
        var fromEnv = Environment.GetEnvironmentVariable("BLUKUBE_BRAVE_PROFILE_PATH");
        var root = string.IsNullOrWhiteSpace(fromEnv) ? DefaultProfileRoot : fromEnv;
        return Path.Combine(root, "sessions", Guid.NewGuid().ToString("N"));
    }

    private static void SeedProfile(string profilePath)
    {
        Directory.CreateDirectory(profilePath);

        var seedPath = ResolveSeedPath();
        if (seedPath is null)
        {
            return;
        }

        CopyProfileSeed(seedPath, profilePath);
    }

    private static string? ResolveSeedPath()
    {
        var fromEnv = Environment.GetEnvironmentVariable("BLUKUBE_BRAVE_PROFILE_SEED_PATH");
        if (!string.IsNullOrWhiteSpace(fromEnv) && Directory.Exists(fromEnv))
        {
            return fromEnv;
        }

        return DefaultProfileSeeds.FirstOrDefault(Directory.Exists);
    }

    private static void CopyProfileSeed(string sourceRoot, string targetRoot)
    {
        foreach (var sourcePath in Directory.EnumerateFileSystemEntries(sourceRoot))
        {
            CopyProfileEntry(sourceRoot, sourcePath, targetRoot);
        }

        RemoveVolatileProfileState(targetRoot);
    }

    private static void CopyProfileEntry(string sourceRoot, string sourcePath, string targetRoot)
    {
        var relativePath = Path.GetRelativePath(sourceRoot, sourcePath);
        if (ShouldSkipSeedPath(relativePath))
        {
            return;
        }

        var targetPath = Path.Combine(targetRoot, relativePath);
        if (Directory.Exists(sourcePath))
        {
            Directory.CreateDirectory(targetPath);
            foreach (var child in Directory.EnumerateFileSystemEntries(sourcePath))
            {
                CopyProfileEntry(sourceRoot, child, targetRoot);
            }
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        File.Copy(sourcePath, targetPath, overwrite: true);
    }

    private static bool ShouldSkipSeedPath(string relativePath)
    {
        var path = relativePath.Replace(Path.DirectorySeparatorChar, '/');
        var name = Path.GetFileName(path);

        if (path == "sessions" || path.StartsWith("sessions/", StringComparison.Ordinal)) return true;
        if (name.StartsWith("Singleton", StringComparison.Ordinal)) return true;
        if (name is "LOCK" or "LOG" or "LOG.old") return true;

        return path == "Default/Sessions" ||
               path.StartsWith("Default/Sessions/", StringComparison.Ordinal) ||
               path == "Default/Session Storage" ||
               path.StartsWith("Default/Session Storage/", StringComparison.Ordinal) ||
               path == "Default/Cache" ||
               path.StartsWith("Default/Cache/", StringComparison.Ordinal) ||
               path == "Default/Code Cache" ||
               path.StartsWith("Default/Code Cache/", StringComparison.Ordinal) ||
               path == "Default/GPUCache" ||
               path.StartsWith("Default/GPUCache/", StringComparison.Ordinal) ||
               path == "Default/DawnWebGPUCache" ||
               path.StartsWith("Default/DawnWebGPUCache/", StringComparison.Ordinal) ||
               path == "Default/DawnGraphiteCache" ||
               path.StartsWith("Default/DawnGraphiteCache/", StringComparison.Ordinal) ||
               path == "Default/blob_storage" ||
               path.StartsWith("Default/blob_storage/", StringComparison.Ordinal);
    }

    private static void RemoveVolatileProfileState(string profilePath)
    {
        DeleteDirectory(Path.Combine(profilePath, "Default", "Sessions"));
        DeleteDirectory(Path.Combine(profilePath, "Default", "Session Storage"));

        foreach (var fileName in new[]
        {
            "Current Session", "Current Tabs", "Last Session", "Last Tabs", "LOCK", "LOG", "LOG.old"
        })
        {
            DeleteFile(Path.Combine(profilePath, "Default", fileName));
        }
    }

    private static void DeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch { }
    }

    private static void DeleteFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch { }
    }

    private static async Task<IPage> PrepareSinglePageAsync(IBrowserContext context)
    {
        var page = context.Pages.FirstOrDefault() ?? await context.NewPageAsync();
        foreach (var extraPage in context.Pages.Where(p => p != page).ToArray())
        {
            try { await extraPage.CloseAsync(new PageCloseOptions { RunBeforeUnload = false }); }
            catch { }
        }
        return page;
    }

    private static Dictionary<string, string> InheritAndPatchEnvironment(IDisplay display, string? pulseSink)
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
        context.AddCookiesAsync(
        [
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
                SameSite = SameSiteAttribute.Lax
            }
        ]);

    private static Cookie ConsentCookie(string domain) => new()
    {
        Name = "CONSENT",
        Value = "YES+cb.20210328-17-p0.en+FX+471",
        Domain = domain,
        Path = "/",
        Secure = true,
        SameSite = SameSiteAttribute.None
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
            """);
    }
}
