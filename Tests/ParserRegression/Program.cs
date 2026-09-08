using Bibliognost.Models;
using Bibliognost.Providers.XivModArchive;

var html = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "xma-similar-mods.html"));
var fallback = new ModSummary { ProviderId = "xivmodarchive", RemoteId = "1", Name = "Fixture", PageUrl = "https://www.xivmodarchive.com/modid/1" };
var details = XmaParser.ParseDetails(html, fallback) ?? throw new Exception("Details parser returned null.");
if (details.ImageUrls.Count != 1 || !details.ImageUrls[0].EndsWith("selected.jpg", StringComparison.Ordinal))
    throw new Exception($"Expected only selected.jpg, received: {string.Join(", ", details.ImageUrls)}");
Console.WriteLine("XMA gallery regression passed.");
var total = XmaParser.ParseSearchTotal("<code>92,776 Results over 6,186 Pages.</code>");
if (total != 92776) throw new Exception($"Expected XMA total 92776, received {total?.ToString() ?? "null"}.");
Console.WriteLine("XMA total-result regression passed.");

namespace Bibliognost.Providers.XivModArchive
{
    internal static class XmaHttpClient { internal static readonly Uri BaseUri = new("https://www.xivmodarchive.com/"); }
    internal static class XmaProvider { internal const string ProviderId = "xivmodarchive"; }
}
