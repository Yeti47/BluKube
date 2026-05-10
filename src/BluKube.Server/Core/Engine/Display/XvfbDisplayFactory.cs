using System.Diagnostics;
using System.Text.RegularExpressions;

namespace BluKube.Server.Core.Engine.Display;

public sealed partial class XvfbDisplayFactory(string socketDirectory = "") : IDisplayFactory
{
    public const int MinDisplayNumber = 100;
    public const int MaxDisplayNumber = 199;

    private const string DefaultSocketDirectory = "/tmp/.X11-unix";

    private readonly string _socketDirectory = string.IsNullOrWhiteSpace(socketDirectory)
        ? DefaultSocketDirectory
        : socketDirectory;

    public async Task<IDisplay> CreateAsync(CancellationToken cancellationToken)
    {
        if (!IsXvfbAvailable())
        {
            throw new InvalidOperationException(
                "Xvfb is required for invisible playback but was not found on PATH. " +
                "Install it with: sudo apt-get install xvfb  (Debian/Ubuntu) " +
                "or: sudo dnf install xorg-x11-server-Xvfb  (Fedora).");
        }

        var number = GetNextAvailableDisplayNumber();
        var displayValue = $":{number}";

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

        var socketReady = WaitForSocketAsync(number, process, cancellationToken);
        var timeout = Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);

        var completed = await Task.WhenAny(socketReady, timeout);

        if (completed == socketReady)
        {
            return await socketReady;
        }

        try { process.Kill(entireProcessTree: true); } catch { }
        throw new InvalidOperationException(
            $"Xvfb did not create its display socket (/tmp/.X11-unix/X{number}) within 5 seconds.");
    }

    public IReadOnlyList<int> GetUsedDisplayNumbers()
    {
        if (!Directory.Exists(_socketDirectory))
        {
            return [];
        }

        var pattern = DisplaySocketRegex();
        var numbers = new List<int>();

        foreach (var file in Directory.EnumerateFiles(_socketDirectory, "X*"))
        {
            var fileName = Path.GetFileName(file);
            var match = pattern.Match(fileName);
            if (!match.Success)
            {
                continue;
            }

            if (int.TryParse(match.Groups[1].Value, out var number) &&
                number is >= MinDisplayNumber and <= MaxDisplayNumber)
            {
                numbers.Add(number);
            }
        }

        numbers.Sort();
        return numbers;
    }

    public int GetNextAvailableDisplayNumber()
    {
        var used = GetUsedDisplayNumbers();
        var usedSet = new HashSet<int>(used);
        return Enumerable.Range(MinDisplayNumber, MaxDisplayNumber - MinDisplayNumber + 1)
            .First(n => !usedSet.Contains(n));
    }

    public bool DisplaySocketExists(int displayNumber)
    {
        return File.Exists($"{_socketDirectory}/X{displayNumber}");
    }

    public static bool IsXvfbAvailable()
    {
        var path = Environment.GetEnvironmentVariable("PATH");

        if (string.IsNullOrEmpty(path))
            return false;

        foreach (var dir in path.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir))
                continue;

            if (File.Exists(Path.Combine(dir, "Xvfb")))
                return true;
        }

        return false;
    }

    [GeneratedRegex(@"^X(\d+)$", RegexOptions.Compiled)]
    private static partial Regex DisplaySocketRegex();

    private async Task<XvfbDisplay> WaitForSocketAsync(
        int displayNumber,
        Process process,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (process.HasExited)
            {
                var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
                throw new InvalidOperationException(
                    $"Xvfb exited before becoming ready (exit code {process.ExitCode}). {stderr}");
            }

            if (DisplaySocketExists(displayNumber))
            {
                var socketPath = $"{_socketDirectory}/X{displayNumber}";
                var socketFile = new FileInfo(socketPath);

                return new XvfbDisplay(process, displayNumber, socketFile);
            }

            await Task.Delay(100, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        throw new InvalidOperationException(
            $"Xvfb startup was cancelled before display socket appeared.");
    }
}
