using System.Runtime.CompilerServices;
using System.Threading.Channels;
using BluKube.Server.Core.Session;

namespace BluKube.Server.Tests.Hubs;

/// <summary>
/// In-memory <see cref="IBrowserSession"/> that actually transitions state,
/// so hub command tests can assert meaningful return values.
/// </summary>
internal sealed class StatefulFakeBrowserSession : IBrowserSession
{
    private readonly Channel<SessionState> _channel =
        Channel.CreateUnbounded<SessionState>(new UnboundedChannelOptions { SingleReader = false });

    public Guid Id { get; } = Guid.NewGuid();
    public SessionState Current { get; private set; } = new IdleState();
    public DateTimeOffset LastActivityAt { get; } = DateTimeOffset.UtcNow;

    private SessionState Transition(SessionState next)
    {
        Current = next;
        _channel.Writer.TryWrite(next);
        return next;
    }

    public Task<SessionState> SearchAsync(string query, int limit, CancellationToken ct = default)
        => Task.FromResult(Transition(new SearchResultsState(query, [])));

    public Task<SessionState> PlayAsync(string videoId, CancellationToken ct = default)
        => Task.FromResult(Transition(new PlaybackState(videoId, TimeSpan.Zero, TimeSpan.FromMinutes(3), true, 1f)));

    public Task<SessionState> PauseAsync(CancellationToken ct = default)
        => Task.FromResult(Current is PlaybackState pb ? Transition(pb with { IsPlaying = false }) : Current);

    public Task<SessionState> ResumeAsync(CancellationToken ct = default)
        => Task.FromResult(Current is PlaybackState pb ? Transition(pb with { IsPlaying = true }) : Current);

    public Task<SessionState> SeekToAsync(TimeSpan position, CancellationToken ct = default)
        => Task.FromResult(Current is PlaybackState pb ? Transition(pb with { Position = position }) : Current);

    public Task<SessionState> SetVolumeAsync(float volume, CancellationToken ct = default)
        => Task.FromResult(Current is PlaybackState pb ? Transition(pb with { Volume = volume }) : Current);

    public async IAsyncEnumerable<SessionState> States(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        yield return Current;
        await foreach (var s in _channel.Reader.ReadAllAsync(ct))
            yield return s;
    }

    public async IAsyncEnumerable<byte[]> AudioFrames(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // Test fake: emit a couple of opaque opus-shaped packets, then idle
        // until cancellation. Real opus payload is irrelevant for hub tests;
        // they just need to verify framing & delivery.
        for (int i = 0; i < 3 && !ct.IsCancellationRequested; i++)
        {
            yield return new byte[] { 0xFC, (byte)i, 0x00, 0x00 };
        }
        try { await Task.Delay(Timeout.Infinite, ct); } catch (OperationCanceledException) { }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
