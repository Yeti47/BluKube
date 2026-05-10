using BluKube.Server.Core.Engine.Browser;

namespace BluKube.Server.Core.Search;

public interface IYouTubeSearchPage : IYouTubePage<SearchPageParams>
{
    Task<IReadOnlyList<MediaItem>> SearchAsync(CancellationToken cancellationToken);
}
