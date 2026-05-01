var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// TODO: Phase 2 — register services, auth, controllers/minimal APIs

var app = builder.Build();

app.MapDefaultEndpoints();

// TODO: Phase 2 — map /v1/* endpoints

app.Run();