using BluKube.Server.Core.Engine.Browser;

namespace BluKube.Server.Core.Playback;

public interface IYouTubeWatchPage : IYouTubePage<WatchPageParams>
{
    Task<PlaybackSnapshot> GetSnapshotAsync(CancellationToken cancellationToken);
    Task<bool> TryEnsurePlayingAsync(CancellationToken cancellationToken);
    Task PauseAsync(CancellationToken cancellationToken);
    Task ResumeAsync(CancellationToken cancellationToken);
    Task SeekRelativeAsync(double deltaSeconds, CancellationToken cancellationToken);
    Task SeekToAsync(double seconds, CancellationToken cancellationToken);
    Task SetVolumeAsync(double volume, CancellationToken cancellationToken);
    Task<bool> IsEndedAsync(CancellationToken cancellationToken);
}
