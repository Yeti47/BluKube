using BluKube.Tui.Rendering;
using BluKube.Web.Xterm;
using Microsoft.JSInterop;
using Spectre.Console;

namespace BluKube.Web.Services;

public sealed class TerminalClientService(
    IJSRuntime js,
    ClientSessionService session,
    AudioStreamService audio,
    TerminalKeyDispatcher keyDispatcher
) : IClientViewService, IAsyncDisposable
{
    private readonly XtermWriter _writer = new();
    private readonly XtermKeyInput _keyInput = new();
    private DotNetObjectReference<TerminalKeyDispatcher>? _keyDispatcherReference;
    private bool _keyDispatcherAttached;

    public ClientView View => ClientView.Terminal;
    public Task? ViewTask { get; private set; }
    public Task? PumpTask { get; private set; }

    public async Task StartAsync()
    {
        EnsureKeyDispatcherAttached();

        var dims = await js.InvokeAsync<TerminalDims>(
            "xtermBridge.init",
            _keyDispatcherReference,
            "xterm-container"
        );

        var console = AnsiConsole.Create(
            new AnsiConsoleSettings
            {
                Ansi = AnsiSupport.Yes,
                ColorSystem = ColorSystemSupport.TrueColor,
                Out = new AnsiConsoleOutput(_writer),
                Interactive = InteractionSupport.No,
            }
        );
        console.Profile.Width = dims.Cols;
        console.Profile.Height = dims.Rows;

        PumpTask = PumpOutputAsync();

        var connection = await session.ConnectAsync();
        audio.Start(WriteWarning);

        var controller = new ViewController(console, _keyInput, connection, enableQuitKeys: false);
        ViewTask = controller.RunAsync(session.Token);
    }

    private void HandleKeyReceived(object? sender, TerminalKeyEventArgs args) =>
        _keyInput.Post(args.Key, args.Shift, args.Ctrl, args.Alt);

    public async Task ActivateAsync()
    {
        try
        {
            await StartAsync();
        }
        catch (Exception ex)
        {
            WriteError(ex.Message);
        }
    }

    public async Task DeactivateAsync(bool resetSession = true)
    {
        await session.StopAsync(ViewTask, PumpTask, audio.PumpTask);
        await DisposeXtermAsync();

        ClearTasks();
        audio.Clear();

        if (resetSession)
            session.ResetCancellation();
    }

    public void ClearState()
    {
        ClearTasks();
    }

    public void WriteError(string message) => _writer.WriteLine($"\x1b[31mError: {message}\x1b[0m");

    public void WriteWarning(string message) => _writer.WriteLine($"\x1b[33m{message}\x1b[0m");

    public async Task DisposeXtermAsync()
    {
        try
        {
            await js.InvokeVoidAsync("xtermBridge.dispose");
        }
        catch { }
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
        if (_keyDispatcherAttached)
            keyDispatcher.KeyReceived -= HandleKeyReceived;

        _keyDispatcherReference?.Dispose();
        _keyInput.Complete();
        _writer.Dispose();
        await DisposeXtermAsync();
    }

    private void EnsureKeyDispatcherAttached()
    {
        if (_keyDispatcherAttached)
            return;

        keyDispatcher.KeyReceived += HandleKeyReceived;
        _keyDispatcherReference = DotNetObjectReference.Create(keyDispatcher);
        _keyDispatcherAttached = true;
    }

    private sealed record TerminalDims(int Cols, int Rows);
}
