using BluKube.Contracts;
using BluKube.Web.Audio;
using BluKube.Web.Clients.ErrorHandling;
using Microsoft.AspNetCore.Components;

namespace BluKube.Web.Clients;

public sealed class NativeClient(ClientSession session, AudioStream audio) : IClientView
{
    private string _query = string.Empty;

    public event EventHandler<NativeClientChangedEventArgs>? StateChanged;

    public ClientView View => ClientView.Native;
    public Task? StateTask { get; private set; }
    public bool SearchBusy { get; private set; }
    public string? SearchBusyMessage { get; private set; }
    public bool PlaybackBusy { get; private set; }
    public string? Error { get; private set; }
    public SessionState State { get; private set; } = new IdleState();
    public IReadOnlyList<MediaItem> Results { get; private set; } = [];
    public string? CurrentTitle { get; private set; }
    public string? CurrentChannel { get; private set; }
    public bool CanSearch => !SearchBusy && !string.IsNullOrWhiteSpace(_query);

    public async Task StartAsync()
    {
        Reset();
        await session.ConnectAsync();
        StateTask = PumpStatesAsync();
        audio.Start(message =>
        {
            Error = message;
            NotifyStateChanged(NativeClientSection.Search | NativeClientSection.Player);
        });
    }

    public void Reset()
    {
        State = new IdleState();
        Results = [];
        Error = null;
        ClearSearchBusy();
        ClearPlaybackBusy();
        CurrentTitle = null;
        CurrentChannel = null;
    }

    public void ClearTasks() => StateTask = null;

    public async Task ActivateAsync()
    {
        try
        {
            await StartAsync();
        }
        catch (ClientStartupException) { }
        catch (Exception ex)
        {
            SetError(ex.Message);
            NotifyStateChanged(NativeClientSection.Search | NativeClientSection.Player);
        }
    }

    public async Task DeactivateAsync(bool resetSession = true)
    {
        await session.StopAsync(audio.PumpTask, StateTask);

        ClearTasks();
        audio.Clear();

        if (resetSession)
            session.ResetCancellation();
    }

    public void ClearState()
    {
        SetQuery(string.Empty);
        Reset();
    }

    public void SetQuery(string query)
    {
        _query = query ?? string.Empty;
    }

    public void SetError(string message)
    {
        Error = message;
        ClearSearchBusy();
        ClearPlaybackBusy();
    }

    public async Task SearchAsync()
    {
        var connection = session.Connection;
        if (connection is null || string.IsNullOrWhiteSpace(_query))
            return;

        SetSearchBusy("Searching...");
        Error = null;
        Results = [];
        NotifyStateChanged(NativeClientSection.Search | NativeClientSection.Results);

        try
        {
            var state = await connection.SearchAsync(_query.Trim(), 10, session.Token);
            ApplyState(state);
            NotifyStateChanged(SectionsForState(state));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Error = ex.Message;
            NotifyStateChanged(NativeClientSection.Search);
        }
        finally
        {
            ClearSearchBusy();
            NotifyStateChanged(NativeClientSection.Search);
        }
    }

    public async Task PlayAsync(MediaItem item)
    {
        var connection = session.Connection;
        if (connection is null)
            return;

        await audio.ResumeAsync();
        SetPlaybackBusy();
        Error = null;
        CurrentTitle = item.Title;
        CurrentChannel = item.Channel;
        NotifyStateChanged(NativeClientSection.Player);

        try
        {
            var state = await connection.PlayAsync(ExtractVideoId(item.Url), session.Token);
            ApplyState(state);
            NotifyStateChanged(SectionsForState(state));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Error = ex.Message;
            NotifyStateChanged(NativeClientSection.Search | NativeClientSection.Player);
        }
        finally
        {
            ClearPlaybackBusy();
            NotifyStateChanged(NativeClientSection.Player);
        }
    }

    public async Task TogglePlaybackAsync()
    {
        var connection = session.Connection;
        if (connection is null || State is not PlaybackState playback)
            return;

        await audio.ResumeAsync();
        SetPlaybackBusy();
        NotifyStateChanged(NativeClientSection.Player);

        try
        {
            var state = playback.IsPlaying
                ? await connection.PauseAsync(session.Token)
                : await connection.ResumeAsync(session.Token);
            ApplyState(state);
            NotifyStateChanged(SectionsForState(state));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Error = ex.Message;
            NotifyStateChanged(NativeClientSection.Search | NativeClientSection.Player);
        }
        finally
        {
            ClearPlaybackBusy();
            NotifyStateChanged(NativeClientSection.Player);
        }
    }

    public async Task SeekAsync(TimeSpan delta)
    {
        var connection = session.Connection;
        if (connection is null || State is not PlaybackState playback)
            return;

        var next = playback.Position + delta;
        if (next < TimeSpan.Zero)
            next = TimeSpan.Zero;
        if (playback.Duration > TimeSpan.Zero && next > playback.Duration)
            next = playback.Duration;

        SetPlaybackBusy();
        NotifyStateChanged(NativeClientSection.Player);

        try
        {
            var state = await connection.SeekToAsync(next, session.Token);
            ApplyState(state);
            NotifyStateChanged(SectionsForState(state));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Error = ex.Message;
            NotifyStateChanged(NativeClientSection.Search | NativeClientSection.Player);
        }
        finally
        {
            ClearPlaybackBusy();
            NotifyStateChanged(NativeClientSection.Player);
        }
    }

    public async Task SetVolumeAsync(ChangeEventArgs args)
    {
        var connection = session.Connection;
        if (connection is null)
            return;
        if (!int.TryParse(args.Value?.ToString(), out var percent))
            return;

        try
        {
            ApplyState(
                await connection.SetVolumeAsync(Math.Clamp(percent, 0, 100) / 100f, session.Token)
            );
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Error = ex.Message;
        }
    }

    public async Task StopAsync()
    {
        var connection = session.Connection;
        if (connection is null)
            return;

        SetPlaybackBusy();
        NotifyStateChanged(NativeClientSection.Player);

        try
        {
            var state = await connection.StopAsync(session.Token);
            ApplyState(state);
            CurrentTitle = null;
            CurrentChannel = null;
            NotifyStateChanged(SectionsForState(state));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Error = ex.Message;
            NotifyStateChanged(NativeClientSection.Search | NativeClientSection.Player);
        }
        finally
        {
            ClearPlaybackBusy();
            NotifyStateChanged(NativeClientSection.Player);
        }
    }

    private async Task PumpStatesAsync()
    {
        var connection = session.Connection;
        if (connection is null)
            return;

        try
        {
            await foreach (var state in connection.StreamStatesAsync(session.Token))
            {
                ApplyState(state);
                NotifyStateChanged(SectionsForState(state));
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

    private void SetSearchBusy(string message)
    {
        SearchBusy = true;
        SearchBusyMessage = message;
    }

    private void ClearSearchBusy()
    {
        SearchBusy = false;
        SearchBusyMessage = null;
    }

    private void SetPlaybackBusy()
    {
        PlaybackBusy = true;
    }

    private void ClearPlaybackBusy()
    {
        PlaybackBusy = false;
    }

    private void NotifyStateChanged(NativeClientSection sections)
    {
        var handler = StateChanged;
        handler?.Invoke(this, new NativeClientChangedEventArgs(sections));
    }

    private static NativeClientSection SectionsForState(SessionState state) =>
        state switch
        {
            SearchResultsState => NativeClientSection.Search | NativeClientSection.Results,
            PlaybackState => NativeClientSection.Player,
            IdleState => NativeClientSection.Player,
            ErrorState => NativeClientSection.Search | NativeClientSection.Player | NativeClientSection.Results,
            _ => NativeClientSection.Player
        };

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
            if (!string.IsNullOrEmpty(segment))
                return segment.TrimEnd('/');
        }

        return url;
    }
}

[Flags]
public enum NativeClientSection
{
    None = 0,
    Search = 1,
    Results = 2,
    Player = 4,
}

public sealed class NativeClientChangedEventArgs(NativeClientSection sections) : EventArgs
{
    public NativeClientSection Sections { get; } = sections;

    public bool Includes(NativeClientSection sections) => (Sections & sections) != 0;
}
