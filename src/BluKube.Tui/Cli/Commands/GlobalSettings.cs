using System.ComponentModel;
using Spectre.Console.Cli;

namespace BluKube.Tui.Cli.Commands;

public class GlobalSettings : CommandSettings
{
    [CommandOption("--server <URL>")]
    [Description("Override the server URL (otherwise: stored config or prompt).")]
    public string? ServerUrl { get; init; }

    [CommandOption("--token <TOKEN>")]
    [Description("Override the bearer token (otherwise: stored config or prompt).")]
    public string? Token { get; init; }

    [CommandOption("--login")]
    [Description("Force re-prompting for server URL and token.")]
    public bool ForceLogin { get; init; }
}
