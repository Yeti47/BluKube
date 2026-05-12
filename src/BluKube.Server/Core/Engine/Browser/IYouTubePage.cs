using Microsoft.Playwright;

namespace BluKube.Server.Core.Engine.Browser;

public interface IYouTubePage<TParams> : IYouTubePage where TParams : class
{
    static abstract IYouTubePage<TParams> Create(IPage page, TParams parameters);
    
}

public interface IYouTubePage
{
    Task NavigateToAsync(CancellationToken cancellationToken);
}