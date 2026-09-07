using System.Numerics;
using System.Text;
using Bibliognost.Models;
using Bibliognost.Providers;
using Bibliognost.Downloads;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace Bibliognost.UI;

public sealed class FeatureHubWindow : Window
{
    private readonly Plugin plugin;
    private bool processing;
    private string status = "Queue mods while browsing, revisit searches, and inspect provider health.";

    public FeatureHubWindow(Plugin plugin) : base("Bibliognost — Library Tools###BibliognostFeatureHub")
    {
        this.plugin = plugin;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(720, 560), MaximumSize = new Vector2(1500, 1600) };
    }

    public override void Draw()
    {
        ImGui.SetWindowFontScale(Math.Clamp(plugin.Configuration.UiScale, .85f, 1.40f));
        MainWindow.DrawArchiveHeader(plugin, "QUEUE · WATCH · DIAGNOSE", "feature-hub-banner");
        ImGui.TextColored(BibliognostTheme.Dim, status);
        if (!ImGui.BeginTabBar("feature-tabs")) return;
        if (ImGui.BeginTabItem($"INSTALL QUEUE ({plugin.Configuration.InstallationQueue.Count})")) { DrawQueue(); ImGui.EndTabItem(); }
        if (ImGui.BeginTabItem("HISTORY")) { DrawHistory(); ImGui.EndTabItem(); }
        if (ImGui.BeginTabItem($"WATCHLISTS ({plugin.Configuration.SavedSearches.Sum(x => x.NewResultCount)})")) { DrawSearches(); ImGui.EndTabItem(); }
        if (ImGui.BeginTabItem("PROVIDER HEALTH")) { DrawHealth(); ImGui.EndTabItem(); }
        ImGui.EndTabBar();
    }

    private void DrawQueue()
    {
        ImGui.TextWrapped("Queued items are reviewed here and installed one at a time. Bibliognost still validates every package and never installs in the background.");
        if (!processing && plugin.Configuration.InstallationQueue.Count > 0 && BibliognostTheme.AccentButton("process-queue", "PROCESS QUEUE", new Vector2(155, 30))) _ = ProcessQueueAsync();
        foreach (var mod in plugin.Configuration.InstallationQueue.ToArray())
        {
            ImGui.PushID("queue-" + mod.ProviderId + mod.RemoteId);
            ImGui.BeginChild("queued", new Vector2(0, 78), true);
            ImGui.TextColored(BibliognostTheme.Text, mod.Name);
            ImGui.TextColored(BibliognostTheme.Dim, $"{mod.Author} · {ProviderLabel(mod.ProviderId)} · {(mod.Version.Length == 0 ? "version unknown" : mod.Version)}");
            if (!processing && BibliognostTheme.AccentButton("review", "REVIEW", new Vector2(90, 26))) { plugin.Main.OpenMod(mod); IsOpen = false; }
            ImGui.SameLine();
            if (!processing && BibliognostTheme.AccentButton("remove", "REMOVE", new Vector2(90, 26))) { plugin.Configuration.InstallationQueue.Remove(mod); plugin.Configuration.Save(); }
            ImGui.EndChild(); ImGui.PopID();
        }
        if (plugin.Configuration.InstallationQueue.Count == 0) ImGui.TextColored(BibliognostTheme.Dim, "The installation queue is empty.");
    }

    private async Task ProcessQueueAsync()
    {
        processing = true;
        try
        {
            foreach (var mod in plugin.Configuration.InstallationQueue.ToArray())
            {
                status = $"Preparing {mod.Name}…";
                var result = await plugin.Catalog.GetDetailsAsync(mod);
                if (!result.Success || result.Value is null) { status = result.Error ?? $"Could not read {mod.Name}."; break; }
                var details = result.Value;
                if (!details.IsDirectDownload) { status = $"{mod.Name} requires its provider's own installation flow. Open it to continue."; break; }
                await plugin.DeliverAsync(details, true);
                if (plugin.Delivery.State != DeliveryState.Complete) { status = plugin.Delivery.Status; break; }
                plugin.Configuration.InstallationQueue.Remove(mod); plugin.Configuration.Save();
            }
            if (plugin.Configuration.InstallationQueue.Count == 0) status = "Queue complete.";
        }
        finally { processing = false; }
    }

    private void DrawHistory()
    {
        ImGui.TextColored(BibliognostTheme.Gold, "INSTALLATION RECEIPTS");
        foreach (var receipt in plugin.Configuration.InstalledModReceipts.OrderByDescending(x => x.InstalledAt))
            ImGui.BulletText($"{receipt.InstalledAt.ToLocalTime():g} · {receipt.Name} · {receipt.InstalledVersion} · {ProviderLabel(receipt.ProviderId)}");
        ImGui.Separator(); ImGui.TextColored(BibliognostTheme.Gold, "RECENT DELIVERY EVENTS");
        foreach (var entry in plugin.Configuration.DeliveryHistory) ImGui.TextWrapped(entry);
    }

    private void DrawSearches()
    {
        ImGui.TextWrapped("Saved searches are manual watchlists. Refresh one to discover changes; Bibliognost will not poll providers in the background.");
        foreach (var saved in plugin.Configuration.SavedSearches.ToArray())
        {
            ImGui.PushID("saved-" + saved.Name);
            ImGui.BeginChild("watch", new Vector2(0, 92), true);
            ImGui.TextColored(saved.NewResultCount > 0 ? BibliognostTheme.GoldBright : BibliognostTheme.Text, saved.Name + (saved.NewResultCount > 0 ? $" · {saved.NewResultCount} NEW" : ""));
            ImGui.TextColored(BibliognostTheme.Dim, $"{saved.SearchText} · last checked {(saved.LastChecked?.ToLocalTime().ToString("g") ?? "never")}");
            if (BibliognostTheme.AccentButton("open", "OPEN", new Vector2(80, 26))) { plugin.Main.ApplySavedSearch(saved); IsOpen = false; }
            ImGui.SameLine(); if (BibliognostTheme.AccentButton("refresh", "REFRESH", new Vector2(95, 26))) _ = RefreshWatchAsync(saved);
            ImGui.SameLine(); if (BibliognostTheme.AccentButton("delete", "DELETE", new Vector2(90, 26))) { plugin.Configuration.SavedSearches.Remove(saved); plugin.Configuration.Save(); }
            ImGui.EndChild(); ImGui.PopID();
        }
        if (plugin.Configuration.SavedSearches.Count == 0) ImGui.TextColored(BibliognostTheme.Dim, "Save the current catalog search from the main window to create a watchlist.");
    }

    private async Task RefreshWatchAsync(SavedModSearch saved)
    {
        status = $"Refreshing {saved.Name}…";
        var result = await plugin.Catalog.SearchAsync(new ModSearchQuery { SearchText = saved.SearchText, Author = saved.Author, Tags = saved.Tags, Affects = saved.Affects, Sort = (ModSort)saved.Sort, Types = saved.Types, DawntrailCompatibleOnly = plugin.Configuration.DawntrailCompatibleOnly }, (ProviderSelection)saved.Provider);
        if (!result.Success || result.Value is null) { status = result.Error ?? "Watchlist refresh failed."; return; }
        var first = result.Value.FirstOrDefault(); var firstKey = first is null ? string.Empty : $"{first.ProviderId}:{first.RemoteId}";
        saved.NewResultCount = saved.LastTopResultKey.Length > 0 && firstKey != saved.LastTopResultKey ? result.Value.TakeWhile(x => $"{x.ProviderId}:{x.RemoteId}" != saved.LastTopResultKey).Count() : 0;
        saved.LastTopResultKey = firstKey; saved.LastChecked = DateTimeOffset.Now; plugin.Configuration.Save();
        status = saved.NewResultCount > 0 ? $"{saved.Name}: {saved.NewResultCount} new result(s)." : $"{saved.Name}: no new top results.";
    }

    private void DrawHealth()
    {
        ImGui.TextColored(BibliognostTheme.Gold, "CAPABILITY STATUS");
        HealthLine("Heliosphere catalog", plugin.Catalog.Diagnostics.Any(x => x.ProviderId == "heliosphere" && x.Error is null), "Public catalog; no sign-in required");
        HealthLine("XMA authentication", !string.IsNullOrWhiteSpace(plugin.Configuration.EncryptedXmaSession), "Optional saved session");
        HealthLine("Nexus authentication", !string.IsNullOrWhiteSpace(plugin.Configuration.EncryptedNexusApiKey), "Personal API key");
        HealthLine("Penumbra delivery", plugin.IsPenumbraLoaded, plugin.IsPenumbraLoaded ? "Plugin available" : "Install/enable Penumbra for direct imports");
        HealthLine("Last delivery", plugin.Delivery.State != DeliveryState.Failed, plugin.Delivery.Status);
        ImGui.Separator(); ImGui.TextColored(BibliognostTheme.Gold, "CATALOG REQUESTS");
        foreach (var item in plugin.Catalog.Diagnostics)
        {
            var healthy = item.Error is null;
            ImGui.TextColored(healthy ? new Vector4(.42f, .90f, .60f, 1) : new Vector4(1f, .42f, .35f, 1), healthy ? "HEALTHY" : "ERROR");
            ImGui.SameLine(); ImGui.Text(item.DisplayName);
            ImGui.TextColored(BibliognostTheme.Dim, $"Catalog: {item.ResultCount} results · {(item.FromCache ? "cache" : $"{item.Duration.TotalSeconds:F1}s")} · last attempt {item.LastAttempt.ToLocalTime():g}");
            if (!healthy) ImGui.TextWrapped(item.Error);
        }
        if (BibliognostTheme.AccentButton("refresh", "FORCE CATALOG REFRESH", new Vector2(205, 29))) plugin.Main.ForceRefresh();
        ImGui.SameLine(); if (BibliognostTheme.AccentButton("export", "EXPORT SAFE REPORT", new Vector2(185, 29))) ExportDiagnostics();
        ImGui.TextColored(BibliognostTheme.Dim, "The report excludes cookies, API keys, download URLs, and local mod contents.");
    }

    private static void HealthLine(string label, bool healthy, string detail)
    {
        ImGui.TextColored(healthy ? new Vector4(.42f, .90f, .60f, 1) : new Vector4(1f, .68f, .25f, 1), healthy ? "●" : "○");
        ImGui.SameLine(); ImGui.Text(label); ImGui.SameLine(); ImGui.TextColored(BibliognostTheme.Dim, detail);
    }

    private void ExportDiagnostics()
    {
        try
        {
            var directory = string.IsNullOrWhiteSpace(plugin.Configuration.DownloadDirectory) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads") : plugin.Configuration.DownloadDirectory;
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, $"Bibliognost-Diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            var lines = new List<string> { $"Bibliognost {typeof(Plugin).Assembly.GetName().Version}", $"Generated {DateTimeOffset.Now:u}", $"OS {Environment.OSVersion}", $"Receipts {plugin.Configuration.InstalledModReceipts.Count}; Queue {plugin.Configuration.InstallationQueue.Count}; Saved searches {plugin.Configuration.SavedSearches.Count}" };
            lines.AddRange(plugin.Catalog.Diagnostics.Select(x => $"{x.DisplayName}: {(x.Error is null ? "OK" : "ERROR")} results={x.ResultCount} cached={x.FromCache} duration={x.Duration.TotalSeconds:F1}s lastSuccess={x.LastSuccess:u} error={x.Error}"));
            File.WriteAllLines(path, lines, Encoding.UTF8); status = $"Safe diagnostic report saved to {path}";
        }
        catch (Exception ex) { status = $"Could not export the report: {ex.Message}"; }
    }

    private static string ProviderLabel(string id) => id switch { "heliosphere" => "Heliosphere", "nexusmods" => "Nexus Mods", _ => "XIV Mod Archive" };
}
