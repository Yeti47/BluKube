namespace BluKube.Server.Core.Domain;

/// <summary>
/// Background events emitted by an <see cref="IMediaPlayer"/>. Driven by
/// polling and by spontaneous engine errors. Command replies do not flow
/// through this stream — they are returned from the command methods.
/// </summary>
public abstract record PlaybackEvent;

public sealed record PositionChanged(PlayerSnapshot Snapshot) : PlaybackEvent;

public sealed record PlaybackFailed(string Code, string Message) : PlaybackEvent;
