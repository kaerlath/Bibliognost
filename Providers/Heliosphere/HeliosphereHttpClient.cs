using System.Net.Http.Json;
using System.Text.Json;

namespace Bibliognost.Providers.Heliosphere;

public sealed class HeliosphereHttpClient : IDisposable
{
    public const string ApiBase = "https://heliosphere.app/api";
    private readonly HttpClient client = new() { BaseAddress = new Uri(ApiBase + "/"), Timeout = TimeSpan.FromSeconds(30) };

    public HeliosphereHttpClient() => client.DefaultRequestHeaders.UserAgent.ParseAdd("Bibliognost/0.2");

    public async Task<JsonDocument> QueryAsync(string query, object variables, CancellationToken cancellationToken)
    {
        using var response = await client.PostAsJsonAsync("graphql", new { query, variables }, cancellationToken);
        response.EnsureSuccessStatusCode();
        var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        if (document.RootElement.TryGetProperty("errors", out var errors))
        {
            var message = errors.EnumerateArray().FirstOrDefault().TryGetProperty("message", out var item) ? item.GetString() : null;
            document.Dispose();
            throw new InvalidDataException("Heliosphere rejected the catalog query" + (message is null ? "." : $": {message}"));
        }
        return document;
    }

    public static string ImageUrl(string packageId, int imageId) => $"{ApiBase}/web/package/{packageId.Replace("-", "")}/image/{imageId}";
    public void Dispose() => client.Dispose();
}
