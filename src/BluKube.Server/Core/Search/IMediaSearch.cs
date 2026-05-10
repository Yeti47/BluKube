namespace BluKube.Server.Core.Search;

public interface IMediaSearch
{
    Task<IReadOnlyList<MediaItem>> SearchAsync(string query, int limit, CancellationToken cancellationToken);
}
