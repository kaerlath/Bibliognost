using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Bibliognost.Models;
using HtmlAgilityPack;

namespace Bibliognost.Providers.XivModArchive;

internal static partial class XmaParser
{
    public static IReadOnlyList<ModSummary> ParseSearch(string html)
    {
        var doc = Load(html);
        var cards = doc.DocumentNode.SelectNodes("//div[contains(concat(' ',normalize-space(@class),' '),' mod-card ')]")
            ?? doc.DocumentNode.SelectNodes("//*[contains(@class,'card')][.//a[contains(@href,'/modid/')]]");
        if (cards is null) return Array.Empty<ModSummary>();

        var results = new List<ModSummary>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var card in cards)
        {
            var link = card.SelectSingleNode(".//a[contains(@href,'/modid/')]");
            var href = link?.GetAttributeValue("href", string.Empty);
            var id = href is null ? null : ModIdRegex().Match(href).Groups[1].Value;
            var title = Clean(card.SelectSingleNode(".//*[contains(@class,'card-title')]")?.InnerText);
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(title) || !seen.Add(id)) continue;

            var image = card.SelectSingleNode(".//img[contains(@class,'card-img') or @data-src or @src]");
            var imageUrl = image?.GetAttributeValue("data-src", string.Empty);
            if (string.IsNullOrWhiteSpace(imageUrl)) imageUrl = image?.GetAttributeValue("src", string.Empty);
            var codes = card.SelectNodes(".//code")?.Select(n => Clean(n.InnerText)).ToArray() ?? [];
            var allText = Clean(card.InnerText);
            results.Add(new ModSummary
            {
                ProviderId = XmaProvider.ProviderId,
                RemoteId = id,
                Name = title,
                Author = Clean(card.SelectSingleNode(".//p[contains(@class,'card-text')]/a")?.InnerText),
                ModType = ValueAfter(codes, "Type:"),
                Gender = ValueAfter(codes, "Genders:"),
                ThumbnailUrl = Absolute(imageUrl),
                PageUrl = new Uri(XmaHttpClient.BaseUri, $"modid/{id}").AbsoluteUri,
                IsAdult = HasAny(allText, "NSFW", "Adult") || HasClass(card, "nsfw"),
            });
        }
        return results;
    }

    public static ModDetails? ParseDetails(string html, ModSummary fallback)
    {
        var doc = Load(html);
        var title = Clean(doc.DocumentNode.SelectSingleNode("//h1|//h2[contains(@class,'mod-title')]")?.InnerText);
        var tags = doc.DocumentNode.SelectNodes("//div[contains(@class,'mod-meta-block')]//a[contains(@href,'tags=')]")
            ?.Select(n => Clean(n.InnerText)).Where(s => s.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() ?? [];
        var images = doc.DocumentNode.SelectNodes("//img[contains(@class,'mod') or contains(@class,'preview') or contains(@class,'carousel')]")
            ?.Select(n => Absolute(string.IsNullOrWhiteSpace(n.GetAttributeValue("data-src", string.Empty)) ? n.GetAttributeValue("src", string.Empty) : n.GetAttributeValue("data-src", string.Empty)))
            .Where(s => s is not null).Cast<string>().Distinct().ToArray() ?? [];
        var descriptionNode = doc.DocumentNode.SelectSingleNode("//*[@id='info']")
            ?? doc.DocumentNode.SelectSingleNode("//*[contains(@class,'mod-description') or @id='mod-description']");
        var downloadNode = doc.DocumentNode.SelectSingleNode("//a[@id='mod-download-link']")
            ?? doc.DocumentNode.SelectSingleNode("//a[contains(@href,'/download/') or contains(@href,'download?') or contains(translate(normalize-space(.),'DIRECTDOWNLOAD','directdownload'),'direct download')]");
        var download = downloadNode?.GetAttributeValue("href", string.Empty);
        var bodyText = Clean(doc.DocumentNode.InnerText);

        var summary = fallback with
        {
            Name = string.IsNullOrWhiteSpace(title) ? fallback.Name : title,
            Version = MetaValue(doc, "Version:"),
            Tags = tags,
            UpdatedAt = TryDate(doc, "Last Version Update"),
            PublishedAt = TryDate(doc, "Original Release Date"),
            IsAdult = fallback.IsAdult || HasAny(bodyText, "NSFW", "Adult Content"),
        };
        return new ModDetails
        {
            Summary = summary,
            Description = Clean(descriptionNode?.InnerText),
            ImageUrls = images,
            DownloadUrl = Absolute(download),
            DownloadFileName = DownloadName(download),
            IsDirectDownload = download is not null && Uri.TryCreate(Absolute(download), UriKind.Absolute, out var uri)
                && uri.Host.EndsWith("xivmodarchive.com", StringComparison.OrdinalIgnoreCase),
            DownloadCount = Metric(bodyText, "Downloads"),
            ViewCount = Metric(bodyText, "Views"),
        };
    }

    public static AuthenticationStatus ParseAuthentication(string html)
    {
        var doc = Load(html);
        var logout = doc.DocumentNode.SelectSingleNode("//a[contains(@href,'logout')]");
        var login = doc.DocumentNode.SelectSingleNode("//a[contains(@href,'login') or contains(@href,'signin')]");
        var account = Clean(doc.DocumentNode.SelectSingleNode("//*[contains(@class,'username') or contains(@class,'user-name')]")?.InnerText);
        if (logout is not null) return new(true, "XMA session verified.", account.Length == 0 ? null : account);
        if (login is not null) return new(false, "XMA did not accept this session. It may have expired.");
        return new(false, "XMA returned an unfamiliar page, so authentication could not be confirmed.");
    }

    private static HtmlDocument Load(string html) { var d = new HtmlDocument(); d.LoadHtml(html); return d; }
    private static string Clean(string? value) => WebUtility.HtmlDecode(value ?? string.Empty).Replace('\u00a0', ' ').Trim();
    private static bool HasClass(HtmlNode node, string value) => node.GetAttributeValue("class", "").Contains(value, StringComparison.OrdinalIgnoreCase);
    private static bool HasAny(string text, params string[] values) => values.Any(v => text.Contains(v, StringComparison.OrdinalIgnoreCase));
    private static string ValueAfter(IEnumerable<string> values, string prefix) => values.FirstOrDefault(v => v.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))?[prefix.Length..].Trim() ?? "";
    private static string? Absolute(string? value) => string.IsNullOrWhiteSpace(value) || value.StartsWith("data:") ? null : new Uri(XmaHttpClient.BaseUri, WebUtility.HtmlDecode(value)).AbsoluteUri;
    private static string? DownloadName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var decoded = WebUtility.HtmlDecode(value);
        return Uri.TryCreate(Absolute(decoded), UriKind.Absolute, out var uri) ? Path.GetFileName(uri.LocalPath) : null;
    }
    private static string MetaValue(HtmlDocument doc, string label) => Clean(doc.DocumentNode.SelectSingleNode($"//code[contains(normalize-space(.),'{label}')]")?.InnerText).Replace(label, "", StringComparison.OrdinalIgnoreCase).Trim();
    private static DateTimeOffset? TryDate(HtmlDocument doc, string label)
    {
        var block = doc.DocumentNode.SelectSingleNode($"//*[contains(@class,'mod-meta-block')][contains(normalize-space(.),'{label}')]");
        var raw = Clean(block?.SelectSingleNode(".//*[contains(@class,'server-date')]")?.InnerText);
        raw = Regex.Replace(raw, @"\s*\([^)]*\)\s*$", string.Empty).Replace("GMT", string.Empty, StringComparison.OrdinalIgnoreCase);
        return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var date) ? date : null;
    }
    private static long? Metric(string text, string name)
    {
        var match = Regex.Match(text, $@"{name}\s*:?\s*([\d,]+)", RegexOptions.IgnoreCase);
        return match.Success && long.TryParse(match.Groups[1].Value.Replace(",", ""), out var value) ? value : null;
    }
    [GeneratedRegex(@"/modid/(\d+)", RegexOptions.IgnoreCase)] private static partial Regex ModIdRegex();
}
