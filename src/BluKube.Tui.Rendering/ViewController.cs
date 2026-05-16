using System.Threading.Channels;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace BluKube.Tui.Rendering;

/// <summary>
/// Top-level live TUI shell. Owns the render loop and the server state pump;
/// delegates view-specific rendering to <see cref="AppShell"/>,
/// <see cref="SearchBox"/>, <see cref="ResultsTable"/>, and
/// <see cref="PlaybackPanel"/>; delegates input handling to the three
/// view-controller classes.
/// </summary>
public sealed class ViewController(
    IAnsiConsole console,
    IKeyInput keys,
    BluKubeConnection connection,
    string? initialQuery = null,
    int limit = 10,
    bool enableQuitKeys = true)
{
    private readonly IView[] _views =
    [
        new SearchViewController(connection, limit),
        new ResultsViewController(connection),
        new PlaybackViewController(connection)
    ];

    public async Task RunAsync(CancellationToken ct)
    {
        var state = new UiState { Query = initialQuery ?? string.Empty };
        var redraw = Channel.CreateUnbounded<bool>(new UnboundedChannelOptions
        {
            SingleReader = true,
            AllowSynchronousContinuations = false
        });

        using var loopCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var stateTask = Task.Run(() => PumpStatesAsync(state, redraw, loopCts.Token), loopCts.Token);
        var keyTask   = Task.Run(() => PumpKeysAsync(state, redraw, loopCts), loopCts.Token);

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

    // -------------------------------------------------------------------------
    // Background pumps
    // -------------------------------------------------------------------------

    private async Task PumpStatesAsync(UiState ui, Channel<bool> redraw, CancellationToken ct)
    {
        try
        {
            await foreach (var state in connection.StreamStatesAsync(ct))
            {
                ui.ServerState = state;
                if (state is ErrorState error)
                    ui.Error = error.Message;
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

                if (key.IsAltCharacter('x'))
                {
                    console.Clear();
                    await redraw.Writer.WriteAsync(true, ct);
                    continue;
                }

                try
                {
                    if (!state.IsBusy)
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

    // -------------------------------------------------------------------------
    // Dispatch
    // -------------------------------------------------------------------------

    private bool ShouldQuit(KeyPress key, ViewMode mode)
        => enableQuitKeys &&
           (key is { Key: Key.Q, Ctrl: true } ||
           key.Key == Key.Q && mode is ViewMode.Results or ViewMode.Player);

    private Task DispatchAsync(KeyPress key, UiState state, Channel<bool> redraw, CancellationToken ct)
    {
        var view = _views.FirstOrDefault(v => v.Mode == state.Mode);
        return view?.DispatchAsync(key, state, redraw, ct) ?? Task.CompletedTask;
    }

    // -------------------------------------------------------------------------
    // Layout
    // -------------------------------------------------------------------------

    private IRenderable BuildLayout(UiState state)
    {
        IRenderable body = state.Mode switch
        {
            ViewMode.Search  => new SearchBox(state.Query, state.Status, state.Error),
            ViewMode.Results => BuildResultsTable(state),
            ViewMode.Player  => new PlaybackPanel(state.ServerState as PlaybackState, state.CurrentTitle, state.CurrentChannel, state.Error, state.CompactPlayback),
            _                => new Markup(string.Empty)
        };

        var footer = state.Mode switch
        {
            ViewMode.Search  => enableQuitKeys ? "enter search  •  esc clear  •  ctrl+q quit" : "enter search  •  esc clear",
            ViewMode.Results => enableQuitKeys ? "↑/↓ select  •  ←/→ page  •  enter play  •  esc search  •  q quit" : "↑/↓ select  •  ←/→ page  •  enter play  •  esc search",
            ViewMode.Player  => enableQuitKeys ? "space play/pause  •  ←/→ seek  •  ↑/↓ volume  •  alt+c compact  •  alt+x clear  •  esc search  •  q quit" : "space play/pause  •  ←/→ seek  •  ↑/↓ volume  •  alt+c compact  •  alt+x clear  •  esc search",
            _                => string.Empty
        };

        return new AppShell(body, footer);
    }

    private static ResultsTable BuildResultsTable(UiState state)
    {
        var pageStart = state.Page * state.PageSize;
        var pageItems = state.Results.Skip(pageStart).Take(state.PageSize).ToList();
        var localIndex = state.SelectedIndex - pageStart;
        var totalPages = (state.Results.Count + state.PageSize - 1) / state.PageSize;
        var pageInfo = totalPages > 1 ? $"page {state.Page + 1}/{totalPages}" : null;
        return new ResultsTable(pageItems, localIndex, state.IsBusy, state.Query, pageInfo);
    }
}

