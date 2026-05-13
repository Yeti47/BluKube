using BluKube.Server.Core.Playback;

namespace BluKube.Server.Core.Engine.Browser;

/// <summary>
/// A loaded YouTube watch page. Created by
/// <see cref="IYouTubeBrowser.OpenWatchAsync(string, CancellationToken)"/>;
/// becomes stale on the next navigation. Every command returns the
/// resulting <see cref="WatchSnapshot"/> so callers don't need a
/// follow-up read.
/// </summary>
public interface IWatchPage
{
    Task<WatchSnapshot> EnsurePlayingAsync(CancellationToken ct);
    Task<WatchSnapshot> PauseAsync(CancellationToken ct);
    Task<WatchSnapshot> ResumeAsync(CancellationToken ct);
    Task<WatchSnapshot> SeekToAsync(TimeSpan position, CancellationToken ct);
    Task<WatchSnapshot> SetVolumeAsync(float volume, CancellationToken ct);
    Task<WatchSnapshot> ReadStateAsync(CancellationToken ct);
}
