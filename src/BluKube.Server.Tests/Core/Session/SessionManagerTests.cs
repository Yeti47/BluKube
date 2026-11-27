using BluKube.Server.Configuration;
using BluKube.Server.Core.Engine.Audio;
using BluKube.Server.Core.Engine.Browser;
using BluKube.Server.Core.Engine.Display;
using BluKube.Server.Core.Session;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BluKube.Server.Tests.Core.Session;

[Trait("Category", "Unit")]
public sealed class SessionManagerTests
{
    [Fact]
    public async Task CreateSession_WhenBrowserLaunchFails_DisposesDisplayAndAudio()
    {
        var displayFactory = new FakeDisplayFactory();
        var audioFactory = new FakeAudioFactory();
        var browserLauncher = new ThrowingBrowserLauncher();
        await using var manager = CreateManager(displayFactory, browserLauncher, audioFactory);

        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.CreateSessionAsync());

        Assert.True(displayFactory.Display.Disposed);
        Assert.True(audioFactory.Audio.Disposed);
        Assert.Equal(audioFactory.Audio.SinkName, browserLauncher.PulseSink);
    }

    [Fact]
    public async Task CreateSession_WhenAudioCreationFails_DisposesDisplayAndDoesNotLaunchBrowser()
    {
        var displayFactory = new FakeDisplayFactory();
        var audioFactory = new FakeAudioFactory { ThrowOnCreate = true };
        var browserLauncher = new ThrowingBrowserLauncher();
        await using var manager = CreateManager(displayFactory, browserLauncher, audioFactory);

        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.CreateSessionAsync());

        Assert.True(displayFactory.Display.Disposed);
        Assert.False(browserLauncher.WasCalled);
    }

    [Fact]
    public async Task Sweeper_KeepsReapingIdleSessions_WhenDisposeHangs()
    {
        var audioFactory = new HangingAudioFactory();
        await using var manager = CreateManager(
            new FakeDisplayFactory(),
            new FakeBrowserLauncher(),
            audioFactory,
            new SessionLimits
            {
                MaxSessions = 4,
                IdleTimeout = TimeSpan.Zero,
                SweepInterval = TimeSpan.FromMilliseconds(20),
                DisposeTimeout = TimeSpan.FromMilliseconds(100),
            }
        );

        // Every dispose hangs. Before the fix, the first reap blocked the
        // sweeper forever and the rest stayed in the cap.
        for (var i = 0; i < 3; i++)
            await CreateIgnoringCapRaceAsync(manager);

        await WaitUntilAsync(async () => (await manager.ListSessionsAsync()).Count == 0);

        // The sweeper must still be alive for sessions created afterwards.
        await CreateIgnoringCapRaceAsync(manager);
        await WaitUntilAsync(async () => (await manager.ListSessionsAsync()).Count == 0);
        Assert.Equal(4, audioFactory.Created);
    }

    [Fact]
    public async Task RemoveSession_Returns_WhenDisposeHangs()
    {
        await using var manager = CreateManager(
            new FakeDisplayFactory(),
            new FakeBrowserLauncher(),
            new HangingAudioFactory(),
            new SessionLimits
            {
                MaxSessions = 4,
                IdleTimeout = TimeSpan.FromHours(1),
                SweepInterval = TimeSpan.FromHours(1),
                DisposeTimeout = TimeSpan.FromMilliseconds(100),
            }
        );

        var session = await manager.CreateSessionAsync();

        var removed = await manager
            .RemoveSessionAsync(session.Id)
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(removed);
        Assert.Empty(await manager.ListSessionsAsync());
    }

    // With IdleTimeout zero the sweeper may reap a session while it is being
    // created; that doesn't matter for these tests.
    private static async Task CreateIgnoringCapRaceAsync(SessionManager manager)
    {
        try
        {
            await manager.CreateSessionAsync();
        }
        catch (InvalidOperationException) { }
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!await condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("Condition was not met in time.");
            await Task.Delay(20);
        }
    }

    private static SessionManager CreateManager(
        IDisplayFactory displayFactory,
        IYouTubeBrowserLauncher browserLauncher,
        IAudioOutputDeviceFactory audioFactory,
        SessionLimits? limits = null
    ) =>
        new(
            displayFactory,
            browserLauncher,
            audioFactory,
            Options.Create(
                limits
                    ?? new SessionLimits
                    {
                        MaxSessions = 4,
                        IdleTimeout = TimeSpan.FromHours(1),
                        SweepInterval = TimeSpan.FromHours(1),
                    }
            ),
            NullLogger<SessionManager>.Instance
        );

    private sealed class HangingAudioFactory : IAudioOutputDeviceFactory
    {
        private int _created;
        public int Created => Volatile.Read(ref _created);

        public Task<IAudioOutputDevice> CreateAsync(CancellationToken ct)
        {
            Interlocked.Increment(ref _created);
            return Task.FromResult<IAudioOutputDevice>(new HangingAudio());
        }
    }

    private sealed class HangingAudio : IAudioOutputDevice
    {
        public string SinkName => "hanging-sink";

        public async IAsyncEnumerable<byte[]> StreamOpusAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct
        )
        {
            await Task.CompletedTask;
            yield break;
        }

        public ValueTask DisposeAsync() => new(new TaskCompletionSource().Task);
    }

    private sealed class FakeBrowserLauncher : IYouTubeBrowserLauncher
    {
        public Task<IYouTubeBrowser> LaunchAsync(
            IDisplay display,
            string? pulseSink,
            CancellationToken cancellationToken
        ) => Task.FromResult<IYouTubeBrowser>(new FakeBrowser());
    }

    private sealed class FakeBrowser : IYouTubeBrowser
    {
        public Task<ISearchPage> OpenSearchAsync(string query, int limit, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<IWatchPage> OpenWatchAsync(string videoId, CancellationToken ct) =>
            throw new NotSupportedException();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeDisplayFactory : IDisplayFactory
    {
        public FakeDisplay Display { get; } = new();

        public Task<IDisplay> CreateAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IDisplay>(Display);
    }

    private sealed class FakeDisplay : IDisplay
    {
        public string DisplayValue => ":123";
        public bool Disposed { get; private set; }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeAudioFactory : IAudioOutputDeviceFactory
    {
        public FakeAudio Audio { get; } = new();
        public bool ThrowOnCreate { get; init; }

        public Task<IAudioOutputDevice> CreateAsync(CancellationToken ct)
        {
            if (ThrowOnCreate)
            {
                throw new InvalidOperationException("audio failed");
            }

            return Task.FromResult<IAudioOutputDevice>(Audio);
        }
    }

    private sealed class FakeAudio : IAudioOutputDevice
    {
        public string SinkName => "fake-sink";
        public bool Disposed { get; private set; }

        public async IAsyncEnumerable<byte[]> StreamOpusAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct
        )
        {
            await Task.CompletedTask;
            yield break;
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowingBrowserLauncher : IYouTubeBrowserLauncher
    {
        public bool WasCalled { get; private set; }
        public string? PulseSink { get; private set; }

        public Task<IYouTubeBrowser> LaunchAsync(
            IDisplay display,
            string? pulseSink,
            CancellationToken cancellationToken
        )
        {
            WasCalled = true;
            PulseSink = pulseSink;
            throw new InvalidOperationException("browser failed");
        }
    }
}
