using Microsoft.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();
builder.Services.AddOpenApi();

var app = builder.Build();

app.MapOpenApi();
app.MapHealthChecks("/health", new HealthCheckOptions { AllowCachingResponses = false });
app.MapHealthChecks("/alive", new HealthCheckOptions
{
    Predicate = _ => false,
    AllowCachingResponses = false
});

app.MapGet("/", () => "BluKube Server — OK");

// TODO: Phase 2 — register ISessionManager, auth middleware, /v1/* endpoints

app.Run();
