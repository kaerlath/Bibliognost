using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Bibliognost.Models;

namespace Bibliognost.Providers.NexusMods;

public sealed partial class NexusModsProvider(NexusModsClient client) : IModProvider
{
    public const string ProviderId = "nexusmods";
    public string Id => ProviderId;
    public string DisplayName => "Nexus Mods";
    public bool SupportsAuthentication => true;
    public bool SupportsDirectDownloads => false;
    public void SetApiKey(string? key) => client.SetApiKey(key);

    public async Task<ProviderResult<IReadOnlyList<ModSummary>>> SearchAsync(ModSearchQuery query, CancellationToken cancellationToken = default)
    {
        if (!client.HasApiKey) return ProviderResult<IReadOnlyList<ModSummary>>.Fail("Connect Nexus Mods in Settings to browse its Final Fantasy XIV catalog.");
        try
        {
            string[] feeds = query.Sort switch
            {
                ModSort.Updated => ["latest_updated"],
                ModSort.Downloads or ModSort.Views => ["trending"],
                _ => ["latest_added", "latest_updated"],
            };
            var documents = await Task.WhenAll(feeds.Select(feed => client.GetAsync($"games/{NexusModsClient.GameDomain}/mods/{feed}.json", cancellationToken)));
            try
            {
                var mods = documents.SelectMany(d => d.RootElement.EnumerateArray()).Select(ParseSummary).Where(m => m is not null).Cast<ModSummary>()
                    .GroupBy(m => m.RemoteId).Select(g => g.First()).Where(m => Matches(m, query));
                if (query.PublishedTodayOnly) mods = mods.Where(m => m.PublishedAt?.ToLocalTime().Date == DateTimeOffset.Now.Date);
                mods = query.Sort switch { ModSort.Name => mods.OrderBy(m => m.Name), _ => mods.OrderByDescending(m => m.UpdatedAt) };
                return ProviderResult<IReadOnlyList<ModSummary>>.Ok(mods.ToArray());
            }
            finally { foreach (var document in documents) document.Dispose(); }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return ProviderResult<IReadOnlyList<ModSummary>>.Fail($"Nexus Mods request failed: {ex.Message}");
        }
    }

    public async Task<ProviderResult<ModDetails>> GetDetailsAsync(string remoteId, CancellationToken cancellationToken = default)
    {
        if (!client.HasApiKey) return ProviderResult<ModDetails>.Fail("Nexus Mods is not connected.");
        try
        {
            using var document = await client.GetAsync($"games/{NexusModsClient.GameDomain}/mods/{Uri.EscapeDataString(remoteId)}.json", cancellationToken);
            var node = document.RootElement;
            var summary = ParseSummary(node);
            if (summary is null) return ProviderResult<ModDetails>.Fail("Nexus returned incomplete mod data.");
            (string Url, string FileName)? download = null;
            try { download = await client.ResolvePrimaryDownloadAsync(remoteId, cancellationToken); }
            catch (HttpRequestException) { /* Free accounts and restricted files may require the Nexus website. */ }
            return ProviderResult<ModDetails>.Ok(new ModDetails
            {
                Summary = summary,
                Description = StripHtml(String(node, "description")),
                ImageUrls = summary.ThumbnailUrl is null ? [] : [summary.ThumbnailUrl],
                DownloadCount = Long(node, "mod_downloads") ?? Long(node, "mod_unique_downloads"),
                DownloadUrl = download?.Url,
                DownloadFileName = download?.FileName,
                IsDirectDownload = download is not null,
            });
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return ProviderResult<ModDetails>.Fail($"Nexus Mods details failed: {ex.Message}");
        }
    }

    public async Task<AuthenticationStatus> VerifyAuthenticationAsync(CancellationToken cancellationToken = default)
    {
        if (!client.HasApiKey) return new(false, "No Nexus Mods API key is stored.");
        try
        {
            using var document = await client.GetAsync("users/validate.json", cancellationToken);
            var name = String(document.RootElement, "name");
            return new(true, "Nexus Mods connection verified.", name.Length == 0 ? null : name);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException) { return new(false, $"Nexus Mods rejected the connection: {ex.Message}"); }
    }

    private static ModSummary? ParseSummary(JsonElement node)
    {
        var id = Long(node, "mod_id")?.ToString() ?? ""; var name = String(node, "name");
        if (id.Length == 0 || name.Length == 0) return null;
        var category = String(node, "category_name");
        var summary = String(node, "summary");
        var updated = Long(node, "updated_timestamp"); var created = Long(node, "created_timestamp");
        return new ModSummary { ProviderId = ProviderId, RemoteId = id, Name = name, Author = String(node, "author").Length > 0 ? String(node, "author") : String(node, "uploaded_by"), ModType = category.Length == 0 ? "Nexus Mods" : category, ThumbnailUrl = String(node, "picture_url") is { Length: > 0 } picture ? picture : null, PageUrl = $"https://www.nexusmods.com/{NexusModsClient.GameDomain}/mods/{id}", IsAdult = Bool(node, "adult_content"), Version = String(node, "version"), UpdatedAt = updated.HasValue ? DateTimeOffset.FromUnixTimeSeconds(updated.Value) : null, PublishedAt = created.HasValue ? DateTimeOffset.FromUnixTimeSeconds(created.Value) : null, Tags = category.Length == 0 ? [] : [category] };
    }
    private static bool Matches(ModSummary mod, ModSearchQuery query)
    {
        var text = string.Join(' ', mod.Name, mod.Author, mod.ModType, string.Join(' ', mod.Tags));
        return Match(text, query.SearchText) && Match(mod.Name, query.Name) && Match(mod.Author, query.Author) && Match(text, query.Tags) && Match(text, query.Races) && Match(text, query.Gender) && Match(text, query.Affects)
            && (query.Types.Count == 0 || query.Types.Select(TypeLabel).Any(label => text.Contains(label, StringComparison.OrdinalIgnoreCase)));
    }
    private static string TypeLabel(string id) => id switch { "1" => "gear", "2" => "body", "3" => "face", "4" => "hair", "5" => "reshade", "7" => "minion", "8" => "mount", "9" => "furniture", "10" => "skin", "13" => "pose", "14" => "vfx", "15" => "animation", "16" => "sound", "17" => "plugin", "18" => "tool", "19" => "app", _ => "other" };
    private static bool Match(string haystack, string needle) => string.IsNullOrWhiteSpace(needle) || needle.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).All(x => haystack.Contains(x, StringComparison.OrdinalIgnoreCase));
    private static string String(JsonElement node, string name) => node.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    private static long? Long(JsonElement node, string name) => node.TryGetProperty(name, out var value) && value.TryGetInt64(out var result) ? result : null;
    private static bool Bool(JsonElement node, string name) => node.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True;
    private static string StripHtml(string value) => WebUtility.HtmlDecode(HtmlRegex().Replace(value, " ")).Trim();
    [GeneratedRegex("<[^>]+>")] private static partial Regex HtmlRegex();
}
