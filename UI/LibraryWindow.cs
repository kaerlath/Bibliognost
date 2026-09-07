using System.Numerics;
using Bibliognost.Models;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace Bibliognost.UI;

public sealed class LibraryWindow : Window
{
    private readonly Plugin plugin;
    private string newCollection = string.Empty;
    private int selectedCollection = -2; // -2 favorites, -1 viewed, 0+ named collections
    private ModSummary? pendingMod;

    public LibraryWindow(Plugin plugin) : base("Bibliognost — Library###BibliognostLibrary")
    {
        this.plugin = plugin;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(680, 520), MaximumSize = new Vector2(1300, 1500) };
    }

    internal void ShowFor(ModSummary? mod = null)
    {
        pendingMod = mod;
        IsOpen = true;
    }

    public override void Draw()
    {
        ImGui.SetWindowFontScale(Math.Clamp(plugin.Configuration.UiScale, .85f, 1.40f));
        MainWindow.DrawArchiveHeader(plugin, "CURATE · ORGANIZE · RETURN", "library-banner");
        ImGui.TextColored(BibliognostTheme.GoldBright, "PERSONAL MOD LIBRARY");
        ImGui.TextWrapped("Collections are stored locally in Bibliognost. Saving a listing never downloads or installs it.");
        ImGui.SetNextItemWidth(Math.Max(180, ImGui.GetContentRegionAvail().X - 155));
        ImGui.InputTextWithHint("##new-collection", "New collection name…", ref newCollection, 60);
        ImGui.SameLine();
        if (BibliognostTheme.AccentButton("create-collection", "CREATE", new Vector2(125, 28))) CreateCollection();

        if (!ImGui.BeginTable("library-layout", 2, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.BordersInnerV)) return;
        ImGui.TableSetupColumn("collections", ImGuiTableColumnFlags.WidthFixed, 210);
        ImGui.TableSetupColumn("entries", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableNextColumn();
        CollectionChoice(-2, $"★  Favorites ({plugin.Configuration.FavoriteMods.Count})");
        CollectionChoice(-1, $"◷  Recently Viewed ({plugin.Configuration.RecentlyViewedMods.Count})");
        ImGui.Separator();
        for (var i = 0; i < plugin.Configuration.ModCollections.Count; i++)
            CollectionChoice(i, $"{plugin.Configuration.ModCollections[i].Name} ({plugin.Configuration.ModCollections[i].Mods.Count})");

        ImGui.TableNextColumn();
        DrawActiveCollection();
        ImGui.EndTable();
    }

    private void CollectionChoice(int index, string label)
    {
        if (ImGui.Selectable(label, selectedCollection == index)) selectedCollection = index;
    }

    private void DrawActiveCollection()
    {
        var entries = ActiveEntries();
        var title = selectedCollection switch
        {
            -2 => "FAVORITES",
            -1 => "RECENTLY VIEWED",
            _ when selectedCollection >= 0 && selectedCollection < plugin.Configuration.ModCollections.Count => plugin.Configuration.ModCollections[selectedCollection].Name.ToUpperInvariant(),
            _ => "COLLECTION",
        };
        ImGui.TextColored(BibliognostTheme.Gold, title);
        if (pendingMod is not null && selectedCollection != -1)
        {
            if (BibliognostTheme.AccentButton("add-pending", $"ADD {Fit(pendingMod.Name, 28)}", new Vector2(Math.Min(330, ImGui.GetContentRegionAvail().X), 29)))
                AddPending(entries);
            ImGui.Spacing();
        }
        if (selectedCollection >= 0 && BibliognostTheme.AccentButton("delete-collection", "DELETE COLLECTION", new Vector2(170, 27)))
        {
            plugin.Configuration.ModCollections.RemoveAt(selectedCollection);
            selectedCollection = -2; plugin.Configuration.Save(); return;
        }
        ImGui.BeginChild("library-entries", new Vector2(0, 0), false);
        foreach (var mod in entries.ToArray())
        {
            ImGui.PushID(mod.ProviderId + mod.RemoteId);
            ImGui.BeginChild("entry", new Vector2(0, 82), true);
            ImGui.TextColored(BibliognostTheme.Text, mod.Name);
            ImGui.TextColored(BibliognostTheme.Dim, $"by {(mod.Author.Length == 0 ? "Unknown" : mod.Author)} · {mod.ProviderId}");
            if (BibliognostTheme.AccentButton("open", "OPEN", new Vector2(82, 26))) { plugin.Main.OpenMod(mod); IsOpen = false; }
            if (selectedCollection != -1)
            {
                ImGui.SameLine();
                if (BibliognostTheme.AccentButton("remove", "REMOVE", new Vector2(92, 26))) { entries.Remove(mod); plugin.Configuration.Save(); }
            }
            ImGui.EndChild(); ImGui.PopID();
        }
        if (entries.Count == 0) ImGui.TextColored(BibliognostTheme.Dim, "This collection is empty.");
        ImGui.EndChild();
    }

    private List<ModSummary> ActiveEntries() => selectedCollection switch
    {
        -2 => plugin.Configuration.FavoriteMods,
        -1 => plugin.Configuration.RecentlyViewedMods,
        _ when selectedCollection >= 0 && selectedCollection < plugin.Configuration.ModCollections.Count => plugin.Configuration.ModCollections[selectedCollection].Mods,
        _ => plugin.Configuration.FavoriteMods,
    };

    private void AddPending(List<ModSummary> entries)
    {
        if (pendingMod is null) return;
        entries.RemoveAll(item => Key(item) == Key(pendingMod));
        entries.Insert(0, pendingMod); plugin.Configuration.Save();
    }

    private void CreateCollection()
    {
        var name = newCollection.Trim();
        if (name.Length == 0 || plugin.Configuration.ModCollections.Any(item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) return;
        plugin.Configuration.ModCollections.Add(new ModCollection { Name = name });
        selectedCollection = plugin.Configuration.ModCollections.Count - 1;
        newCollection = string.Empty; plugin.Configuration.Save();
    }

    private static string Key(ModSummary mod) => $"{mod.ProviderId}:{mod.RemoteId}";
    private static string Fit(string value, int length) => value.Length <= length ? value : value[..Math.Max(1, length - 1)] + "…";
}
