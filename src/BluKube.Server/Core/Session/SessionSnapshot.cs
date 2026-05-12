using BluKube.Server.Core.Search;

namespace BluKube.Server.Core.Session;

public record Idle();
public record SearchResults(string Query, IReadOnlyList<MediaItem> Items);
public record Playback(
    string VideoId, 
    TimeSpan Position, 
    TimeSpan Duration, 
    bool IsPlaying, 
    float Volume);
public record Error(string Message);

public union SessionSnapshot(Idle, SearchResults, Playback, Error);