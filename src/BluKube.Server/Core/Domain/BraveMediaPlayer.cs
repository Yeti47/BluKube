using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using BluKube.Server.Core.Engine.Browser;
using BluKube.Server.Core.Engine.Display;
using BluKube.Server.Core.Playback;
using BluKube.Server.Core.Search;

namespace BluKube.Server.Core.Domain;

/// <summary>
/// Brave + Playwright implementation of the player and search seams.
/// Owns a single isolated engine (Display + Browser) and one watch page
/// at a time. Polls the watch page for position ticks while playing.
/// </summary>
public sealed class BraveMediaPlayer(IDisplay display, IYouTubeBrowser browser) : IMediaPlayer, IMediaSearch
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    private readonly CancellationTokenSource _disposeCts = new();
    private readonly ConcurrentDictionary<Guid, Channel<PlaybackEvent>> _subscribers = new();
    private readonly SemaphoreSlim _gate = new(1, 1);

    private IWatchPage? _currentWatch;
    private string? _currentVideoId;
    private CancellationTokenSource? _pollCts;
    private Task? _pollTask;
    private int _userPaused;
    private int _disposed;

    public async Task<IReadOnlyList<MediaItem>> SearchAsync(string query, int limit, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            ThrowIfDisposed();
            await StopPollingAsync();
            _currentWatch = null;
            _currentVideoId = null;

            var page = await browser.OpenSearchAsync(query, limit, ct);
            return await page.ReadResultsAsync(ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<PlayerSnapshot> PlayAsync(string videoId, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            ThrowIfDisposed();
            await StopPollingAsync();

            var watch = await browser.OpenWatchAsync(videoId, ct);
            _currentWatch = watch;
            _currentVideoId = videoId;

            var snapshot = await watch.EnsurePlayingAsync(ct);
            Interlocked.Exchange(ref _userPaused, 0);
            StartPolling();
            return snapshot.ToPlayerSnapshot(_currentVideoId ?? string.Empty);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<PlayerSnapshot> PauseAsync(CancellationToken ct)
        => OnWatch(async (watch, linkedCt) =>
        {
            Interlocked.Exchange(ref _userPaused, 1);
            await StopPollingAsync();
            return (await watch.PauseAsync(linkedCt)).ToPlayerSnapshot(_currentVideoId ?? string.Empty);
        }, ct);

    public Task<PlayerSnapshot> ResumeAsync(CancellationToken ct)
        => OnWatch(async (watch, linkedCt) =>
        {
            await StopPollingAsync();
            var snapshot = (await watch.EnsurePlayingAsync(linkedCt)).ToPlayerSnapshot(_currentVideoId ?? string.Empty);
            Interlocked.Exchange(ref _userPaused, 0);
            StartPolling();
            return snapshot;
        }, ct);

    public Task<PlayerSnapshot> SeekToAsync(TimeSpan position, CancellationToken ct)
        => OnWatch(async (watch, linkedCt) =>
            (await watch.SeekToAsync(position, linkedCt)).ToPlayerSnapshot(_currentVideoId ?? string.Empty), ct);

    public Task<PlayerSnapshot> SetVolumeAsync(float volume, CancellationToken ct)
        => OnWatch(async (watch, linkedCt) =>
            (await watch.SetVolumeAsync(Math.Clamp(volume, 0f, 1f), linkedCt)).ToPlayerSnapshot(_currentVideoId ?? string.Empty), ct);

    public async IAsyncEnumerable<PlaybackEvent> Events(
        [EnumeratorCancellation] CancellationToken ct)
    {
        var channel = Channel.CreateUnbounded<PlaybackEvent>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
        var key = Guid.NewGuid();
        _subscribers[key] = channel;

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _disposeCts.Token);
        try
        {
            await foreach (var ev in channel.Reader.ReadAllAsync(linked.Token))
            {
                yield return ev;
            }
        }
        finally
        {
            _subscribers.TryRemove(key, out _);
        }
    }

    private async Task<PlayerSnapshot> OnWatch(
        Func<IWatchPage, CancellationToken, Task<PlayerSnapshot>> action,
        CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            ThrowIfDisposed();
            if (_currentWatch is not { } watch)
            {
                throw new InvalidOperationException("No active playback.");
            }
            return await action(watch, ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    private void StartPolling()
    {
        if (Volatile.Read(ref _disposed) == 1) return;
        if (_pollTask is { IsCompleted: false }) return;

        var cts = CancellationTokenSource.CreateLinkedTokenSource(_disposeCts.Token);
        _pollCts = cts;
        _pollTask = Task.Run(() => PollLoopAsync(cts.Token));
    }

    private async Task StopPollingAsync()
    {
        var cts = Interlocked.Exchange(ref _pollCts, null);
        var task = Interlocked.Exchange(ref _pollTask, null);
        if (cts is null) return;
        try { cts.Cancel(); } catch { }
        if (task is not null)
        {
            try { await task.ConfigureAwait(false); } catch { }
        }
        cts.Dispose();
    }

    private async Task PollLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(PollInterval, ct);

                var watch = _currentWatch;
                if (watch is null) continue;

                PlayerSnapshot snapshot;
                try
                {
                    var pageSnapshot = await watch.ReadStateAsync(ct);
                    if (!pageSnapshot.IsPlaying &&
                        !pageSnapshot.IsEnded &&
                        pageSnapshot.Duration > TimeSpan.Zero &&
                        Volatile.Read(ref _userPaused) == 0)
                    {
                        pageSnapshot = await watch.EnsurePlayingAsync(ct);
                    }
                    snapshot = pageSnapshot.ToPlayerSnapshot(_currentVideoId ?? string.Empty);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    Publish(new PlaybackFailed("polling_failed", ex.Message));
                    return;
                }

                Publish(new PositionChanged(snapshot));
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on stop / dispose.
        }
    }

    private void Publish(PlaybackEvent ev)
    {
        foreach (var sub in _subscribers.Values)
        {
            sub.Writer.TryWrite(ev);
        }
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) == 1)
        {
            throw new ObjectDisposedException(nameof(BraveMediaPlayer));
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;

        await _gate.WaitAsync();
        try
        {
            await StopPollingAsync();
            try { _disposeCts.Cancel(); } catch { }

            foreach (var sub in _subscribers.Values)
            {
                sub.Writer.TryComplete();
            }
            _subscribers.Clear();

            try { await browser.DisposeAsync(); } catch { }
            try { await display.DisposeAsync(); } catch { }

            _disposeCts.Dispose();
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }
}
