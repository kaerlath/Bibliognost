using Bibliognost.Models;

namespace Bibliognost.Providers;

public enum ProviderSelection { All, XivModArchive, Heliosphere, NexusMods }

public sealed record SourceMatchCandidate(ModSummary Summary, float Confidence, string Explanation);

public sealed class ModCatalog(IEnumerable<IModProvider> providers, Configuration configuration)
{
    private readonly IReadOnlyDictionary<string, IModProvider> providers = providers.ToDictionary(p => p.Id, StringComparer.Ordinal);
    private readonly Dictionary<string, (DateTimeOffset Stored, ProviderResult<IReadOnlyList<ModSummary>> Result)> searchCache = new(StringComparer.Ordinal);
    public IReadOnlyList<SourceMatchCandidate> LastCandidates { get; private set; } = [];
    public string LastMatchExplanation { get; private set; } = string.Empty;

    public async Task<ProviderResult<IReadOnlyList<ModSummary>>> SearchAsync(ModSearchQuery query, ProviderSelection selection, CancellationToken cancellationToken = default)
    {
        var selected = providers.Values.Where(p => selection switch { ProviderSelection.XivModArchive => p.Id == "xivmodarchive", ProviderSelection.Heliosphere => p.Id == "heliosphere", ProviderSelection.NexusMods => p.Id == "nexusmods", _ => true }).ToArray();
        var pages = selection == ProviderSelection.All ? Enumerable.Range(1, Math.Max(1, query.Page)) : [query.Page];
        var results = await Task.WhenAll(selected.SelectMany(p => pages.Select(async page => (Provider: p, Result: await CachedSearchAsync(p, query with { Page = page }, cancellationToken)))));
        var successes = results.Where(x => x.Result.Success && x.Result.Value is not null).ToArray();
        if (successes.Length == 0) return ProviderResult<IReadOnlyList<ModSummary>>.Fail(string.Join("  ", results.Select(x => x.Result.Error).Where(x => x is not null)));
        if (selection != ProviderSelection.All && selected.Length == 1)
        {
            // A provider-specific view should mirror that provider's own ordering exactly.
            // Re-sorting XMA cards by dates scraped from detail pages moves cards with an
            // unavailable/unparseable date to the bottom and makes the page look incomplete.
            var providerPage = successes.SelectMany(x => x.Result.Value!)
                .GroupBy(x => $"{x.ProviderId}:{x.RemoteId}")
                .Select(group => group.First())
                .ToArray();
            var providerWarnings = results.Where(x => !x.Result.Success).Select(x => $"{x.Provider.DisplayName}: {x.Result.Error}").ToArray();
            return new ProviderResult<IReadOnlyList<ModSummary>>(true, providerPage, providerWarnings.Length == 0 ? null : string.Join("  ", providerWarnings.Distinct()));
        }
        var unique = successes.SelectMany(x => x.Result.Value!).GroupBy(x => $"{x.ProviderId}:{x.RemoteId}").Select(g => g.First());
        var groups = new List<List<ModSummary>>();
        foreach (var mod in unique)
        {
            var group = groups.FirstOrDefault(existing => IsAccepted(existing[0], mod));
            if (group is null) groups.Add([mod]); else group.Add(mod);
        }
        var merged = groups.Select(group =>
        {
            var preferred = group.OrderByDescending(x => Chronology(x, query.Sort)).First();
            var sources = group.Select(x => new ModSourceReference(x.ProviderId, x.RemoteId, x.PageUrl, x.Version, x.UpdatedAt)).ToArray();
            return preferred with { Sources = sources };
        });
        merged = query.Sort switch { ModSort.Name => merged.OrderBy(x => x.Name), _ => merged.OrderByDescending(x => Chronology(x, query.Sort)) };
        var warnings = results.Where(x => !x.Result.Success).Select(x => $"{x.Provider.DisplayName}: {x.Result.Error}").ToArray();
        var timeline = merged.ToArray();
        if (selection == ProviderSelection.All) timeline = timeline.Skip((Math.Max(1, query.Page) - 1) * 24).Take(24).ToArray();
        return new ProviderResult<IReadOnlyList<ModSummary>>(true, timeline, warnings.Length == 0 ? null : string.Join("  ", warnings.Distinct()));
    }

    public async Task<ProviderResult<ModDetails>> GetDetailsAsync(ModSummary summary, CancellationToken cancellationToken = default)
    {
        if (!providers.TryGetValue(summary.ProviderId, out var provider)) return ProviderResult<ModDetails>.Fail("The selected provider is no longer available.");
        var result = await provider.GetDetailsAsync(summary.RemoteId, cancellationToken);
        return result is { Success: true, Value: not null }
            ? result with { Value = result.Value with { Summary = result.Value.Summary with { Sources = summary.Sources } } }
            : result;
    }

    public Task<ProviderResult<ModDetails>> GetDetailsAsync(string providerId, string remoteId, CancellationToken cancellationToken = default)
        => providers.TryGetValue(providerId, out var provider)
            ? provider.GetDetailsAsync(remoteId, cancellationToken)
            : Task.FromResult(ProviderResult<ModDetails>.Fail("That provider is no longer configured."));

    public async Task<ProviderResult<IReadOnlyList<ModDetails>>> GetAllSourceDetailsAsync(ModSummary summary, CancellationToken cancellationToken = default)
    {
        LastCandidates = [];
        LastMatchExplanation = "No alternate source was confirmed.";
        var references = summary.Sources.Count > 0 ? summary.Sources.ToList() : [new(summary.ProviderId, summary.RemoteId, summary.PageUrl, summary.Version, summary.UpdatedAt)];
        var missing = providers.Values.Where(provider => references.All(source => source.ProviderId != provider.Id)).ToArray();
        var searches = await Task.WhenAll(missing.Select(async provider => (provider, candidates: await FindCandidatesAsync(provider, summary, cancellationToken))));
        var suggestions = new List<SourceMatchCandidate>();
        var explanations = new List<string>();
        foreach (var (provider, candidates) in searches)
        {
            var ranked = candidates.Select(candidate => (candidate, score: IdentityConfidence(summary, candidate)))
                .OrderByDescending(item => item.score).ToArray();
            var match = ranked.FirstOrDefault(item => IsAccepted(summary, item.candidate));
            if (match.candidate is not null)
            {
                references.Add(new(provider.Id, match.candidate.RemoteId, match.candidate.PageUrl, match.candidate.Version, match.candidate.UpdatedAt));
                explanations.Add($"{provider.DisplayName}: {ExplainScore(summary, match.candidate, match.score)}");
            }
            foreach (var item in ranked.Where(item => item.score >= .42f && !IsAccepted(summary, item.candidate) && !IsRejected(summary, item.candidate)).Take(3))
                suggestions.Add(new(item.candidate, item.score, ExplainScore(summary, item.candidate, item.score)));
        }
        LastCandidates = suggestions;
        if (explanations.Count > 0) LastMatchExplanation = string.Join("  ", explanations);
        references = references.GroupBy(source => $"{source.ProviderId}:{source.RemoteId}").Select(group => group.First()).ToList();
        var detailTasks = references.Select(async source =>
        {
            if (!providers.TryGetValue(source.ProviderId, out var provider)) return null;
            var result = await provider.GetDetailsAsync(source.RemoteId, cancellationToken);
            return result.Success && result.Value is not null
                ? result.Value with { Summary = result.Value.Summary with { Sources = references } }
                : null;
        });
        var details = (await Task.WhenAll(detailTasks)).Where(value => value is not null).Cast<ModDetails>().ToArray();
        return details.Length == 0
            ? ProviderResult<IReadOnlyList<ModDetails>>.Fail("No connected source returned mod details.")
            : ProviderResult<IReadOnlyList<ModDetails>>.Ok(details);
    }

    private static DateTimeOffset Chronology(ModSummary mod, ModSort sort)
        => sort == ModSort.Newest
            ? mod.PublishedAt ?? mod.UpdatedAt ?? DateTimeOffset.MinValue
            : mod.UpdatedAt ?? mod.PublishedAt ?? DateTimeOffset.MinValue;

    private async Task<ProviderResult<IReadOnlyList<ModSummary>>> CachedSearchAsync(IModProvider provider, ModSearchQuery query, CancellationToken cancellationToken)
    {
        var key = string.Join('|', provider.Id, query.Page, query.Sort, query.Direction, query.SearchText, query.Name, query.Author, query.Gender, query.Races, query.Tags, query.Affects, query.AdultContent, query.DawntrailCompatibleOnly, query.PublishedTodayOnly, string.Join(',', query.Types));
        lock (searchCache)
            if (searchCache.TryGetValue(key, out var cached) && DateTimeOffset.Now - cached.Stored < TimeSpan.FromMinutes(10)) return cached.Result;
        var result = await provider.SearchAsync(query, cancellationToken);
        if (result.Success) lock (searchCache) searchCache[key] = (DateTimeOffset.Now, result);
        return result;
    }

    private async Task<IReadOnlyList<ModSummary>> FindCandidatesAsync(IModProvider provider, ModSummary summary, CancellationToken cancellationToken)
    {
        var author = ProbeAuthor(summary.Author);
        var probes = new List<ModSearchQuery>();
        for (var page = 1; page <= 3; page++)
        {
            probes.Add(new ModSearchQuery { Name = ProbeTitle(summary.Name), Sort = ModSort.Name, Page = page, DawntrailCompatibleOnly = false });
            if (author.Length > 0) probes.Add(new ModSearchQuery { Author = author, Sort = ModSort.Name, Page = page, DawntrailCompatibleOnly = false });
        }
        var results = await Task.WhenAll(probes.Select(probe => CachedSearchAsync(provider, probe, cancellationToken)));
        return results.Where(result => result.Success && result.Value is not null).SelectMany(result => result.Value!)
            .GroupBy(candidate => $"{candidate.ProviderId}:{candidate.RemoteId}").Select(group => group.First()).ToArray();
    }

    private static bool IsSameWork(ModSummary left, ModSummary right)
        => IdentityConfidence(left, right) >= .75f;

    private bool IsAccepted(ModSummary left, ModSummary right)
        => configuration.ConfirmedSourceMatches.Contains(PairKey(left, right), StringComparer.Ordinal) || (!IsRejected(left, right) && IsSameWork(left, right));

    private bool IsRejected(ModSummary left, ModSummary right)
        => configuration.RejectedSourceMatches.Contains(PairKey(left, right), StringComparer.Ordinal);

    public void ConfirmMatch(ModSummary left, ModSummary right)
    {
        var key = PairKey(left, right);
        configuration.RejectedSourceMatches.RemoveAll(item => item == key);
        if (!configuration.ConfirmedSourceMatches.Contains(key, StringComparer.Ordinal)) configuration.ConfirmedSourceMatches.Add(key);
        configuration.Save();
    }

    public void RejectMatch(ModSummary left, ModSummary right)
    {
        var key = PairKey(left, right);
        configuration.ConfirmedSourceMatches.RemoveAll(item => item == key);
        if (!configuration.RejectedSourceMatches.Contains(key, StringComparer.Ordinal)) configuration.RejectedSourceMatches.Add(key);
        configuration.Save();
    }

    private static string PairKey(ModSummary left, ModSummary right)
    {
        var values = new[] { $"{left.ProviderId}:{left.RemoteId}", $"{right.ProviderId}:{right.RemoteId}" };
        Array.Sort(values, StringComparer.Ordinal);
        return string.Join('|', values);
    }

    private static string ExplainScore(ModSummary left, ModSummary right, float score)
    {
        static string N(string value) => new(value.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
        var author = N(left.Author) == N(right.Author) ? "same creator" : "compatible creator name";
        return $"{score:P0} confidence · {author} · {CanonicalTitle(left.Name).Intersect(CanonicalTitle(right.Name)).Count()} shared title terms";
    }

    private static float IdentityConfidence(ModSummary left, ModSummary right)
    {
        static string Normalize(string value) => new(value.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
        var leftName = CanonicalTitle(left.Name); var rightName = CanonicalTitle(right.Name);
        var leftAuthor = Normalize(left.Author); var rightAuthor = Normalize(right.Author);
        if (leftName.Count == 0 || rightName.Count == 0) return 0;
        var authorMatches = leftAuthor == rightAuthor || leftAuthor.Contains(rightAuthor, StringComparison.Ordinal) || rightAuthor.Contains(leftAuthor, StringComparison.Ordinal);
        var intersection = leftName.Intersect(rightName, StringComparer.Ordinal).Count();
        var union = leftName.Union(rightName, StringComparer.Ordinal).Count();
        var titleScore = union == 0 ? 0 : intersection / (float)union;
        if (titleScore < .50f) return 0;
        var authorScore = authorMatches ? 1f : leftAuthor.Length == 0 || rightAuthor.Length == 0 ? .35f : 0f;
        return titleScore * .55f + authorScore * .45f;
    }

    private static HashSet<string> CanonicalTitle(string value)
    {
        var tokens = Tokenize(value).Select(token => token switch
        {
            "m" or "masculine" => "male",
            "f" or "feminine" => "female",
            "miqo" or "miqote" => "miqote",
            "aura" or "aurae" => "aura",
            "viera" or "vieras" => "viera",
            _ => token,
        });
        return tokens.Where(token => token.Length > 1 || token is "male" or "female").ToHashSet(StringComparer.Ordinal);
    }

    private static string ProbeTitle(string value)
    {
        var generic = new HashSet<string>(["for", "male", "female", "unisex", "miqo", "miqote", "aura", "viera", "elezen", "hyur", "lalafell", "roegadyn", "hrothgar"], StringComparer.Ordinal);
        var words = Tokenize(value).Where(token => token.Length > 1 && !generic.Contains(token)).Take(3).ToArray();
        return words.Length == 0 ? value : string.Join(' ', words);
    }

    private static string ProbeAuthor(string value)
    {
        var first = value.Split(['/', '(', '[', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? value;
        return first.Trim();
    }

    private static IEnumerable<string> Tokenize(string value)
        => new string(value.ToLowerInvariant().Select(character => char.IsLetterOrDigit(character) ? character : ' ').ToArray())
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
}
