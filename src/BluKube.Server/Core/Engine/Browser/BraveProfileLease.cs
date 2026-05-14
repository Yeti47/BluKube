using Microsoft.Extensions.Logging;

namespace BluKube.Server.Core.Engine.Browser;

public sealed class BraveProfileLease(string profilePath, ILogger? logger = null) : IAsyncDisposable
{
    private bool _disposed;

    public string ProfilePath { get; } = profilePath;

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (Directory.Exists(ProfilePath))
                {
                    Directory.Delete(ProfilePath, recursive: true);
                }
                return;
            }
            catch (Exception ex) when (attempt < 4)
            {
                logger?.LogDebug(ex, "Failed to delete Brave profile {ProfilePath}; retrying", ProfilePath);
                await Task.Delay(100);
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Failed to delete Brave profile {ProfilePath}", ProfilePath);
                return;
            }
        }
    }
}