using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Dalamud.Interface.Textures;
using Dalamud.Plugin.Services;

namespace Bibliognost.Images;

public sealed class ThumbnailCache : IDisposable
{
    private readonly string cacheDirectory;
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly ITextureProvider textures;
    private readonly ConcurrentDictionary<string, Task<string?>> pending = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource shutdown = new();

    public ThumbnailCache(string cacheDirectory, ITextureProvider textures)
    {
        this.cacheDirectory = cacheDirectory;
        this.textures = textures;
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Bibliognost/0.2");
        Directory.CreateDirectory(cacheDirectory);
    }

    public ISharedImmediateTexture? Get(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        var path = PathFor(url);
        if (File.Exists(path)) return textures.GetFromFile(path);
        pending.GetOrAdd(url, DownloadAsync);
        return null;
    }

    private async Task<string?> DownloadAsync(string url)
    {
        var path = PathFor(url);
        try
        {
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, shutdown.Token);
            response.EnsureSuccessStatusCode();
            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (mediaType is not null && !mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Preview did not return an image.");
            var bytes = await response.Content.ReadAsByteArrayAsync(shutdown.Token);
            if (bytes.Length is 0 or > 15_000_000) return null;
            var temp = path + ".tmp";
            await File.WriteAllBytesAsync(temp, bytes, shutdown.Token);
            File.Move(temp, path, true);
            return path;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException or InvalidDataException)
        {
            Plugin.Log.Debug($"Preview cache miss for {new Uri(url).Host}: {ex.Message}");
            return null;
        }
        finally { pending.TryRemove(url, out _); }
    }

    private string PathFor(string url)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url))).ToLowerInvariant();
        return Path.Combine(cacheDirectory, hash + ".image");
    }

    public void Dispose() { shutdown.Cancel(); shutdown.Dispose(); http.Dispose(); }
}
