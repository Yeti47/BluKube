using System.Threading.Channels;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace BluKube.Tui.Rendering;

/// <summary>
/// Live TUI shell for searching, selecting, and controlling playback.
/// </summary>
public sealed class PlayerView(
    IAnsiConsole console,
    IKeyInput keys,
    BluKubeConnection connection,
    string? initialQuery = null,
    int limit = 10)
{
    public async Task RunAsync(CancellationToken ct)
    {
        var state = new UiState
        {
            Query = initialQuery ?? string.Empty
        };
        var redraw = Channel.CreateUnbounded<bool>(new UnboundedChannelOptions
        {
            SingleReader = true,
            AllowSynchronousContinuations = false
        });

        using var loopCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var stateTask = Task.Run(() => PumpStatesAsync(state, redraw, loopCts.Token), loopCts.Token);
        var keyTask = Task.Run(() => PumpKeysAsync(state, redraw, loopCts), loopCts.Token);

        try
        {
            await console.Live(BuildLayout(state))
                .StartAsync(async ctx =>
                {
                    while (await redraw.Reader.WaitToReadAsync(loopCts.Token))
                    {
                        while (redraw.Reader.TryRead(out _)) { }
                        ctx.UpdateTarget(BuildLayout(state));
                        ctx.Refresh();
                    }
                });
        }
        catch (OperationCanceledException) { }
        finally
        {
            loopCts.Cancel();
            try { await Task.WhenAll(stateTask, keyTask); }
            catch (OperationCanceledException) { }
        }
    }

    private async Task PumpStatesAsync(UiState ui, Channel<bool> redraw, CancellationToken ct)
    {
        try
        {
            await foreach (var state in connection.StreamStatesAsync(ct))
            {
                ui.ServerState = state;
                if (state is ErrorState error)
                {
                    ui.Error = error.Message;
                }
                await redraw.Writer.WriteAsync(true, ct);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            redraw.Writer.TryComplete();
        }
    }

    private async Task PumpKeysAsync(UiState state, Channel<bool> redraw, CancellationTokenSource loopCts)
    {
        var ct = loopCts.Token;
        try
        {
            await foreach (var key in keys.ReadKeysAsync(ct))
            {
                if (ShouldQuit(key, state.Mode))
                {
                    loopCts.Cancel();
                    break;
                }

                try
                {
                    await DispatchAsync(key, state, redraw, ct);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    state.Error = ex.Message;
                    state.IsBusy = false;
                    await redraw.Writer.WriteAsync(true, ct);
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    private static bool ShouldQuit(KeyPress key, ViewMode mode)
        => key is { Key: Key.Q, Ctrl: true } ||
           key.Key == Key.Q && mode is ViewMode.Results or ViewMode.Player;

    private async Task DispatchAsync(KeyPress key, UiState state, Channel<bool> redraw, CancellationToken ct)
    {
        if (state.IsBusy) return;

        switch (state.Mode)
        {
            case ViewMode.Search:
                await DispatchSearchAsync(key, state, redraw, ct);
                break;
            case ViewMode.Results:
                await DispatchResultsAsync(key, state, redraw, ct);
                break;
            case ViewMode.Player:
                await DispatchPlayerAsync(key, state, redraw, ct);
                break;
        }
    }

    private async Task DispatchSearchAsync(KeyPress key, UiState state, Channel<bool> redraw, CancellationToken ct)
    {
        switch (key.Key)
        {
            case Key.Enter when !string.IsNullOrWhiteSpace(state.Query):
                await SearchAsync(state, redraw, ct);
                break;
            case Key.Backspace when state.Query.Length > 0:
                state.Query = state.Query[..^1];
                state.Error = null;
                await redraw.Writer.WriteAsync(true, ct);
                break;
            case Key.Escape:
                state.Query = string.Empty;
                state.Error = null;
                await redraw.Writer.WriteAsync(true, ct);
                break;
            case Key.Space:
                state.Query += ' ';
                await redraw.Writer.WriteAsync(true, ct);
                break;
            default:
                if (TryGetInputCharacter(key, out var character))
                {
                    state.Query += character;
                    state.Error = null;
                    await redraw.Writer.WriteAsync(true, ct);
                }
                break;
        }
    }

    private async Task SearchAsync(UiState state, Channel<bool> redraw, CancellationToken ct)
    {
        state.IsBusy = true;
        state.Error = null;
        state.Status = "Searching...";
        await redraw.Writer.WriteAsync(true, ct);

        var response = await connection.SearchAsync(state.Query.Trim(), limit, ct);
        state.ServerState = response;
        state.IsBusy = false;

        if (response is SearchResultsState results)
        {
            state.Results = results.Items;
            state.SelectedIndex = 0;
            state.Mode = ViewMode.Results;
            state.Status = results.Items.Count == 0 ? "No results." : null;
            state.Error = null;
        }
        else if (response is ErrorState error)
        {
            state.Error = error.Message;
            state.Status = null;
        }

        await redraw.Writer.WriteAsync(true, ct);
    }

    private async Task DispatchResultsAsync(KeyPress key, UiState state, Channel<bool> redraw, CancellationToken ct)
    {
        switch (key.Key)
        {
            case Key.Escape:
            case Key.Backspace:
                state.Mode = ViewMode.Search;
                state.Error = null;
                await redraw.Writer.WriteAsync(true, ct);
                break;
            case Key.UpArrow:
                if (state.Results.Count > 0)
                {
                    state.SelectedIndex = Math.Max(0, state.SelectedIndex - 1);
                    await redraw.Writer.WriteAsync(true, ct);
                }
                break;
            case Key.DownArrow:
                if (state.Results.Count > 0)
                {
                    state.SelectedIndex = Math.Min(state.Results.Count - 1, state.SelectedIndex + 1);
                    await redraw.Writer.WriteAsync(true, ct);
                }
                break;
            case Key.Enter when state.Results.Count > 0:
                await PlaySelectedAsync(state, redraw, ct);
                break;
            default:
                if (TryGetInputCharacter(key, out var character))
                {
                    state.Mode = ViewMode.Search;
                    state.Query = character.ToString();
                    state.Error = null;
                    await redraw.Writer.WriteAsync(true, ct);
                }
                break;
        }
    }

    private async Task PlaySelectedAsync(UiState state, Channel<bool> redraw, CancellationToken ct)
    {
        var item = state.Results[state.SelectedIndex];
        var videoId = ExtractVideoId(item.Url);

        state.IsBusy = true;
        state.Error = null;
        await redraw.Writer.WriteAsync(true, ct);

        var response = await connection.PlayAsync(videoId, ct);
        state.ServerState = response;
        state.IsBusy = false;

        if (response is ErrorState error)
        {
            state.Error = error.Message;
            state.Status = null;
            await redraw.Writer.WriteAsync(true, ct);
            return;
        }

        state.CurrentTitle = item.Title;
        state.CurrentChannel = item.Channel;
        state.Mode = ViewMode.Player;
        state.Status = null;
        await redraw.Writer.WriteAsync(true, ct);
    }

    private async Task DispatchPlayerAsync(KeyPress key, UiState state, Channel<bool> redraw, CancellationToken ct)
    {
        switch (key.Key)
        {
            case Key.Escape:
                await connection.StopAsync(ct);
                state.Mode = ViewMode.Search;
                state.Error = null;
                await redraw.Writer.WriteAsync(true, ct);
                return;
        }

        if (state.ServerState is not PlaybackState playback)
        {
            return;
        }

        switch (key.Key)
        {
            case Key.Space:
                state.ServerState = playback with { IsPlaying = !playback.IsPlaying };
                await redraw.Writer.WriteAsync(true, ct);
                state.ServerState = playback.IsPlaying
                    ? await connection.PauseAsync(ct)
                    : await connection.ResumeAsync(ct);
                await redraw.Writer.WriteAsync(true, ct);
                break;
            case Key.UpArrow:
                await SetVolumeAsync(state, playback.Volume + 0.05f, redraw, ct);
                break;
            case Key.DownArrow:
                await SetVolumeAsync(state, playback.Volume - 0.05f, redraw, ct);
                break;
            case Key.LeftArrow:
                await SeekAsync(state, playback, -TimeSpan.FromSeconds(key.Shift ? 30 : 10), redraw, ct);
                break;
            case Key.RightArrow:
                await SeekAsync(state, playback, TimeSpan.FromSeconds(key.Shift ? 30 : 10), redraw, ct);
                break;
        }
    }

    private async Task SetVolumeAsync(UiState state, float volume, Channel<bool> redraw, CancellationToken ct)
    {
        state.ServerState = await connection.SetVolumeAsync(Math.Clamp(volume, 0f, 1f), ct);
        await redraw.Writer.WriteAsync(true, ct);
    }

    private async Task SeekAsync(
        UiState state,
        PlaybackState playback,
        TimeSpan delta,
        Channel<bool> redraw,
        CancellationToken ct)
    {
        var next = playback.Position + delta;
        if (next < TimeSpan.Zero) next = TimeSpan.Zero;
        if (playback.Duration > TimeSpan.Zero && next > playback.Duration) next = playback.Duration;

        state.ServerState = await connection.SeekToAsync(next, ct);
        await redraw.Writer.WriteAsync(true, ct);
    }

    private static bool TryGetInputCharacter(KeyPress key, out char character)
    {
        if (key.Key is Key.Char or Key.Q && !char.IsControl(key.Character) && key.Character != '\0')
        {
            character = key.Character;
            return true;
        }

        character = default;
        return false;
    }

    private static IRenderable BuildLayout(UiState state)
    {
        var grid = new Grid().AddColumn();
        grid.AddRow(new Rule("[bold blue]BluKube[/]").LeftJustified());
        grid.AddRow(new Padder(BodyFor(state), new Padding(0, 1)));
        grid.AddRow(new Rule().LeftJustified());
        grid.AddRow(new Markup(Markup.Escape(FooterFor(state.Mode))));
        return grid;
    }

    private static string FooterFor(ViewMode mode) => mode switch
    {
        ViewMode.Search => "enter search  •  esc clear  •  ctrl+q quit",
        ViewMode.Results => "↑/↓ select  •  enter play  •  esc search  •  q quit",
        ViewMode.Player => "space play/pause  •  ←/→ seek  •  ↑/↓ volume  •  esc search  •  q quit",
        _ => string.Empty
    };

    private static IRenderable BodyFor(UiState state) => state.Mode switch
    {
        ViewMode.Search => RenderSearch(state),
        ViewMode.Results => RenderResults(state),
        ViewMode.Player => RenderPlayer(state),
        _ => new Markup(string.Empty),
    };

    private static IRenderable RenderSearch(UiState state)
    {
        // Fixed-width column so the panel never shrinks below a usable size and
        // always reserves height for the status row (avoids layout jumps when
        // "Searching..." appears).
        var inputCol = new TableColumn(string.Empty).Width(40);
        var inputTable = new Table().NoBorder().HideHeaders().AddColumn(inputCol);

        var input = string.IsNullOrEmpty(state.Query)
            ? "[grey]>[/] [grey]_[/]"
            : $"[grey]>[/] {Markup.Escape(state.Query)}[grey]_[/]";
        inputTable.AddRow(new Markup(input));

        // Always render the status row so height stays constant.
        var statusText = !string.IsNullOrWhiteSpace(state.Status)
            ? $"[grey]{Markup.Escape(state.Status)}[/]"
            : " ";
        inputTable.AddRow(new Markup(statusText));

        if (!string.IsNullOrWhiteSpace(state.Error))
        {
            inputTable.AddRow(new Markup($"[red]{Markup.Escape(state.Error)}[/]"));
        }

        return new Panel(inputTable).Header("[bold]search[/]");
    }

    private static IRenderable RenderResults(UiState state)
    {
        if (state.Results.Count == 0)
        {
            return new Panel(new Markup("[grey]No results.[/]")).Header("[bold]results[/]");
        }

        var table = new Table().Border(TableBorder.Rounded)
            .AddColumn(new TableColumn(string.Empty).Width(1))
            .AddColumn("Title")
            .AddColumn("Channel")
            .AddColumn("Length");

        if (state.IsBusy)
        {
            // Keep every row so column widths don't change.  Dim all content
            // to near-invisible and stamp a loading label over the middle row.
            var midRow = state.Results.Count / 2;
            for (var index = 0; index < state.Results.Count; index++)
            {
                var item = state.Results[index];
                if (index == midRow)
                {
                    table.AddRow(
                        string.Empty,
                        "[bold blue on blue] Loading... [/]",
                        $"[grey]{Markup.Escape(item.Channel)}[/]",
                        $"[grey]{FormatTime(item.Duration)}[/]");
                }
                else
                {
                    table.AddRow(
                        string.Empty,
                        $"[grey]{Markup.Escape(item.Title)}[/]",
                        $"[grey]{Markup.Escape(item.Channel)}[/]",
                        $"[grey]{FormatTime(item.Duration)}[/]");
                }
            }
        }
        else
        {
            for (var index = 0; index < state.Results.Count; index++)
            {
                var item = state.Results[index];
                var selected = index == state.SelectedIndex;
                table.AddRow(
                    selected ? "[blue]>[/]" : string.Empty,
                    FormatCell(item.Title, selected),
                    FormatCell(item.Channel, selected),
                    FormatCell(FormatTime(item.Duration), selected));
            }
        }

        return new Panel(table).Header($"[bold]results · {Markup.Escape(state.Query)}[/]");
    }

    private static IRenderable RenderPlayer(UiState state)
    {
        if (state.ServerState is not PlaybackState playback)
        {
            return new Panel(new Markup("[grey]Loading...[/]")).Header("[bold]playback[/]");
        }

        var percent = playback.Duration.TotalSeconds > 0
            ? Math.Clamp(playback.Position.TotalSeconds / playback.Duration.TotalSeconds, 0d, 1d)
            : 0d;

        var bar = new BreakdownChart()
            .Width(60)
            .HideTags()
            .AddItem("done", percent * 100, Color.Green)
            .AddItem("left", (1 - percent) * 100, Color.Grey);

        var title = state.CurrentTitle ?? playback.VideoId;
        var meta = new Grid().AddColumn().AddColumn()
            .AddRow("[grey]title[/]", Markup.Escape(title))
            .AddRow("[grey]channel[/]", Markup.Escape(state.CurrentChannel ?? string.Empty))
            .AddRow("[grey]video[/]", Markup.Escape(playback.VideoId))
            .AddRow("[grey]state[/]", playback.IsPlaying ? "[green]playing[/]" : "[yellow]paused[/]")
            .AddRow("[grey]time[/]", $"{FormatTime(playback.Position)} / {FormatTime(playback.Duration)}");

        var volume = new Grid().AddColumn().AddColumn()
            .AddRow("[grey]volume[/]", $"{(int)Math.Round(playback.Volume * 100)}%");

        var stack = new Grid().AddColumn();
        stack.AddRow(meta);
        stack.AddRow(new Markup(string.Empty)); // separator before volume
        stack.AddRow(volume);
        stack.AddRow(new Markup(string.Empty)); // separator before progress bar
        stack.AddRow(bar);

        if (!string.IsNullOrWhiteSpace(state.Error))
        {
            stack.AddRow(new Markup($"[red]{Markup.Escape(state.Error)}[/]"));
        }

        return new Panel(stack).Header("[bold]playback[/]");
    }

    private static string FormatCell(string value, bool selected)
    {
        var escaped = Markup.Escape(value);
        return selected ? $"[bold blue]{escaped}[/]" : escaped;
    }

    private static string FormatTime(TimeSpan time) =>
        time.TotalHours >= 1
            ? $"{(int)time.TotalHours}:{time.Minutes:D2}:{time.Seconds:D2}"
            : $"{time.Minutes}:{time.Seconds:D2}";

    private static string ExtractVideoId(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            var query = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in query)
            {
                var pair = part.Split('=', 2);
                if (pair.Length == 2 && pair[0] == "v")
                {
                    return Uri.UnescapeDataString(pair[1]);
                }
            }

            var segment = uri.Segments.LastOrDefault();
            if (!string.IsNullOrEmpty(segment)) return segment.TrimEnd('/');
        }

        return url;
    }

    private enum ViewMode
    {
        Search,
        Results,
        Player
    }

    private sealed class UiState
    {
        public ViewMode Mode { get; set; } = ViewMode.Search;
        public SessionState ServerState { get; set; } = new IdleState();
        public string Query { get; set; } = string.Empty;
        public IReadOnlyList<MediaItem> Results { get; set; } = [];
        public int SelectedIndex { get; set; }
        public bool IsBusy { get; set; }
        public string? Status { get; set; }
        public string? Error { get; set; }
        public string? CurrentTitle { get; set; }
        public string? CurrentChannel { get; set; }
    }
}