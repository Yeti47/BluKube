using BluKube.Server.Core.Domain;
using BluKube.Server.Core.Search;
using BluKube.Server.Core.Session;

namespace BluKube.Server.Tests.Core.Session;

[Trait("Category", "Unit")]
public sealed class BrowserSessionTests
{
    [Fact]
    public void NewSession_StartsIdle()
    {
        var fake = new FakeMediaPlayer();
        var session = new BrowserSession(fake, fake);

        Assert.IsType<IdleState>(session.Current);
        Assert.NotEqual(Guid.Empty, session.Id);
    }

    [Fact]
    public async Task SearchAsync_ReturnsResultsState_AndPublishes()
    {
        var fake = new FakeMediaPlayer
        {
            NextResults = new[]
            {
                new MediaItem("Hello", "Adele", "https://x/y", TimeSpan.FromMinutes(4)),
            },
        };
        var session = new BrowserSession(fake, fake);

        var result = await session.SearchAsync("hello", 5);

        var search = Assert.IsType<SearchResultsState>(result);
        Assert.Equal("hello", search.Query);
        Assert.Single(search.Items);
        Assert.Equal("search:hello:5", Assert.Single(fake.Commands));
        Assert.Same(result, session.Current);
    }

    [Fact]
    public async Task PlayAsync_ReturnsPlaybackState()
    {
        var fake = new FakeMediaPlayer
        {
            NextSnapshot = new PlayerSnapshot(
                "abc",
                TimeSpan.FromSeconds(1),
                TimeSpan.FromMinutes(3),
                true,
                0.8f
            ),
        };
        var session = new BrowserSession(fake, fake);

        var result = await session.PlayAsync("abc");

        var pb = Assert.IsType<PlaybackState>(result);
        Assert.Equal("abc", pb.VideoId);
        Assert.True(pb.IsPlaying);
        Assert.Equal(0.8f, pb.Volume);
    }

    [Fact]
    public async Task FailingCommand_ProducesErrorState_WithPreviousPreserved()
    {
        var fake = new FakeMediaPlayer();
        var session = new BrowserSession(fake, fake);
        var idleSnapshot = session.Current;

        fake.ThrowOnNext = new InvalidOperationException("boom");
        var result = await session.PlayAsync("abc");

        var err = Assert.IsType<ErrorState>(result);
        Assert.Equal("play_failed", err.Code);
        Assert.Equal("boom", err.Message);
        Assert.Same(idleSnapshot, err.Previous);
    }

    [Fact]
    public async Task States_YieldsCurrentImmediately_AndSubsequentUpdates()
    {
        var fake = new FakeMediaPlayer();
        var session = new BrowserSession(fake, fake);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var enumerator = session.States(cts.Token).GetAsyncEnumerator(cts.Token);

        Assert.True(await enumerator.MoveNextAsync());
        Assert.IsType<IdleState>(enumerator.Current);

        await session.SearchAsync("q", 3);

        Assert.True(await enumerator.MoveNextAsync());
        Assert.IsType<SearchResultsState>(enumerator.Current);

        await enumerator.DisposeAsync();
    }

    [Fact]
    public async Task PlayerEvent_FlowsAsPlaybackState()
    {
        var fake = new FakeMediaPlayer();
        var session = new BrowserSession(fake, fake);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var enumerator = session.States(cts.Token).GetAsyncEnumerator(cts.Token);

        // Skip the initial idle baseline.
        Assert.True(await enumerator.MoveNextAsync());

        fake.EmitEvent(
            new PositionChanged(
                new PlayerSnapshot(
                    "vid",
                    TimeSpan.FromSeconds(7),
                    TimeSpan.FromMinutes(2),
                    true,
                    1f
                )
            )
        );

        Assert.True(await enumerator.MoveNextAsync());
        var pb = Assert.IsType<PlaybackState>(enumerator.Current);
        Assert.Equal("vid", pb.VideoId);
        Assert.Equal(TimeSpan.FromSeconds(7), pb.Position);

        await enumerator.DisposeAsync();
    }

    [Fact]
    public async Task PlayerFailure_FlowsAsErrorState()
    {
        var fake = new FakeMediaPlayer();
        var session = new BrowserSession(fake, fake);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var enumerator = session.States(cts.Token).GetAsyncEnumerator(cts.Token);
        Assert.True(await enumerator.MoveNextAsync()); // baseline

        fake.EmitEvent(new PlaybackFailed("polling_failed", "network blip"));

        Assert.True(await enumerator.MoveNextAsync());
        var err = Assert.IsType<ErrorState>(enumerator.Current);
        Assert.Equal("polling_failed", err.Code);
        Assert.Equal("network blip", err.Message);

        await enumerator.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_DisposesPlayer()
    {
        var fake = new FakeMediaPlayer();
        var session = new BrowserSession(fake, fake);

        await session.DisposeAsync();

        Assert.True(fake.Disposed);
    }

    [Fact]
    public async Task LastActivityAt_AdvancesOnCommand()
    {
        var fake = new FakeMediaPlayer();
        var session = new BrowserSession(fake, fake);
        var before = session.LastActivityAt;

        await Task.Delay(15);
        await session.SearchAsync("q", 1);

        Assert.True(session.LastActivityAt > before);
    }
}
