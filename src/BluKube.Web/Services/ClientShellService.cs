using BluKube.Web.Components.Pages;
using Microsoft.JSInterop;

namespace BluKube.Web.Services;

public enum ClientState { Loading, Login, Client }

public enum ClientView { Terminal, Native }

public sealed class ClientShellService(
    IJSRuntime js,
    ClientSessionService session,
    AudioStreamService audio,
    TerminalClientService terminal,
    NativeClientService native)
{
    private bool _pendingTerminalInit;
    private bool _pendingNativeInit;

    public event EventHandler? StateChanged;

    public ClientState State { get; private set; } = ClientState.Loading;
    public ClientView View { get; private set; } = ClientView.Terminal;
    public bool IsNativeView => View == ClientView.Native;
    public string PageClass => IsNativeView ? "native-page" : "terminal-page";

    public async Task InitializeAsync()
    {
        if (!await session.HasSavedSettingsAsync())
        {
            State = ClientState.Login;
        }
        else
        {
            View = await PreferNativeClientAsync();
            State = ClientState.Client;
            QueueClientInit();
        }

        NotifyStateChanged();
    }

    public async Task StartClientAsync()
    {
        View = await PreferNativeClientAsync();
        State = ClientState.Client;
        QueueClientInit();
        NotifyStateChanged();
    }

    public async Task ClearConfigAsync()
    {
        await session.ClearSettingsAsync();
        await StopAsync();

        native.Query = string.Empty;
        native.Reset();
        _pendingTerminalInit = false;
        _pendingNativeInit = false;
        State = ClientState.Login;
        NotifyStateChanged();
    }

    public Task ShowNativeAsync() => SwitchClientViewAsync(ClientView.Native);

    public Task ShowTerminalAsync() => SwitchClientViewAsync(ClientView.Terminal);

    public async Task StartPendingClientAsync(DotNetObjectReference<Home>? homeReference)
    {
        if (_pendingTerminalInit)
        {
            _pendingTerminalInit = false;
            try
            {
                if (homeReference is not null)
                    await terminal.StartAsync(homeReference);
            }
            catch (Exception ex)
            {
                terminal.WriteError(ex.Message);
            }
        }

        if (_pendingNativeInit)
        {
            _pendingNativeInit = false;
            try
            {
                await native.StartAsync();
            }
            catch (Exception ex)
            {
                native.SetError(ex.Message);
                NotifyStateChanged();
            }
        }
    }

    public void PostKey(string key, bool shift, bool ctrl, bool alt) =>
        terminal.PostKey(key, shift, ctrl, alt);

    public async Task StopAsync(bool resetSession = true)
    {
        await session.StopAsync(terminal.ViewTask, terminal.PumpTask, audio.PumpTask, native.StateTask);
        await terminal.DisposeXtermAsync();

        terminal.ClearTasks();
        native.ClearTasks();
        audio.Clear();

        if (resetSession)
            session.ResetCancellation();
    }

    private async Task SwitchClientViewAsync(ClientView view)
    {
        if (View == view) return;

        await StopAsync();

        View = view;
        native.Reset();
        QueueClientInit();
        NotifyStateChanged();
    }

    private void QueueClientInit()
    {
        _pendingTerminalInit = View == ClientView.Terminal;
        _pendingNativeInit = View == ClientView.Native;
    }

    private async Task<ClientView> PreferNativeClientAsync()
    {
        try
        {
            return await js.InvokeAsync<bool>("xtermBridge.prefersNativeClient")
                ? ClientView.Native
                : ClientView.Terminal;
        }
        catch
        {
            return ClientView.Terminal;
        }
    }

    private void NotifyStateChanged()
    {
        var handler = StateChanged;
        handler?.Invoke(this, EventArgs.Empty);
    }
}