using BluKube.Server.Core.Search;

namespace BluKube.Server.Core.Engine.Browser;

/// <summary>
/// A loaded YouTube search results page. Created by
/// <see cref="IYouTubeBrowser.OpenSearchAsync(string, int, CancellationToken)"/>;
/// becomes stale on the next navigation.
/// </summary>
public interface ISearchPage
{
    Task<IReadOnlyList<MediaItem>> ReadResultsAsync(CancellationToken ct);
}
