using BluKube.Client.Core;

namespace BluKube.Web.Services;

public sealed class ClientSessionService(IConfigStore configStore) : IAsyncDisposable
{
    private CancellationTokenSource _cts = new();

    public CancellationToken Token => _cts.Token;
    public BluKubeConnection? Connection { get; private set; }

    public async Task<bool> HasSavedSettingsAsync() =>
        await configStore.LoadAsync(Token) is not null;

    public Task ClearSettingsAsync() => configStore.ClearAsync(CancellationToken.None);

    public async Task<BluKubeConnection> ConnectAsync()
    {
        if (Connection is not null)
            return Connection;

        var settings =
            await configStore.LoadAsync(Token)
            ?? throw new InvalidOperationException("No connection settings found.");

        var connection = new BluKubeConnection(settings);
        await connection.ConnectAsync(Token);
        _ = await connection.CreateSessionAsync(Token);
        Connection = connection;
        return connection;
    }

    public async Task StopAsync(params Task?[] tasks)
    {
        try
        {
            await _cts.CancelAsync();
        }
        catch { }

        foreach (var task in tasks)
        {
            if (task is null)
                continue;
            try
            {
                await task;
            }
            catch { }
        }

        var connection = Connection;
        if (connection?.SessionId is Guid sessionId)
        {
            try
            {
                await connection.CloseSessionAsync(sessionId, CancellationToken.None);
            }
            catch { }
        }

        if (connection is not null)
        {
            try
            {
                await connection.DisposeAsync();
            }
            catch { }
        }

        Connection = null;
    }

    public void ResetCancellation()
    {
        _cts.Dispose();
        _cts = new CancellationTokenSource();
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _cts.Dispose();
    }
}
