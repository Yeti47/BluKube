using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using BluKube.Server.Core.Session;

namespace BluKube.Server.Tests.Hubs;

/// <summary>
/// Integration tests for <c>SessionHub</c> over a real SignalR connection
/// routed through <see cref="HubFactory"/>'s in-process test server.
/// </summary>
public sealed class SessionHubTests : IClassFixture<HubFactory>, IAsyncLifetime
{
    private readonly HubFactory _factory;
    private readonly List<HubConnection> _connections = [];

    public SessionHubTests(HubFactory factory) => _factory = factory;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var hub in _connections)
            await hub.DisposeAsync();
    }

    // ── Auth ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Connect_WithoutToken_Throws()
    {
        var hub = Track(_factory.CreateHubConnection(token: null));
        await Assert.ThrowsAnyAsync<Exception>(() => hub.StartAsync());
    }

    [Fact]
    public async Task Connect_WithWrongToken_Throws()
    {
        var hub = Track(_factory.CreateHubConnection("completely-wrong"));
        await Assert.ThrowsAnyAsync<Exception>(() => hub.StartAsync());
    }

    // ── Session lifecycle ─────────────────────────────────────────────────────

    [Fact]
    public async Task CreateSession_ReturnsNonEmptyGuid()
    {
        var hub = await ConnectedAsync();
        var id = await hub.InvokeAsync<Guid>("CreateSession");
        Assert.NotEqual(Guid.Empty, id);
    }

    [Fact]
    public async Task CloseSession_SubsequentCommandThrowsHubException()
    {
        var hub = await ConnectedAsync();
        var id = await hub.InvokeAsync<Guid>("CreateSession");
        await hub.InvokeAsync("CloseSession", id);

        var ex = await Assert.ThrowsAsync<HubException>(
            () => hub.InvokeAsync<SessionState>("GetState"));
        Assert.Contains("No session", ex.Message);
    }

    [Fact]
    public async Task CloseSession_ForDifferentSession_ThrowsAndKeepsOwnSession()
    {
        var first = await SessionAsync();
        var ownId = await first.InvokeAsync<Guid>("CreateSession");
        var second = await SessionAsync();
        var otherId = await second.InvokeAsync<Guid>("CreateSession");

        var ex = await Assert.ThrowsAsync<HubException>(
            () => first.InvokeAsync("CloseSession", otherId));

        Assert.Contains("not attached", ex.Message);
        Assert.IsType<IdleState>(await first.InvokeAsync<SessionState>("GetState"));

        var manager = _factory.Services.GetRequiredService<ISessionManager>();
        Assert.NotNull(await manager.GetSessionAsync(ownId));
        Assert.NotNull(await manager.GetSessionAsync(otherId));
    }

    [Fact]
    public async Task Disconnect_RemovesSession()
    {
        var hub = await ConnectedAsync();
        var id = await hub.InvokeAsync<Guid>("CreateSession");
        await hub.DisposeAsync();

        var manager = _factory.Services.GetRequiredService<ISessionManager>();
        await EventuallyAsync(async () => Assert.Null(await manager.GetSessionAsync(id)));
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetState_WithoutSession_ThrowsHubException()
    {
        var hub = await ConnectedAsync();
        var ex = await Assert.ThrowsAsync<HubException>(
            () => hub.InvokeAsync<SessionState>("GetState"));
        Assert.Contains("No session", ex.Message);
    }

    [Fact]
    public async Task GetState_AfterCreate_ReturnsIdleState()
    {
        var hub = await SessionAsync();
        var state = await hub.InvokeAsync<SessionState>("GetState");
        Assert.IsType<IdleState>(state);
    }

    [Fact]
    public async Task Search_ReturnsSearchResultsState()
    {
        var hub = await SessionAsync();
        var state = await hub.InvokeAsync<SessionState>("Search", "lofi hip hop", 5);
        var results = Assert.IsType<SearchResultsState>(state);
        Assert.Equal("lofi hip hop", results.Query);
    }

    [Fact]
    public async Task Play_ReturnsPlaybackState()
    {
        var hub = await SessionAsync();
        var state = await hub.InvokeAsync<SessionState>("Play", "dQw4w9WgXcQ");
        var pb = Assert.IsType<PlaybackState>(state);
        Assert.Equal("dQw4w9WgXcQ", pb.VideoId);
        Assert.True(pb.IsPlaying);
    }

    [Fact]
    public async Task Pause_ReturnsPlaybackStateWithIsPlayingFalse()
    {
        var hub = await SessionAsync();
        await hub.InvokeAsync<SessionState>("Play", "dQw4w9WgXcQ");
        var state = await hub.InvokeAsync<SessionState>("Pause");
        var pb = Assert.IsType<PlaybackState>(state);
        Assert.False(pb.IsPlaying);
    }

    [Fact]
    public async Task Resume_ReturnsPlaybackStateWithIsPlayingTrue()
    {
        var hub = await SessionAsync();
        await hub.InvokeAsync<SessionState>("Play", "dQw4w9WgXcQ");
        await hub.InvokeAsync<SessionState>("Pause");
        var state = await hub.InvokeAsync<SessionState>("Resume");
        var pb = Assert.IsType<PlaybackState>(state);
        Assert.True(pb.IsPlaying);
    }

    [Fact]
    public async Task SeekTo_UpdatesPosition()
    {
        var hub = await SessionAsync();
        await hub.InvokeAsync<SessionState>("Play", "dQw4w9WgXcQ");
        var target = TimeSpan.FromSeconds(42);
        var state = await hub.InvokeAsync<SessionState>("SeekTo", target);
        var pb = Assert.IsType<PlaybackState>(state);
        Assert.Equal(target, pb.Position);
    }

    [Fact]
    public async Task SetVolume_UpdatesVolume()
    {
        var hub = await SessionAsync();
        await hub.InvokeAsync<SessionState>("Play", "dQw4w9WgXcQ");
        var state = await hub.InvokeAsync<SessionState>("SetVolume", 0.42f);
        var pb = Assert.IsType<PlaybackState>(state);
        Assert.Equal(0.42f, pb.Volume, precision: 4);
    }

    // ── Streaming ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task StreamStates_YieldsAtLeastInitialState()
    {
        var hub = await SessionAsync();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var stream = hub.StreamAsync<SessionState>("StreamStates", cts.Token);
        var first = await stream.FirstAsync(cts.Token);
        cts.Cancel(); // stop consuming

        Assert.IsType<IdleState>(first);
    }

    [Fact]
    public async Task StreamStates_YieldsStateAfterCommand()
    {
        var hub = await SessionAsync();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var received = new List<SessionState>();
        var consuming = Task.Run(async () =>
        {
            await foreach (var s in hub.StreamAsync<SessionState>("StreamStates", cts.Token))
                received.Add(s);
        }, cts.Token);

        // Issue a command that causes a state transition.
        await hub.InvokeAsync<SessionState>("Play", "abc123", CancellationToken.None);

        // Wait briefly for the streamed state to arrive, then cancel.
        await Task.Delay(200, CancellationToken.None);
        await cts.CancelAsync();
        await consuming.IgnoreCancellation();

        Assert.Contains(received, s => s is PlaybackState);
    }

    [Fact]
    public async Task StreamAudio_YieldsOpusPackets()
    {
        var hub = await SessionAsync();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var packets = new List<byte[]>();
        try
        {
            await foreach (var pkt in hub.StreamAsync<byte[]>("StreamAudio", cts.Token))
            {
                packets.Add(pkt);
                if (packets.Count >= 3) break;
            }
        }
        catch (OperationCanceledException) { }

        Assert.Equal(3, packets.Count);
        Assert.All(packets, p => Assert.NotEmpty(p));
        // First byte from the test fake's synthetic packets.
        Assert.Equal(0xFC, packets[0][0]);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private HubConnection Track(HubConnection hub) { _connections.Add(hub); return hub; }

    private async Task<HubConnection> ConnectedAsync()
    {
        var hub = Track(_factory.CreateHubConnection());
        await hub.StartAsync();
        return hub;
    }

    /// <summary>Returns a connected hub with a session already created.</summary>
    private async Task<HubConnection> SessionAsync()
    {
        var hub = await ConnectedAsync();
        await hub.InvokeAsync<Guid>("CreateSession");
        return hub;
    }

    private static async Task EventuallyAsync(Func<Task> assertion)
    {
        Exception? last = null;
        for (var attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                await assertion();
                return;
            }
            catch (Exception ex)
            {
                last = ex;
                await Task.Delay(50);
            }
        }

        throw last ?? new TimeoutException("Timed out waiting for assertion.");
    }
}

file static class TaskExtensions
{
    /// <summary>Swallows <see cref="OperationCanceledException"/> so awaiting a
    /// deliberately-cancelled task does not fail the test.</summary>
    internal static async Task IgnoreCancellation(this Task t)
    {
        try { await t; }
        catch (OperationCanceledException) { }
    }
}
