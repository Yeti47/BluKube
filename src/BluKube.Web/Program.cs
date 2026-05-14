using BluKube.Client.Core;
using BluKube.Web.Storage;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddScoped<IConfigStore, LocalStorageConfigStore>();

var app = builder.Build();

app.MapStaticAssets();
app.UseAntiforgery();

app.MapRazorComponents<BluKube.Web.Components.App>()
    .AddInteractiveServerRenderMode();

app.Run();
