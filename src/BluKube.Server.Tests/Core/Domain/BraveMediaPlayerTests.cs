using BluKube.Server.Core.Domain;
using BluKube.Server.Core.Engine.Browser;
using BluKube.Server.Core.Engine.Display;
using BluKube.Server.Core.Playback;

namespace BluKube.Server.Tests.Core.Domain;

[Trait("Category", "Unit")]
public sealed class BraveMediaPlayerTests
{
    private static readonly TimeSpan StepTimeout = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan TestDeadline = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Dispose_KillsDisplay_WhenPollReadHangs()
    {
        var display = new FakeDisplay();
        var watch = new HangingReadWatchPage();
        var browser = new FakeBrowser(watch);
        var player = new BraveMediaPlayer(display, browser, StepTimeout);

        await player.PlayAsync("video-1", CancellationToken.None);
        // Let the poll loop get stuck inside a read that ignores cancellation.
        await watch.ReadStarted.Task.WaitAsync(TestDeadline);

        await player.DisposeAsync().AsTask().WaitAsync(TestDeadline);

        Assert.True(browser.Disposed);
        Assert.True(display.Disposed);
    }

    [Fact]
    public async Task Dispose_KillsDisplay_WhenBrowserCloseHangs()
    {
        var display = new FakeDisplay();
        var browser = new FakeBrowser(new HangingReadWatchPage()) { HangOnDispose = true };
        var player = new BraveMediaPlayer(display, browser, StepTimeout);

        await player.DisposeAsync().AsTask().WaitAsync(TestDeadline);

        Assert.True(display.Disposed);
    }

    private sealed class FakeDisplay : IDisplay
    {
        public string DisplayValue => ":99";
        public bool Disposed { get; private set; }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeBrowser(IWatchPage watch) : IYouTubeBrowser
    {
        public bool HangOnDispose { get; init; }
        public bool Disposed { get; private set; }

        public Task<ISearchPage> OpenSearchAsync(string query, int limit, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<IWatchPage> OpenWatchAsync(string videoId, CancellationToken ct) =>
            Task.FromResult(watch);

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return HangOnDispose
                ? new ValueTask(new TaskCompletionSource().Task)
                : ValueTask.CompletedTask;
        }
    }

    /// <summary>
    /// Mimics a wedged YouTube tab: playback starts, but every state read
    /// afterwards never returns and ignores its cancellation token, like a
    /// Playwright EvaluateAsync against an unresponsive renderer.
    /// </summary>
    private sealed class HangingReadWatchPage : IWatchPage
    {
        private static readonly WatchSnapshot Playing = new(
            TimeSpan.Zero,
            TimeSpan.FromMinutes(3),
            IsPlaying: true,
            IsEnded: false,
            Volume: 1f
        );

        public TaskCompletionSource ReadStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<WatchSnapshot> ReadStateAsync(CancellationToken ct)
        {
            ReadStarted.TrySetResult();
            return new TaskCompletionSource<WatchSnapshot>().Task;
        }

        public Task<WatchSnapshot> EnsurePlayingAsync(CancellationToken ct) =>
            Task.FromResult(Playing);

        public Task<WatchSnapshot> PauseAsync(CancellationToken ct) => Task.FromResult(Playing);

        public Task<WatchSnapshot> ResumeAsync(CancellationToken ct) => Task.FromResult(Playing);

        public Task<WatchSnapshot> SeekToAsync(TimeSpan position, CancellationToken ct) =>
            Task.FromResult(Playing);

        public Task<WatchSnapshot> SetVolumeAsync(float volume, CancellationToken ct) =>
            Task.FromResult(Playing);
    }
}
