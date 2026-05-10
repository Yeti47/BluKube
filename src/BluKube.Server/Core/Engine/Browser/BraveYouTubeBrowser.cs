using BluKube.Server.Core.Engine.Display;
using Microsoft.Playwright;

namespace BluKube.Server.Core.Engine.Browser;

public sealed class BraveYouTubeBrowser : IYouTubeBrowser
{
    private readonly IPage _page;
    private readonly IBrowser _browser;
    private readonly IBrowserContext _context;
    private readonly IPlaywright _playwright;
    private readonly IDisplay _display;

    public BraveYouTubeBrowser(
        IPage page,
        IBrowser browser,
        IBrowserContext context,
        IPlaywright playwright,
        IDisplay display)
    {
        _page = page;
        _browser = browser;
        _context = context;
        _playwright = playwright;
        _display = display;
    }

    public async Task<TPage> GoToAsync<TPage, TParams>(TParams parameters, CancellationToken cancellationToken)
        where TPage : IYouTubePage<TParams>
        where TParams : class
    {
        var page = (TPage)TPage.Create(_page, parameters);
        await page.NavigateToAsync(cancellationToken);
        return page;
    }

    public async ValueTask DisposeAsync()
    {
        _playwright.Dispose();

        try { await _context.CloseAsync(); } catch { }
        try { await _browser.CloseAsync(); } catch { }
        try { await _display.DisposeAsync(); } catch { }
    }
}
