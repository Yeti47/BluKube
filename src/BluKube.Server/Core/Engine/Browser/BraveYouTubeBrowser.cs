using BluKube.Server.Core.Engine.Display;
using BluKube.Server.Core.Playback;
using BluKube.Server.Core.Search;
using Microsoft.Playwright;

namespace BluKube.Server.Core.Engine.Browser;

public sealed class BraveYouTubeBrowser(
    IPage page,
    IBrowserContext context,
    IPlaywright playwright,
    BraveProfileLease profile) : IYouTubeBrowser
{
    private readonly IPage _page = page;
    private bool _disposed;

    public async Task<ISearchPage> OpenSearchAsync(string query, int limit, CancellationToken ct)
    {
        var searchPage = new YouTubeSearchPage(_page, query, limit);
        await searchPage.NavigateAsync(ct);
        return searchPage;
    }

    public async Task<IWatchPage> OpenWatchAsync(string videoId, CancellationToken ct)
    {
        var watchPage = new YouTubeWatchPage(_page, videoId);
        await watchPage.NavigateAsync(ct);
        return watchPage;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        try { await context.CloseAsync(); } catch { }
        playwright.Dispose();
        await profile.DisposeAsync();
    }
}
