using YtCliRadio.Domain;

namespace YtCliRadio.Browser;

public interface IYouTubeBrowserClient : IAsyncDisposable
{
    Task<IReadOnlyList<VideoSearchResult>> SearchAsync(string query, int limit, CancellationToken cancellationToken);
    Task StartPlaybackAsync(VideoSearchResult selection, CancellationToken cancellationToken);
    Task PauseAsync(CancellationToken cancellationToken);
    Task ResumeAsync(CancellationToken cancellationToken);
    Task<bool> IsPausedAsync(CancellationToken cancellationToken);
    Task<bool> IsTrackEndedAsync(CancellationToken cancellationToken);
}
