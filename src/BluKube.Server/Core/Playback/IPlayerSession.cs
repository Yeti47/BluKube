namespace BluKube.Server.Core.Playback;

public interface IPlayerSession : IAsyncDisposable
{
    Guid Id { get; }

    IReadOnlyList<Core.Search.MediaItem> Queue { get; }
    int CurrentIndex { get; }

    void Enqueue(IReadOnlyList<Core.Search.MediaItem> items, bool replace);
    void Insert(int index, Core.Search.MediaItem item);
    void Remove(int index);
    void ClearQueue();

    Task PlayAsync(int? index, CancellationToken cancellationToken);
    Task PauseAsync(CancellationToken cancellationToken);
    Task ResumeAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
    Task NextAsync(CancellationToken cancellationToken);
    Task PreviousAsync(CancellationToken cancellationToken);
    Task SeekRelativeAsync(double deltaSeconds, CancellationToken cancellationToken);
    Task SeekToAsync(double seconds, CancellationToken cancellationToken);
    Task SetVolumeAsync(double volume, CancellationToken cancellationToken);
    Task<PlayerState> GetStateAsync(CancellationToken cancellationToken);
    IAsyncEnumerable<PlayerEvent> Events(CancellationToken cancellationToken);
}
