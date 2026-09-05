using Bibliognost.Models;

namespace Bibliognost.Providers.XivModArchive;

public sealed class XmaProvider(XmaHttpClient http) : IModProvider
{
    // XMA treats an omitted `types` parameter as its own partial/default selection.
    // Sending every current website type explicitly is the only reliable "all types" query.
    private static readonly string[] AllTypeIds =
    [
        "1", "3", "7", "9", "12", "15", "2", "4", "8",
        "10", "14", "16", "17", "18", "19", "13", "6", "5",
    ];
    private readonly Dictionary<string, ModSummary> knownMods = new(StringComparer.Ordinal);
    public const string ProviderId = "xivmodarchive";
    public string Id => ProviderId;
    public string DisplayName => "XIV Mod Archive";
    public bool SupportsAuthentication => true;
    public bool SupportsDirectDownloads => false;

    public void SetSession(string? session) => http.SetSession(session);

    public async Task<ProviderResult<IReadOnlyList<ModSummary>>> SearchAsync(ModSearchQuery query, CancellationToken cancellationToken = default)
    {
        try
        {
            var pairs = new Dictionary<string, string>
            {
                ["sortby"] = query.Sort switch { ModSort.Relevance => "rank", ModSort.Updated => "time_edited", ModSort.Downloads => "downloads", ModSort.Views => "views", ModSort.Name => "name_slug", _ => "time_posted" },
                ["sortorder"] = query.Direction == SortDirection.Ascending ? "asc" : "desc",
                ["dt_compat"] = query.DawntrailCompatibleOnly ? "1" : "0",
                ["page"] = Math.Max(1, query.Page).ToString(),
            };
            Add("basic_text", query.SearchText);
            Add("name", query.Name);
            Add("author", query.Author);
            Add("genders", query.Gender);
            Add("races", query.Races);
            Add("tags", query.Tags);
            Add("affects", query.Affects);
            if (query.AdultContent.HasValue) pairs["nsfw"] = query.AdultContent.Value ? "true" : "false";
            pairs["types"] = string.Join(',', query.Types.Count > 0 ? query.Types : AllTypeIds);
            var url = "search?" + string.Join('&', pairs.Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));
            var html = await http.GetStringAsync(url, cancellationToken);
            var parsed = XmaParser.ParseSearch(html);
            if (query.PublishedTodayOnly || query.Sort is ModSort.Newest or ModSort.Updated)
            {
                using var throttle = new SemaphoreSlim(4);
                var detailTasks = parsed.Select(async mod =>
                {
                    await throttle.WaitAsync(cancellationToken);
                    try
                    {
                        var detailHtml = await http.GetStringAsync($"modid/{Uri.EscapeDataString(mod.RemoteId)}", cancellationToken);
                        return XmaParser.ParseDetails(detailHtml, mod)?.Summary ?? mod;
                    }
                    catch (HttpRequestException) { return mod; }
                    finally { throttle.Release(); }
                });
                var enriched = (await Task.WhenAll(detailTasks)).Where(m => m is not null).Cast<ModSummary>();
                parsed = (query.PublishedTodayOnly ? enriched.Where(m => m.PublishedAt?.ToLocalTime().Date == DateTimeOffset.Now.Date) : enriched).ToArray();
            }
            foreach (var mod in parsed) knownMods[mod.RemoteId] = mod;
            if (parsed.Count == 0 && (html.Contains("cf-chl-", StringComparison.OrdinalIgnoreCase) || html.Contains("Just a moment", StringComparison.OrdinalIgnoreCase)))
                return ProviderResult<IReadOnlyList<ModSummary>>.Fail("XMA's anti-bot page blocked this request.");
            return ProviderResult<IReadOnlyList<ModSummary>>.Ok(parsed);

            void Add(string key, string value)
            {
                if (!string.IsNullOrWhiteSpace(value)) pairs[key] = value.Trim();
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidDataException)
        {
            return ProviderResult<IReadOnlyList<ModSummary>>.Fail($"XMA request failed: {ex.Message}");
        }
    }

    public async Task<ProviderResult<ModDetails>> GetDetailsAsync(string remoteId, CancellationToken cancellationToken = default)
    {
        try
        {
            var html = await http.GetStringAsync($"modid/{Uri.EscapeDataString(remoteId)}", cancellationToken);
            var fallback = knownMods.GetValueOrDefault(remoteId) ?? new ModSummary { ProviderId = Id, RemoteId = remoteId, Name = "Mod details", PageUrl = new Uri(XmaHttpClient.BaseUri, $"modid/{remoteId}").AbsoluteUri };
            var details = XmaParser.ParseDetails(html, fallback);
            return details is null ? ProviderResult<ModDetails>.Fail("XMA returned a page Bibliognost could not parse.") : ProviderResult<ModDetails>.Ok(details);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return ProviderResult<ModDetails>.Fail($"XMA details request failed: {ex.Message}");
        }
    }

    public async Task<AuthenticationStatus> VerifyAuthenticationAsync(CancellationToken cancellationToken = default)
    {
        if (!http.HasSession) return new(false, "No XMA session is stored.");
        // XMA exposes no supported identity/session endpoint. Atomos likewise attaches
        // the cookie directly and lets XMA enforce access on subsequent content requests.
        try
        {
            await http.GetStringAsync("/", cancellationToken);
            return new(true, "XMA is reachable and the session is securely stored. XMA does not expose a direct account-verification endpoint; access is confirmed when account-restricted results are requested.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { return new(false, $"Could not contact XMA: {ex.Message}"); }
    }
}
