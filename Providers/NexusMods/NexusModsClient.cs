using System.Net.Http.Json;
using System.Text.Json;

namespace Bibliognost.Providers.NexusMods;

public sealed class NexusModsClient : IDisposable
{
    public const string GameDomain = "finalfantasy14";
    private readonly HttpClient client = new() { BaseAddress = new Uri("https://api.nexusmods.com/v1/"), Timeout = TimeSpan.FromSeconds(30) };
    public bool HasApiKey { get; private set; }

    public NexusModsClient() => client.DefaultRequestHeaders.UserAgent.ParseAdd("Bibliognost/0.3");

    public void SetApiKey(string? key)
    {
        client.DefaultRequestHeaders.Remove("apikey");
        HasApiKey = !string.IsNullOrWhiteSpace(key);
        if (HasApiKey) client.DefaultRequestHeaders.TryAddWithoutValidation("apikey", key!.Trim());
    }

    public async Task<JsonDocument> GetAsync(string path, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(path, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
    }

    public async Task<(string Url, string FileName)?> ResolvePrimaryDownloadAsync(string modId, CancellationToken cancellationToken)
    {
        using var files = await GetAsync($"games/{GameDomain}/mods/{Uri.EscapeDataString(modId)}/files.json", cancellationToken);
        if (!files.RootElement.TryGetProperty("files", out var nodes)) return null;
        var candidates = nodes.EnumerateArray().ToArray();
        var file = candidates.FirstOrDefault(x => x.TryGetProperty("is_primary", out var primary) && primary.GetBoolean());
        if (file.ValueKind == JsonValueKind.Undefined)
            file = candidates.FirstOrDefault(x => x.TryGetProperty("category_name", out var category) && category.GetString() == "MAIN");
        if (file.ValueKind == JsonValueKind.Undefined || !file.TryGetProperty("file_id", out var fileId)) return null;
        var name = file.TryGetProperty("file_name", out var fileName) ? fileName.GetString() ?? $"nexus-{modId}.zip" : $"nexus-{modId}.zip";
        using var links = await GetAsync($"games/{GameDomain}/mods/{Uri.EscapeDataString(modId)}/files/{fileId.GetInt64()}/download_link.json", cancellationToken);
        var first = links.RootElement.ValueKind == JsonValueKind.Array ? links.RootElement.EnumerateArray().FirstOrDefault() : default;
        var url = first.ValueKind == JsonValueKind.Object && first.TryGetProperty("URI", out var uri) ? uri.GetString() : null;
        return string.IsNullOrWhiteSpace(url) ? null : (url, name);
    }

    public void Dispose() => client.Dispose();
}
