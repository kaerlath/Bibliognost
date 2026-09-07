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
    private readonly SemaphoreSlim downloadSlots = new(4);

    public ThumbnailCache(string cacheDirectory, ITextureProvider textures)
    {
        this.cacheDirectory = cacheDirectory;
        this.textures = textures;
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Bibliognost/0.2");
        Directory.CreateDirectory(cacheDirectory);
        PruneCache();
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
        var acquired = false;
        try
        {
            await downloadSlots.WaitAsync(shutdown.Token); acquired = true;
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
        finally { if (acquired) downloadSlots.Release(); pending.TryRemove(url, out _); }
    }

    private string PathFor(string url)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url))).ToLowerInvariant();
        return Path.Combine(cacheDirectory, hash + ".image");
    }

    private void PruneCache()
    {
        try
        {
            var files = new DirectoryInfo(cacheDirectory).EnumerateFiles("*.image").OrderByDescending(file => file.LastWriteTimeUtc).ToArray();
            long retained = 0;
            foreach (var file in files)
            {
                retained += file.Length;
                if (file.LastWriteTimeUtc < DateTime.UtcNow.AddDays(-30) || retained > 512L * 1024 * 1024) file.Delete();
            }
        }
        catch (IOException) { }
    }

    public void Dispose() { shutdown.Cancel(); shutdown.Dispose(); downloadSlots.Dispose(); http.Dispose(); }
}
