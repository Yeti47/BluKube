using Microsoft.JSInterop;

namespace BluKube.Web.Services;

public enum ClientState
{
    Loading,
    Login,
    Client,
}

public enum ClientView
{
    Terminal,
    Native,
}

public sealed class ClientShellService(
    IJSRuntime js,
    ClientSessionService session,
    IEnumerable<IClientViewService> clients
)
{
    private readonly IReadOnlyDictionary<ClientView, IClientViewService> _clients =
        clients.ToDictionary(client => client.View);
    private bool _pendingClientInit;

    public event EventHandler? StateChanged;

    public ClientState State { get; private set; } = ClientState.Loading;
    public ClientView View { get; private set; } = ClientView.Terminal;
    public bool IsNativeView => View == ClientView.Native;
    public string PageClass => IsNativeView ? "native-page" : "terminal-page";

    private IClientViewService CurrentClient => _clients[View];

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

        foreach (var client in _clients.Values)
            client.ClearState();

        _pendingClientInit = false;
        State = ClientState.Login;
        NotifyStateChanged();
    }

    public Task ShowNativeAsync() => SwitchClientViewAsync(ClientView.Native);

    public Task ShowTerminalAsync() => SwitchClientViewAsync(ClientView.Terminal);

    public async Task StartPendingClientAsync()
    {
        if (!_pendingClientInit)
            return;

        _pendingClientInit = false;
        await CurrentClient.ActivateAsync();
    }

    public async Task StopAsync(bool resetSession = true)
    {
        await CurrentClient.DeactivateAsync(resetSession);
    }

    private async Task SwitchClientViewAsync(ClientView view)
    {
        if (View == view)
            return;

        await StopAsync();

        View = view;
        QueueClientInit();
        NotifyStateChanged();
    }

    private void QueueClientInit()
    {
        _pendingClientInit = true;
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
