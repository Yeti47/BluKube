using System.Text.Json;
using BluKube.Client.Core;
using Microsoft.JSInterop;

namespace BluKube.Web.Storage;

/// <summary>
/// Stores BluKube connection settings in the browser's <c>localStorage</c>
/// under the key <c>blukube.config</c>. Works only while a valid JS interop
/// channel is open (i.e. after interactive Blazor has hydrated).
/// </summary>
public sealed class LocalStorageConfigStore(IJSRuntime js) : IConfigStore
{
    private const string Key = "blukube.config";

    public async Task<ConnectionSettings?> LoadAsync(CancellationToken ct = default)
    {
        var json = await js.InvokeAsync<string?>("bluKubeStorage.load", ct, Key);
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            var dto = JsonSerializer.Deserialize<Dto>(json);
            if (dto is null || string.IsNullOrWhiteSpace(dto.ServerUrl)) return null;
            return new ConnectionSettings(new Uri(dto.ServerUrl), dto.Token);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async Task SaveAsync(ConnectionSettings settings, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(
            new Dto(settings.ServerUrl.ToString(), settings.Token));
        await js.InvokeVoidAsync("bluKubeStorage.save", ct, Key, json);
    }

    public async Task ClearAsync(CancellationToken ct = default) =>
        await js.InvokeVoidAsync("bluKubeStorage.remove", ct, Key);

    private sealed record Dto(string ServerUrl, string? Token);
}
