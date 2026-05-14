using BluKube.Client.Core;
using BluKube.Tui.Cli;
using BluKube.Tui.Cli.Commands;
using BluKube.Tui.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;

var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

var services = new ServiceCollection();
services.AddSingleton(AnsiConsole.Console);
services.AddSingleton<FileConfigStore>();
services.AddSingleton<IConfigStore>(sp => sp.GetRequiredService<FileConfigStore>());
services.AddSingleton<ConnectionResolver>();
services.AddSingleton(typeof(CancellationToken), cts.Token);

var app = new CommandApp<PlayCommand>(new TypeRegistrar(services));
app.Configure(c =>
{
    c.SetApplicationName("blukube");
    c.AddCommand<PlayCommand>("play").WithDescription("Search and play. Drops into the live TUI.");
    c.AddCommand<ConfigCommand>("config").WithDescription("Show or clear stored connection config.");
});

return await app.RunAsync(args);
