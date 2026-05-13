using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using BluKube.Server.Configuration;
using BluKube.Server.Core.Engine.Browser;
using BluKube.Server.Core.Engine.Display;
using BluKube.Server.Core.Session;
using BluKube.Server.Hubs;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();
builder.Services.AddOpenApi();
builder.Services.AddSignalR();

builder.Services
    .AddOptions<SessionLimits>()
    .Bind(builder.Configuration.GetSection(SessionLimits.SectionName))
    .ValidateDataAnnotations();
builder.Services.AddSingleton(TimeProvider.System);

// Engine factories — singletons; each session asks for a fresh instance.
builder.Services.AddSingleton<IDisplayFactory, XvfbDisplayFactory>();
builder.Services.AddSingleton<IYouTubeBrowserLauncher, BraveYouTubeBrowserLauncher>();

// Session registry — singleton; owns the lifetime of all sessions.
builder.Services.AddSingleton<ISessionManager, SessionManager>();

var app = builder.Build();

app.MapOpenApi();
app.MapHealthChecks("/health", new HealthCheckOptions { AllowCachingResponses = false });
app.MapHealthChecks("/alive", new HealthCheckOptions
{
    Predicate = _ => false,
    AllowCachingResponses = false
});

app.MapGet("/", () => "BluKube Server — OK");
app.MapHub<SessionHub>("/hubs/session");

// TODO: auth middleware, /v1/* REST endpoints

app.Run();

public partial class Program;

