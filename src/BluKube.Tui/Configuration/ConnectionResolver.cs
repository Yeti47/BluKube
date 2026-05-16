using BluKube.Client.Core;
using Spectre.Console;

namespace BluKube.Tui.Configuration;

/// <summary>
/// Resolves <see cref="ConnectionSettings"/> for the TUI: tries the
/// config store, then prompts the user (server URL + token) and
/// persists the result.
/// </summary>
public sealed class ConnectionResolver(IConfigStore store, IAnsiConsole console)
{
    public async Task<ConnectionSettings> ResolveAsync(
        string? overrideUrl,
        string? overrideToken,
        bool forcePrompt,
        CancellationToken ct
    )
    {
        var existing = await store.LoadAsync(ct);

        var url = overrideUrl ?? existing?.ServerUrl.ToString();
        var token = overrideToken ?? existing?.Token;

        if (forcePrompt || string.IsNullOrWhiteSpace(url))
        {
            url = console.Prompt(
                new TextPrompt<string>("[grey]Server URL:[/]").DefaultValue(
                    url ?? "http://127.0.0.1:8765"
                )
            );
        }

        if (forcePrompt || string.IsNullOrWhiteSpace(token))
        {
            token = console.Prompt(new TextPrompt<string>("[grey]Auth token:[/]").Secret());
        }

        var settings = new ConnectionSettings(new Uri(url!), token);
        await store.SaveAsync(settings, ct);
        return settings;
    }
}
