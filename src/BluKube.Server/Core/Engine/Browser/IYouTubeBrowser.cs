namespace BluKube.Server.Core.Engine.Browser;

/// <summary>
/// One-tab YouTube driver. Each call navigates the underlying tab to a new
/// page and returns a thin wrapper for that page. Previously returned page
/// wrappers become stale after the next navigation.
/// </summary>
public interface IYouTubeBrowser : IAsyncDisposable
{
    Task<ISearchPage> OpenSearchAsync(string query, int limit, CancellationToken ct);
    Task<IWatchPage> OpenWatchAsync(string videoId, CancellationToken ct);
}
