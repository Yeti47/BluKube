using Microsoft.Playwright;

namespace BluKube.Server.Core.Engine.Browser;

public interface IYouTubePage<TParams> where TParams : class
{
    static abstract IYouTubePage<TParams> Create(IPage page, TParams parameters);
    Task NavigateToAsync(CancellationToken cancellationToken);
}
