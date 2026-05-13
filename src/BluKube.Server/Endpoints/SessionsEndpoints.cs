using BluKube.Server.Core.Session;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace BluKube.Server.Endpoints;

/// <summary>
/// Thin REST surface for one-shot, non-interactive session management.
/// Real-time interaction goes through the SignalR hub.
/// </summary>
public static class SessionsEndpoints
{
    public static IEndpointRouteBuilder MapSessionsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/sessions");

        group.MapGet("/", async (ISessionManager manager) =>
        {
            var sessions = await manager.ListSessionsAsync();
            return Results.Ok(sessions.Select(SessionSummary.From));
        });

        group.MapPost("/", async (ISessionManager manager, CancellationToken ct) =>
        {
            try
            {
                var session = await manager.CreateSessionAsync(ct);
                return Results.Created($"/v1/sessions/{session.Id}", SessionSummary.From(session));
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status409Conflict);
            }
        });

        group.MapDelete("/{id:guid}", async (Guid id, ISessionManager manager) =>
        {
            var removed = await manager.RemoveSessionAsync(id);
            return removed ? Results.NoContent() : Results.NotFound();
        });

        group.MapGet("/{id:guid}/state", async (Guid id, ISessionManager manager) =>
        {
            var session = await manager.GetSessionAsync(id);
            return session is null ? Results.NotFound() : Results.Ok(session.Current);
        });

        return app;
    }

    public sealed record SessionSummary(Guid Id, DateTimeOffset LastActivityAt)
    {
        public static SessionSummary From(IBrowserSession s) => new(s.Id, s.LastActivityAt);
    }
}
