using BluKube.Contracts;
using Microsoft.AspNetCore.Components;

namespace BluKube.Web.Services;

public sealed class NativeClientService(ClientSessionService session, AudioStreamService audio)
{
    public event Func<Task>? StateChanged;

    public Task? StateTask { get; private set; }
    public string Query { get; set; } = string.Empty;
    public bool Busy { get; private set; }
    public string? BusyMessage { get; private set; }
    public string? Error { get; private set; }
    public SessionState State { get; private set; } = new IdleState();
    public IReadOnlyList<MediaItem> Results { get; private set; } = [];
    public string? CurrentTitle { get; private set; }
    public string? CurrentChannel { get; private set; }

    public async Task StartAsync()
    {
        Reset();
        await session.ConnectAsync();
        StateTask = PumpStatesAsync();
        audio.Start(message =>
        {
            Error = message;
            _ = NotifyStateChangedAsync();
        });
    }

    public void Reset()
    {
        State = new IdleState();
        Results = [];
        Error = null;
        ClearBusy();
        CurrentTitle = null;
        CurrentChannel = null;
    }

    public void ClearTasks() => StateTask = null;

    public void SetError(string message)
    {
        Error = message;
        ClearBusy();
    }

    public async Task SearchAsync()
    {
        var connection = session.Connection;
        if (connection is null || string.IsNullOrWhiteSpace(Query)) return;

        SetBusy("Searching...");
        Error = null;
        Results = [];
        await NotifyStateChangedAsync();

        try
        {
            var state = await connection.SearchAsync(Query.Trim(), 10, session.Token);
            ApplyState(state);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Error = ex.Message;
        }
        finally
        {
            ClearBusy();
        }
    }

    public async Task PlayAsync(MediaItem item)
    {
        var connection = session.Connection;
        if (connection is null) return;

        await audio.ResumeAsync();
        SetBusy("Loading track...");
        Error = null;
        CurrentTitle = item.Title;
        CurrentChannel = item.Channel;
        await NotifyStateChangedAsync();

        try
        {
            var state = await connection.PlayAsync(ExtractVideoId(item.Url), session.Token);
            ApplyState(state);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Error = ex.Message;
        }
        finally
        {
            ClearBusy();
        }
    }

    public async Task TogglePlaybackAsync()
    {
        var connection = session.Connection;
        if (connection is null || State is not PlaybackState playback) return;

        await audio.ResumeAsync();
        SetBusy(playback.IsPlaying ? "Pausing..." : "Resuming...");
        await NotifyStateChangedAsync();

        try
        {
            var state = playback.IsPlaying
                ? await connection.PauseAsync(session.Token)
                : await connection.ResumeAsync(session.Token);
            ApplyState(state);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Error = ex.Message;
        }
        finally
        {
            ClearBusy();
        }
    }

    public async Task SeekAsync(TimeSpan delta)
    {
        var connection = session.Connection;
        if (connection is null || State is not PlaybackState playback) return;

        var next = playback.Position + delta;
        if (next < TimeSpan.Zero) next = TimeSpan.Zero;
        if (playback.Duration > TimeSpan.Zero && next > playback.Duration) next = playback.Duration;

        SetBusy("Seeking...");
        await NotifyStateChangedAsync();

        try
        {
            ApplyState(await connection.SeekToAsync(next, session.Token));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Error = ex.Message;
        }
        finally
        {
            ClearBusy();
        }
    }

    public async Task SetVolumeAsync(ChangeEventArgs args)
    {
        var connection = session.Connection;
        if (connection is null) return;
        if (!int.TryParse(args.Value?.ToString(), out var percent)) return;

        try
        {
            ApplyState(await connection.SetVolumeAsync(Math.Clamp(percent, 0, 100) / 100f, session.Token));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Error = ex.Message;
        }
    }

    public async Task StopAsync()
    {
        var connection = session.Connection;
        if (connection is null) return;

        SetBusy("Stopping...");
        await NotifyStateChangedAsync();

        try
        {
            ApplyState(await connection.StopAsync(session.Token));
            CurrentTitle = null;
            CurrentChannel = null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Error = ex.Message;
        }
        finally
        {
            ClearBusy();
        }
    }

    private async Task PumpStatesAsync()
    {
        var connection = session.Connection;
        if (connection is null) return;

        try
        {
            await foreach (var state in connection.StreamStatesAsync(session.Token))
            {
                ApplyState(state);
                await NotifyStateChangedAsync();
            }
        }
        catch (OperationCanceledException) { }
    }

    private void ApplyState(SessionState state)
    {
        State = state;

        switch (state)
        {
            case SearchResultsState results:
                Results = results.Items;
                Error = results.Items.Count == 0 ? "No results." : null;
                break;
            case ErrorState error:
                Error = error.Message;
                if (error.Previous is not null)
                    State = error.Previous;
                break;
            default:
                Error = null;
                break;
        }
    }

    private void SetBusy(string message)
    {
        Busy = true;
        BusyMessage = message;
    }

    private void ClearBusy()
    {
        Busy = false;
        BusyMessage = null;
    }

    private async Task NotifyStateChangedAsync()
    {
        var handler = StateChanged;
        if (handler is not null)
            await handler.Invoke();
    }

    private static string ExtractVideoId(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            var query = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in query)
            {
                var pair = part.Split('=', 2);
                if (pair.Length == 2 && pair[0] == "v")
                    return Uri.UnescapeDataString(pair[1]);
            }

            var segment = uri.Segments.LastOrDefault();
            if (!string.IsNullOrEmpty(segment)) return segment.TrimEnd('/');
        }

        return url;
    }
}
