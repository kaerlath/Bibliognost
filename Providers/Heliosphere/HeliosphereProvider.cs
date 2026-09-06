using System.Text.Json;
using Bibliognost.Models;

namespace Bibliognost.Providers.Heliosphere;

public sealed class HeliosphereProvider(HeliosphereHttpClient http) : IModProvider
{
    public const string ProviderId = "heliosphere";
    private readonly Dictionary<string, ModSummary> knownMods = new(StringComparer.Ordinal);
    public string Id => ProviderId;
    public string DisplayName => "Heliosphere";
    public bool SupportsAuthentication => false;
    public bool SupportsDirectDownloads => true;

    public async Task<ProviderResult<IReadOnlyList<ModSummary>>> SearchAsync(ModSearchQuery query, CancellationToken cancellationToken = default)
    {
        try
        {
            var useSearch = HasSearch(query);
            using var document = useSearch
                ? await http.QueryAsync(SearchQuery, SearchVariables(query), cancellationToken)
                : await http.QueryAsync(BrowseQuery, BrowseVariables(query), cancellationToken);
            var data = document.RootElement.GetProperty("data");
            var nodes = useSearch
                ? data.GetProperty("searchVersions").GetProperty("versions").EnumerateArray().Select(v => (Version: v, Package: v.GetProperty("variant").GetProperty("package")))
                : data.GetProperty("packages").GetProperty("packages").EnumerateArray().Select(p => (Version: LatestVersion(p), Package: p));
            var results = nodes.Select(x => ParseSummary(x.Package, x.Version)).Where(x => x is not null).Cast<ModSummary>().ToList();
            if (!string.IsNullOrWhiteSpace(query.Author)) results.RemoveAll(x => !x.Author.Contains(query.Author, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(query.Races)) results.RemoveAll(x => !x.Tags.Any(t => t.Contains(query.Races, StringComparison.OrdinalIgnoreCase)));
            if (!string.IsNullOrWhiteSpace(query.Gender)) results.RemoveAll(x => !x.Tags.Any(t => t.Contains(query.Gender, StringComparison.OrdinalIgnoreCase)));
            if (query.PublishedTodayOnly) results.RemoveAll(x => x.PublishedAt?.ToLocalTime().Date != DateTimeOffset.Now.Date);
            foreach (var mod in results) knownMods[mod.RemoteId] = mod;
            return ProviderResult<IReadOnlyList<ModSummary>>.Ok(results);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidDataException or JsonException or KeyNotFoundException)
        {
            return ProviderResult<IReadOnlyList<ModSummary>>.Fail($"Heliosphere request failed: {ex.Message}");
        }
    }

    public async Task<ProviderResult<ModDetails>> GetDetailsAsync(string remoteId, CancellationToken cancellationToken = default)
    {
        try
        {
            using var document = await http.QueryAsync(DetailsQuery, new { id = remoteId }, cancellationToken);
            var package = document.RootElement.GetProperty("data").GetProperty("package");
            if (package.ValueKind == JsonValueKind.Null) return ProviderResult<ModDetails>.Fail("Heliosphere no longer has this package.");
            var version = LatestVersion(package);
            var summary = ParseSummary(package, version) ?? knownMods.GetValueOrDefault(remoteId);
            if (summary is null) return ProviderResult<ModDetails>.Fail("Heliosphere returned incomplete package data.");
            var images = Images(package).Select(id => HeliosphereHttpClient.ImageUrl(remoteId, id)).ToArray();
            var affects = version.ValueKind == JsonValueKind.Object && version.TryGetProperty("affects", out var a)
                ? a.EnumerateArray().Select(x => x.GetString()).Where(x => x is not null).Cast<string>().ToArray() : [];
            return ProviderResult<ModDetails>.Ok(new ModDetails
            {
                Summary = summary,
                Description = String(package, "description"),
                ImageUrls = images,
                Affects = affects,
                DownloadCount = Long(package, "downloads"),
            });
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidDataException or JsonException or KeyNotFoundException)
        {
            return ProviderResult<ModDetails>.Fail($"Heliosphere details failed: {ex.Message}");
        }
    }

    public Task<AuthenticationStatus> VerifyAuthenticationAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(new AuthenticationStatus(true, "Heliosphere public browsing does not require sign-in."));

    private static bool HasSearch(ModSearchQuery q) => new[] { q.SearchText, q.Name, q.Author, q.Races, q.Tags, q.Affects, q.Gender }.Any(s => !string.IsNullOrWhiteSpace(s)) || q.Types.Count > 0;
    private static object BrowseVariables(ModSearchQuery q) => new { page = Math.Max(0, q.Page - 1), count = 24, filter = Filter(q) };
    private static object SearchVariables(ModSearchQuery q)
    {
        var tags = Split(q.Tags).Concat(q.Types.Select(TypeTag)).Concat(Split(q.Races)).Concat(Split(q.Gender)).Where(x => x.Length > 0).Distinct().ToArray();
        var name = string.Join(' ', new[] { q.SearchText, q.Name }.Where(x => !string.IsNullOrWhiteSpace(x)));
        return new { page = Math.Max(0, q.Page - 1), amount = 24, filter = Filter(q), info = new { name = name.Length == 0 ? null : name, affects = Split(q.Affects), includeTags = tags, excludeTags = Array.Empty<string>(), order = Order(q.Sort), direction = q.Direction == SortDirection.Ascending ? "ASCENDING" : "DESCENDING", subscriber = "ALL", updateThreshold = q.DawntrailCompatibleOnly ? "DAWNTRAIL" : null } };
    }
    private static object Filter(ModSearchQuery q) => new { nsfw = q.AdultContent == true, nsfl = false, cw = true, paid = true };
    private static string Order(ModSort sort) => sort switch { ModSort.Updated => "UPDATED_AT", ModSort.Downloads => "DOWNLOADS", ModSort.Views => "DOWNLOADS_LAST_MONTH", _ => "CREATED_AT" };
    private static string[] Split(string value) => value.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    private static string TypeTag(string id) => id switch { "1" => "gear", "2" => "body-replacement", "3" => "face", "4" => "hair", "7" => "minion", "8" => "mount", "9" => "housing", "10" => "skin", "13" => "pose", "14" => "vfx", "15" => "animation", "16" => "sound", _ => "" };
    private static JsonElement LatestVersion(JsonElement package) => package.TryGetProperty("variants", out var variants) && variants.GetArrayLength() > 0 && variants[0].TryGetProperty("versions", out var versions) && versions.GetArrayLength() > 0 ? versions[0] : default;
    private static IEnumerable<int> Images(JsonElement package) => package.TryGetProperty("images", out var images) ? images.EnumerateArray().OrderBy(x => x.GetProperty("displayOrder").GetInt32()).Select(x => x.GetProperty("id").GetInt32()) : [];
    private static ModSummary? ParseSummary(JsonElement package, JsonElement version)
    {
        var id = String(package, "id"); var name = String(package, "name");
        if (id.Length == 0 || name.Length == 0) return null;
        var tags = package.TryGetProperty("tags", out var tagNodes) ? tagNodes.EnumerateArray().Select(t => String(t, "slug")).Where(t => t.Length > 0).ToArray() : [];
        var firstImage = Images(package).FirstOrDefault();
        var restricted = package.TryGetProperty("nsfw", out var r) && ((r.TryGetProperty("nsfw", out var n) && n.GetBoolean()) || (r.TryGetProperty("nsfl", out var l) && l.GetBoolean()));
        return new ModSummary { ProviderId = ProviderId, RemoteId = id, Name = name, Author = package.TryGetProperty("user", out var u) ? String(u, "visibleName") : "", ModType = tags.FirstOrDefault() ?? "Heliosphere", ThumbnailUrl = firstImage == 0 ? null : HeliosphereHttpClient.ImageUrl(id, firstImage), PageUrl = PublicPageUrl(package, id), IsAdult = restricted, Version = version.ValueKind == JsonValueKind.Object ? String(version, "version") : "", UpdatedAt = Date(version, "updatedAt"), PublishedAt = Date(package, "createdAt"), Tags = tags };
    }
    private static string PublicPageUrl(JsonElement package, string id)
    {
        var vanity = String(package, "vanityUrl").Trim();
        if (Uri.TryCreate(vanity, UriKind.Absolute, out var absolute)) return absolute.AbsoluteUri;
        vanity = vanity.Trim('/');
        if (vanity.StartsWith("mod/", StringComparison.OrdinalIgnoreCase)) vanity = vanity[4..];
        var shortId = package.TryGetProperty("variants", out var variants) && variants.GetArrayLength() > 0
            ? String(variants[0], "shortId").Trim()
            : string.Empty;
        return "https://heliosphere.app/mod/" + (vanity.Length > 0 ? vanity : shortId.Length > 0 ? shortId : id);
    }
    private static string String(JsonElement node, string name) => node.ValueKind == JsonValueKind.Object && node.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    private static long? Long(JsonElement node, string name) => node.TryGetProperty(name, out var value) && value.TryGetInt64(out var result) ? result : null;
    private static DateTimeOffset? Date(JsonElement node, string name) => DateTimeOffset.TryParse(String(node, name), out var result) ? result : null;

    private const string PackageFields = "id name tagline description createdAt updatedAt vanityUrl downloads tags { slug } nsfw { nsfw nsfl cw } images { id displayOrder } user { visibleName } variants { shortId versions(limit: 1) { id version createdAt updatedAt affects } }";
    private static readonly string BrowseQuery = $"query Browse($page:Int!,$count:Int!,$filter:FilterInfo!) {{ packages(page:$page,count:$count,filterInfo:$filter) {{ packages {{ {PackageFields} }} pageInfo {{ prev next total }} }} }}";
    private static readonly string SearchQuery = $"query Search($info:SearchRequest!,$filter:FilterInfo!,$amount:Int!,$page:Int) {{ searchVersions(info:$info,filterInfo:$filter,amount:$amount,page:$page) {{ versions {{ id version createdAt updatedAt affects variant {{ package {{ {PackageFields} }} }} }} pageInfo {{ prev next total }} }} }}";
    private static readonly string DetailsQuery = $"query Details($id:UUID!) {{ package(id:$id) {{ {PackageFields} }} }}";
}
