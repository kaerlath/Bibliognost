using System.Diagnostics;
using System.Numerics;
using Bibliognost.Models;
using Bibliognost.Downloads;
using Bibliognost.Providers;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace Bibliognost.UI;

public sealed class MainWindow : Window
{
    private readonly Plugin plugin;
    private readonly List<ModSummary> mods = [];
    private string search = string.Empty;
    private string name = string.Empty;
    private string author = string.Empty;
    private string races = string.Empty;
    private string tags = string.Empty;
    private string affects = string.Empty;
    private int gender;
    private int sort = (int)ModSort.Updated;
    private int providerSelection;
    private bool showFilters;
    private readonly HashSet<string> selectedTypes = [];
    private static readonly (string Id, string Label)[] ModTypes =
    [
        ("1", "Gear"), ("2", "Body"), ("3", "Face"), ("4", "Hair"), ("5", "Reshade"),
        ("6", "Other"), ("7", "Minion"), ("8", "Mount"), ("9", "Furniture"), ("10", "Skin"),
        ("12", "Racial Scaling"), ("13", "Pose"), ("14", "VFX"), ("15", "Animation"),
        ("16", "Sound"), ("17", "Dalamud Plugin"), ("18", "Modding Tool"), ("19", "App"),
    ];
    private string status = "Ready to index the archive.";
    private bool loading;
    private int page = 1;
    private int pageInput = 1;
    private string pageInputText = "1";
    private int highestVisitedPage = 1;
    private bool latestReleases;
    private ModDetails? details;
    private IReadOnlyList<ModDetails> sourceDetails = [];
    private ModSummary? selectedSummary;
    private float drawer;
    private bool showDescription;
    private float descriptionExpansion;
    private int selectedImageIndex;
    private float selectionFlash;
    private ModDetails? pendingInstall;

    public MainWindow(Plugin plugin) : base("Bibliognost — The Eorzean Mod Archive###BibliognostMain")
    {
        this.plugin = plugin;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(720, 520), MaximumSize = new Vector2(4096, 4096) };
    }

    public override void OnOpen() { if (mods.Count == 0 && !loading) _ = SearchAsync(); }

    public override void Draw()
    {
        DrawBackdrop();
        DrawArchiveHeader(plugin, "DISCOVER · CATALOGUE · REMEMBER", "main-banner");

        var settingsWidth = 92f;
        ImGui.SetNextItemWidth(Math.Max(220, ImGui.GetContentRegionAvail().X - settingsWidth - 390));
        var previousSearch = search;
        var submit = ImGui.InputTextWithHint("##search", "Search every connected mod archive…", ref search, 180, ImGuiInputTextFlags.EnterReturnsTrue);
        var searchWasCleared = previousSearch.Length > 0 && search.Length == 0;
        ImGui.SameLine();
        ImGui.SetNextItemWidth(166);
        var providerLabels = new[] { "All sources", "XIV Mod Archive", "Heliosphere", "Nexus Mods" };
        if (ImGui.BeginCombo("##provider", providerLabels[providerSelection]))
        {
            for (var i = 0; i < providerLabels.Length; i++) if (ImGui.Selectable(providerLabels[i], providerSelection == i)) { providerSelection = i; latestReleases = false; highestVisitedPage = 1; GoToPage(1); }
            ImGui.EndCombo();
        }
        ImGui.SameLine();
        if (!loading && BibliognostTheme.AccentButton("search", "SEARCH", new Vector2(88, 30))) submit = true;
        ImGui.SameLine();
        if (BibliognostTheme.AccentButton("updates", "UPDATES", new Vector2(92, 30))) plugin.Updates.IsOpen = true;
        ImGui.SameLine();
        if (BibliognostTheme.AccentButton("settings", "SETTINGS", new Vector2(settingsWidth, 30))) plugin.Settings.IsOpen = true;
        if ((submit || searchWasCleared) && !loading) { latestReleases = false; highestVisitedPage = 1; GoToPage(1); }

        if (BibliognostTheme.AccentButton("filters", showFilters ? "HIDE FILTERS" : "FILTERS", new Vector2(112, 27))) showFilters = !showFilters;
        ImGui.SameLine();
        if (!loading && BibliognostTheme.AccentButton("latest-releases", "LATEST RELEASES", new Vector2(160, 27))) ShowLatestReleases();
        ImGui.SameLine();
        if (!loading && BibliognostTheme.AccentButton("recent-updates", "RECENTLY UPDATED", new Vector2(168, 27))) ShowTimeline(ModSort.Updated);
        ImGui.SameLine();
        if (!loading && BibliognostTheme.AccentButton("popular", "POPULAR", new Vector2(100, 27))) ShowTimeline(ModSort.Downloads);
        if (latestReleases) { ImGui.SameLine(); ImGui.TextColored(BibliognostTheme.GoldBright, "TODAY · ALL SOURCES"); }
        if (selectedTypes.Count > 0 || gender > 0 || name.Length > 0 || author.Length > 0 || races.Length > 0 || tags.Length > 0 || affects.Length > 0)
        {
            ImGui.SameLine();
            ImGui.TextColored(BibliognostTheme.Gold, "FILTERED SEARCH");
        }
        if (showFilters) DrawFilters();

        ImGui.TextColored(loading ? BibliognostTheme.Gold : BibliognostTheme.Dim, loading ? "SCANNING ARCHIVE…" : status);
        ImGui.Spacing();
        DrawWorkspace();
    }

    private void DrawFilters()
    {
        ImGui.BeginChild("search-filters", new Vector2(0, 176), true);
        var fieldWidth = Math.Max(150, (ImGui.GetContentRegionAvail().X - 24) / 4);
        ImGui.SetNextItemWidth(fieldWidth); ImGui.InputTextWithHint("##name-filter", "Mod name", ref name, 100);
        ImGui.SameLine(); ImGui.SetNextItemWidth(fieldWidth); ImGui.InputTextWithHint("##author-filter", "Author", ref author, 100);
        ImGui.SameLine(); ImGui.SetNextItemWidth(fieldWidth); ImGui.InputTextWithHint("##race-filter", "Races (e.g. Viera)", ref races, 100);
        ImGui.SameLine(); ImGui.SetNextItemWidth(fieldWidth); ImGui.InputTextWithHint("##affects-filter", "Affects / clothing slot", ref affects, 100);
        ImGui.SetNextItemWidth(fieldWidth); ImGui.InputTextWithHint("##tag-filter", "Tags (e.g. dress)", ref tags, 100);
        ImGui.SameLine(); ImGui.SetNextItemWidth(fieldWidth);
        var genders = new[] { "Any gender", "Male", "Female", "Unisex" };
        if (ImGui.BeginCombo("##gender-filter", genders[gender]))
        {
            for (var i = 0; i < genders.Length; i++) if (ImGui.Selectable(genders[i], gender == i)) gender = i;
            ImGui.EndCombo();
        }
        ImGui.SameLine(); ImGui.SetNextItemWidth(fieldWidth);
        var sorts = new[] { "Newest", "Recently updated", "Most downloaded", "Most viewed", "Name" };
        if (ImGui.BeginCombo("##sort-filter", sorts[sort]))
        {
            for (var i = 0; i < sorts.Length; i++) if (ImGui.Selectable(sorts[i], sort == i)) sort = i;
            ImGui.EndCombo();
        }
        ImGui.TextColored(BibliognostTheme.Dim, "MOD TYPES");
        for (var i = 0; i < ModTypes.Length; i++)
        {
            var type = ModTypes[i];
            var chosen = selectedTypes.Contains(type.Id);
            if (ImGui.Checkbox(type.Label, ref chosen)) { if (chosen) selectedTypes.Add(type.Id); else selectedTypes.Remove(type.Id); }
            if (i == 9) ImGui.NewLine(); else if (i + 1 < ModTypes.Length) ImGui.SameLine();
        }
        ImGui.NewLine();
        if (!loading && BibliognostTheme.AccentButton("apply-filters", "APPLY FILTERS", new Vector2(132, 29))) { latestReleases = false; highestVisitedPage = 1; GoToPage(1); }
        ImGui.SameLine();
        if (BibliognostTheme.AccentButton("clear-filters", "CLEAR", new Vector2(82, 29))) { ClearFilters(); latestReleases = false; highestVisitedPage = 1; GoToPage(1); }
        ImGui.EndChild();
    }

    private void ClearFilters()
    {
        name = author = races = tags = affects = string.Empty;
        gender = 0;
        sort = (int)ModSort.Updated;
        selectedTypes.Clear();
    }

    private void DrawWorkspace()
    {
        var target = details is null ? 0f : 1f;
        drawer += (target - drawer) * Math.Clamp(ImGui.GetIO().DeltaTime * 9f, 0f, 1f);
        var total = ImGui.GetContentRegionAvail().X;
        var drawerWidth = drawer < .01f ? 0f : Math.Clamp(total * .42f, 500f, 900f) * drawer;
        var catalogWidth = Math.Max(300, total - drawerWidth - (drawerWidth > 0 ? 12 : 0));
        if (!ImGui.BeginTable("workspace", drawerWidth > 0 ? 2 : 1, ImGuiTableFlags.SizingFixedFit)) return;
        ImGui.TableSetupColumn("catalog-column", ImGuiTableColumnFlags.WidthFixed, catalogWidth);
        if (drawerWidth > 0) ImGui.TableSetupColumn("details-column", ImGuiTableColumnFlags.WidthFixed, drawerWidth);
        ImGui.TableNextColumn();
        DrawCatalog(catalogWidth);
        DrawPager();
        if (drawerWidth > 0) { ImGui.TableNextColumn(); DrawDrawer(drawerWidth); }
        ImGui.EndTable();
    }

    private void DrawCatalog(float available)
    {
        var requestedWidth = Math.Clamp(plugin.Configuration.CardWidth, 480, 900);
        var columns = Math.Max(1, (int)((available + 10) / (requestedWidth + 10)));
        var cardWidth = Math.Max(420, (available - (columns - 1) * 10) / columns);
        if (mods.Count == 0 && !loading)
        {
            ImGui.Dummy(new Vector2(1, 80));
            ImGui.TextColored(BibliognostTheme.Dim, "No entries are visible. Try a broader search or choose another source.");
            return;
        }

        var catalogTop = ImGui.GetCursorScreenPos();
        ImGui.BeginChild("catalog", new Vector2(available, -46), false);
        if (ImGui.BeginTable("catalog-grid", columns, ImGuiTableFlags.SizingStretchSame, new Vector2(available, 0)))
        {
            foreach (var mod in mods)
            {
                if (plugin.Configuration.AdultContent == AdultContentMode.HideAdult && mod.IsAdult) continue;
                ImGui.TableNextColumn();
                DrawCard(mod, cardWidth - 8);
            }
            ImGui.EndTable();
        }
        ImGui.EndChild();
        var catalogBottom = ImGui.GetCursorScreenPos().Y - 4;
        // Mask clipped borders from the next card row so the pager has a clean edge.
        ImGui.GetWindowDrawList().AddRectFilled(new Vector2(catalogTop.X, catalogBottom - 12), new Vector2(catalogTop.X + available, catalogBottom + 2), ImGui.GetColorU32(BibliognostTheme.Surface));
    }

    private void DrawCard(ModSummary mod, float width)
    {
        ImGui.PushID(mod.ProviderId + ":" + mod.RemoteId);
        var start = ImGui.GetCursorScreenPos();
        var imageHeight = plugin.Configuration.CompactCards ? Math.Clamp(width * .48f, 220f, 400f) : Math.Clamp(width * .68f, 320f, 570f);
        var size = new Vector2(width, imageHeight + (plugin.Configuration.CompactCards ? 90 : 116));
        ImGui.InvisibleButton("##card", size);
        var hovered = ImGui.IsItemHovered();
        var t = BibliognostTheme.AnimateHover("card-" + mod.ProviderId + ":" + mod.RemoteId, hovered);
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(start - new Vector2(0, t * 3), start + size - new Vector2(0, t * 3), ImGui.GetColorU32(Vector4.Lerp(BibliognostTheme.Surface, new Vector4(.08f, .08f, .09f, 1), t)), 5);
        draw.AddRect(start - new Vector2(0, t * 3), start + size - new Vector2(0, t * 3), ImGui.GetColorU32(Vector4.Lerp(new Vector4(.18f, .18f, .18f, 1), BibliognostTheme.Gold, t)), 5, ImDrawFlags.None, 1 + t);
        var imageMin = start + new Vector2(8, 8 - t * 3);
        var imageMax = imageMin + new Vector2(width - 16, imageHeight);
        var texture = plugin.Thumbnails.Get(mod.ThumbnailUrl)?.GetWrapOrDefault();
        if (texture is not null) draw.AddImage(texture.Handle, imageMin, imageMax);
        else draw.AddRectFilled(imageMin, imageMax, ImGui.GetColorU32(new Vector4(.07f, .075f, .09f, 1)), 3);
        if (mod.IsAdult && plugin.Configuration.BlurAdultPreviews && !hovered)
        {
            draw.AddRectFilled(imageMin, imageMax, ImGui.GetColorU32(new Vector4(.025f, .025f, .03f, .94f)), 3);
            var label = "ADULT PREVIEW";
            draw.AddText(imageMin + (imageMax - imageMin - ImGui.CalcTextSize(label)) / 2, ImGui.GetColorU32(BibliognostTheme.Gold), label);
        }
        var textPos = new Vector2(imageMin.X, imageMax.Y + 10);
        var source = CardSourceList(mod);
        var sourceSize = ImGui.CalcTextSize(source);
        draw.AddRectFilled(new Vector2(imageMax.X - sourceSize.X - 14, imageMin.Y + 7), new Vector2(imageMax.X - 5, imageMin.Y + sourceSize.Y + 13), ImGui.GetColorU32(new Vector4(.03f, .035f, .045f, .90f)), 3);
        draw.AddText(new Vector2(imageMax.X - sourceSize.X - 10, imageMin.Y + 10), ImGui.GetColorU32(BibliognostTheme.GoldBright), source);
        draw.AddText(textPos, ImGui.GetColorU32(BibliognostTheme.Text), FitText(mod.Name, width - 16));
        draw.AddText(textPos + new Vector2(0, 24), ImGui.GetColorU32(BibliognostTheme.Dim), FitText("by " + (mod.Author.Length == 0 ? "Unknown" : mod.Author), width - 16));
        if (!plugin.Configuration.CompactCards) draw.AddText(textPos + new Vector2(0, 49), ImGui.GetColorU32(BibliognostTheme.Gold), FitText(mod.ModType.Length == 0 ? "XIV MOD ARCHIVE" : mod.ModType.ToUpperInvariant(), width - 16));
        if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left)) _ = LoadDetailsAsync(mod);
        ImGui.PopID();
    }

    private void DrawPager()
    {
        if (details is not null || loading) return;
        if (page > 1 && BibliognostTheme.AccentButton("previous", "PREVIOUS", new Vector2(100, 28))) GoToPage(page - 1);
        if (page > 1) ImGui.SameLine();
        var first = Math.Max(1, page - 4); var last = Math.Min(Math.Max(highestVisitedPage, page), first + 8);
        for (var number = first; number <= last; number++)
        {
            if (BibliognostTheme.AccentButton("page-" + number, number == page ? $"[{number}]" : number.ToString(), new Vector2(42, 28))) GoToPage(number);
            ImGui.SameLine();
        }
        if (mods.Count > 0 && BibliognostTheme.AccentButton("next", "NEXT", new Vector2(82, 28))) GoToPage(page + 1);
        ImGui.SameLine(); ImGui.TextColored(BibliognostTheme.Dim, "GO TO"); ImGui.SameLine();
        ImGui.SetNextItemWidth(58); ImGui.InputText("##page-input", ref pageInputText, 6, ImGuiInputTextFlags.CharsDecimal);
        ImGui.SameLine();
        if (!loading && BibliognostTheme.AccentButton("go-page", "GO", new Vector2(48, 28)) && int.TryParse(pageInputText, out var requestedPage)) GoToPage(requestedPage);
    }

    private void GoToPage(int target)
    {
        page = pageInput = Math.Max(1, target); pageInputText = page.ToString();
        highestVisitedPage = Math.Max(highestVisitedPage, page);
        _ = SearchAsync();
    }

    private void ShowLatestReleases()
    {
        search = string.Empty; ClearFilters();
        providerSelection = 0; sort = (int)ModSort.Newest; latestReleases = true;
        highestVisitedPage = 1; GoToPage(1);
    }

    private void ShowTimeline(ModSort timelineSort)
    {
        search = string.Empty; ClearFilters(); providerSelection = 0; sort = (int)timelineSort;
        latestReleases = false; highestVisitedPage = 1; GoToPage(1);
    }

    private void DrawDrawer()
    {
        DrawDrawer(Math.Clamp(ImGui.GetContentRegionAvail().X, 500f, 900f));
    }

    private void DrawDrawer(float panelWidth)
    {
        if (details is null) return;
        var currentDetails = details;
        var availableHeight = Math.Max(420, ImGui.GetContentRegionAvail().Y);
        // Collapsing the description must never collapse the rest of the dossier.
        // Keep the drawer at the workspace height and let its content child scroll.
        var panelHeight = availableHeight;
        ImGui.BeginChild("details", new Vector2(panelWidth, panelHeight), false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        selectionFlash = Math.Max(0, selectionFlash - ImGui.GetIO().DeltaTime * 1.8f);
        BibliognostTheme.DrawGlowFrame("details-frame", true);
        ImGui.SetCursorPos(new Vector2(16, 16));
        ImGui.BeginChild("details-content", new Vector2(-16, -16), false);
        if (BibliognostTheme.AccentButton("close-details", "CLOSE", new Vector2(78, 27))) details = null;
        ImGui.Spacing();
        DrawDetailsHeader(currentDetails.Summary, selectionFlash);
        var gallery = currentDetails.ImageUrls.Count > 0 ? currentDetails.ImageUrls : currentDetails.Summary.ThumbnailUrl is null ? [] : [currentDetails.Summary.ThumbnailUrl];
        selectedImageIndex = Math.Clamp(selectedImageIndex, 0, Math.Max(0, gallery.Count - 1));
        var heroUrl = gallery.Count == 0 ? null : gallery[selectedImageIndex];
        var hero = plugin.Thumbnails.Get(heroUrl)?.GetWrapOrDefault();
        if (hero is not null && hero.Width > 0 && hero.Height > 0)
        {
            const float heroSafeInset = 6f;
            var contentWidth = ImGui.GetContentRegionAvail().X;
            var maxWidth = Math.Max(1, contentWidth - heroSafeInset * 2);
            var aspect = (float)hero.Width / hero.Height;
            var heroSize = new Vector2(maxWidth, Math.Min(maxWidth / aspect, 560));
            if (heroSize.Y == 560) heroSize.X = heroSize.Y * aspect;
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + heroSafeInset + Math.Max(0, (maxWidth - heroSize.X) * .5f));
            var imageMin = ImGui.GetCursorScreenPos();
            ImGui.Image(hero.Handle, heroSize);
            BibliognostTheme.DrawGlowRect(ImGui.GetWindowDrawList(), imageMin - new Vector2(2), imageMin + heroSize + new Vector2(2), .58f + selectionFlash);
            ImGui.Spacing();
        }
        if (gallery.Count > 1) DrawGalleryStrip(gallery);
        ImGui.TextColored(BibliognostTheme.Gold, "MOD DOSSIER");
        if (currentDetails.Summary.Version.Length > 0) { ImGui.TextColored(BibliognostTheme.Dim, "VERSION"); ImGui.SameLine(); ImGui.Text(currentDetails.Summary.Version); }
        ImGui.TextColored(BibliognostTheme.Dim, "SOURCES"); ImGui.SameLine(); ImGui.Text(SourceList(currentDetails.Summary));
        ImGui.Spacing();
        DrawWrappedTags(currentDetails.Summary.Tags.Take(18));
        var descriptionLabel = showDescription ? "HIDE DESCRIPTION  ▲" : "VIEW DESCRIPTION  ▼";
        if (BibliognostTheme.AccentButton("toggle-description", descriptionLabel, new Vector2(210, 32))) showDescription = !showDescription;
        descriptionExpansion += ((showDescription ? 1f : 0f) - descriptionExpansion) * Math.Clamp(ImGui.GetIO().DeltaTime * 10f, 0f, 1f);
        if (descriptionExpansion > .015f)
        {
            ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(.018f, .022f, .035f, .96f));
            ImGui.BeginChild("description-panel", new Vector2(0, 330 * descriptionExpansion), true);
            ImGui.TextColored(BibliognostTheme.GoldBright, "ARCHIVE NOTES"); ImGui.Separator();
            ImGui.TextWrapped(currentDetails.Description.Length == 0 ? "No description was returned by this source." : currentDetails.Description);
            ImGui.EndChild(); ImGui.PopStyleColor();
        }
        ImGui.Spacing();
        var sources = currentDetails.Summary.Sources;
        if (sources.Count > 1)
        {
            ImGui.TextColored(BibliognostTheme.Gold, $"AVAILABLE FROM {sources.Count} SOURCES");
            foreach (var source in sources)
            {
                var label = source.ProviderId switch { "heliosphere" => "HELIOSPHERE", "nexusmods" => "NEXUS MODS", _ => "XIV MOD ARCHIVE" };
                if (BibliognostTheme.AccentButton("source-" + source.ProviderId, label + (source.Version.Length == 0 ? "" : "  " + source.Version), new Vector2(165, 29)))
                    Process.Start(new ProcessStartInfo(source.PageUrl) { UseShellExecute = true });
                ImGui.SameLine();
            }
            ImGui.NewLine();
        }
        DrawSourceIdentityReview(currentDetails);
        ImGui.TextColored(BibliognostTheme.Gold, "GET THIS MOD");
        foreach (var sourceDetail in sourceDetails.Count > 0 ? sourceDetails : [currentDetails])
            DrawDeliveryControls(sourceDetail);
        ImGui.Dummy(new Vector2(1, 14));
        ImGui.EndChild();
        ImGui.EndChild();
    }

    private void DrawSourceIdentityReview(ModDetails currentDetails)
    {
        if (!string.IsNullOrWhiteSpace(plugin.Catalog.LastMatchExplanation))
        {
            ImGui.TextColored(BibliognostTheme.Dim, "MATCH LOG"); ImGui.SameLine();
            ImGui.TextWrapped(plugin.Catalog.LastMatchExplanation);
        }
        foreach (var candidate in plugin.Catalog.LastCandidates)
        {
            ImGui.PushID("candidate-" + candidate.Summary.ProviderId + candidate.Summary.RemoteId);
            ImGui.TextColored(BibliognostTheme.Gold, $"POSSIBLE MATCH · {candidate.Confidence:P0}");
            ImGui.TextWrapped($"{candidate.Summary.Name} — {candidate.Summary.Author} · {ProviderLabel(candidate.Summary.ProviderId)}");
            ImGui.TextColored(BibliognostTheme.Dim, candidate.Explanation);
            if (BibliognostTheme.AccentButton("confirm", "SAME MOD", new Vector2(112, 28)))
            {
                plugin.Catalog.ConfirmMatch(currentDetails.Summary, candidate.Summary);
                _ = LoadDetailsAsync(currentDetails.Summary);
            }
            ImGui.SameLine();
            if (BibliognostTheme.AccentButton("reject", "NOT THE SAME", new Vector2(126, 28)))
            {
                plugin.Catalog.RejectMatch(currentDetails.Summary, candidate.Summary);
                _ = LoadDetailsAsync(currentDetails.Summary);
            }
            ImGui.PopID();
        }
        if (sourceDetails.Count > 1 && selectedSummary is not null)
        {
            foreach (var alternate in sourceDetails.Where(item => item.Summary.ProviderId != selectedSummary.ProviderId || item.Summary.RemoteId != selectedSummary.RemoteId))
            {
                ImGui.PushID("unlink-" + alternate.Summary.ProviderId + alternate.Summary.RemoteId);
                if (BibliognostTheme.AccentButton("unlink", $"NOT SAME AS {ProviderLabel(alternate.Summary.ProviderId)}", new Vector2(210, 26)))
                {
                    plugin.Catalog.RejectMatch(selectedSummary, alternate.Summary);
                    _ = LoadDetailsAsync(selectedSummary);
                }
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Keep these provider entries separate and remember that decision.");
                ImGui.PopID();
            }
        }
    }

    private void DrawDeliveryControls(ModDetails currentDetails)
    {
        ImGui.PushID("delivery-" + currentDetails.Summary.ProviderId + ":" + currentDetails.Summary.RemoteId);
        var delivery = plugin.Delivery;
        ImGui.TextColored(BibliognostTheme.Dim, ProviderLabel(currentDetails.Summary.ProviderId));
        ImGui.SameLine();
        if (currentDetails.IsDirectDownload && !string.IsNullOrWhiteSpace(currentDetails.DownloadUrl))
        {
            var installable = ModDeliveryService.CanInstall(currentDetails);
            var unknownType = ModDeliveryService.HasUnknownFileType(currentDetails);
            var installed = (installable || unknownType) && delivery.AppearsInstalled(currentDetails.Summary.Name);
            var label = installable ? (installed ? "UPDATE IN PENUMBRA" : "INSTALL TO PENUMBRA") : unknownType ? "DOWNLOAD · INSTALL IF COMPATIBLE" : "DOWNLOAD FILE";
            if (!delivery.Busy && BibliognostTheme.AccentButton("deliver-mod", label, new Vector2(270, 34)))
            {
                if (installable || unknownType) { pendingInstall = currentDetails; ImGui.OpenPopup("Confirm Penumbra Install"); }
                else _ = plugin.DeliverAsync(currentDetails, false);
            }
            if (delivery.Busy)
            {
                ImGui.ProgressBar(delivery.Progress, new Vector2(Math.Min(320, ImGui.GetContentRegionAvail().X - 92), 24), delivery.Status);
                ImGui.SameLine();
                if (BibliognostTheme.AccentButton("cancel-delivery", "CANCEL", new Vector2(82, 26))) delivery.Cancel();
            }
            else if (delivery.State != DeliveryState.Idle)
                ImGui.TextWrapped(delivery.Status);
            ImGui.TextColored(BibliognostTheme.Dim, installable || unknownType
                ? "Downloads a safe local copy, then asks Penumbra to import it."
                : "This file is not a recognized Penumbra package and will only be saved to Downloads.");
        }
        else
        {
            var providerLabel = currentDetails.Summary.ProviderId == "heliosphere" ? "OPEN IN HELIOSPHERE" : "OPEN PROVIDER DOWNLOAD";
            if (BibliognostTheme.AccentButton("view-page", providerLabel, new Vector2(205, 34)))
                Process.Start(new ProcessStartInfo(currentDetails.Summary.PageUrl) { UseShellExecute = true });
            ImGui.TextColored(BibliognostTheme.Dim, currentDetails.Summary.ProviderId == "heliosphere"
                ? "Heliosphere's official installer handles its manifest-based packages and choices."
                : "This provider requires its own website flow for this file or account tier.");
        }
        DrawInstallConfirmation();
        ImGui.PopID();
    }

    private void DrawInstallConfirmation()
    {
        if (!ImGui.BeginPopupModal("Confirm Penumbra Install", ImGuiWindowFlags.AlwaysAutoResize)) return;
        var item = pendingInstall;
        ImGui.TextColored(BibliognostTheme.GoldBright, "INSTALL MOD THROUGH PENUMBRA?");
        ImGui.Separator();
        if (item is not null)
        {
            ImGui.TextWrapped(item.Summary.Name);
            ImGui.TextColored(BibliognostTheme.Dim, $"SOURCE  {ProviderLabel(item.Summary.ProviderId)}");
            ImGui.TextColored(BibliognostTheme.Dim, $"VERSION  {(item.Summary.Version.Length == 0 ? "Unknown" : item.Summary.Version)}");
            ImGui.TextColored(BibliognostTheme.Dim, $"FILE  {item.DownloadFileName ?? "Provider-supplied filename"}");
            ImGui.TextWrapped("Bibliognost will download and validate the package before asking Penumbra to import it.");
            if (BibliognostTheme.AccentButton("confirm-install", "DOWNLOAD & INSTALL", new Vector2(180, 32)))
            { _ = plugin.DeliverAsync(item, true); pendingInstall = null; ImGui.CloseCurrentPopup(); }
            ImGui.SameLine();
        }
        if (BibliognostTheme.AccentButton("cancel-install", "CANCEL", new Vector2(100, 32))) { pendingInstall = null; ImGui.CloseCurrentPopup(); }
        ImGui.EndPopup();
    }

    private static void DrawDetailsHeader(ModSummary summary, float flash)
    {
        var min = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        const float height = 90f;
        ImGui.InvisibleButton("##details-header", new Vector2(width, height));
        var max = min + new Vector2(width, height);
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilledMultiColor(min, max, ImGui.GetColorU32(new Vector4(.10f, .075f, .025f, .94f)), ImGui.GetColorU32(new Vector4(.035f, .045f, .075f, .96f)), ImGui.GetColorU32(new Vector4(.018f, .022f, .045f, .98f)), ImGui.GetColorU32(new Vector4(.055f, .035f, .025f, .96f)));
        BibliognostTheme.DrawGlowRect(draw, min, max, .9f + flash);
        draw.AddLine(min + new Vector2(18, height - 18), max - new Vector2(18, 18), ImGui.GetColorU32(new Vector4(1f, .78f, .30f, .72f)), 1.5f);
        var title = FitText(summary.Name.ToUpperInvariant(), width - 38);
        var titlePos = min + new Vector2(18, 12);
        draw.AddText(ImGui.GetFont(), ImGui.GetFontSize() * 1.22f, titlePos + new Vector2(1, 2), ImGui.GetColorU32(new Vector4(1f, .58f, .12f, .30f)), title);
        draw.AddText(ImGui.GetFont(), ImGui.GetFontSize() * 1.22f, titlePos, ImGui.GetColorU32(BibliognostTheme.GoldBright), title);
        draw.AddText(min + new Vector2(18, 47), ImGui.GetColorU32(BibliognostTheme.Dim), "CREATED BY  ");
        draw.AddText(min + new Vector2(104, 47), ImGui.GetColorU32(BibliognostTheme.Text), summary.Author.Length == 0 ? "UNKNOWN" : summary.Author);
        ImGui.Spacing();
    }

    internal static void DrawArchiveHeader(Plugin plugin, string subtitle, string id)
    {
        const float height = 126f;
        var min = ImGui.GetCursorScreenPos(); var width = ImGui.GetContentRegionAvail().X;
        ImGui.BeginChild("archive-header-" + id, new Vector2(width, height), false, ImGuiWindowFlags.NoScrollbar);
        var draw = ImGui.GetWindowDrawList(); var max = min + new Vector2(width, height);
        draw.AddRectFilledMultiColor(min, max, ImGui.GetColorU32(new Vector4(.025f, .035f, .065f, .98f)), ImGui.GetColorU32(new Vector4(.055f, .025f, .045f, .98f)), ImGui.GetColorU32(new Vector4(.015f, .018f, .035f, .99f)), ImGui.GetColorU32(new Vector4(.018f, .028f, .055f, .99f)));
        var time = (float)ImGui.GetTime(); var center = min.X + width * .5f;
        var ribbonWidth = Math.Min(width - 80, 760f); var left = center - ribbonWidth * .5f;
        const int segments = 96;
        for (var strand = 0; strand < 3; strand++)
        {
            for (var i = 0; i < segments; i++)
            {
                var p0 = i / (float)segments; var p1 = (i + 1) / (float)segments;
                var x0 = left + ribbonWidth * p0; var x1 = left + ribbonWidth * p1;
                var phase = time * (1.0f + strand * .12f) + strand * 2.1f;
                var y0 = max.Y - 15 + MathF.Sin(p0 * MathF.PI * 4 + phase) * (5 + strand * 1.5f);
                var y1 = max.Y - 15 + MathF.Sin(p1 * MathF.PI * 4 + phase) * (5 + strand * 1.5f);
                var color = Rainbow((p0 + time * .055f + strand * .08f) % 1f, .82f);
                draw.AddLine(new Vector2(x0, y0), new Vector2(x1, y1), ImGui.GetColorU32(color), strand == 1 ? 3.2f : 2f);
            }
        }
        for (var i = 0; i < 9; i++)
        {
            var progress = (time * (.08f + i * .002f) + i / 9f) % 1f;
            var x = left + ribbonWidth * progress; var y = max.Y - 15 + MathF.Sin(progress * MathF.PI * 4 + time + i * .7f) * 7;
            var color = Rainbow((progress + time * .055f) % 1f, 1f);
            draw.AddCircleFilled(new Vector2(x, y), 2.2f, ImGui.GetColorU32(color));
            draw.AddCircle(new Vector2(x, y), 5.5f, ImGui.GetColorU32(color with { W = .22f }), 16, 2f);
        }
        if (plugin.BannerFont is not null)
        {
            using (plugin.BannerFont.Push())
            {
                var label = "BIBLIOGNOST"; var size = ImGui.CalcTextSize(label); var pos = new Vector2(Math.Max(12, (width - size.X) * .5f), 5);
                ImGui.SetCursorPos(pos + new Vector2(2, 2)); ImGui.TextColored(new Vector4(.25f, .60f, 1f, .35f), label);
                ImGui.SetCursorPos(pos); ImGui.TextColored(new Vector4(.94f, .96f, 1f, 1f), label);
            }
        }
        else { ImGui.SetCursorPos(new Vector2(18, 18)); ImGui.TextColored(BibliognostTheme.GoldBright, "B I B L I O G N O S T"); }
        var subtitleSize = ImGui.CalcTextSize(subtitle);
        draw.AddText(new Vector2(center - subtitleSize.X * .5f, min.Y + 70), ImGui.GetColorU32(new Vector4(.72f, .76f, .84f, 1f)), subtitle);
        BibliognostTheme.DrawGlowRect(draw, min + new Vector2(1), max - new Vector2(1), .45f, id);
        ImGui.EndChild(); ImGui.Spacing();
    }

    private static Vector4 Rainbow(float hue, float alpha)
    {
        hue = hue - MathF.Floor(hue); var h = hue * 6f; var x = 1f - MathF.Abs(h % 2f - 1f);
        var rgb = (int)h switch { 0 => new Vector3(1, x, 0), 1 => new Vector3(x, 1, 0), 2 => new Vector3(0, 1, x), 3 => new Vector3(0, x, 1), 4 => new Vector3(x, 0, 1), _ => new Vector3(1, 0, x) };
        return new Vector4(rgb, alpha);
    }

    private static string SourceList(ModSummary summary)
    {
        var ids = summary.Sources.Count > 0 ? summary.Sources.Select(x => x.ProviderId) : [summary.ProviderId];
        return string.Join("  |  ", ids.Distinct().Select(id => id switch { "heliosphere" => "Heliosphere", "nexusmods" => "Nexus Mods", _ => "XIV Mod Archive" }));
    }

    private static string ProviderLabel(string id) => id switch { "heliosphere" => "HELIOSPHERE", "nexusmods" => "NEXUS MODS", _ => "XIV MOD ARCHIVE" };

    private static string CardSourceList(ModSummary summary)
    {
        var ids = summary.Sources.Count > 0 ? summary.Sources.Select(source => source.ProviderId) : [summary.ProviderId];
        return string.Join(" + ", ids.Distinct().Select(id => id switch { "heliosphere" => "HELIOSPHERE", "nexusmods" => "NEXUS", _ => "XMA" }));
    }

    private static void DrawWrappedTags(IEnumerable<string> tags)
    {
        var first = true;
        foreach (var tag in tags)
        {
            var width = ImGui.CalcTextSize(tag).X + ImGui.GetStyle().FramePadding.X * 2;
            if (!first)
            {
                var rightEdge = ImGui.GetWindowPos().X + ImGui.GetWindowContentRegionMax().X;
                if (ImGui.GetItemRectMax().X + ImGui.GetStyle().ItemSpacing.X + width < rightEdge) ImGui.SameLine();
            }
            ImGui.SmallButton(tag);
            first = false;
        }
        ImGui.NewLine();
    }

    private void DrawGalleryStrip(IReadOnlyList<string> gallery)
    {
        ImGui.TextColored(BibliognostTheme.Dim, $"PREVIEW {selectedImageIndex + 1} / {gallery.Count}");
        var shown = Math.Min(gallery.Count, 6); const float width = 92f; const float height = 58f;
        for (var i = 0; i < shown; i++)
        {
            ImGui.PushID("gallery-" + i);
            var min = ImGui.GetCursorScreenPos();
            if (ImGui.InvisibleButton("##preview", new Vector2(width, height))) { selectedImageIndex = i; selectionFlash = .7f; }
            var texture = plugin.Thumbnails.Get(gallery[i])?.GetWrapOrDefault();
            var draw = ImGui.GetWindowDrawList();
            if (texture is not null) draw.AddImage(texture.Handle, min, min + new Vector2(width, height));
            draw.AddRect(min, min + new Vector2(width, height), ImGui.GetColorU32(i == selectedImageIndex ? BibliognostTheme.GoldBright : new Vector4(.25f, .27f, .32f, 1)), 3, ImDrawFlags.None, i == selectedImageIndex ? 2f : 1f);
            ImGui.PopID();
            if (i + 1 < shown) ImGui.SameLine();
        }
        ImGui.Spacing();
    }

    private async Task SearchAsync()
    {
        loading = true; status = providerSelection switch { 1 => "Contacting XIV Mod Archive…", 2 => "Contacting Heliosphere…", 3 => "Contacting Nexus Mods…", _ => "Contacting mod archives…" };
        var result = await plugin.Catalog.SearchAsync(new ModSearchQuery
        {
            SearchText = search, Name = name, Author = author, Races = races, Tags = tags, Affects = affects,
            Gender = gender switch { 1 => "male", 2 => "female", 3 => "unisex", _ => "" },
            Sort = (ModSort)sort, Types = selectedTypes.ToArray(), Page = page, PublishedTodayOnly = latestReleases,
            DawntrailCompatibleOnly = plugin.Configuration.DawntrailCompatibleOnly,
            AdultContent = plugin.Configuration.AdultContent switch { AdultContentMode.HideAdult => false, AdultContentMode.ShowAdult => true, _ => null },
        }, (ProviderSelection)providerSelection);
        mods.Clear();
        if (result.Success && result.Value is not null) { mods.AddRange(result.Value.Take(plugin.Configuration.ResultsPerPage)); status = latestReleases ? $"{mods.Count} releases published today across connected sources" + (result.Error is null ? "." : $". One source reported: {result.Error}") : $"{mods.Count} entries found" + (result.Error is null ? "." : $". One source reported: {result.Error}"); }
        else status = result.Error ?? "The archive could not be read.";
        loading = false;
    }

    private async Task LoadDetailsAsync(ModSummary mod)
    {
        selectedSummary = mod;
        status = $"Reading {mod.Name}…";
        var result = await plugin.Catalog.GetAllSourceDetailsAsync(mod);
        if (result.Success && result.Value is { Count: > 0 })
        {
            sourceDetails = result.Value;
            details = sourceDetails.FirstOrDefault(item => item.Summary.ProviderId == mod.ProviderId) ?? sourceDetails[0];
            var merged = details.Summary;
            var index = mods.FindIndex(item => item.ProviderId == mod.ProviderId && item.RemoteId == mod.RemoteId);
            if (index >= 0) mods[index] = mods[index] with { Sources = merged.Sources };
            status = merged.Sources.Count > 1 ? $"Matched across {merged.Sources.Count} sources." : $"Reading {mod.Name}.";
            selectedImageIndex = 0; showDescription = false; descriptionExpansion = 0; selectionFlash = 1f;
        }
        else status = result.Error ?? "Details were unavailable.";
    }

    internal void OpenMod(ModSummary mod)
    {
        IsOpen = true;
        _ = LoadDetailsAsync(mod);
    }

    private static string FitText(string value, float maxWidth)
    {
        if (ImGui.CalcTextSize(value).X <= maxWidth) return value;
        while (value.Length > 2 && ImGui.CalcTextSize(value + "…").X > maxWidth) value = value[..^1];
        return value + "…";
    }
    private static void DrawBackdrop()
    {
        var draw = ImGui.GetWindowDrawList();
        var min = ImGui.GetWindowPos(); var max = min + ImGui.GetWindowSize();
        draw.AddRectFilled(min, max, ImGui.GetColorU32(BibliognostTheme.Surface));
        for (var x = min.X; x < max.X; x += 42) draw.AddLine(new Vector2(x, min.Y), new Vector2(x, max.Y), ImGui.GetColorU32(new Vector4(.4f, .32f, .16f, .035f)));
    }
}
