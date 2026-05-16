using Microsoft.JSInterop;
using BluKube.Web.Clients.ErrorHandling;

namespace BluKube.Web.Clients;

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

public sealed class ClientShell
{
    private readonly IJSRuntime _js;
    private readonly ClientSession _session;
    private readonly IReadOnlyDictionary<ClientView, IClientView> _clients;
    private bool _pendingClientInit;
    private bool _showAuthenticatingOverlay;

    public event EventHandler? StateChanged;

    public ClientState State { get; private set; } = ClientState.Loading;
    public ClientView View { get; private set; } = ClientView.Terminal;
    public string? LoginError { get; private set; }
    public bool ShowAuthenticatingOverlay => _showAuthenticatingOverlay;
    public bool IsNativeView => View == ClientView.Native;
    public string PageClass => IsNativeView ? "native-page" : "terminal-page";

    private IClientView CurrentClient => _clients[View];

    public ClientShell(IJSRuntime js, ClientSession session, IEnumerable<IClientView> clients)
    {
        _js = js;
        _session = session;
        _clients = clients.ToDictionary(client => client.View);
        _session.StartupFailed += OnStartupFailed;
    }

    public async Task InitializeAsync()
    {
        if (!await _session.HasSavedSettingsAsync())
        {
            LoginError = null;
            State = ClientState.Login;
            _showAuthenticatingOverlay = false;
        }
        else
        {
            View = await PreferNativeClientAsync();
            LoginError = null;
            State = ClientState.Client;
            QueueClientInit(showAuthenticatingOverlay: true);
        }

        NotifyStateChanged();
    }

    public async Task StartClientAsync()
    {
        View = await PreferNativeClientAsync();
        LoginError = null;
        State = ClientState.Client;
        QueueClientInit(showAuthenticatingOverlay: true);
        NotifyStateChanged();
    }

    public async Task ClearConfigAsync()
    {
        await _session.ClearSettingsAsync();
        await StopAsync();

        foreach (var client in _clients.Values)
            client.ClearState();

        _pendingClientInit = false;
        _showAuthenticatingOverlay = false;
        LoginError = null;
        State = ClientState.Login;
        NotifyStateChanged();
    }

    public void ClearLoginError()
    {
        if (LoginError is null)
            return;

        LoginError = null;
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

        if (_showAuthenticatingOverlay)
        {
            _showAuthenticatingOverlay = false;
            NotifyStateChanged();
        }
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
        QueueClientInit(showAuthenticatingOverlay: false);
        NotifyStateChanged();
    }

    private void QueueClientInit(bool showAuthenticatingOverlay)
    {
        _pendingClientInit = true;
        _showAuthenticatingOverlay = showAuthenticatingOverlay;
    }

    private async Task<ClientView> PreferNativeClientAsync()
    {
        try
        {
            return await _js.InvokeAsync<bool>("xtermBridge.prefersNativeClient")
                ? ClientView.Native
                : ClientView.Terminal;
        }
        catch
        {
            return ClientView.Terminal;
        }
    }

    private void OnStartupFailed(object? sender, ClientStartupFailedEventArgs args)
    {
        _ = ReturnToLoginAsync(args.Error.Message);
    }

    private async Task ReturnToLoginAsync(string message)
    {
        await _session.ClearSettingsAsync();

        foreach (var client in _clients.Values)
            client.ClearState();

        _pendingClientInit = false;
        _showAuthenticatingOverlay = false;
        LoginError = message;
        State = ClientState.Login;
        NotifyStateChanged();
    }

    private void NotifyStateChanged()
    {
        var handler = StateChanged;
        handler?.Invoke(this, EventArgs.Empty);
    }
}
