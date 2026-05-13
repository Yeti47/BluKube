using System.ComponentModel;
using BluKube.Client.Core;
using BluKube.Tui.Configuration;
using BluKube.Tui.Input;
using BluKube.Tui.Rendering;
using Spectre.Console;
using Spectre.Console.Cli;

namespace BluKube.Tui.Cli.Commands;

public sealed class AttachCommand(
    ConnectionResolver resolver,
    IAnsiConsole console,
    CancellationToken ct) : AsyncCommand<AttachCommand.Settings>
{
    public sealed class Settings : GlobalSettings
    {
        [CommandOption("--session <ID>")]
        [Description("Session ID to attach to.")]
        public Guid SessionId { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings s)
    {
        if (s.SessionId == Guid.Empty)
        {
            console.MarkupLine("[red]--session is required[/]");
            return 1;
        }

        var settings = await resolver.ResolveAsync(s.ServerUrl, s.Token, s.ForceLogin, ct);
        await using var conn = new BluKubeConnection(settings);
        await conn.ConnectAsync(ct);
        await conn.AttachSessionAsync(s.SessionId, ct);

        var view = new PlayerView(console, new ConsoleKeyInput(), conn);
        await view.RunAsync(ct);
        return 0;
    }
}
