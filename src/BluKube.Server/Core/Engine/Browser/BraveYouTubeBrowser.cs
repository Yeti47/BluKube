using BluKube.Server.Core.Engine.Display;
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
    private IYouTubePage? _currentPage;

    public IYouTubePage? CurrentPage => _currentPage;

    public async Task<TPage> GoToAsync<TPage, TParams>(TParams parameters, CancellationToken cancellationToken)
        where TPage : IYouTubePage<TParams>
        where TParams : class
    {
        var pageInstance = (TPage)TPage.Create(_page, parameters);
        await pageInstance.NavigateToAsync(cancellationToken);
        _currentPage = pageInstance;
        return pageInstance;
    }

    public async ValueTask DisposeAsync()
    {
        playwright.Dispose();

        try { await context.CloseAsync(); } catch { }
        try { await browser.CloseAsync(); } catch { }
        try { await display.DisposeAsync(); } catch { }
    }
}
