using BluKube.Server.Core.Search;

namespace BluKube.Server.Core.Session;

/// <summary>
/// Snapshot of a session's current observable state. Pushed to attached
/// clients via the SignalR hub and returned from command methods so that
/// callers always know what the world looks like after their action.
/// </summary>
public abstract record SessionState;

public sealed record IdleState : SessionState;

public sealed record SearchResultsState(
    string Query,
    IReadOnlyList<MediaItem> Items) : SessionState;

public sealed record PlaybackState(
    string VideoId,
    TimeSpan Position,
    TimeSpan Duration,
    bool IsPlaying,
    float Volume) : SessionState;

public sealed record ErrorState(
    string Code,
    string Message,
    SessionState? Previous) : SessionState;
