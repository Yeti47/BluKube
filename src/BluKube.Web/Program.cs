using BluKube.Client.Core;
using BluKube.Web.Audio;
using BluKube.Web.Clients;
using BluKube.Web.Storage;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents().AddInteractiveServerComponents();

builder.Services.AddScoped<IConfigStore, LocalStorageConfigStore>();
builder.Services.AddScoped<ClientSession>();
builder.Services.AddScoped<AudioStream>();
builder.Services.AddScoped<TerminalKeyDispatcher>();
builder.Services.AddScoped<TerminalClient>();
builder.Services.AddScoped<NativeClient>();
builder.Services.AddScoped<IClientView>(serviceProvider =>
    serviceProvider.GetRequiredService<TerminalClient>()
);
builder.Services.AddScoped<IClientView>(serviceProvider =>
    serviceProvider.GetRequiredService<NativeClient>()
);
builder.Services.AddScoped<ClientShell>();

var app = builder.Build();

app.MapStaticAssets();
app.UseAntiforgery();

app.MapRazorComponents<BluKube.Web.Components.App>().AddInteractiveServerRenderMode();

app.Run();
