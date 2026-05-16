using System.Threading.Channels;
using BluKube.Server.Core.Domain;
using BluKube.Server.Core.Search;

namespace BluKube.Server.Tests.Core.Session;

/// <summary>
/// In-memory <see cref="IMediaPlayer"/> + <see cref="IMediaSearch"/> for
/// session-level tests. Commands are recorded and return canned snapshots;
/// background events can be pushed via <see cref="EmitEvent"/>.
/// </summary>
internal sealed class FakeMediaPlayer : IMediaPlayer, IMediaSearch
{
    private readonly Channel<PlaybackEvent> _events = Channel.CreateUnbounded<PlaybackEvent>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false }
    );

    public List<string> Commands { get; } = new();

    public PlayerSnapshot NextSnapshot { get; set; } =
        new("video-1", TimeSpan.Zero, TimeSpan.FromMinutes(3), true, 1f);

    public IReadOnlyList<MediaItem> NextResults { get; set; } = Array.Empty<MediaItem>();

    public Func<string, int, IReadOnlyList<MediaItem>>? OnSearch { get; set; }

    public Exception? ThrowOnNext { get; set; }

    public bool Disposed { get; private set; }

    public void EmitEvent(PlaybackEvent ev) => _events.Writer.TryWrite(ev);

    public void CompleteEvents() => _events.Writer.TryComplete();

    public Task<IReadOnlyList<MediaItem>> SearchAsync(string query, int limit, CancellationToken ct)
    {
        Commands.Add($"search:{query}:{limit}");
        ThrowIfPending();
        return Task.FromResult(OnSearch?.Invoke(query, limit) ?? NextResults);
    }

    public Task<PlayerSnapshot> PlayAsync(string videoId, CancellationToken ct)
    {
        Commands.Add($"play:{videoId}");
        ThrowIfPending();
        NextSnapshot = NextSnapshot with { VideoId = videoId, IsPlaying = true };
        return Task.FromResult(NextSnapshot);
    }

    public Task StopAsync(CancellationToken ct)
    {
        Commands.Add("stop");
        ThrowIfPending();
        return Task.CompletedTask;
    }

    public Task<PlayerSnapshot> PauseAsync(CancellationToken ct)
    {
        Commands.Add("pause");
        ThrowIfPending();
        NextSnapshot = NextSnapshot with { IsPlaying = false };
        return Task.FromResult(NextSnapshot);
    }

    public Task<PlayerSnapshot> ResumeAsync(CancellationToken ct)
    {
        Commands.Add("resume");
        ThrowIfPending();
        NextSnapshot = NextSnapshot with { IsPlaying = true };
        return Task.FromResult(NextSnapshot);
    }

    public Task<PlayerSnapshot> SeekToAsync(TimeSpan position, CancellationToken ct)
    {
        Commands.Add($"seek:{position}");
        ThrowIfPending();
        NextSnapshot = NextSnapshot with { Position = position };
        return Task.FromResult(NextSnapshot);
    }

    public Task<PlayerSnapshot> SetVolumeAsync(float volume, CancellationToken ct)
    {
        Commands.Add($"volume:{volume}");
        ThrowIfPending();
        NextSnapshot = NextSnapshot with { Volume = volume };
        return Task.FromResult(NextSnapshot);
    }

    public async IAsyncEnumerable<PlaybackEvent> Events(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct
    )
    {
        await foreach (var ev in _events.Reader.ReadAllAsync(ct))
        {
            yield return ev;
        }
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        _events.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }

    private void ThrowIfPending()
    {
        var ex = ThrowOnNext;
        if (ex is null)
            return;
        ThrowOnNext = null;
        throw ex;
    }
}
