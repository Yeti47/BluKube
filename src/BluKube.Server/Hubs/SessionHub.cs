using Microsoft.AspNetCore.SignalR;
using BluKube.Server.Core.Session;

namespace BluKube.Server.Hubs;

public class SessionHub : Hub
{
    private readonly ISessionManager _sessionManager;
    private const string SessionIdKey = "SessionId";

    public SessionHub(ISessionManager sessionManager)
    {
        _sessionManager = sessionManager;
    }

    public async Task<SessionSnapshot> Connect()
    {
        var session = await _sessionManager.CreateSessionAsync(Context.ConnectionAborted);
        Context.Items[SessionIdKey] = session.Id;
        
        return new Idle();
    }

    public async Task<SessionSnapshot> Dispatch(ClientEvent clientEvent)
    {
        var session = await GetCurrentSessionAsync();
        if (session == null)
        {
            throw new HubException("No session found. Call Connect() first.");
        }

        return await session.DispatchAsync(clientEvent, Context.ConnectionAborted);
    }

    public IAsyncEnumerable<SessionSnapshot> StreamSnapshots()
    {
        var session = GetCurrentSessionAsync().Result;
        if (session == null)
        {
            throw new HubException("No session found. Call Connect() first.");
        }

        return session.Snapshots(Context.ConnectionAborted);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (Context.Items.TryGetValue(SessionIdKey, out var sessionIdObj) 
            && sessionIdObj is Guid sessionId)
        {
            await _sessionManager.RemoveSessionAsync(sessionId);
        }

        await base.OnDisconnectedAsync(exception);
    }

    private async Task<IBrowserSession?> GetCurrentSessionAsync()
    {
        if (Context.Items.TryGetValue(SessionIdKey, out var sessionIdObj) 
            && sessionIdObj is Guid sessionId)
        {
            return await _sessionManager.GetSessionAsync(sessionId);
        }

        return null;
    }
}