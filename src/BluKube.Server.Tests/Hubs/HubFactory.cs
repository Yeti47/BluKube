using System.Collections.Concurrent;
using BluKube.Server.Configuration;
using BluKube.Server.Core.Session;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BluKube.Server.Tests.Hubs;

/// <summary>
/// <see cref="WebApplicationFactory{TEntryPoint}"/> used by all hub tests.
/// Replaces <see cref="ISessionManager"/> with a lightweight in-memory
/// implementation backed by <see cref="StatefulFakeBrowserSession"/>.
/// </summary>
public sealed class HubFactory : WebApplicationFactory<Program>
{
    public const string Token = "hub-test-token-xyz789";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.PostConfigure<BluKube.Server.Configuration.AuthOptions>(opts =>
            {
                opts.Token = Token;
                opts.TokenFile = Path.Combine(
                    Path.GetTempPath(),
                    $"blukube-hub-test-{Guid.NewGuid():N}.token"
                );
            });
            services.RemoveAll<ISessionManager>();
            services.AddSingleton<ISessionManager, HubFakeSessionManager>();
        });
    }

    /// <summary>
    /// Creates a <see cref="HubConnection"/> routed through the in-process
    /// test server. Uses long-polling so no real WebSocket upgrade is needed.
    /// </summary>
    public HubConnection CreateHubConnection(string? token = Token)
    {
        return new HubConnectionBuilder()
            .WithUrl(
                "http://localhost/hubs/session",
                opts =>
                {
                    opts.HttpMessageHandlerFactory = _ => Server.CreateHandler();
                    opts.Transports = HttpTransportType.LongPolling;
                    if (token is not null)
                        opts.AccessTokenProvider = () => Task.FromResult<string?>(token);
                }
            )
            .Build();
    }
}

internal sealed class HubFakeSessionManager : ISessionManager
{
    private readonly ConcurrentDictionary<Guid, StatefulFakeBrowserSession> _sessions = new();
    public int MaxSessions { get; set; } = 100;

    public Task<IBrowserSession> CreateSessionAsync(CancellationToken ct = default)
    {
        if (_sessions.Count >= MaxSessions)
            throw new InvalidOperationException("Session cap reached.");
        var s = new StatefulFakeBrowserSession();
        _sessions[s.Id] = s;
        return Task.FromResult<IBrowserSession>(s);
    }

    public Task<IBrowserSession?> GetSessionAsync(Guid sessionId) =>
        Task.FromResult<IBrowserSession?>(_sessions.TryGetValue(sessionId, out var s) ? s : null);

    public Task<bool> RemoveSessionAsync(Guid sessionId) =>
        Task.FromResult(_sessions.TryRemove(sessionId, out _));

    public Task<IReadOnlyList<IBrowserSession>> ListSessionsAsync() =>
        Task.FromResult<IReadOnlyList<IBrowserSession>>(
            _sessions.Values.Cast<IBrowserSession>().ToList()
        );
}
