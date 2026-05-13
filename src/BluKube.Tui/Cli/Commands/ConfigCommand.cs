using BluKube.Tui.Configuration;
using Spectre.Console;
using Spectre.Console.Cli;

namespace BluKube.Tui.Cli.Commands;

public sealed class ConfigCommand(
    FileConfigStore store,
    IAnsiConsole console) : Command<ConfigCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("--show")] public bool Show { get; init; }
        [CommandOption("--clear")] public bool Clear { get; init; }
    }

    public override int Execute(CommandContext context, Settings s)
    {
        if (s.Clear)
        {
            store.ClearAsync().GetAwaiter().GetResult();
            console.MarkupLine($"[grey]cleared[/] {store.Path}");
            return 0;
        }

        var current = store.LoadAsync().GetAwaiter().GetResult();
        console.MarkupLine($"[grey]config:[/] {store.Path}");
        if (current is null)
            console.MarkupLine("[grey](empty)[/]");
        else
        {
            console.MarkupLine($"[grey]server:[/] {current.ServerUrl}");
            console.MarkupLine($"[grey]token:[/]  {(string.IsNullOrEmpty(current.Token) ? "(none)" : "***")}");
        }
        return 0;
    }
}
