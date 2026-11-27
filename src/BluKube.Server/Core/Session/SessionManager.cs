using System.Collections.Concurrent;
using BluKube.Server.Configuration;
using BluKube.Server.Core.Domain;
using BluKube.Server.Core.Engine.Audio;
using BluKube.Server.Core.Engine.Browser;
using BluKube.Server.Core.Engine.Display;
using Microsoft.Extensions.Options;

namespace BluKube.Server.Core.Session;

public sealed class SessionManager : ISessionManager, IAsyncDisposable
{
    private readonly IDisplayFactory _displayFactory;
    private readonly IYouTubeBrowserLauncher _browserLauncher;
    private readonly IAudioOutputDeviceFactory _audioFactory;
    private readonly ILogger<SessionManager> _logger;
    private readonly TimeProvider _clock;
    private readonly SessionLimits _options;
    private readonly ConcurrentDictionary<Guid, BrowserSession> _sessions = new();
    private readonly CancellationTokenSource _disposeCts = new();
    private readonly Task _sweeper;

    public SessionManager(
        IDisplayFactory displayFactory,
        IYouTubeBrowserLauncher browserLauncher,
        IAudioOutputDeviceFactory audioFactory,
        IOptions<SessionLimits> options,
        ILogger<SessionManager> logger,
        TimeProvider? clock = null
    )
    {
        _displayFactory = displayFactory;
        _browserLauncher = browserLauncher;
        _audioFactory = audioFactory;
        _options = options.Value;
        _logger = logger;
        _clock = clock ?? TimeProvider.System;
        _sweeper = Task.Run(SweepLoopAsync);
    }

    public async Task<IBrowserSession> CreateSessionAsync(
        CancellationToken cancellationToken = default
    )
    {
        if (_sessions.Count >= _options.MaxSessions)
        {
            throw new InvalidOperationException(
                $"Session cap reached ({_options.MaxSessions}). Close an existing session and retry."
            );
        }

        IDisplay? display = null;
        IAudioOutputDevice? audio = null;
        IYouTubeBrowser? browser = null;

        try
        {
            display = await _displayFactory.CreateAsync(cancellationToken);
            audio = await _audioFactory.CreateAsync(cancellationToken);
            browser = await _browserLauncher.LaunchAsync(
                display,
                audio.SinkName,
                cancellationToken
            );

            var player = new BraveMediaPlayer(display, browser);
            display = null;
            browser = null;

            var session = new BrowserSession(player, player, audio);
            audio = null;

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
                    $"Session cap reached ({_options.MaxSessions}). Close an existing session and retry."
                );
            }

            return session;
        }
        catch
        {
            if (browser is not null)
            {
                try
                {
                    await browser.DisposeAsync();
                }
                catch { }
            }
            if (audio is not null)
            {
                try
                {
                    await audio.DisposeAsync();
                }
                catch { }
            }
            if (display is not null)
            {
                try
                {
                    await display.DisposeAsync();
                }
                catch { }
            }
            throw;
        }
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
            await DisposeBoundedAsync(session);
            return true;
        }
        return false;
    }

    // A wedged browser must never block the caller (the idle sweeper or a hub
    // call) indefinitely: one hung dispose used to stop the sweeper for good,
    // so idle sessions piled up until the cap rejected every new client.
    private async Task DisposeBoundedAsync(IBrowserSession session)
    {
        var dispose = session.DisposeAsync().AsTask();
        try
        {
            await dispose.WaitAsync(_options.DisposeTimeout, _clock);
        }
        catch (TimeoutException)
        {
            _logger.LogWarning(
                "Disposing session {SessionId} did not finish within {Timeout}; no longer waiting for it",
                session.Id,
                _options.DisposeTimeout
            );
            _ = dispose.ContinueWith(
                t =>
                    _logger.LogWarning(
                        t.Exception,
                        "Late dispose of session {SessionId} failed",
                        session.Id
                    ),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to dispose session {SessionId}", session.Id);
        }
    }

    public Task<IReadOnlyList<IBrowserSession>> ListSessionsAsync()
    {
        return Task.FromResult<IReadOnlyList<IBrowserSession>>(
            _sessions.Values.Cast<IBrowserSession>().ToList()
        );
    }

    private async Task SweepLoopAsync()
    {
        var ct = _disposeCts.Token;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(_options.SweepInterval, _clock, ct);
                try
                {
                    await SweepOnceAsync();
                }
                catch (Exception ex)
                {
                    // Keep sweeping: a dead sweeper silently fills the session cap.
                    _logger.LogError(ex, "Idle session sweep failed");
                }
            }
        }
        catch (OperationCanceledException)
        { /* dispose */
        }
    }

    private async Task SweepOnceAsync()
    {
        var cutoff = _clock.GetUtcNow() - _options.IdleTimeout;
        var reaped = new List<IBrowserSession>();
        foreach (var (id, session) in _sessions)
        {
            if (session.LastActivityAt > cutoff)
                continue;

            if (_sessions.TryRemove(id, out var removed))
            {
                _logger.LogInformation(
                    "Reaping idle session {SessionId} (last activity {LastActivity:o})",
                    id,
                    removed.LastActivityAt
                );
                reaped.Add(removed);
            }
        }

        await Task.WhenAll(reaped.Select(DisposeBoundedAsync));
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            _disposeCts.Cancel();
        }
        catch { }
        try
        {
            await _sweeper;
        }
        catch { }

        var sessions = _sessions.Values.ToList();
        _sessions.Clear();

        await Task.WhenAll(sessions.Select(DisposeBoundedAsync));

        _disposeCts.Dispose();
    }
}
