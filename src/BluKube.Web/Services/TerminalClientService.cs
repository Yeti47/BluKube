using BluKube.Tui.Rendering;
using BluKube.Web.Components.Pages;
using BluKube.Web.Xterm;
using Microsoft.JSInterop;
using Spectre.Console;

namespace BluKube.Web.Services;

public sealed class TerminalClientService(
    IJSRuntime js,
    ClientSessionService session,
    AudioStreamService audio) : IAsyncDisposable
{
    private readonly XtermWriter _writer = new();
    private readonly XtermKeyInput _keyInput = new();

    public Task? ViewTask { get; private set; }
    public Task? PumpTask { get; private set; }

    public async Task StartAsync(DotNetObjectReference<Home> homeReference)
    {
        var dims = await js.InvokeAsync<TerminalDims>(
            "xtermBridge.init", homeReference, "xterm-container");

        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.Yes,
            ColorSystem = ColorSystemSupport.TrueColor,
            Out = new AnsiConsoleOutput(_writer),
            Interactive = InteractionSupport.No,
        });
        console.Profile.Width = dims.Cols;
        console.Profile.Height = dims.Rows;

        PumpTask = PumpOutputAsync();

        var connection = await session.ConnectAsync();
        audio.Start(WriteWarning);

        var controller = new ViewController(console, _keyInput, connection, enableQuitKeys: false);
        ViewTask = controller.RunAsync(session.Token);
    }

    public void PostKey(string key, bool shift, bool ctrl, bool alt) =>
        _keyInput.Post(key, shift, ctrl, alt);

    public void WriteError(string message) =>
        _writer.WriteLine($"\x1b[31mError: {message}\x1b[0m");

    public void WriteWarning(string message) =>
        _writer.WriteLine($"\x1b[33m{message}\x1b[0m");

    public async Task DisposeXtermAsync()
    {
        try { await js.InvokeVoidAsync("xtermBridge.dispose"); } catch { }
    }

    public void ClearTasks()
    {
        ViewTask = null;
        PumpTask = null;
    }

    private async Task PumpOutputAsync()
    {
        try
        {
            await foreach (var chunk in _writer.Output.ReadAllAsync(session.Token))
                await js.InvokeVoidAsync("xtermBridge.write", session.Token, chunk);
        }
        catch (OperationCanceledException) { }
        catch (JSDisconnectedException) { }
    }

    public async ValueTask DisposeAsync()
    {
        _keyInput.Complete();
        _writer.Dispose();
        await DisposeXtermAsync();
    }

    private sealed record TerminalDims(int Cols, int Rows);
}
