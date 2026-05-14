using Microsoft.AspNetCore.SignalR.Client;

namespace BluKube.Client.Core;

/// <summary>
/// Strongly-typed wrapper around the BluKube <c>SessionHub</c> connection.
/// Owns one <see cref="HubConnection"/> bound to one session at a time.
/// </summary>
/// <remarks>
/// The connection is created lazily on <see cref="ConnectAsync"/>; callers
/// must call <see cref="DisposeAsync"/> when done. Use
/// <see cref="OnState"/> to observe pushed state updates from the server.
/// </remarks>
public sealed class BluKubeConnection(ConnectionSettings settings) : IAsyncDisposable
{
    private HubConnection? _hub;
    private Guid? _sessionId;

    public Guid? SessionId => _sessionId;
    public HubConnectionState State => _hub?.State ?? HubConnectionState.Disconnected;

    /// <summary>Raised whenever the server pushes a new state.</summary>
    public event Action<SessionState>? OnState;

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (_hub is not null) return;

        var hub = new HubConnectionBuilder()
            .WithUrl(settings.HubUrl, opts =>
            {
                if (!string.IsNullOrEmpty(settings.Token))
                {
                    opts.AccessTokenProvider = () => Task.FromResult<string?>(settings.Token);
                }
            })
            .WithAutomaticReconnect()
            .Build();

        hub.On<SessionState>("State", s => OnState?.Invoke(s));

        await hub.StartAsync(ct);
        _hub = hub;
    }

    public async Task<Guid> CreateSessionAsync(CancellationToken ct = default)
    {
        var hub = Require();
        _sessionId = await hub.InvokeAsync<Guid>("CreateSession", ct);
        return _sessionId.Value;
    }

    public async Task<SessionState> AttachSessionAsync(Guid id, CancellationToken ct = default)
    {
        var hub = Require();
        var state = await hub.InvokeAsync<SessionState>("AttachSession", id, ct);
        _sessionId = id;
        return state;
    }

    public Task LeaveSessionAsync(CancellationToken ct = default)
    {
        _sessionId = null;
        return Require().InvokeAsync("LeaveSession", ct);
    }

    public Task CloseSessionAsync(Guid id, CancellationToken ct = default)
        => Require().InvokeAsync("CloseSession", id, ct);

    // --- Commands ------------------------------------------------------------

    public Task<SessionState> SearchAsync(string query, int limit, CancellationToken ct = default)
        => Require().InvokeAsync<SessionState>("Search", query, limit, ct);

    public Task<SessionState> PlayAsync(string videoId, CancellationToken ct = default)
        => Require().InvokeAsync<SessionState>("Play", videoId, ct);

    public Task<SessionState> PauseAsync(CancellationToken ct = default)
        => Require().InvokeAsync<SessionState>("Pause", ct);

    public Task<SessionState> ResumeAsync(CancellationToken ct = default)
        => Require().InvokeAsync<SessionState>("Resume", ct);

    public Task<SessionState> SeekToAsync(TimeSpan position, CancellationToken ct = default)
        => Require().InvokeAsync<SessionState>("SeekTo", position, ct);

    public Task<SessionState> SetVolumeAsync(float volume, CancellationToken ct = default)
        => Require().InvokeAsync<SessionState>("SetVolume", volume, ct);

    public Task<SessionState> GetStateAsync(CancellationToken ct = default)
        => Require().InvokeAsync<SessionState>("GetState", ct);

    /// <summary>
    /// Server-streamed state updates for the attached session. Yields the
    /// current state immediately, then every subsequent update.
    /// </summary>
    public IAsyncEnumerable<SessionState> StreamStatesAsync(CancellationToken ct = default)
        => Require().StreamAsync<SessionState>("StreamStates", ct);

    /// <summary>
    /// Server-streamed Opus audio packets for the attached session. Each
    /// element is one Opus packet; format is described by
    /// <see cref="AudioFormat"/>.
    /// </summary>
    public IAsyncEnumerable<byte[]> StreamAudioAsync(CancellationToken ct = default)
        => Require().StreamAsync<byte[]>("StreamAudio", ct);

    private HubConnection Require()
        => _hub ?? throw new InvalidOperationException("Not connected. Call ConnectAsync first.");

    public async ValueTask DisposeAsync()
    {
        if (_hub is null) return;
        try { await _hub.DisposeAsync(); }
        finally { _hub = null; }
    }
}
