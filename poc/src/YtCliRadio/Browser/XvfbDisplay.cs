using System.Diagnostics;

namespace YtCliRadio.Browser;

/// <summary>
/// Manages an ephemeral Xvfb (X virtual framebuffer) instance so Brave can
/// run in headed mode \u2014 with Shields and audio fully functional \u2014 without
/// drawing a window on the user's real display.
/// </summary>
public sealed class XvfbDisplay : IAsyncDisposable
{
    private readonly Process _process;

    public string DisplayValue { get; }

    private XvfbDisplay(Process process, string displayValue)
    {
        _process = process;
        DisplayValue = displayValue;
    }

    public static async Task<XvfbDisplay> StartAsync(CancellationToken cancellationToken)
    {
        if (!IsXvfbAvailable())
        {
            throw new InvalidOperationException(
                "Xvfb is required for invisible playback but was not found on PATH. " +
                "Install it with: sudo dnf install xorg-x11-server-Xvfb  (Fedora) " +
                "or: sudo apt-get install xvfb  (Debian/Ubuntu).");
        }

        // Pick a free display number in a reasonable range; race-tolerant via -displayfd.
        // Simpler approach: pick a high number unlikely to collide with the user's :0/:1.
        var displayNumber = 99 + Random.Shared.Next(0, 100);
        var displayValue = $":{displayNumber}";

        var startInfo = new ProcessStartInfo
        {
            FileName = "Xvfb",
            ArgumentList =
            {
                displayValue,
                "-screen", "0", "1280x720x24",
                "-nolisten", "tcp",
                "-noreset"
            },
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start Xvfb process.");

        // Wait briefly for the X socket to appear.
        var socketPath = $"/tmp/.X11-unix/X{displayNumber}";
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (process.HasExited)
            {
                var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
                throw new InvalidOperationException(
                    $"Xvfb exited before becoming ready (exit code {process.ExitCode}). {stderr}");
            }

            if (File.Exists(socketPath))
            {
                return new XvfbDisplay(process, displayValue);
            }

            await Task.Delay(100, cancellationToken);
        }

        try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
        throw new InvalidOperationException(
            $"Xvfb did not create its display socket ({socketPath}) within 5 seconds.");
    }

    private static bool IsXvfbAvailable()
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv))
        {
            return false;
        }

        foreach (var dir in pathEnv.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            if (File.Exists(Path.Combine(dir, "Xvfb")))
            {
                return true;
            }
        }

        return false;
    }

    public async ValueTask DisposeAsync()
    {
        if (!_process.HasExited)
        {
            try
            {
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync();
            }
            catch
            {
                // Best-effort cleanup.
            }
        }

        _process.Dispose();
    }
}
