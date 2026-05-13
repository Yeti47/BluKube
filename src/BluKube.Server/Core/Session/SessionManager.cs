using System.Collections.Concurrent;
using BluKube.Server.Configuration;
using BluKube.Server.Core.Domain;
using BluKube.Server.Core.Engine.Browser;
using BluKube.Server.Core.Engine.Display;
using Microsoft.Extensions.Options;

namespace BluKube.Server.Core.Session;

public sealed class SessionManager : ISessionManager, IAsyncDisposable
{
    private readonly IDisplayFactory _displayFactory;
    private readonly IYouTubeBrowserLauncher _browserLauncher;
    private readonly ILogger<SessionManager> _logger;
    private readonly TimeProvider _clock;
    private readonly SessionLimits _options;
    private readonly ConcurrentDictionary<Guid, BrowserSession> _sessions = new();
    private readonly CancellationTokenSource _disposeCts = new();
    private readonly Task _sweeper;

    public SessionManager(
        IDisplayFactory displayFactory,
        IYouTubeBrowserLauncher browserLauncher,
        IOptions<SessionLimits> options,
        ILogger<SessionManager> logger,
        TimeProvider? clock = null)
    {
        _displayFactory = displayFactory;
        _browserLauncher = browserLauncher;
        _options = options.Value;
        _logger = logger;
        _clock = clock ?? TimeProvider.System;
        _sweeper = Task.Run(SweepLoopAsync);
    }

    public async Task<IBrowserSession> CreateSessionAsync(CancellationToken cancellationToken = default)
    {
        if (_sessions.Count >= _options.MaxSessions)
        {
            throw new InvalidOperationException(
                $"Session cap reached ({_options.MaxSessions}). Close an existing session and retry.");
        }

        var display = await _displayFactory.CreateAsync(cancellationToken);
        var browser = await _browserLauncher.LaunchAsync(display, cancellationToken);

        var player = new BraveMediaPlayer(display, browser);
        var session = new BrowserSession(player, player);

        if (!_sessions.TryAdd(session.Id, session))
        {
            await session.DisposeAsync();
            throw new InvalidOperationException("Failed to add session to manager");
        }

        // Re-check cap under contention; if we slipped over, drop this one.
        if (_sessions.Count > _options.MaxSessions)
        {
            _sessions.TryRemove(session.Id, out _);
            await session.DisposeAsync();
            throw new InvalidOperationException(
                $"Session cap reached ({_options.MaxSessions}). Close an existing session and retry.");
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

    private async Task SweepLoopAsync()
    {
        var ct = _disposeCts.Token;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(_options.SweepInterval, _clock, ct);
                await SweepOnceAsync();
            }
        }
        catch (OperationCanceledException) { /* dispose */ }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Idle session sweeper crashed");
        }
    }

    private async Task SweepOnceAsync()
    {
        var cutoff = _clock.GetUtcNow() - _options.IdleTimeout;
        foreach (var (id, session) in _sessions)
        {
            if (session.LastActivityAt > cutoff) continue;

            if (_sessions.TryRemove(id, out var removed))
            {
                _logger.LogInformation(
                    "Reaping idle session {SessionId} (last activity {LastActivity:o})",
                    id, removed.LastActivityAt);
                try { await removed.DisposeAsync(); }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to dispose idle session {SessionId}", id);
                }
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        try { _disposeCts.Cancel(); } catch { }
        try { await _sweeper; } catch { }

        var sessions = _sessions.Values.ToList();
        _sessions.Clear();

        foreach (var session in sessions)
        {
            try { await session.DisposeAsync(); }
            catch { }
        }

        _disposeCts.Dispose();
    }
}
