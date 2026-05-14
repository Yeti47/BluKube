using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using BluKube.Server.Auth;
using BluKube.Server.Configuration;
using BluKube.Server.Core.Engine.Browser;
using BluKube.Server.Core.Engine.Display;
using BluKube.Server.Core.Session;
using BluKube.Server.Endpoints;
using BluKube.Server.Hubs;

var builder = WebApplication.CreateBuilder(args);

// --- Bind --------------------------------------------------------------------
// BLUKUBE_BIND lets ops pin the listen address (e.g. "0.0.0.0:8765" for LAN).
// Default is loopback so a fresh container is not exposed by accident.
var bind = Environment.GetEnvironmentVariable("BLUKUBE_BIND");
if (!string.IsNullOrWhiteSpace(bind))
{
    builder.WebHost.UseUrls($"http://{bind}");
}
else if (!builder.Environment.IsDevelopment())
{
    builder.WebHost.UseUrls("http://127.0.0.1:8765");
}

// --- Options -----------------------------------------------------------------
builder.Services
    .AddOptions<SessionLimits>()
    .Bind(builder.Configuration.GetSection(SessionLimits.SectionName))
    .ValidateDataAnnotations();

builder.Services
    .AddOptions<AuthOptions>()
    .Bind(builder.Configuration.GetSection(AuthOptions.SectionName))
    .Configure(opts =>
    {
        var envToken = Environment.GetEnvironmentVariable("BLUKUBE_TOKEN");
        if (!string.IsNullOrWhiteSpace(envToken)) opts.Token = envToken;

        var envFile = Environment.GetEnvironmentVariable("BLUKUBE_TOKEN_FILE");
        if (!string.IsNullOrWhiteSpace(envFile)) opts.TokenFile = envFile;
    });

builder.Services
    .AddOptions<CorsOptions>()
    .Bind(builder.Configuration.GetSection(CorsOptions.SectionName));

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<AuthTokenProvider>();

// --- CORS (opt-in) -----------------------------------------------------------
var corsOrigins = builder.Configuration
    .GetSection(CorsOptions.SectionName)
    .Get<CorsOptions>()?.Origins ?? [];

if (corsOrigins.Length > 0)
{
    builder.Services.AddCors(o => o.AddPolicy(CorsOptions.PolicyName, p => p
        .WithOrigins(corsOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials()));
}

// --- Framework ---------------------------------------------------------------
builder.Services.AddHealthChecks();
builder.Services.AddOpenApi();
builder.Services.AddSignalR();

// --- Engine factories — singletons; each session asks for a fresh instance. -
builder.Services.AddSingleton<IDisplayFactory, XvfbDisplayFactory>();
builder.Services.AddSingleton<IBraveProfileProvisioner, BraveProfileProvisioner>();
builder.Services.AddSingleton<IYouTubeBrowserLauncher, BraveYouTubeBrowserLauncher>();
builder.Services.AddSingleton<BluKube.Server.Core.Engine.Audio.IAudioOutputDeviceFactory,
    BluKube.Server.Core.Engine.Audio.PulseAudioOutputDeviceFactory>();

// --- Session registry — singleton; owns the lifetime of all sessions. -------
builder.Services.AddSingleton<ISessionManager, SessionManager>();

var app = builder.Build();

// Force token resolution on startup so failures surface immediately.
_ = app.Services.GetRequiredService<AuthTokenProvider>().Token;

if (corsOrigins.Length > 0)
{
    app.UseCors(CorsOptions.PolicyName);
}

app.UseMiddleware<BearerTokenMiddleware>();

app.MapOpenApi();
app.MapHealthChecks("/health", new HealthCheckOptions { AllowCachingResponses = false });
app.MapHealthChecks("/alive", new HealthCheckOptions
{
    Predicate = _ => false,
    AllowCachingResponses = false
});

app.MapGet("/", () => "BluKube Server — OK");
app.MapHub<SessionHub>("/hubs/session");
app.MapSessionsEndpoints();

app.Run();

public partial class Program;
