using System.Threading.Channels;
using BluKube.Server.Core.Engine.Browser;
using BluKube.Server.Core.Engine.Display;
using BluKube.Server.Core.Playback;
using BluKube.Server.Core.Search;

namespace BluKube.Server.Core.Session;

public sealed class BrowserSession : IBrowserSession
{
    private readonly IDisplay _display;
    private readonly IYouTubeBrowser _browser;
    private readonly Channel<SessionSnapshot> _snapshotChannel;
    private readonly CancellationTokenSource _disposeCts = new();
    
    private string? _currentVideoId;
    private TimeSpan? _currentDuration;
    private Task? _pollingTask;
    
    public Guid Id { get; } = Guid.NewGuid();

    public BrowserSession(IDisplay display, IYouTubeBrowser browser)
    {
        _display = display;
        _browser = browser;
        _snapshotChannel = Channel.CreateUnbounded<SessionSnapshot>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true
        });
    }

    public async Task<SessionSnapshot> DispatchAsync(ClientEvent clientEvent, CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposeCts.Token);
        var ct = cts.Token;

        var snapshot = clientEvent switch
        {
            SearchEvent search => await HandleSearchAsync(search, ct),
            PlayEvent play => await HandlePlayAsync(play, ct),
            PauseEvent => await HandlePauseAsync(ct),
            ResumeEvent => await HandleResumeAsync(ct),
            SetVolumeEvent volume => await HandleSetVolumeAsync(volume, ct),
            SeekToEvent seek => await HandleSeekToAsync(seek, ct),
        };

        await PushSnapshotAsync(snapshot, ct);
        return snapshot;
    }

    private async Task<SessionSnapshot> HandleSearchAsync(SearchEvent search, CancellationToken ct)
    {
        try
        {
            StopPolling();
            
            var searchPage = await _browser.GoToAsync<YouTubeSearchPage, SearchPageParams>(
                new SearchPageParams(search.Query, search.Limit), ct);
            
            if (searchPage == null)
            {
                return new Error("Failed to navigate to search page");
            }

            var results = await searchPage.SearchAsync(ct);
            return new SearchResults(search.Query, results);
        }
        catch (Exception ex)
        {
            return new Error($"Search failed: {ex.Message}");
        }
    }

    private async Task<SessionSnapshot> HandlePlayAsync(PlayEvent play, CancellationToken ct)
    {
        try
        {
            StopPolling();
            
            _currentVideoId = play.VideoId;
            
            var watchPage = await _browser.GoToAsync<YouTubeWatchPage, WatchPageParams>(
                new WatchPageParams(play.VideoId), ct);
            
            if (watchPage == null)
            {
                return new Error("Failed to navigate to watch page");
            }

            await watchPage.TryEnsurePlayingAsync(ct);
            
            // Get initial snapshot for duration
            var rawSnapshot = await watchPage.GetSnapshotAsync(ct);
            _currentDuration = rawSnapshot.Ended 
                ? TimeSpan.Zero 
                : TimeSpan.FromSeconds(rawSnapshot.CurrentTime * 2); // Estimate until we have real duration
            
            // Start polling for position updates
            StartPolling();
            
            return CreatePlaybackSnapshot(rawSnapshot, isPlaying: true);
        }
        catch (Exception ex)
        {
            return new Error($"Play failed: {ex.Message}");
        }
    }

    private async Task<SessionSnapshot> HandlePauseAsync(CancellationToken ct)
    {
        try
        {
            if (_browser.CurrentPage is not YouTubeWatchPage watchPage)
            {
                return new Error("Not on a watch page");
            }

            await watchPage.PauseAsync(ct);
            var rawSnapshot = await watchPage.GetSnapshotAsync(ct);
            
            return CreatePlaybackSnapshot(rawSnapshot, isPlaying: false);
        }
        catch (Exception ex)
        {
            return new Error($"Pause failed: {ex.Message}");
        }
    }

    private async Task<SessionSnapshot> HandleResumeAsync(CancellationToken ct)
    {
        try
        {
            if (_browser.CurrentPage is not YouTubeWatchPage watchPage)
            {
                return new Error("Not on a watch page");
            }

            await watchPage.ResumeAsync(ct);
            
            // Restart polling if it was stopped
            StartPolling();
            
            var rawSnapshot = await watchPage.GetSnapshotAsync(ct);
            return CreatePlaybackSnapshot(rawSnapshot, isPlaying: true);
        }
        catch (Exception ex)
        {
            return new Error($"Resume failed: {ex.Message}");
        }
    }

    private async Task<SessionSnapshot> HandleSetVolumeAsync(SetVolumeEvent volume, CancellationToken ct)
    {
        try
        {
            if (_browser.CurrentPage is not YouTubeWatchPage watchPage)
            {
                return new Error("Not on a watch page");
            }

            await watchPage.SetVolumeAsync(volume.Value, ct);
            var rawSnapshot = await watchPage.GetSnapshotAsync(ct);
            var isPlaying = !rawSnapshot.Paused && !rawSnapshot.Ended;
            
            return CreatePlaybackSnapshot(rawSnapshot, isPlaying);
        }
        catch (Exception ex)
        {
            return new Error($"SetVolume failed: {ex.Message}");
        }
    }

    private async Task<SessionSnapshot> HandleSeekToAsync(SeekToEvent seek, CancellationToken ct)
    {
        try
        {
            if (_browser.CurrentPage is not YouTubeWatchPage watchPage)
            {
                return new Error("Not on a watch page");
            }

            await watchPage.SeekToAsync(seek.Seconds, ct);
            var rawSnapshot = await watchPage.GetSnapshotAsync(ct);
            var isPlaying = !rawSnapshot.Paused && !rawSnapshot.Ended;
            
            return CreatePlaybackSnapshot(rawSnapshot, isPlaying);
        }
        catch (Exception ex)
        {
            return new Error($"SeekTo failed: {ex.Message}");
        }
    }

    private Playback CreatePlaybackSnapshot(PlaybackSnapshot raw, bool isPlaying)
    {
        return new Playback(
            _currentVideoId ?? "",
            TimeSpan.FromSeconds(raw.CurrentTime),
            _currentDuration ?? TimeSpan.FromSeconds(raw.CurrentTime * 2),
            isPlaying && !raw.Paused && !raw.Ended,
            raw.Muted ? 0f : 1f);
    }

    private void StartPolling()
    {
        if (_pollingTask != null) return;
        
        _pollingTask = Task.Run(async () =>
        {
            try
            {
                while (!_disposeCts.Token.IsCancellationRequested)
                {
                    await Task.Delay(500, _disposeCts.Token);
                    
                    if (_browser.CurrentPage is not YouTubeWatchPage watchPage)
                        continue;

                    var rawSnapshot = await watchPage.GetSnapshotAsync(_disposeCts.Token);
                    var isPlaying = !rawSnapshot.Paused && !rawSnapshot.Ended;
                    
                    // Update duration if available
                    if (rawSnapshot.CurrentTime > 0)
                    {
                        _currentDuration = TimeSpan.FromSeconds(rawSnapshot.CurrentTime * 2); // Estimate
                    }
                    
                    var snapshot = CreatePlaybackSnapshot(rawSnapshot, isPlaying);
                    await PushSnapshotAsync(snapshot, _disposeCts.Token);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected on disposal
            }
            catch (Exception)
            {
                // Log and stop polling on error
            }
        });
    }

    private void StopPolling()
    {
        // Just stop creating new poll tasks; existing task will complete naturally
        // We don't cancel the token as that would cancel the whole session
        _pollingTask = null;
    }

    private async Task PushSnapshotAsync(SessionSnapshot snapshot, CancellationToken ct)
    {
        await _snapshotChannel.Writer.WriteAsync(snapshot, ct);
    }

    public IAsyncEnumerable<SessionSnapshot> Snapshots(CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposeCts.Token);
        return _snapshotChannel.Reader.ReadAllAsync(cts.Token);
    }

    public async ValueTask DisposeAsync()
    {
        _disposeCts.Cancel();
        
        try
        {
            _snapshotChannel.Writer.Complete();
        }
        catch { }

        try
        {
            await _browser.DisposeAsync();
        }
        catch { }

        try
        {
            await _display.DisposeAsync();
        }
        catch { }
        
        _disposeCts.Dispose();
    }
}