using System.Threading.Channels;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace BluKube.Tui.Rendering;

/// <summary>
/// The interactive player view. Pluggable on three axes:
/// <list type="bullet">
///   <item><description><see cref="IAnsiConsole"/> — output sink (real terminal or xterm.js bridge).</description></item>
///   <item><description><see cref="IKeyInput"/> — keystroke source.</description></item>
///   <item><description><see cref="BluKubeConnection"/> — server transport.</description></item>
/// </list>
/// </summary>
public sealed class PlayerView(IAnsiConsole console, IKeyInput keys, BluKubeConnection connection)
{

    /// <summary>
    /// Runs the live render loop until the user quits or
    /// <paramref name="ct"/> is cancelled.
    /// </summary>
    public async Task RunAsync(CancellationToken ct)
    {
        var stateRef = new StateBox(new IdleState());
        var redraw = Channel.CreateUnbounded<bool>(new UnboundedChannelOptions
        {
            SingleReader = true,
            AllowSynchronousContinuations = false
        });

        using var loopCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var stateTask = Task.Run(() => PumpStatesAsync(stateRef, redraw, loopCts.Token), loopCts.Token);
        var keyTask = Task.Run(() => PumpKeysAsync(stateRef, redraw, loopCts), loopCts.Token);

        try
        {
            await console.Live(BuildLayout(stateRef.Current))
                .StartAsync(async ctx =>
                {
                    while (await redraw.Reader.WaitToReadAsync(loopCts.Token))
                    {
                        while (redraw.Reader.TryRead(out _)) { /* drain */ }
                        ctx.UpdateTarget(BuildLayout(stateRef.Current));
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

    // --- pumps ---------------------------------------------------------------

    private async Task PumpStatesAsync(StateBox box, Channel<bool> redraw, CancellationToken ct)
    {
        try
        {
            await foreach (var state in connection.StreamStatesAsync(ct))
            {
                box.Current = state;
                await redraw.Writer.WriteAsync(true, ct);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            redraw.Writer.TryComplete();
        }
    }

    private async Task PumpKeysAsync(StateBox box, Channel<bool> redraw, CancellationTokenSource loopCts)
    {
        var ct = loopCts.Token;
        try
        {
            await foreach (var key in keys.ReadKeysAsync(ct))
            {
                if (key.Key == Key.Q || key.Key == Key.Escape)
                {
                    loopCts.Cancel();
                    break;
                }

                try
                {
                    await DispatchAsync(key, box.Current, ct);
                }
                catch (Exception ex)
                {
                    box.Current = new ErrorState("client_error", ex.Message, box.Current);
                    await redraw.Writer.WriteAsync(true, ct);
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task DispatchAsync(KeyPress key, SessionState current, CancellationToken ct)
    {
        switch (key.Key)
        {
            case Key.Space:
                if (current is PlaybackState pb)
                {
                    if (pb.IsPlaying) await connection.PauseAsync(ct);
                    else await connection.ResumeAsync(ct);
                }
                break;

            case Key.Plus:
                if (current is PlaybackState p1)
                {
                    await connection.SetVolumeAsync(Math.Clamp(p1.Volume + 0.05f, 0f, 1f), ct);
                }
                break;

            case Key.Minus:
                if (current is PlaybackState p2)
                {
                    await connection.SetVolumeAsync(Math.Clamp(p2.Volume - 0.05f, 0f, 1f), ct);
                }
                break;

            case Key.LeftArrow:
                if (current is PlaybackState p3)
                {
                    var next = p3.Position - TimeSpan.FromSeconds(key.Shift ? 30 : 10);
                    if (next < TimeSpan.Zero) next = TimeSpan.Zero;
                    await connection.SeekToAsync(next, ct);
                }
                break;

            case Key.RightArrow:
                if (current is PlaybackState p4)
                {
                    var next = p4.Position + TimeSpan.FromSeconds(key.Shift ? 30 : 10);
                    if (p4.Duration > TimeSpan.Zero && next > p4.Duration) next = p4.Duration;
                    await connection.SeekToAsync(next, ct);
                }
                break;
        }
    }

    // --- rendering -----------------------------------------------------------

    private static IRenderable BuildLayout(SessionState state)
    {
        var grid = new Grid().AddColumn();
        grid.AddRow(new Rule("[bold blue]BluKube[/]").LeftJustified());
        grid.AddRow(new Padder(BodyFor(state), new Padding(0, 1)));
        grid.AddRow(new Rule().LeftJustified());
        grid.AddRow(new Markup(
            "[grey]space[/] play/pause   " +
            "[grey]←/→[/] seek 10s ([grey]shift[/] = 30s)   " +
            "[grey]+/-[/] volume   " +
            "[grey]q[/] quit"));
        return grid;
    }

    private static IRenderable BodyFor(SessionState state) => state switch
    {
        IdleState => new Markup("[grey]Idle. Use the CLI to start playback.[/]"),

        SearchResultsState s => RenderSearchResults(s),

        PlaybackState p => RenderPlayback(p),

        ErrorState e => new Panel(new Markup($"[red]{Markup.Escape(e.Message)}[/]"))
            .Header($"[red]error · {Markup.Escape(e.Code)}[/]")
            .BorderColor(Color.Red),

        _ => new Markup($"[grey]{Markup.Escape(state.GetType().Name)}[/]"),
    };

    private static IRenderable RenderSearchResults(SearchResultsState s)
    {
        var table = new Table().Border(TableBorder.Rounded)
            .AddColumn("#")
            .AddColumn("Title")
            .AddColumn("Channel")
            .AddColumn("Length");
        for (var i = 0; i < s.Items.Count; i++)
        {
            var it = s.Items[i];
            table.AddRow(
                (i + 1).ToString(),
                Markup.Escape(it.Title),
                Markup.Escape(it.Channel),
                FormatTime(it.Duration));
        }
        return new Panel(table).Header($"[bold]results · {Markup.Escape(s.Query)}[/]");
    }

    private static IRenderable RenderPlayback(PlaybackState p)
    {
        var pct = p.Duration.TotalSeconds > 0
            ? Math.Clamp(p.Position.TotalSeconds / p.Duration.TotalSeconds, 0d, 1d)
            : 0d;

        var bar = new BreakdownChart()
            .Width(60)
            .HideTags()
            .AddItem("done", pct * 100, Color.Green)
            .AddItem("left", (1 - pct) * 100, Color.Grey);

        var info = new Grid().AddColumn().AddColumn()
            .AddRow("[grey]video[/]", Markup.Escape(p.VideoId))
            .AddRow("[grey]state[/]", p.IsPlaying ? "[green]playing[/]" : "[yellow]paused[/]")
            .AddRow("[grey]time[/]", $"{FormatTime(p.Position)} / {FormatTime(p.Duration)}")
            .AddRow("[grey]volume[/]", $"{(int)Math.Round(p.Volume * 100)}%");

        var stack = new Grid().AddColumn();
        stack.AddRow(info);
        stack.AddRow(bar);
        return new Panel(stack).Header("[bold]playback[/]");
    }

    private static string FormatTime(TimeSpan t) =>
        t.TotalHours >= 1
            ? $"{(int)t.TotalHours}:{t.Minutes:D2}:{t.Seconds:D2}"
            : $"{t.Minutes}:{t.Seconds:D2}";

    private sealed class StateBox(SessionState initial)
    {
        public SessionState Current { get; set; } = initial;
    }
}
