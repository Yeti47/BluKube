namespace BluKube.Server.Core.Playback;

public sealed record PlayerState(
    bool IsPlaying,
    bool IsPaused,
    double CurrentTimeSeconds,
    double DurationSeconds,
    double Volume,
    Core.Search.MediaItem? CurrentTrack,
    int QueueIndex,
    int QueueLength);
