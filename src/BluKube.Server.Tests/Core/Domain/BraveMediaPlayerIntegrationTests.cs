using BluKube.Server.Core.Domain;
using BluKube.Server.Core.Engine.Browser;
using BluKube.Server.Core.Engine.Display;

namespace BluKube.Server.Tests.Core.Domain;

/// <summary>
/// End-to-end smoke test for the real engine stack:
/// Xvfb display + Brave browser + YouTube watch page, driven through the
/// <see cref="BraveMediaPlayer"/> seam. Requires the full Docker dev image.
/// </summary>
[Trait("Category", "Integration")]
public sealed class BraveMediaPlayerIntegrationTests
{
    // A tiny, evergreen Creative Commons clip on YouTube.
    // "Big Buck Bunny" trailer is widely mirrored; if this id ever rots,
    // pick another stable, short, known-good public id.
    private const string SampleVideoId = "YE7VzlLtp-4";

    [DockerOnlyFact]
    public async Task PlayAndPause_DrivesPlayerThroughLifecycle()
    {
        await using var display = await new XvfbDisplayFactory().CreateAsync(CancellationToken.None);
        await using var browser = await new BraveYouTubeBrowserLauncher()
            .LaunchAsync(display, pulseSink: null, CancellationToken.None);
        await using var player = new BraveMediaPlayer(display, browser);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var snapshot = await player.PlayAsync(SampleVideoId, cts.Token);

        Assert.Equal(SampleVideoId, snapshot.VideoId);
        Assert.True(snapshot.IsPlaying, "Expected playback to be running after PlayAsync.");

        var paused = await player.PauseAsync(cts.Token);
        Assert.False(paused.IsPlaying);

    }
}
