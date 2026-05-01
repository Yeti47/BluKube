using Spectre.Console;
using YtCliRadio.Browser;
using YtCliRadio.Configuration;
using YtCliRadio.Domain;

namespace YtCliRadio.App;

public sealed class CliApplication
{
    private readonly AppOptions _options;
    private readonly IYouTubeBrowserClient _browserClient;

    public CliApplication(AppOptions options, IYouTubeBrowserClient browserClient)
    {
        _options = options;
        _browserClient = browserClient;
    }

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        await using var browser = _browserClient;

        var state = await BuildQueueStateAsync(_options.Query, cancellationToken);
        if (state is null)
        {
            return 3;
        }

        if (_options.DryRun)
        {
            var selected = state.Queue[state.Index];
            var escapedTitle = Markup.Escape(selected.Title);
            var escapedChannel = Markup.Escape(selected.Channel);
            AnsiConsole.MarkupLine($"[green]Dry run:[/] {escapedTitle} by {escapedChannel}");
            return 0;
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            var selected = state.Queue[state.Index];
            var escapedTitle = Markup.Escape(selected.Title);
            var escapedChannel = Markup.Escape(selected.Channel);
            AnsiConsole.MarkupLine($"[green]Now playing:[/] {escapedTitle} — {escapedChannel}");
            AnsiConsole.MarkupLine("[grey]Controls: Space=pause/resume, N=next, R=search again, Q=quit[/]");

            await _browserClient.StartPlaybackAsync(selected, cancellationToken);
            var action = await WaitForActionAsync(cancellationToken);

            switch (action)
            {
                case PlaybackAction.NextTrack:
                {
                    if (state.Index < state.Queue.Count - 1)
                    {
                        state.Index++;
                        continue;
                    }

                    AnsiConsole.MarkupLine("[yellow]Queue ended.[/]");
                    return 0;
                }
                case PlaybackAction.NewSearch:
                {
                    var newQuery = await AskTextAsync("New search query:", cancellationToken);
                    state = await BuildQueueStateAsync(newQuery, cancellationToken);
                    if (state is null)
                    {
                        return 3;
                    }

                    continue;
                }
                case PlaybackAction.Quit:
                    return 0;
                default:
                    throw new InvalidOperationException("Unknown playback action.");
            }
        }

        return 130;
    }

    private async Task<PlaybackAction> WaitForActionAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (await _browserClient.IsTrackEndedAsync(cancellationToken))
            {
                return PlaybackAction.NextTrack;
            }

            if (Console.IsInputRedirected || !Console.KeyAvailable)
            {
                await Task.Delay(250, cancellationToken);
                continue;
            }

            var key = Console.ReadKey(intercept: true).Key;
            switch (key)
            {
                case ConsoleKey.Spacebar:
                {
                    if (await _browserClient.IsPausedAsync(cancellationToken))
                    {
                        await _browserClient.ResumeAsync(cancellationToken);
                        if (await _browserClient.IsPausedAsync(cancellationToken))
                        {
                            AnsiConsole.MarkupLine("[yellow]Still paused (playback blocked).[/]");
                            AnsiConsole.MarkupLine("[grey]Tip: headless media playback is environment-dependent; verify audio sink/output and runtime playback permissions.[/]");
                        }
                        else
                        {
                            AnsiConsole.MarkupLine("[grey]Resumed[/]");
                        }
                    }
                    else
                    {
                        await _browserClient.PauseAsync(cancellationToken);
                        if (await _browserClient.IsPausedAsync(cancellationToken))
                        {
                            AnsiConsole.MarkupLine("[grey]Paused[/]");
                        }
                        else
                        {
                            AnsiConsole.MarkupLine("[yellow]Pause command was not applied.[/]");
                        }
                    }

                    break;
                }
                case ConsoleKey.N:
                    return PlaybackAction.NextTrack;
                case ConsoleKey.R:
                    return PlaybackAction.NewSearch;
                case ConsoleKey.Q:
                    return PlaybackAction.Quit;
            }
        }

        return PlaybackAction.Quit;
    }

    private async Task<QueueState?> BuildQueueStateAsync(string query, CancellationToken cancellationToken)
    {
        var effectiveQuery = string.IsNullOrWhiteSpace(query)
            ? await AskTextAsync("Search query:", cancellationToken)
            : query;
        var results = await _browserClient.SearchAsync(effectiveQuery, _options.ResultLimit, cancellationToken);
        if (results.Count == 0)
        {
            AnsiConsole.MarkupLine("[red]No search results found.[/]");
            return null;
        }

        var selected = await SelectResultAsync(results, cancellationToken);
        var selectedIndex = FindSelectedIndex(results, selected);
        return new QueueState(results, selectedIndex);
    }

    private static async Task<VideoSearchResult> SelectResultAsync(
        IReadOnlyList<VideoSearchResult> results,
        CancellationToken cancellationToken)
    {
        if (!AnsiConsole.Profile.Capabilities.Interactive)
        {
            return results[0];
        }

        var selector = new SelectionPrompt<VideoSearchResult>()
            .Title("Pick a track:")
            .PageSize(10)
            .UseConverter(item =>
            {
                var durationSuffix = string.IsNullOrWhiteSpace(item.Duration) ? string.Empty : $" [{item.Duration}]";
                return Markup.Escape($"{item.Title} — {item.Channel}{durationSuffix}");
            })
            .AddChoices(results);

        return await selector.ShowAsync(AnsiConsole.Console, cancellationToken);
    }

    private static async Task<string> AskTextAsync(string prompt, CancellationToken cancellationToken)
    {
        var textPrompt = new TextPrompt<string>(prompt);
        return await textPrompt.ShowAsync(AnsiConsole.Console, cancellationToken);
    }

    private static int FindSelectedIndex(IReadOnlyList<VideoSearchResult> results, VideoSearchResult selected)
    {
        for (var i = 0; i < results.Count; i++)
        {
            if (results[i] == selected)
            {
                return i;
            }
        }

        return 0;
    }

    private enum PlaybackAction
    {
        NextTrack,
        NewSearch,
        Quit
    }

    private sealed class QueueState
    {
        public QueueState(IReadOnlyList<VideoSearchResult> queue, int index)
        {
            Queue = queue;
            Index = index;
        }

        public IReadOnlyList<VideoSearchResult> Queue { get; }
        public int Index { get; set; }
    }
}
