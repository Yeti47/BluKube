using System.ComponentModel;
using BluKube.Client.Core;
using BluKube.Tui.Configuration;
using BluKube.Tui.Input;
using BluKube.Tui.Rendering;
using Spectre.Console;
using Spectre.Console.Cli;

namespace BluKube.Tui.Cli.Commands;

public sealed class PlayCommand(
    ConnectionResolver resolver,
    IAnsiConsole console,
    CancellationToken ct) : AsyncCommand<PlayCommand.Settings>
{
    public sealed class Settings : GlobalSettings
    {
        [CommandArgument(0, "[query]")]
        [Description("Optional search query. Omit to drop straight into the player.")]
        public string? Query { get; init; }

        [CommandOption("--limit <N>")]
        [Description("Number of search results to fetch.")]
        public int Limit { get; init; } = 10;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings s)
    {
        var settings = await resolver.ResolveAsync(s.ServerUrl, s.Token, s.ForceLogin, ct);
        await using var conn = new BluKubeConnection(settings);
        await conn.ConnectAsync(ct);

        var sessionId = await conn.CreateSessionAsync(ct);
        console.MarkupLine($"[grey]session[/] {sessionId}");

        if (!string.IsNullOrWhiteSpace(s.Query))
        {
            var state = await conn.SearchAsync(s.Query, s.Limit, ct);
            if (state is SearchResultsState results && results.Items.Count > 0)
            {
                var pick = console.Prompt(new SelectionPrompt<int>()
                    .Title("Pick a track:")
                    .AddChoices(Enumerable.Range(0, results.Items.Count))
                    .UseConverter(i => $"{i + 1}. {results.Items[i].Title} — {results.Items[i].Channel}"));
                var videoId = ExtractVideoId(results.Items[pick].Url);
                await conn.PlayAsync(videoId, ct);
            }
            else if (state is ErrorState err)
            {
                console.MarkupLine($"[red]error[/] {Markup.Escape(err.Message)}");
                return 1;
            }
        }

        var view = new PlayerView(console, new ConsoleKeyInput(), conn);
        await view.RunAsync(ct);

        try { await conn.CloseSessionAsync(sessionId, CancellationToken.None); }
        catch { /* best-effort */ }
        return 0;
    }

    private static string ExtractVideoId(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var u))
        {
            var v = System.Web.HttpUtility.ParseQueryString(u.Query)["v"];
            if (!string.IsNullOrEmpty(v)) return v;
            var seg = u.Segments.LastOrDefault();
            if (!string.IsNullOrEmpty(seg)) return seg.TrimEnd('/');
        }
        return url;
    }
}
