using System.Net;
using System.Net.Http.Headers;

namespace Bibliognost.Providers.XivModArchive;

public sealed class XmaHttpClient : IDisposable
{
    public static readonly Uri BaseUri = new("https://www.xivmodarchive.com/");
    private readonly CookieContainer cookies = new();
    private readonly HttpClient client;

    public XmaHttpClient()
    {
        var handler = new HttpClientHandler
        {
            CookieContainer = cookies,
            UseCookies = true,
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.All,
        };
        client = new HttpClient(handler) { BaseAddress = BaseUri, Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Bibliognost", "0.1"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.8");
    }

    public bool HasSession { get; private set; }

    public void SetSession(string? connectSid)
    {
        foreach (Cookie cookie in cookies.GetCookies(BaseUri)) cookie.Expired = true;
        HasSession = !string.IsNullOrWhiteSpace(connectSid);
        if (!HasSession) return;
        cookies.Add(BaseUri, new Cookie("connect.sid", connectSid!.Trim(), "/", ".xivmodarchive.com")
        {
            Secure = true,
            HttpOnly = true,
        });
    }

    public async Task<string> GetStringAsync(string relativeOrAbsoluteUrl, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(relativeOrAbsoluteUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    public async Task<byte[]> GetBytesAsync(string absoluteUrl, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(absoluteUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (mediaType is not null && !mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The preview URL did not return an image.");
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    public async Task<HttpResponseMessage> GetDownloadAsync(string url, CancellationToken cancellationToken)
    {
        var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode) { response.Dispose(); response.EnsureSuccessStatusCode(); }
        return response;
    }

    public void Dispose() => client.Dispose();
}
