namespace BluKube.Server.Core.Engine.Browser;

public interface IYouTubeBrowser : IAsyncDisposable
{
    IYouTubePage? CurrentPage { get; }
    
    Task<TPage> GoToAsync<TPage, TParams>(TParams parameters, CancellationToken cancellationToken)
        where TPage : IYouTubePage<TParams>
        where TParams : class;
}
