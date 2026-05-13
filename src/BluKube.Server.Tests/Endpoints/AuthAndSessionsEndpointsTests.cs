using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BluKube.Server.Core.Session;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BluKube.Server.Tests.Endpoints;

public sealed class AuthAndSessionsEndpointsTests : IClassFixture<AuthAndSessionsEndpointsTests.Factory>
{
    private const string Token = "test-token-abc123";
    private readonly Factory _factory;

    public AuthAndSessionsEndpointsTests(Factory factory) => _factory = factory;

    [Fact]
    public async Task Health_IsPublic()
    {
        using var client = _factory.CreateClient();
        var resp = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task Sessions_RequiresToken()
    {
        using var client = _factory.CreateClient();
        var resp = await client.GetAsync("/v1/sessions");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        Assert.Equal("Bearer", resp.Headers.WwwAuthenticate.ToString());
    }

    [Fact]
    public async Task Sessions_RejectsWrongToken()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "wrong");
        var resp = await client.GetAsync("/v1/sessions");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Sessions_AcceptsCorrectToken()
    {
        using var client = AuthedClient();
        var resp = await client.GetAsync("/v1/sessions");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task Sessions_CreateGetDelete_RoundTrip()
    {
        using var client = AuthedClient();

        var create = await client.PostAsync("/v1/sessions", content: null);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var summary = await create.Content.ReadFromJsonAsync<SessionSummaryDto>();
        Assert.NotNull(summary);
        Assert.NotEqual(Guid.Empty, summary!.Id);

        var list = await client.GetFromJsonAsync<SessionSummaryDto[]>("/v1/sessions");
        Assert.NotNull(list);
        Assert.Contains(list!, s => s.Id == summary.Id);

        var stateResp = await client.GetAsync($"/v1/sessions/{summary.Id}/state");
        Assert.Equal(HttpStatusCode.OK, stateResp.StatusCode);
        var stateJson = await stateResp.Content.ReadAsStringAsync();
        Assert.Contains("IdleState", stateJson);

        var del = await client.DeleteAsync($"/v1/sessions/{summary.Id}");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

        var missing = await client.GetAsync($"/v1/sessions/{summary.Id}/state");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task DeleteUnknown_ReturnsNotFound()
    {
        using var client = AuthedClient();
        var resp = await client.DeleteAsync($"/v1/sessions/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    private HttpClient AuthedClient()
    {
        var c = _factory.CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        return c;
    }

    private sealed record SessionSummaryDto(Guid Id, DateTimeOffset LastActivityAt);

    public sealed class Factory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            Environment.SetEnvironmentVariable("BLUKUBE_TOKEN", Token);
            Environment.SetEnvironmentVariable(
                "BLUKUBE_TOKEN_FILE",
                Path.Combine(Path.GetTempPath(), $"blukube-test-{Guid.NewGuid():N}.token"));

            builder.ConfigureServices(services =>
            {
                // Replace real engine-backed manager with an in-memory fake.
                services.RemoveAll<ISessionManager>();
                services.AddSingleton<ISessionManager, FakeSessionManager>();
            });
        }
    }
}
