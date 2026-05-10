namespace BluKube.Server.Core.Engine.Browser;

public interface IYouTubeBrowser : IAsyncDisposable
{
    Task<TPage> GoToAsync<TPage, TParams>(TParams parameters, CancellationToken cancellationToken)
        where TPage : IYouTubePage<TParams>
        where TParams : class;
}
