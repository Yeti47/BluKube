using System.Collections.Concurrent;
using BluKube.Server.Core.Engine.Browser;
using BluKube.Server.Core.Engine.Display;

namespace BluKube.Server.Core.Session;

public sealed class SessionManager : ISessionManager, IAsyncDisposable
{
    private readonly IDisplayFactory _displayFactory;
    private readonly IYouTubeBrowserLauncher _browserLauncher;
    private readonly ConcurrentDictionary<Guid, BrowserSession> _sessions = new();

    public SessionManager(IDisplayFactory displayFactory, IYouTubeBrowserLauncher browserLauncher)
    {
        _displayFactory = displayFactory;
        _browserLauncher = browserLauncher;
    }

    public async Task<IBrowserSession> CreateSessionAsync(CancellationToken cancellationToken = default)
    {
        var display = await _displayFactory.CreateAsync(cancellationToken);
        var browser = await _browserLauncher.LaunchAsync(display, cancellationToken);
        
        var session = new BrowserSession(display, browser);
        
        if (!_sessions.TryAdd(session.Id, session))
        {
            await session.DisposeAsync();
            throw new InvalidOperationException("Failed to add session to manager");
        }

        return session;
    }

    public Task<IBrowserSession?> GetSessionAsync(Guid sessionId)
    {
        _sessions.TryGetValue(sessionId, out var session);
        return Task.FromResult<IBrowserSession?>(session);
    }

    public async Task<bool> RemoveSessionAsync(Guid sessionId)
    {
        if (_sessions.TryRemove(sessionId, out var session))
        {
            await session.DisposeAsync();
            return true;
        }
        return false;
    }

    public Task<IReadOnlyList<IBrowserSession>> ListSessionsAsync()
    {
        return Task.FromResult<IReadOnlyList<IBrowserSession>>(
            _sessions.Values.Cast<IBrowserSession>().ToList());
    }

    public async ValueTask DisposeAsync()
    {
        var sessions = _sessions.Values.ToList();
        _sessions.Clear();

        foreach (var session in sessions)
        {
            try
            {
                await session.DisposeAsync();
            }
            catch { }
        }
    }
}