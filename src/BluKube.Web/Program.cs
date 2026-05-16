using BluKube.Client.Core;
using BluKube.Web.Services;
using BluKube.Web.Storage;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddScoped<IConfigStore, LocalStorageConfigStore>();
builder.Services.AddScoped<ClientSessionService>();
builder.Services.AddScoped<AudioStreamService>();
builder.Services.AddScoped<TerminalClientService>();
builder.Services.AddScoped<NativeClientService>();

var app = builder.Build();

app.MapStaticAssets();
app.UseAntiforgery();

app.MapRazorComponents<BluKube.Web.Components.App>()
    .AddInteractiveServerRenderMode();

app.Run();
