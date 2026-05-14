using System.ComponentModel;
using BluKube.Client.Core;
using BluKube.Client.Core.Audio;
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

        await using var audio = new AudioPlayer();
        using var audioCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var audioTask = Task.Run(async () =>
        {
            try { await audio.PlayAsync(conn.StreamAudioAsync(audioCts.Token), audioCts.Token); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { console.MarkupLine($"[yellow]audio stopped:[/] {Markup.Escape(ex.Message)}"); }
        }, audioCts.Token);

        var view = new PlayerView(console, new ConsoleKeyInput(), conn);
        await view.RunAsync(ct);

        try { audioCts.Cancel(); } catch { }
        try { await audioTask; } catch { }
        return 0;
    }
}
