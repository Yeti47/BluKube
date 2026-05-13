namespace BluKube.Server.Core.Session;

/// <summary>
/// A single user's session. Owns one isolated engine (Display + Browser).
/// Commands are typed methods that return the resulting <see cref="SessionState"/>.
/// Continuous updates (e.g. playback ticks) flow through <see cref="States"/>.
/// </summary>
public interface IBrowserSession : IAsyncDisposable
{
    Guid Id { get; }

    SessionState Current { get; }

    /// <summary>
    /// Wall-clock timestamp of the last command or state subscription on
    /// this session. Used by the idle sweeper to reap forgotten sessions.
    /// </summary>
    DateTimeOffset LastActivityAt { get; }

    Task<SessionState> SearchAsync(string query, int limit, CancellationToken ct = default);
    Task<SessionState> PlayAsync(string videoId, CancellationToken ct = default);
    Task<SessionState> PauseAsync(CancellationToken ct = default);
    Task<SessionState> ResumeAsync(CancellationToken ct = default);
    Task<SessionState> SeekToAsync(TimeSpan position, CancellationToken ct = default);
    Task<SessionState> SetVolumeAsync(float volume, CancellationToken ct = default);

    /// <summary>
    /// Push stream of state updates. Includes the initial state and every
    /// subsequent change (command-driven or polled). Multiple subscribers
    /// each receive their own enumeration.
    /// </summary>
    IAsyncEnumerable<SessionState> States(CancellationToken ct = default);
}