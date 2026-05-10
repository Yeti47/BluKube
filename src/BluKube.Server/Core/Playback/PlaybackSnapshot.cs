namespace BluKube.Server.Core.Playback;

public sealed record PlaybackSnapshot(
    bool Paused,
    bool Ended,
    bool Muted,
    double CurrentTime,
    int ReadyState,
    int NetworkState,
    int? ErrorCode);
