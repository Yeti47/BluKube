namespace BluKube.Server.Core.Playback;

public interface IMediaPlayer : IAsyncDisposable
{
    Task PlayAsync(Core.Search.MediaItem track, CancellationToken cancellationToken);
    Task PauseAsync(CancellationToken cancellationToken);
    Task ResumeAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
    Task SeekRelativeAsync(double deltaSeconds, CancellationToken cancellationToken);
    Task SeekToAsync(double seconds, CancellationToken cancellationToken);
    Task SetVolumeAsync(double volume, CancellationToken cancellationToken);
    Task<PlayerState> GetStateAsync(CancellationToken cancellationToken);
    IAsyncEnumerable<PlayerEvent> Events(CancellationToken cancellationToken);
}
