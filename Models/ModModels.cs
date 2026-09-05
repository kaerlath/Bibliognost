namespace Bibliognost.Models;

public sealed record ModSummary
{
    public required string ProviderId { get; init; }
    public required string RemoteId { get; init; }
    public required string Name { get; init; }
    public string Author { get; init; } = string.Empty;
    public string ModType { get; init; } = string.Empty;
    public string Gender { get; init; } = string.Empty;
    public string? ThumbnailUrl { get; init; }
    public required string PageUrl { get; init; }
    public bool IsAdult { get; init; }
    public string Version { get; init; } = string.Empty;
    public DateTimeOffset? UpdatedAt { get; init; }
    public DateTimeOffset? PublishedAt { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
    public IReadOnlyList<ModSourceReference> Sources { get; init; } = Array.Empty<ModSourceReference>();
}

public sealed record ModSourceReference(string ProviderId, string RemoteId, string PageUrl, string Version, DateTimeOffset? UpdatedAt);

public sealed record ModDetails
{
    public required ModSummary Summary { get; init; }
    public string Description { get; init; } = string.Empty;
    public IReadOnlyList<string> ImageUrls { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Races { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Affects { get; init; } = Array.Empty<string>();
    public string? DownloadUrl { get; init; }
    public bool IsDirectDownload { get; init; }
    public string? DownloadFileName { get; init; }
    public long? DownloadCount { get; init; }
    public long? ViewCount { get; init; }
}

public sealed record ModSearchQuery
{
    public string SearchText { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Author { get; init; } = string.Empty;
    public string Gender { get; init; } = string.Empty;
    public string Races { get; init; } = string.Empty;
    public string Tags { get; init; } = string.Empty;
    public string Affects { get; init; } = string.Empty;
    public bool? AdultContent { get; init; }
    public int Page { get; init; } = 1;
    public ModSort Sort { get; init; } = ModSort.Newest;
    public SortDirection Direction { get; init; } = SortDirection.Descending;
    public bool DawntrailCompatibleOnly { get; init; } = true;
    public IReadOnlyList<string> Types { get; init; } = Array.Empty<string>();
    public bool PublishedTodayOnly { get; init; }
}

public enum ModSort { Newest = 0, Updated = 1, Downloads = 2, Views = 3, Name = 4, Relevance = 5 }
public enum SortDirection { Ascending, Descending }

public sealed record ProviderResult<T>(bool Success, T? Value, string? Error)
{
    public static ProviderResult<T> Ok(T value) => new(true, value, null);
    public static ProviderResult<T> Fail(string error) => new(false, default, error);
}

public sealed record AuthenticationStatus(bool IsAuthenticated, string Message, string? AccountName = null);

public sealed class InstalledModReceipt
{
    public string ProviderId { get; set; } = string.Empty;
    public string RemoteId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string InstalledVersion { get; set; } = string.Empty;
    public string PageUrl { get; set; } = string.Empty;
    public DateTimeOffset InstalledAt { get; set; }
    public string IgnoredVersion { get; set; } = string.Empty;
}
