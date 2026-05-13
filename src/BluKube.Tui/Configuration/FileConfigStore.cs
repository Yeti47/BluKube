using System.Text.Json;
using BluKube.Client.Core;

namespace BluKube.Tui.Configuration;

/// <summary>
/// Stores connection settings as JSON under
/// <c>$XDG_CONFIG_HOME/blukube/config.json</c> (or
/// <c>~/.config/blukube/config.json</c>).
/// </summary>
public sealed class FileConfigStore(string? overridePath = null) : IConfigStore
{
    private readonly string _path = overridePath ?? DefaultPath();

    public async Task<ConnectionSettings?> LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_path)) return null;
        await using var fs = File.OpenRead(_path);
        var dto = await JsonSerializer.DeserializeAsync<Dto>(fs, cancellationToken: ct);
        if (dto is null || string.IsNullOrWhiteSpace(dto.ServerUrl)) return null;
        return new ConnectionSettings(new Uri(dto.ServerUrl), dto.Token);
    }

    public async Task SaveAsync(ConnectionSettings settings, CancellationToken ct = default)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
        await using var fs = File.Create(_path);
        await JsonSerializer.SerializeAsync(
            fs,
            new Dto(settings.ServerUrl.ToString(), settings.Token),
            new JsonSerializerOptions { WriteIndented = true },
            ct);

        try
        {
#pragma warning disable CA1416 // Validate platform compatibility
            File.SetUnixFileMode(_path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite);
#pragma warning restore CA1416 // Validate platform compatibility
        }
        catch (PlatformNotSupportedException) { }
    }

    public Task ClearAsync(CancellationToken ct = default)
    {
        if (File.Exists(_path)) File.Delete(_path);
        return Task.CompletedTask;
    }

    public string Path => _path;

    private static string DefaultPath()
    {
        var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        var baseDir = !string.IsNullOrWhiteSpace(xdg)
            ? xdg
            : System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        return System.IO.Path.Combine(baseDir, "blukube", "config.json");
    }

    private sealed record Dto(string ServerUrl, string? Token);
}
