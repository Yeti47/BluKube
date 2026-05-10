namespace BluKube.Server.Core.Playback;

public record PositionTick(double Seconds);
public record PlaybackStateChanged(PlayerState State);
public record TrackEnded(Core.Search.MediaItem Track);
public record PlaybackError(string Message);
public record QueueChanged(IReadOnlyList<Core.Search.MediaItem> Queue, int CurrentIndex);

public union PlayerEvent(PositionTick, PlaybackStateChanged, TrackEnded, PlaybackError, QueueChanged);
