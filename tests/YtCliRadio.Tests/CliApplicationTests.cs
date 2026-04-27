using YtCliRadio.App;
using YtCliRadio.Browser;
using YtCliRadio.Configuration;
using YtCliRadio.Domain;

namespace YtCliRadio.Tests;

public sealed class CliApplicationTests
{
    [Fact]
    public async Task RunAsync_DryRun_ReturnsZeroWithoutPlayback()
    {
        var options = new AppOptions("lofi", 3, true, null);
        var fake = new FakeYouTubeBrowserClient
        {
            SearchResults =
            [
                new("Track 1", "Channel 1", "https://example.com/1", "3:10"),
                new("Track 2", "Channel 2", "https://example.com/2", "2:45")
            ]
        };

        var app = new CliApplication(options, fake);
        var result = await app.RunAsync(CancellationToken.None);

        Assert.Equal(0, result);
        Assert.Equal(1, fake.SearchCalls);
        Assert.Equal(0, fake.StartPlaybackCalls);
    }

    [Fact]
    public async Task RunAsync_NoResults_ReturnsThree()
    {
        var options = new AppOptions("nothing", 3, true, null);
        var fake = new FakeYouTubeBrowserClient();
        var app = new CliApplication(options, fake);

        var result = await app.RunAsync(CancellationToken.None);
        Assert.Equal(3, result);
    }

    private sealed class FakeYouTubeBrowserClient : IYouTubeBrowserClient
    {
        public IReadOnlyList<VideoSearchResult> SearchResults { get; set; } = [];
        public int SearchCalls { get; private set; }
        public int StartPlaybackCalls { get; private set; }
        public bool IsPaused { get; set; }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public Task<IReadOnlyList<VideoSearchResult>> SearchAsync(string query, int limit, CancellationToken cancellationToken)
        {
            SearchCalls++;
            return Task.FromResult(SearchResults);
        }

        public Task StartPlaybackAsync(VideoSearchResult selection, CancellationToken cancellationToken)
        {
            StartPlaybackCalls++;
            return Task.CompletedTask;
        }

        public Task PauseAsync(CancellationToken cancellationToken)
        {
            IsPaused = true;
            return Task.CompletedTask;
        }

        public Task ResumeAsync(CancellationToken cancellationToken)
        {
            IsPaused = false;
            return Task.CompletedTask;
        }

        public Task<bool> IsPausedAsync(CancellationToken cancellationToken) => Task.FromResult(IsPaused);
        public Task<bool> IsTrackEndedAsync(CancellationToken cancellationToken) => Task.FromResult(false);
    }
}
