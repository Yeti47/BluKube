using BluKube.Server.Core.Engine.Display;
using BluKube.Server.Core.Playback;
using BluKube.Server.Core.Search;
using Microsoft.Playwright;

namespace BluKube.Server.Core.Engine.Browser;

public sealed class BraveYouTubeBrowser(
    IPage page,
    IBrowser browser,
    IBrowserContext context,
    IPlaywright playwright,
    IDisplay display) : IYouTubeBrowser
{
    private readonly IPage _page = page;

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
        playwright.Dispose();

        try { await context.CloseAsync(); } catch { }
        try { await browser.CloseAsync(); } catch { }
        try { await display.DisposeAsync(); } catch { }
    }
}
