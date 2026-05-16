using System.ComponentModel;
using BluKube.Client.Core;
using BluKube.Client.Core.Audio;
using BluKube.Tui.Configuration;
using BluKube.Tui.Input;
using BluKube.Tui.Rendering;
using Spectre.Console;
using Spectre.Console.Cli;

namespace BluKube.Tui.Cli.Commands;

public sealed class PlayCommand(
    ConnectionResolver resolver,
    IAnsiConsole console,
    CancellationToken ct
) : AsyncCommand<PlayCommand.Settings>
{
    public sealed class Settings : GlobalSettings
    {
        [CommandArgument(0, "[query]")]
        [Description("Optional initial search query.")]
        public string? Query { get; init; }

        [CommandOption("--limit <N>")]
        [Description("Number of search results to fetch.")]
        public int Limit { get; init; } = 30;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings s)
    {
        var settings = await resolver.ResolveAsync(s.ServerUrl, s.Token, s.ForceLogin, ct);
        await using var conn = new BluKubeConnection(settings);
        await conn.ConnectAsync(ct);

        var sessionId = await conn.CreateSessionAsync(ct);

        AudioPlayer? audio = null;
        CancellationTokenSource? audioCts = null;
        Task? audioTask = null;

        try
        {
            audio = new AudioPlayer();
            audioCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            audioTask = Task.Run(
                async () =>
                {
                    try
                    {
                        await audio.PlayAsync(
                            conn.StreamAudioAsync(audioCts.Token),
                            audioCts.Token
                        );
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception ex)
                    {
                        console.MarkupLine(
                            $"[yellow]audio stopped:[/] {Markup.Escape(ex.Message)}"
                        );
                    }
                },
                audioCts.Token
            );

            var view = new ViewController(console, new ConsoleKeyInput(), conn, s.Query, s.Limit);
            await view.RunAsync(ct);
        }
        finally
        {
            try
            {
                audioCts?.Cancel();
            }
            catch { }
            if (audioTask is not null)
            {
                try
                {
                    await audioTask;
                }
                catch { }
            }
            if (audio is not null)
            {
                try
                {
                    await audio.DisposeAsync();
                }
                catch { }
            }

            audioCts?.Dispose();
            try
            {
                await conn.CloseSessionAsync(sessionId, CancellationToken.None);
            }
            catch
            { /* best-effort */
            }
        }
        return 0;
    }
}
