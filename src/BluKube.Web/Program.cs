using BluKube.Client.Core;
using BluKube.Web.Storage;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddScoped<IConfigStore, LocalStorageConfigStore>();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<BluKube.Web.Components.App>()
    .AddInteractiveServerRenderMode();

app.Run();
