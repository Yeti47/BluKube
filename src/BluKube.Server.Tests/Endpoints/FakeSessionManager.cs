using System.Collections.Concurrent;
using BluKube.Server.Core.Session;

namespace BluKube.Server.Tests.Endpoints;

/// <summary>
/// Lightweight in-memory <see cref="ISessionManager"/> for REST tests:
/// no Brave, no Xvfb, just a dictionary of <see cref="FakeBrowserSession"/>.
/// </summary>
internal sealed class FakeSessionManager : ISessionManager
{
    private readonly ConcurrentDictionary<Guid, FakeBrowserSession> _sessions = new();
    public int MaxSessions { get; set; } = 4;

    public Task<IBrowserSession> CreateSessionAsync(CancellationToken cancellationToken = default)
    {
        if (_sessions.Count >= MaxSessions)
        {
            throw new InvalidOperationException("Session cap reached.");
        }
        var s = new FakeBrowserSession();
        _sessions[s.Id] = s;
        return Task.FromResult<IBrowserSession>(s);
    }

    public Task<IBrowserSession?> GetSessionAsync(Guid sessionId)
        => Task.FromResult<IBrowserSession?>(_sessions.TryGetValue(sessionId, out var s) ? s : null);

    public Task<bool> RemoveSessionAsync(Guid sessionId)
        => Task.FromResult(_sessions.TryRemove(sessionId, out _));

    public Task<IReadOnlyList<IBrowserSession>> ListSessionsAsync()
        => Task.FromResult<IReadOnlyList<IBrowserSession>>(_sessions.Values.Cast<IBrowserSession>().ToList());
}

internal sealed class FakeBrowserSession : IBrowserSession
{
    public Guid Id { get; } = Guid.NewGuid();
    public SessionState Current { get; private set; } = new IdleState();
    public DateTimeOffset LastActivityAt { get; } = DateTimeOffset.UtcNow;

    public Task<SessionState> SearchAsync(string query, int limit, CancellationToken ct = default)
        => Task.FromResult(Current);
    public Task<SessionState> PlayAsync(string videoId, CancellationToken ct = default)
        => Task.FromResult(Current);
    public Task<SessionState> StopAsync(CancellationToken ct = default) => Task.FromResult(Current);
    public Task<SessionState> PauseAsync(CancellationToken ct = default) => Task.FromResult(Current);
    public Task<SessionState> ResumeAsync(CancellationToken ct = default) => Task.FromResult(Current);
    public Task<SessionState> SeekToAsync(TimeSpan position, CancellationToken ct = default)
        => Task.FromResult(Current);
    public Task<SessionState> SetVolumeAsync(float volume, CancellationToken ct = default)
        => Task.FromResult(Current);

    public async IAsyncEnumerable<SessionState> States(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        yield return Current;
        await Task.CompletedTask;
    }

    public IAsyncEnumerable<byte[]> AudioFrames(CancellationToken ct = default)
        => throw new InvalidOperationException("Audio not configured for endpoint test fake.");

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
