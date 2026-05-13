using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using BluKube.Server.Core.Domain;

namespace BluKube.Server.Core.Session;

/// <summary>
/// A single user session. Holds the current <see cref="SessionState"/>,
/// translates engine-level events to state updates, and fans out to any
/// number of subscribers. All engine work is delegated to the injected
/// <see cref="IMediaPlayer"/> and <see cref="IMediaSearch"/>.
/// </summary>
public sealed class BrowserSession : IBrowserSession
{
    private readonly IMediaPlayer _player;
    private readonly IMediaSearch _search;
    private readonly CancellationTokenSource _disposeCts = new();
    private readonly ConcurrentDictionary<Guid, Channel<SessionState>> _subscribers = new();
    private readonly object _stateLock = new();

    private SessionState _current = new IdleState();
    private long _lastActivityTicks = DateTimeOffset.UtcNow.UtcTicks;
    private Task? _eventPump;

    public Guid Id { get; } = Guid.NewGuid();

    public SessionState Current
    {
        get { lock (_stateLock) { return _current; } }
    }

    public DateTimeOffset LastActivityAt
        => new(Interlocked.Read(ref _lastActivityTicks), TimeSpan.Zero);

    public BrowserSession(IMediaPlayer player, IMediaSearch search)
    {
        _player = player;
        _search = search;
        _eventPump = Task.Run(PumpPlayerEventsAsync);
    }

    public Task<SessionState> SearchAsync(string query, int limit, CancellationToken ct = default)
        => RunAsync("search_failed", async linkedCt =>
        {
            var results = await _search.SearchAsync(query, limit, linkedCt);
            return (SessionState)new SearchResultsState(query, results);
        }, ct);

    public Task<SessionState> PlayAsync(string videoId, CancellationToken ct = default)
        => RunAsync("play_failed", async linkedCt =>
            (SessionState)ToPlaybackState(await _player.PlayAsync(videoId, linkedCt)), ct);

    public Task<SessionState> PauseAsync(CancellationToken ct = default)
        => RunAsync("pause_failed", async linkedCt =>
            (SessionState)ToPlaybackState(await _player.PauseAsync(linkedCt)), ct);

    public Task<SessionState> ResumeAsync(CancellationToken ct = default)
        => RunAsync("resume_failed", async linkedCt =>
            (SessionState)ToPlaybackState(await _player.ResumeAsync(linkedCt)), ct);

    public Task<SessionState> SeekToAsync(TimeSpan position, CancellationToken ct = default)
        => RunAsync("seek_failed", async linkedCt =>
            (SessionState)ToPlaybackState(await _player.SeekToAsync(position, linkedCt)), ct);

    public Task<SessionState> SetVolumeAsync(float volume, CancellationToken ct = default)
        => RunAsync("volume_failed", async linkedCt =>
            (SessionState)ToPlaybackState(await _player.SetVolumeAsync(volume, linkedCt)), ct);

    public async IAsyncEnumerable<SessionState> States(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var channel = Channel.CreateUnbounded<SessionState>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
        var key = Guid.NewGuid();
        _subscribers[key] = channel;
        Touch();

        // Yield current state immediately so subscribers always have a baseline.
        yield return Current;

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _disposeCts.Token);
        try
        {
            await foreach (var state in channel.Reader.ReadAllAsync(linked.Token))
            {
                yield return state;
            }
        }
        finally
        {
            _subscribers.TryRemove(key, out _);
        }
    }

    private async Task<SessionState> RunAsync(
        string errorCode,
        Func<CancellationToken, Task<SessionState>> action,
        CancellationToken ct)
    {
        Touch();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _disposeCts.Token);
        SessionState result;
        try
        {
            result = await action(linked.Token);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            result = new ErrorState(errorCode, ex.Message, Current);
        }

        Publish(result);
        return result;
    }

    private async Task PumpPlayerEventsAsync()
    {
        try
        {
            await foreach (var ev in _player.Events(_disposeCts.Token))
            {
                switch (ev)
                {
                    case PositionChanged pc:
                        Publish(ToPlaybackState(pc.Snapshot));
                        break;
                    case PlaybackFailed pf:
                        Publish(new ErrorState(pf.Code, pf.Message, Current));
                        break;
                }
            }
        }
        catch (OperationCanceledException) { /* dispose */ }
        catch (Exception ex)
        {
            Publish(new ErrorState("event_pump_failed", ex.Message, Current));
        }
    }

    private static PlaybackState ToPlaybackState(PlayerSnapshot s)
        => new(s.VideoId, s.Position, s.Duration, s.IsPlaying, s.Volume);

    private void Touch()
        => Interlocked.Exchange(ref _lastActivityTicks, DateTimeOffset.UtcNow.UtcTicks);

    private void Publish(SessionState state)
    {
        lock (_stateLock)
        {
            _current = state;
        }

        foreach (var sub in _subscribers.Values)
        {
            sub.Writer.TryWrite(state);
        }
    }

    public async ValueTask DisposeAsync()
    {
        try { _disposeCts.Cancel(); } catch { }

        foreach (var sub in _subscribers.Values)
        {
            sub.Writer.TryComplete();
        }
        _subscribers.Clear();

        if (_eventPump is { } pump)
        {
            try { await pump; } catch { }
            _eventPump = null;
        }

        try { await _player.DisposeAsync(); } catch { }

        _disposeCts.Dispose();
    }
}
