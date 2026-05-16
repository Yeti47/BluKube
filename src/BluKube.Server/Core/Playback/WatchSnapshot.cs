namespace BluKube.Server.Core.Playback;

/// <summary>
/// Page-level playback snapshot. Already in domain shape (TimeSpans, a
/// single boolean for playing) so callers don't have to convert.
/// </summary>
public sealed record WatchSnapshot(
    TimeSpan Position,
    TimeSpan Duration,
    bool IsPlaying,
    bool IsEnded,
    float Volume
);
