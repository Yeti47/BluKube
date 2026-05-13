using System.Net.Http.Headers;
using System.Net.Http.Json;
using BluKube.Client.Core;
using BluKube.Tui.Configuration;
using Spectre.Console;
using Spectre.Console.Cli;

namespace BluKube.Tui.Cli.Commands;

public sealed class SessionsCommand(
    ConnectionResolver resolver,
    IAnsiConsole console,
    CancellationToken ct) : AsyncCommand<SessionsCommand.Settings>
{
    public sealed class Settings : GlobalSettings
    {
        [CommandOption("--list")] public bool List { get; init; }
        [CommandOption("--new")] public bool New { get; init; }
        [CommandOption("--close <ID>")] public Guid? Close { get; init; }
    }

    private sealed record SessionDto(Guid Id, DateTimeOffset LastActivityAt);

    public override async Task<int> ExecuteAsync(CommandContext context, Settings s)
    {
        var settings = await resolver.ResolveAsync(s.ServerUrl, s.Token, s.ForceLogin, ct);

        using var http = new HttpClient { BaseAddress = settings.RestBase };
        if (!string.IsNullOrEmpty(settings.Token))
        {
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.Token);
        }

        if (s.New)
        {
            var resp = await http.PostAsync("sessions", content: null, ct);
            resp.EnsureSuccessStatusCode();
            var dto = await resp.Content.ReadFromJsonAsync<SessionDto>(cancellationToken: ct);
            console.MarkupLine($"[green]created[/] {dto!.Id}");
            return 0;
        }

        if (s.Close is { } id)
        {
            var resp = await http.DeleteAsync($"sessions/{id}", ct);
            console.MarkupLine(resp.IsSuccessStatusCode
                ? $"[green]closed[/] {id}"
                : $"[red]{(int)resp.StatusCode} {resp.ReasonPhrase}[/]");
            return resp.IsSuccessStatusCode ? 0 : 1;
        }

        var list = await http.GetFromJsonAsync<SessionDto[]>("sessions", ct) ?? [];
        if (list.Length == 0)
        {
            console.MarkupLine("[grey]no active sessions[/]");
            return 0;
        }

        var table = new Table().Border(TableBorder.Rounded)
            .AddColumn("Id").AddColumn("Last activity");
        foreach (var x in list)
            table.AddRow(x.Id.ToString(), x.LastActivityAt.ToLocalTime().ToString("HH:mm:ss"));
        console.Write(table);
        return 0;
    }
}
