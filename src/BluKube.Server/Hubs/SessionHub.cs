using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.SignalR;
using BluKube.Server.Core.Session;

namespace BluKube.Server.Hubs;

public class SessionHub : Hub<ISessionClient>
{
    private readonly ISessionManager _sessionManager;
    private const string SessionIdKey = "SessionId";

    public SessionHub(ISessionManager sessionManager)
    {
        _sessionManager = sessionManager;
    }

    // --- Lifecycle -----------------------------------------------------------

    public async Task<Guid> CreateSession()
    {
        var session = await _sessionManager.CreateSessionAsync(Context.ConnectionAborted);
        Context.Items[SessionIdKey] = session.Id;
        return session.Id;
    }

    public async Task<SessionState> AttachSession(Guid sessionId)
    {
        var session = await _sessionManager.GetSessionAsync(sessionId)
            ?? throw new HubException($"Session {sessionId} not found");
        Context.Items[SessionIdKey] = session.Id;
        return session.Current;
    }

    public Task LeaveSession()
    {
        Context.Items.Remove(SessionIdKey);
        return Task.CompletedTask;
    }

    public async Task CloseSession(Guid sessionId)
    {
        await _sessionManager.RemoveSessionAsync(sessionId);
        if (Context.Items.TryGetValue(SessionIdKey, out var current)
            && current is Guid id && id == sessionId)
        {
            Context.Items.Remove(SessionIdKey);
        }
    }

    // --- Commands ------------------------------------------------------------

    public async Task<SessionState> Search(string query, int limit)
        => await (await RequireSessionAsync()).SearchAsync(query, limit, Context.ConnectionAborted);

    public async Task<SessionState> Play(string videoId)
        => await (await RequireSessionAsync()).PlayAsync(videoId, Context.ConnectionAborted);

    public async Task<SessionState> Pause()
        => await (await RequireSessionAsync()).PauseAsync(Context.ConnectionAborted);

    public async Task<SessionState> Resume()
        => await (await RequireSessionAsync()).ResumeAsync(Context.ConnectionAborted);

    public async Task<SessionState> SeekTo(TimeSpan position)
        => await (await RequireSessionAsync()).SeekToAsync(position, Context.ConnectionAborted);

    public async Task<SessionState> SetVolume(float volume)
        => await (await RequireSessionAsync()).SetVolumeAsync(volume, Context.ConnectionAborted);

    public async Task<SessionState> GetState()
        => (await RequireSessionAsync()).Current;

    // --- Streaming -----------------------------------------------------------

    public async IAsyncEnumerable<SessionState> StreamStates(
        [EnumeratorCancellation] CancellationToken ct)
    {
        var session = await RequireSessionAsync();
        await foreach (var state in session.States(ct))
        {
            yield return state;
        }
    }

    // --- Connection lifecycle ------------------------------------------------

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        // Sessions outlive connections; do not destroy on disconnect.
        // Idle timeout (enforced by SessionManager) handles eventual cleanup.
        return base.OnDisconnectedAsync(exception);
    }

    private async Task<IBrowserSession> RequireSessionAsync()
    {
        if (Context.Items.TryGetValue(SessionIdKey, out var raw) && raw is Guid id)
        {
            var session = await _sessionManager.GetSessionAsync(id);
            if (session is not null) return session;
        }
        throw new HubException("No session attached to this connection. Call CreateSession or AttachSession first.");
    }
}