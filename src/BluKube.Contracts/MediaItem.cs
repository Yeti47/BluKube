namespace BluKube.Contracts;

/// <summary>
/// A single search result (or queue entry) returned by the server.
/// Wire-level type — shared by server and clients.
/// </summary>
public sealed record MediaItem(
    string Title,
    string Channel,
    string Url,
    TimeSpan Duration);
