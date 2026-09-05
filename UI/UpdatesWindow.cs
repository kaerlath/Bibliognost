using System.Numerics;
using Bibliognost.Models;
using Bibliognost.Providers;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace Bibliognost.UI;

internal enum UpdateState { UpToDate, UpdateAvailable, VersionUnclear, Ignored, MissingLocally, SourceUnavailable }
internal sealed record UpdateCheck(InstalledModReceipt Receipt, ModDetails? Remote, UpdateState State, string Message);
internal sealed record LegacyMatch(string PenumbraName, ModSummary Candidate, float Confidence);

public sealed class UpdatesWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private readonly List<UpdateCheck> checks = [];
    private readonly List<LegacyMatch> legacyMatches = [];
    private CancellationTokenSource? cancellation;
    private float progress;
    private string status = "Choose Quick Scan to check linked mods. Nothing is downloaded during a scan.";
    private bool busy;

    public UpdatesWindow(Plugin plugin) : base("Bibliognost — Mod Updates###BibliognostUpdates")
    {
        this.plugin = plugin;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(720, 560), MaximumSize = new Vector2(1400, 1600) };
    }

    public override void Draw()
    {
        MainWindow.DrawArchiveHeader(plugin, "OBSERVE · COMPARE · DECIDE", "updates-banner");
        ImGui.TextColored(BibliognostTheme.GoldBright, "MANUAL UPDATE CENTRE");
        ImGui.TextWrapped("Bibliognost never updates automatically. Scans compare metadata only; every download and installation still requires your approval.");
        ImGui.Spacing();
        if (!busy && BibliognostTheme.AccentButton("quick-scan", "QUICK SCAN", new Vector2(145, 32))) _ = QuickScanAsync();
        ImGui.SameLine();
        if (!busy && BibliognostTheme.AccentButton("discover-legacy", "DISCOVER LEGACY SOURCES", new Vector2(220, 32))) _ = DiscoverLegacyAsync();
        if (busy)
        {
            ImGui.ProgressBar(progress, new Vector2(Math.Max(220, ImGui.GetContentRegionAvail().X - 110), 26), status);
            ImGui.SameLine(); if (BibliognostTheme.AccentButton("cancel-scan", "CANCEL", new Vector2(90, 28))) cancellation?.Cancel();
        }
        else ImGui.TextColored(BibliognostTheme.Dim, status);
        ImGui.Separator();

        foreach (var check in checks.ToArray()) DrawCheck(check);
        if (legacyMatches.Count > 0)
        {
            ImGui.TextColored(BibliognostTheme.Gold, "POSSIBLE SOURCES FOR UNLINKED PENUMBRA MODS");
            foreach (var match in legacyMatches.ToArray()) DrawLegacyMatch(match);
        }
    }

    private void DrawCheck(UpdateCheck check)
    {
        ImGui.PushID(check.Receipt.ProviderId + check.Receipt.RemoteId);
        ImGui.BeginChild("update-card", new Vector2(0, 116), true);
        var color = check.State == UpdateState.UpdateAvailable ? new Vector4(.98f, .68f, .22f, 1) : check.State == UpdateState.UpToDate ? new Vector4(.4f, .88f, .58f, 1) : BibliognostTheme.Dim;
        ImGui.TextColored(color, StateLabel(check.State)); ImGui.SameLine(); ImGui.Text(check.Receipt.Name);
        ImGui.TextColored(BibliognostTheme.Dim, check.Message);
        if (check.Remote is not null && check.State is UpdateState.UpdateAvailable or UpdateState.VersionUnclear)
        {
            if (BibliognostTheme.AccentButton("review", "REVIEW UPDATE", new Vector2(140, 28))) plugin.Main.OpenMod(check.Remote.Summary);
            ImGui.SameLine();
            if (BibliognostTheme.AccentButton("ignore", "IGNORE THIS VERSION", new Vector2(175, 28)))
            { check.Receipt.IgnoredVersion = check.Remote.Summary.Version; plugin.Configuration.Save(); _ = QuickScanAsync(); }
            ImGui.SameLine();
        }
        if (BibliognostTheme.AccentButton("unlink", "UNLINK", new Vector2(90, 28)))
        { plugin.Configuration.InstalledModReceipts.Remove(check.Receipt); plugin.Configuration.Save(); checks.Remove(check); ImGui.EndChild(); ImGui.PopID(); return; }
        ImGui.EndChild(); ImGui.PopID();
    }

    private void DrawLegacyMatch(LegacyMatch match)
    {
        ImGui.PushID(match.PenumbraName + match.Candidate.ProviderId + match.Candidate.RemoteId);
        ImGui.BeginChild("legacy-card", new Vector2(0, 112), true);
        ImGui.TextColored(BibliognostTheme.GoldBright, $"{match.Confidence:P0} POSSIBLE MATCH"); ImGui.SameLine(); ImGui.Text(match.PenumbraName);
        ImGui.TextWrapped($"{match.Candidate.Name} by {match.Candidate.Author} · {match.Candidate.ProviderId}");
        if (BibliognostTheme.AccentButton("link", "LINK THIS SOURCE", new Vector2(155, 28)))
        {
            plugin.Configuration.InstalledModReceipts.Add(new InstalledModReceipt { ProviderId = match.Candidate.ProviderId, RemoteId = match.Candidate.RemoteId, Name = match.PenumbraName, Author = match.Candidate.Author, PageUrl = match.Candidate.PageUrl, InstalledAt = DateTimeOffset.Now });
            plugin.Configuration.Save(); legacyMatches.Remove(match);
        }
        ImGui.SameLine(); if (BibliognostTheme.AccentButton("dismiss", "NOT A MATCH", new Vector2(130, 28))) legacyMatches.Remove(match);
        ImGui.EndChild(); ImGui.PopID();
    }

    private async Task QuickScanAsync()
    {
        BeginScan(); checks.Clear(); legacyMatches.Clear();
        try
        {
            var receipts = plugin.Configuration.InstalledModReceipts.ToArray();
            var inventoryResult = PenumbraInventory();
            if (!inventoryResult.Available) { status = "Penumbra is not available. Install or enable Penumbra, then scan again."; return; }
            var inventory = inventoryResult.Names;
            for (var i = 0; i < receipts.Length; i++)
            {
                cancellation!.Token.ThrowIfCancellationRequested(); var receipt = receipts[i];
                status = $"Checking {receipt.Name}…"; progress = receipts.Length == 0 ? 1 : i / (float)receipts.Length;
                var local = inventory.Any(name => SimilarName(name, receipt.Name) >= .72f);
                var result = await plugin.Catalog.GetDetailsAsync(receipt.ProviderId, receipt.RemoteId, cancellation.Token);
                if (!result.Success || result.Value is null) checks.Add(new(receipt, null, UpdateState.SourceUnavailable, result.Error ?? "The source did not respond."));
                else checks.Add(Compare(receipt, result.Value, local));
            }
            progress = 1; status = receipts.Length == 0 ? "No linked mods yet. Use Discover Legacy Sources or install through Bibliognost." : $"Scan complete: {checks.Count(item => item.State == UpdateState.UpdateAvailable)} update(s) available.";
        }
        catch (OperationCanceledException) { status = "Update scan cancelled."; }
        finally { EndScan(); }
    }

    private async Task DiscoverLegacyAsync()
    {
        BeginScan(); legacyMatches.Clear();
        try
        {
            var linked = plugin.Configuration.InstalledModReceipts.Select(item => item.Name).ToArray();
            var inventoryResult = PenumbraInventory();
            if (!inventoryResult.Available) { status = "Penumbra is not available. Install or enable Penumbra, then try again."; return; }
            var inventory = inventoryResult.Names.Where(name => linked.All(existing => SimilarName(name, existing) < .72f)).ToArray();
            for (var i = 0; i < inventory.Length; i++)
            {
                cancellation!.Token.ThrowIfCancellationRequested(); var name = inventory[i];
                status = $"Looking for {name}…"; progress = inventory.Length == 0 ? 1 : i / (float)inventory.Length;
                var result = await plugin.Catalog.SearchAsync(new ModSearchQuery { Name = name, Sort = ModSort.Name, DawntrailCompatibleOnly = false }, ProviderSelection.All, cancellation.Token);
                var best = result.Value?.Select(candidate => (candidate, score: SimilarName(name, candidate.Name))).OrderByDescending(item => item.score).FirstOrDefault();
                if (best is { candidate: not null, score: >= .58f }) legacyMatches.Add(new(name, best.Value.candidate, best.Value.score));
            }
            progress = 1; status = $"Discovery complete: {legacyMatches.Count} possible source link(s) require review.";
        }
        catch (OperationCanceledException) { status = "Legacy discovery cancelled."; }
        finally { EndScan(); }
    }

    private void BeginScan() { cancellation?.Dispose(); cancellation = new(); busy = true; progress = 0; }
    private void EndScan() { busy = false; cancellation?.Dispose(); cancellation = null; }
    private static UpdateCheck Compare(InstalledModReceipt receipt, ModDetails remote, bool local)
    {
        if (!local) return new(receipt, remote, UpdateState.MissingLocally, "The linked mod was not found in Penumbra.");
        var installed = receipt.InstalledVersion; var available = remote.Summary.Version;
        if (available.Length > 0 && available == receipt.IgnoredVersion) return new(receipt, remote, UpdateState.Ignored, $"Version {available} is ignored.");
        if (installed.Length == 0 || available.Length == 0) return new(receipt, remote, UpdateState.VersionUnclear, $"Installed version is {(installed.Length == 0 ? "unknown" : installed)}; provider reports {(available.Length == 0 ? "no version" : available)}.");
        UpdateState state;
        if (VersionsEqual(installed, available)) state = UpdateState.UpToDate;
        else if (TryVersion(installed, out var installedVersion) && TryVersion(available, out var availableVersion))
            state = availableVersion > installedVersion ? UpdateState.UpdateAvailable : UpdateState.UpToDate;
        else state = remote.Summary.UpdatedAt > receipt.InstalledAt.AddMinutes(2) ? UpdateState.UpdateAvailable : UpdateState.VersionUnclear;
        return new(receipt, remote, state, $"Installed {installed} · Available {available}");
    }
    private static bool VersionsEqual(string left, string right) => NormalizeVersion(left) == NormalizeVersion(right);
    private static string NormalizeVersion(string value) => value.Trim().TrimStart('v', 'V').Replace(" ", string.Empty).ToLowerInvariant();
    private static bool TryVersion(string value, out Version version)
    {
        var clean = NormalizeVersion(value).Split(['-', '+'], 2)[0];
        return Version.TryParse(clean, out version!);
    }
    private static (bool Available, IReadOnlyCollection<string> Names) PenumbraInventory()
    {
        try { return (true, Plugin.PluginInterface.GetIpcSubscriber<Dictionary<string, string>>("Penumbra.GetModList").InvokeFunc().Values.ToArray()); }
        catch { return (false, []); }
    }
    private static float SimilarName(string left, string right)
    {
        static HashSet<string> Words(string value) => new string(value.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray()).Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(word => word.Length > 1).ToHashSet();
        var a = Words(left); var b = Words(right); var union = a.Union(b).Count(); return union == 0 ? 0 : a.Intersect(b).Count() / (float)union;
    }
    private static string StateLabel(UpdateState state) => state switch { UpdateState.UpToDate => "UP TO DATE", UpdateState.UpdateAvailable => "UPDATE AVAILABLE", UpdateState.VersionUnclear => "REVIEW NEEDED", UpdateState.Ignored => "IGNORED", UpdateState.MissingLocally => "NOT FOUND LOCALLY", _ => "SOURCE UNAVAILABLE" };
    public void Dispose() { cancellation?.Cancel(); cancellation?.Dispose(); }
}
