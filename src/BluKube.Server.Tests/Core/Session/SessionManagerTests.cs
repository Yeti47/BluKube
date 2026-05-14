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

    private static SessionManager CreateManager(
        IDisplayFactory displayFactory,
        IYouTubeBrowserLauncher browserLauncher,
        IAudioOutputDeviceFactory audioFactory)
        => new(
            displayFactory,
            browserLauncher,
            audioFactory,
            Options.Create(new SessionLimits
            {
                MaxSessions = 4,
                IdleTimeout = TimeSpan.FromHours(1),
                SweepInterval = TimeSpan.FromHours(1)
            }),
            NullLogger<SessionManager>.Instance);

    private sealed class FakeDisplayFactory : IDisplayFactory
    {
        public FakeDisplay Display { get; } = new();

        public Task<IDisplay> CreateAsync(CancellationToken cancellationToken)
            => Task.FromResult<IDisplay>(Display);
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
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
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

        public Task<IYouTubeBrowser> LaunchAsync(IDisplay display, string? pulseSink, CancellationToken cancellationToken)
        {
            WasCalled = true;
            PulseSink = pulseSink;
            throw new InvalidOperationException("browser failed");
        }
    }
}