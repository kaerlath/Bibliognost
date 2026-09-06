using System.Net.Http.Headers;
using System.IO.Compression;
using Bibliognost.Models;

namespace Bibliognost.Downloads;

internal enum DeliveryState { Idle, Downloading, Installing, Complete, Failed, Cancelled }

internal sealed class ModDeliveryService : IDisposable
{
    private const long MaximumBytes = 4L * 1024 * 1024 * 1024;
    private static readonly string[] InstallableExtensions = [".ttmp", ".ttmp2", ".pmp"];
    private readonly HttpClient client = new() { Timeout = TimeSpan.FromMinutes(30) };
    private readonly Configuration configuration;
    private CancellationTokenSource? cancellation;
    private DateTimeOffset penumbraCacheTime;
    private IReadOnlyCollection<string> penumbraNames = [];
    internal DeliveryState State { get; private set; }
    internal float Progress { get; private set; }
    internal string Status { get; private set; } = "Ready.";
    internal bool Busy => State is DeliveryState.Downloading or DeliveryState.Installing;
    internal ModDetails? LastDetails { get; private set; }
    internal bool LastInstallIntent { get; private set; }

    internal ModDeliveryService(Configuration configuration) => this.configuration = configuration;

    internal static bool CanInstall(ModDetails details) => details.IsDirectDownload && IsInstallable(details.DownloadFileName ?? details.DownloadUrl);
    internal static bool HasUnknownFileType(ModDetails details)
    {
        var extension = Path.GetExtension((details.DownloadFileName ?? details.DownloadUrl)?.Split('?', '#')[0] ?? "");
        return details.IsDirectDownload && (string.IsNullOrWhiteSpace(extension) || extension.Equals(".zip", StringComparison.OrdinalIgnoreCase));
    }

    internal bool AppearsInstalled(string modName)
    {
        if (DateTimeOffset.Now - penumbraCacheTime > TimeSpan.FromSeconds(8))
        {
            try
            {
                penumbraNames = Plugin.PluginInterface.GetIpcSubscriber<Dictionary<string, string>>("Penumbra.GetModList").InvokeFunc().Values.ToArray();
                penumbraCacheTime = DateTimeOffset.Now;
            }
            catch { penumbraNames = []; }
        }
        var needle = NormalizeName(modName);
        return needle.Length >= 5 && penumbraNames.Any(name => { var candidate = NormalizeName(name); return candidate.Contains(needle, StringComparison.Ordinal) || needle.Contains(candidate, StringComparison.Ordinal); });
    }

    internal async Task DeliverAsync(ModDetails details, bool install, Func<string, CancellationToken, Task<HttpResponseMessage>>? authenticatedDownload = null)
    {
        if (Busy || string.IsNullOrWhiteSpace(details.DownloadUrl)) return;
        LastDetails = details; LastInstallIntent = install;
        cancellation = new CancellationTokenSource();
        try
        {
            State = DeliveryState.Downloading; Progress = 0; Status = "Preparing authorized download…";
            var directory = string.IsNullOrWhiteSpace(configuration.DownloadDirectory)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")
                : Path.GetFullPath(Environment.ExpandEnvironmentVariables(configuration.DownloadDirectory));
            Directory.CreateDirectory(directory);
            using var response = authenticatedDownload is null
                ? await client.GetAsync(details.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellation.Token)
                : await authenticatedDownload(details.DownloadUrl, cancellation.Token);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentType?.MediaType?.Contains("html", StringComparison.OrdinalIgnoreCase) == true)
                throw new InvalidDataException("The provider returned a web page instead of a downloadable file. Sign-in may have expired.");
            var length = response.Content.Headers.ContentLength;
            if (length > MaximumBytes) throw new InvalidDataException("The package exceeds Bibliognost's 4 GB safety limit.");
            var name = SafeFileName(details.DownloadFileName ?? HeaderName(response.Content.Headers.ContentDisposition) ?? Path.GetFileName(response.RequestMessage?.RequestUri?.LocalPath));
            if (string.IsNullOrWhiteSpace(Path.GetExtension(name))) name += ".download";
            var path = UniquePath(directory, name);
            await using (var input = await response.Content.ReadAsStreamAsync(cancellation.Token))
            await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                var buffer = new byte[81920]; long received = 0;
                while (true)
                {
                    var count = await input.ReadAsync(buffer, cancellation.Token); if (count == 0) break;
                    received += count; if (received > MaximumBytes) throw new InvalidDataException("The package exceeded Bibliognost's 4 GB safety limit.");
                    await output.WriteAsync(buffer.AsMemory(0, count), cancellation.Token);
                    Progress = length > 0 ? Math.Clamp((float)received / length.Value, 0, 1) : 0;
                    Status = length > 0 ? $"Downloading… {Progress:P0}" : $"Downloading… {received / 1048576f:F1} MB";
                }
            }
            if (new FileInfo(path).Length == 0) throw new InvalidDataException("The provider returned an empty file.");
            if (install)
            {
                var package = InspectPackage(path);
                if (!package.Installable)
                {
                    Status = $"Saved to Downloads: {Path.GetFileName(path)}. {package.Message}";
                    Progress = 1; State = DeliveryState.Complete;
                    Remember($"{DateTimeOffset.Now:u}  DOWNLOADED  {details.Summary.Name}  [{details.Summary.ProviderId}]  {Status}");
                    return;
                }
                State = DeliveryState.Installing; Status = "Handing the validated package to Penumbra…";
                var result = Plugin.PluginInterface.GetIpcSubscriber<string, int>("Penumbra.InstallMod.V5").InvokeFunc(path);
                if (result != 0) throw new InvalidOperationException($"Penumbra declined the package (code {result}).");
                if (configuration.KeepDownloadedPackages) Status = $"Installed through Penumbra. A copy remains in Downloads: {Path.GetFileName(path)}";
                else { File.Delete(path); Status = "Installed through Penumbra; the temporary package was removed."; }
                RememberInstallation(details);
            }
            else Status = $"Saved to Downloads: {Path.GetFileName(path)}";
            Progress = 1; State = DeliveryState.Complete;
            Remember($"{DateTimeOffset.Now:u}  SUCCESS  {details.Summary.Name}  [{details.Summary.ProviderId}]  {Status}");
        }
        catch (OperationCanceledException) { State = DeliveryState.Cancelled; Status = "Download cancelled."; }
        catch (Exception ex) { State = DeliveryState.Failed; Status = ex.Message; Remember($"{DateTimeOffset.Now:u}  FAILED  {details.Summary.Name}  [{details.Summary.ProviderId}]  {Status}"); Plugin.Log.Warning(ex, "Mod delivery failed."); }
        finally { cancellation?.Dispose(); cancellation = null; }
    }

    internal void Cancel() => cancellation?.Cancel();
    private void Remember(string entry)
    {
        configuration.DeliveryHistory.Insert(0, entry);
        if (configuration.DeliveryHistory.Count > 30) configuration.DeliveryHistory.RemoveRange(30, configuration.DeliveryHistory.Count - 30);
        configuration.Save();
    }
    private void RememberInstallation(ModDetails details)
    {
        var receipt = configuration.InstalledModReceipts.FirstOrDefault(item => item.ProviderId == details.Summary.ProviderId && item.RemoteId == details.Summary.RemoteId);
        if (receipt is null) { receipt = new InstalledModReceipt(); configuration.InstalledModReceipts.Add(receipt); }
        receipt.ProviderId = details.Summary.ProviderId; receipt.RemoteId = details.Summary.RemoteId;
        receipt.Name = details.Summary.Name; receipt.Author = details.Summary.Author;
        receipt.InstalledVersion = details.Summary.Version; receipt.PageUrl = details.Summary.PageUrl;
        receipt.InstalledAt = DateTimeOffset.Now; receipt.IgnoredVersion = string.Empty;
        configuration.Save();
    }
    private static bool IsInstallable(string? value) => InstallableExtensions.Contains(Path.GetExtension(value?.Split('?', '#')[0] ?? ""), StringComparer.OrdinalIgnoreCase);
    private static string NormalizeName(string value) => new(value.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
    private static bool HasZipSignature(string path)
    {
        Span<byte> signature = stackalloc byte[4];
        using var stream = File.OpenRead(path);
        return stream.Read(signature) == 4 && signature[0] == (byte)'P' && signature[1] == (byte)'K'
            && ((signature[2] == 3 && signature[3] == 4) || (signature[2] == 5 && signature[3] == 6) || (signature[2] == 7 && signature[3] == 8));
    }
    private static (bool Installable, string Message) InspectPackage(string path)
    {
        if (!HasZipSignature(path)) return (false, "The file is not a valid ZIP-based Penumbra or TexTools package, so it was not sent to Penumbra.");
        try
        {
            using var archive = ZipFile.OpenRead(path);
            var names = archive.Entries.Select(entry => entry.FullName.Replace('\\', '/').TrimStart('/')).ToArray();
            var hasPenumbraManifest = names.Any(name => name.Equals("meta.json", StringComparison.OrdinalIgnoreCase));
            var hasTexToolsManifest = names.Any(name => Path.GetFileName(name).Equals("TTMPD.mpl", StringComparison.OrdinalIgnoreCase));
            return hasPenumbraManifest || hasTexToolsManifest
                ? (true, "Validated Penumbra-compatible package.")
                : (false, "This is a general ZIP archive and contains neither a root meta.json nor a TexTools TTMPD.mpl manifest, so it was not sent to Penumbra.");
        }
        catch (InvalidDataException)
        {
            return (false, "The archive is damaged or uses an unsupported format, so it was not sent to Penumbra.");
        }
    }
    private static string? HeaderName(ContentDispositionHeaderValue? header) => header?.FileNameStar?.Trim('"') ?? header?.FileName?.Trim('"');
    private static string SafeFileName(string? value)
    {
        var name = string.IsNullOrWhiteSpace(value) ? "Bibliognost-download" : Path.GetFileName(value);
        foreach (var invalid in Path.GetInvalidFileNameChars()) name = name.Replace(invalid, '_');
        return name;
    }
    private static string UniquePath(string directory, string name)
    {
        var path = Path.Combine(directory, name); if (!File.Exists(path)) return path;
        var stem = Path.GetFileNameWithoutExtension(name); var extension = Path.GetExtension(name);
        for (var i = 2; ; i++) { path = Path.Combine(directory, $"{stem} ({i}){extension}"); if (!File.Exists(path)) return path; }
    }
    public void Dispose() { cancellation?.Cancel(); cancellation?.Dispose(); client.Dispose(); }
}
