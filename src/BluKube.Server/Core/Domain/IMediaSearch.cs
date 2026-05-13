using BluKube.Server.Core.Search;

namespace BluKube.Server.Core.Domain;

/// <summary>
/// Searches for media items. Stateless from the caller's point of view —
/// each call is independent.
/// </summary>
public interface IMediaSearch
{
    Task<IReadOnlyList<MediaItem>> SearchAsync(string query, int limit, CancellationToken ct);
}
