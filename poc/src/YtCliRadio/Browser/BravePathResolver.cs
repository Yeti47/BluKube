namespace YtCliRadio.Browser;

public static class BravePathResolver
{
    private static readonly string[] KnownPaths =
    [
        "/usr/bin/brave-browser",
        "/usr/bin/brave",
        "/opt/brave.com/brave/brave-browser"
    ];

    public static string Resolve(string? explicitPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            return explicitPath;
        }

        var fromEnv = Environment.GetEnvironmentVariable("BRAVE_EXECUTABLE_PATH");
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            return fromEnv;
        }

        return KnownPaths.FirstOrDefault(File.Exists)
            ?? throw new InvalidOperationException(
                "Unable to find Brave executable. Set --brave-path or BRAVE_EXECUTABLE_PATH.");
    }
}
