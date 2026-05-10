namespace BluKube.Server.Core.Search;

public sealed record MediaItem(
    string Title,
    string Channel,
    string Url,
    TimeSpan Duration
);
