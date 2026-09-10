using System.Diagnostics;
using System.Numerics;
using Bibliognost.Models;
using Bibliognost.Downloads;
using Bibliognost.Providers;
using Bibliognost.Services;
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
    private bool detailOverlayRequested;
    private bool showDescription;
    private float descriptionExpansion;
    private int selectedImageIndex;
    private float selectionFlash;
    private ModDetails? pendingInstall;
    private CancellationTokenSource? searchCancellation;
    private int libraryView;
    private int selectedCardIndex;
    private int lastGridColumns = 1;
    private readonly List<ModSummary> relatedMods = [];
    private ModDetails? comparisonDetails;
    private string savedSearchName = string.Empty;
    private bool restoreCatalogScroll = true;
    private float previewZoom = 1f;

    public MainWindow(Plugin plugin) : base("Bibliognost — The Eorzean Mod Archive###BibliognostMain")
    {
        this.plugin = plugin;
        search = plugin.Configuration.LastSearchText;
        name = plugin.Configuration.LastNameFilter; author = plugin.Configuration.LastAuthorFilter;
        races = plugin.Configuration.LastRaceFilter; tags = plugin.Configuration.LastTagFilter; affects = plugin.Configuration.LastAffectsFilter;
        gender = Math.Clamp(plugin.Configuration.LastGenderFilter, 0, 3);
        providerSelection = Math.Clamp(plugin.Configuration.LastProviderSelection, 0, 3);
        sort = Math.Clamp(plugin.Configuration.LastSort, 0, 5);
        page = Math.Max(1, plugin.Configuration.LastPage);
        pageInput = page; pageInputText = page.ToString(); highestVisitedPage = page;
        foreach (var type in plugin.Configuration.LastSelectedTypes) selectedTypes.Add(type);
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(720, 520), MaximumSize = new Vector2(4096, 4096) };
    }

    public override void OnOpen()
    {
        // Closing a Dalamud window only hides it, so the previous in-memory results remain.
        // Always refresh the active view when Bibliognost is shown again while preserving
        // the user's source, filters, sort, and current page.
        ResetCatalogScroll();
        if (libraryView != 0) ShowLibrary(libraryView); else if (!loading) _ = SearchAsync();
    }

    public override void OnClose()
    {
        SaveBrowsingSession();
    }

    public override void Draw()
    {
        ImGui.SetWindowFontScale(Math.Clamp(plugin.Configuration.UiScale, .85f, 1.40f));
        DrawBackdrop();
        DrawArchiveHeader(plugin, "DISCOVER · CATALOGUE · REMEMBER", "main-banner");

        var settingsWidth = 92f;
        ImGui.SetNextItemWidth(Math.Max(220, ImGui.GetContentRegionAvail().X - settingsWidth - 390));
        var previousSearch = search;
        var submit = ImGui.InputTextWithHint("##search", "Search every connected mod archive…", ref search, 180, ImGuiInputTextFlags.EnterReturnsTrue);
        var searchWasCleared = previousSearch.Length > 0 && search.Length == 0;
        var xmaSearchWasStarted = providerSelection == (int)ProviderSelection.XivModArchive && previousSearch.Length == 0 && search.Length > 0;
        if (xmaSearchWasStarted) sort = (int)ModSort.Relevance;
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
        ImGui.SameLine();
        if (BibliognostTheme.AccentButton("favorites", "FAVORITES", new Vector2(110, 27))) ShowLibrary(1);
        ImGui.SameLine();
        if (BibliognostTheme.AccentButton("recently-viewed", "VIEWED", new Vector2(92, 27))) ShowLibrary(2);
        ImGui.SameLine();
        if (BibliognostTheme.AccentButton("collections", "COLLECTIONS", new Vector2(125, 27))) plugin.Library.ShowFor();
        ImGui.SameLine();
        if (BibliognostTheme.AccentButton("feature-hub", $"TOOLS ({plugin.Configuration.InstallationQueue.Count})", new Vector2(112, 27))) plugin.FeatureHub.IsOpen = true;
        ImGui.SameLine();
        if (BibliognostTheme.AccentButton("save-search", "SAVE SEARCH", new Vector2(120, 27))) ImGui.OpenPopup("Save Search");
        DrawSaveSearchPopup();
        if (latestReleases) { ImGui.SameLine(); ImGui.TextColored(BibliognostTheme.GoldBright, "TODAY · ALL SOURCES"); }
        if (selectedTypes.Count > 0 || gender > 0 || name.Length > 0 || author.Length > 0 || races.Length > 0 || tags.Length > 0 || affects.Length > 0)
        {
            ImGui.SameLine();
            ImGui.TextColored(BibliognostTheme.Gold, "FILTERED SEARCH");
        }
        if (showFilters) DrawFilters();

        ImGui.TextColored(loading ? BibliognostTheme.Gold : BibliognostTheme.Dim, loading ? "SCANNING ARCHIVE…" : status);
        ImGui.Spacing();
        HandleCatalogNavigation();
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
        var sorts = new[] { "Newest", "Recently updated", "Most downloaded", "Most viewed", "Name", "Relevance" };
        if (ImGui.BeginCombo("##sort-filter", sorts[sort]))
        {
            for (var i = 0; i < sorts.Length; i++) if (ImGui.Selectable(sorts[i], sort == i)) sort = i;
            ImGui.EndCombo();
        }
        ImGui.TextColored(BibliognostTheme.Dim, "MOD TYPES");
        var allTypes = selectedTypes.Count == ModTypes.Length;
        if (ImGui.Checkbox("All", ref allTypes))
        {
            selectedTypes.Clear();
            if (allTypes)
                foreach (var type in ModTypes) selectedTypes.Add(type.Id);
        }
        ImGui.SameLine();
        for (var i = 0; i < ModTypes.Length; i++)
        {
            var type = ModTypes[i];
            var chosen = selectedTypes.Contains(type.Id);
            if (ImGui.Checkbox(type.Label, ref chosen)) { if (chosen) selectedTypes.Add(type.Id); else selectedTypes.Remove(type.Id); }
            if (i == 8) ImGui.NewLine(); else if (i + 1 < ModTypes.Length) ImGui.SameLine();
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
        var total = ImGui.GetContentRegionAvail().X;
        DrawCatalog(total);
        DrawPager();
        DrawDetailsOverlay();
    }

    private void DrawDetailsOverlay()
    {
        const string popupId = "Mod Showcase###BibliognostModShowcase";
        if (detailOverlayRequested)
        {
            ImGui.OpenPopup(popupId);
            detailOverlayRequested = false;
        }

        if (details is null) return;
        var hostPos = ImGui.GetWindowPos();
        var hostSize = ImGui.GetWindowSize();
        var proportions = plugin.Configuration.ShowcaseSize switch { ShowcaseSize.Compact => new Vector2(.64f, .72f), ShowcaseSize.Cinematic => new Vector2(.94f, .94f), _ => new Vector2(.84f, .88f) };
        var overlaySize = new Vector2(
            Math.Clamp(hostSize.X * proportions.X, Math.Min(560f, hostSize.X - 24f), 1500f),
            Math.Clamp(hostSize.Y * proportions.Y, Math.Min(460f, hostSize.Y - 24f), 1250f));
        ImGui.SetNextWindowPos(hostPos + hostSize * .5f, ImGuiCond.Always, new Vector2(.5f));
        ImGui.SetNextWindowSize(overlaySize, ImGuiCond.Always);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1f);
        ImGui.PushStyleColor(ImGuiCol.PopupBg, BibliognostTheme.Surface);
        ImGui.PushStyleColor(ImGuiCol.Border, BibliognostTheme.Gold);
        if (ImGui.BeginPopup(popupId, ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoCollapse))
        {
            DrawDrawer(ImGui.GetContentRegionAvail().X);
            ImGui.EndPopup();
        }
        ImGui.PopStyleColor(2);
        ImGui.PopStyleVar(2);
        if (!ImGui.IsPopupOpen(popupId)) details = null;
    }

    private void DrawCatalog(float available)
    {
        var requestedWidth = Math.Clamp(plugin.Configuration.CardWidth, 260, 900);
        var columns = Math.Max(1, (int)((available + 10) / (requestedWidth + 10)));
        lastGridColumns = columns;
        var cardWidth = Math.Max(230, (available - (columns - 1) * 10) / columns);
        if (mods.Count == 0 && !loading)
        {
            ImGui.Dummy(new Vector2(1, 80));
            ImGui.TextColored(BibliognostTheme.Dim, "No entries are visible. Try a broader search or choose another source.");
            return;
        }

        var catalogTop = ImGui.GetCursorScreenPos();
        ImGui.BeginChild("catalog", new Vector2(available, -46), false);
        if (restoreCatalogScroll) { ImGui.SetScrollY(plugin.Configuration.LastCatalogScrollY); restoreCatalogScroll = false; }
        if (ImGui.BeginTable("catalog-grid", columns, ImGuiTableFlags.SizingStretchSame, new Vector2(available, 0)))
        {
            for (var index = 0; index < mods.Count; index++)
            {
                var mod = mods[index];
                if (plugin.Configuration.AdultContent == AdultContentMode.HideAdult && mod.IsAdult) continue;
                ImGui.TableNextColumn();
                DrawCard(mod, cardWidth - 8, index);
            }
            ImGui.EndTable();
        }
        plugin.Configuration.LastCatalogScrollY = ImGui.GetScrollY();
        ImGui.EndChild();
        var catalogBottom = ImGui.GetCursorScreenPos().Y - 4;
        // Mask clipped borders from the next card row so the pager has a clean edge.
        ImGui.GetWindowDrawList().AddRectFilled(new Vector2(catalogTop.X, catalogBottom - 12), new Vector2(catalogTop.X + available, catalogBottom + 2), ImGui.GetColorU32(BibliognostTheme.Surface));
    }

    private void DrawCard(ModSummary mod, float width, int index)
    {
        ImGui.PushID(mod.ProviderId + ":" + mod.RemoteId);
        var start = ImGui.GetCursorScreenPos();
        var imageHeight = plugin.Configuration.CompactCards
            ? Math.Clamp(width * .48f, 130f, 400f)
            : Math.Clamp(width * .68f, 170f, 570f);
        var showAuthor = plugin.Configuration.InterfaceDensity != InterfaceDensity.Minimal;
        var showType = !plugin.Configuration.CompactCards && plugin.Configuration.InterfaceDensity == InterfaceDensity.Comfortable;
        var metadataHeight = plugin.Configuration.CardTitleFontSize + (showAuthor ? plugin.Configuration.CardAuthorFontSize + 8 : 0) +
            (showType ? plugin.Configuration.CardTypeFontSize + 8 : 0) + 34;
        var size = new Vector2(width, imageHeight + metadataHeight);
        ImGui.InvisibleButton("##card", size);
        var hovered = ImGui.IsItemHovered();
        var t = BibliognostTheme.AnimateHover("card-" + mod.ProviderId + ":" + mod.RemoteId, hovered);
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(start - new Vector2(0, t * 3), start + size - new Vector2(0, t * 3), ImGui.GetColorU32(Vector4.Lerp(BibliognostTheme.Surface, BibliognostTheme.Gold with { W = 1f }, t * .13f)), 5);
        draw.AddRect(start - new Vector2(0, t * 3), start + size - new Vector2(0, t * 3), ImGui.GetColorU32(Vector4.Lerp(new Vector4(.18f, .18f, .18f, 1), BibliognostTheme.Gold, t)), 5, ImDrawFlags.None, 1 + t);
        if (index == selectedCardIndex) draw.AddRect(start - new Vector2(2), start + size + new Vector2(2), ImGui.GetColorU32(BibliognostTheme.GoldBright), 5, ImDrawFlags.None, 2.2f);
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
        var receipt = plugin.Configuration.InstalledModReceipts.FirstOrDefault(item => item.ProviderId == mod.ProviderId && item.RemoteId == mod.RemoteId);
        var queued = plugin.Configuration.InstallationQueue.Any(item => ModKey(item) == ModKey(mod));
        var downloaded = plugin.Configuration.DeliveryHistory.Any(item => item.Contains(mod.Name, StringComparison.OrdinalIgnoreCase) && item.Contains("DOWNLOADED", StringComparison.OrdinalIgnoreCase));
        if (receipt is not null || queued || downloaded)
        {
            var badge = queued ? "QUEUED" : receipt is not null ? IsNewer(mod.Version, receipt.InstalledVersion) ? "UPDATE" : "INSTALLED" : "DOWNLOADED";
            var badgeSize = ImGui.CalcTextSize(badge);
            draw.AddRectFilled(imageMin + new Vector2(6, 7), imageMin + new Vector2(badgeSize.X + 16, badgeSize.Y + 13), ImGui.GetColorU32(new Vector4(.02f, .09f, .06f, .94f)), 3);
            draw.AddText(imageMin + new Vector2(10, 10), ImGui.GetColorU32(new Vector4(.45f, 1f, .66f, 1)), badge);
        }
        using (plugin.CardFonts.Push(CardFontRole.Title))
        {
            var title = FitText(mod.Name, width - 16);
            draw.AddText(textPos, ImGui.GetColorU32(BibliognostTheme.Text), title);
            if (plugin.Configuration.CardTitleBold)
                draw.AddText(textPos + new Vector2(.7f, 0), ImGui.GetColorU32(BibliognostTheme.Text), title);
        }
        var authorPos = textPos + new Vector2(0, plugin.Configuration.CardTitleFontSize + 8);
        if (showAuthor)
            using (plugin.CardFonts.Push(CardFontRole.Author))
                draw.AddText(authorPos, ImGui.GetColorU32(BibliognostTheme.Dim), FitText("by " + (mod.Author.Length == 0 ? "Unknown" : mod.Author), width - 16));
        if (showType)
        {
            var typePos = authorPos + new Vector2(0, plugin.Configuration.CardAuthorFontSize + 8);
            using (plugin.CardFonts.Push(CardFontRole.Type))
                draw.AddText(typePos, ImGui.GetColorU32(BibliognostTheme.Gold), FitText(mod.ModType.Length == 0 ? "XIV MOD ARCHIVE" : mod.ModType.ToUpperInvariant(), width - 16));
        }
        if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left)) { selectedCardIndex = index; _ = LoadDetailsAsync(mod); }
        ImGui.PopID();
    }

    private void DrawPager()
    {
        if (details is not null || loading || libraryView != 0) return;
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
        libraryView = 0;
        page = pageInput = Math.Max(1, target); pageInputText = page.ToString();
        highestVisitedPage = Math.Max(highestVisitedPage, page);
        ResetCatalogScroll();
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

    private void ShowAuthorMods(string creator)
    {
        search = string.Empty;
        ClearFilters();
        author = creator.Trim();
        providerSelection = (int)ProviderSelection.All;
        sort = (int)ModSort.Updated;
        latestReleases = false;
        libraryView = 0;
        details = null;
        sourceDetails = [];
        page = pageInput = 1;
        pageInputText = "1";
        highestVisitedPage = 1;
        ResetCatalogScroll();
        status = $"Finding every listing credited exactly to {author}…";
        _ = SearchAsync();
    }

    private void ShowLibrary(int view)
    {
        libraryView = view; details = null; page = 1; highestVisitedPage = 1;
        ResetCatalogScroll();
        mods.Clear();
        mods.AddRange(view == 1 ? plugin.Configuration.FavoriteMods : plugin.Configuration.RecentlyViewedMods);
        status = view == 1 ? $"{mods.Count} favorite mod(s)." : $"{mods.Count} recently viewed mod(s).";
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
        // Keep the showcase at the popup height and let its content child scroll.
        var panelHeight = availableHeight;
        ImGui.PushStyleColor(ImGuiCol.ChildBg, BibliognostTheme.Surface);
        ImGui.BeginChild("details", new Vector2(panelWidth, panelHeight), false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        selectionFlash = Math.Max(0, selectionFlash - ImGui.GetIO().DeltaTime * 1.8f);
        BibliognostTheme.DrawGlowFrame("details-frame", true);
        ImGui.SetCursorPos(new Vector2(10, 10));
        ImGui.BeginChild("details-content", new Vector2(-10, -10), false);
        DrawDetailsHeader(currentDetails.Summary, selectionFlash);
        var favorite = plugin.Configuration.FavoriteMods.Any(item => ModKey(item) == ModKey(currentDetails.Summary));
        if (BibliognostTheme.AccentButton("favorite-mod", favorite ? "★  FAVORITED" : "☆  ADD FAVORITE", new Vector2(145, 28)))
        {
            if (favorite) plugin.Configuration.FavoriteMods.RemoveAll(item => ModKey(item) == ModKey(currentDetails.Summary));
            else plugin.Configuration.FavoriteMods.Insert(0, currentDetails.Summary);
            plugin.Configuration.Save();
        }
        ImGui.SameLine();
        if (BibliognostTheme.AccentButton("collect-mod", "ADD TO COLLECTION", new Vector2(175, 28))) plugin.Library.ShowFor(currentDetails.Summary);
        ImGui.SameLine();
        var queued = plugin.Configuration.InstallationQueue.Any(item => ModKey(item) == ModKey(currentDetails.Summary));
        if (BibliognostTheme.AccentButton("queue-mod", queued ? "REMOVE FROM QUEUE" : "ADD TO QUEUE", new Vector2(165, 28)))
        {
            plugin.Configuration.InstallationQueue.RemoveAll(item => ModKey(item) == ModKey(currentDetails.Summary));
            if (!queued) plugin.Configuration.InstallationQueue.Add(currentDetails.Summary);
            plugin.Configuration.Save();
        }
        if (!string.IsNullOrWhiteSpace(currentDetails.Summary.Author))
        {
            if (BibliognostTheme.AccentButton("all-author-mods", "ALL MODS FROM THIS AUTHOR", new Vector2(235, 29)))
            {
                ShowAuthorMods(currentDetails.Summary.Author);
                ImGui.CloseCurrentPopup();
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip($"Search every connected archive for creator: {currentDetails.Summary.Author}");
        }
        ImGui.Spacing();
        var gallery = currentDetails.ImageUrls.Count > 0 ? currentDetails.ImageUrls : currentDetails.Summary.ThumbnailUrl is null ? [] : [currentDetails.Summary.ThumbnailUrl];
        selectedImageIndex = Math.Clamp(selectedImageIndex, 0, Math.Max(0, gallery.Count - 1));
        var heroUrl = gallery.Count == 0 ? null : gallery[selectedImageIndex];
        var hero = plugin.Thumbnails.Get(heroUrl)?.GetWrapOrDefault();
        if (gallery.Count > 1)
        {
            if (BibliognostTheme.AccentButton("hero-previous", "‹  PREVIOUS", new Vector2(112, 27))) selectedImageIndex = (selectedImageIndex - 1 + gallery.Count) % gallery.Count;
            ImGui.SameLine(); ImGui.TextColored(BibliognostTheme.Dim, $"IMAGE {selectedImageIndex + 1} OF {gallery.Count}"); ImGui.SameLine();
            if (BibliognostTheme.AccentButton("hero-next", "NEXT  ›", new Vector2(92, 27))) selectedImageIndex = (selectedImageIndex + 1) % gallery.Count;
        }
        if (hero is not null && hero.Width > 0 && hero.Height > 0)
        {
            const float heroSafeInset = 6f;
            var contentWidth = ImGui.GetContentRegionAvail().X;
            var maxWidth = Math.Max(1, contentWidth - heroSafeInset * 2);
            var aspect = (float)hero.Width / hero.Height;
            var maxHeroHeight = plugin.Configuration.ShowcaseSize == ShowcaseSize.Cinematic ? 720f : plugin.Configuration.ShowcaseSize == ShowcaseSize.Compact ? 380f : 560f;
            var heroSize = plugin.Configuration.HeroImageFill ? new Vector2(maxWidth, maxHeroHeight) : new Vector2(maxWidth, Math.Min(maxWidth / aspect, maxHeroHeight));
            if (!plugin.Configuration.HeroImageFill && heroSize.Y == maxHeroHeight) heroSize.X = heroSize.Y * aspect;
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + heroSafeInset + Math.Max(0, (maxWidth - heroSize.X) * .5f));
            var imageMin = ImGui.GetCursorScreenPos();
            if (ImGui.InvisibleButton("##hero-image", heroSize)) ImGui.OpenPopup("Full Image Preview");
            ImGui.GetWindowDrawList().AddImage(hero.Handle, imageMin, imageMin + heroSize);
            BibliognostTheme.DrawGlowRect(ImGui.GetWindowDrawList(), imageMin - new Vector2(2), imageMin + heroSize + new Vector2(2), .58f + selectionFlash);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Click to open the full image preview.");
            DrawImagePreview(hero, gallery);
            ImGui.Spacing();
        }
        if (gallery.Count > 1) DrawGalleryStrip(gallery);
        ImGui.SetWindowFontScale(Math.Clamp(plugin.Configuration.DossierTextScale, .85f, 1.5f));
        ImGui.TextColored(BibliognostTheme.Gold, "MOD DOSSIER");
        if (currentDetails.Summary.Version.Length > 0) { ImGui.TextColored(BibliognostTheme.Dim, "VERSION"); ImGui.SameLine(); ImGui.Text(currentDetails.Summary.Version); }
        ImGui.TextColored(BibliognostTheme.Dim, "SOURCES"); ImGui.SameLine(); ImGui.Text(SourceList(currentDetails.Summary));
        ImGui.Spacing();
        DrawWrappedTags(currentDetails.Summary.Tags.Take(18));
        ImGui.SetWindowFontScale(Math.Clamp(plugin.Configuration.DescriptionTextScale, .85f, 1.5f));
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
        ImGui.SetWindowFontScale(Math.Clamp(plugin.Configuration.DossierTextScale, .85f, 1.5f));
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
        DrawRelatedAndComparison(currentDetails);
        ImGui.SetWindowFontScale(Math.Clamp(plugin.Configuration.ButtonTextScale, .85f, 1.4f));
        ImGui.TextColored(BibliognostTheme.Gold, "GET THIS MOD");
        foreach (var sourceDetail in sourceDetails.Count > 0 ? sourceDetails : [currentDetails])
            DrawDeliveryControls(sourceDetail);
        ImGui.Dummy(new Vector2(1, 14));
        ImGui.EndChild();
        ImGui.EndChild();
        ImGui.PopStyleColor();
        ImGui.SetWindowFontScale(1f);
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
            var isHeliosphere = currentDetails.Summary.ProviderId == "heliosphere";
            var providerLabel = isHeliosphere
                ? plugin.IsHeliosphereLoaded ? "INSTALL WITH HELIOSPHERE" : "OPEN IN HELIOSPHERE"
                : "OPEN PROVIDER DOWNLOAD";
            if (BibliognostTheme.AccentButton("view-page", providerLabel, new Vector2(205, 34)))
            {
                if (!isHeliosphere || !plugin.OpenHeliosphereInstaller(currentDetails.Summary))
                    Process.Start(new ProcessStartInfo(currentDetails.Summary.PageUrl) { UseShellExecute = true });
            }
            ImGui.TextColored(BibliognostTheme.Dim, isHeliosphere
                ? plugin.IsHeliosphereLoaded
                    ? "Hands this mod to Heliosphere's in-game installer for confirmation, variants, and Penumbra delivery."
                    : "Install and enable Heliosphere for an in-game handoff; otherwise its official web page will open."
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
        const float height = 112f;
        ImGui.InvisibleButton("##details-header", new Vector2(width, height));
        var max = min + new Vector2(width, height);
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilledMultiColor(min, max, ImGui.GetColorU32(new Vector4(.10f, .075f, .025f, .94f)), ImGui.GetColorU32(new Vector4(.035f, .045f, .075f, .96f)), ImGui.GetColorU32(new Vector4(.018f, .022f, .045f, .98f)), ImGui.GetColorU32(new Vector4(.055f, .035f, .025f, .96f)));
        BibliognostTheme.DrawGlowRect(draw, min, max, .9f + flash);
        draw.AddLine(min + new Vector2(18, height - 18), max - new Vector2(18, 18), ImGui.GetColorU32(new Vector4(1f, .78f, .30f, .72f)), 1.5f);
        const float titleScale = 1.55f;
        var title = FitText(summary.Name.ToUpperInvariant(), (width - 38) / titleScale);
        var titlePos = min + new Vector2(18, 14);
        draw.AddText(ImGui.GetFont(), ImGui.GetFontSize() * titleScale, titlePos + new Vector2(1, 2), ImGui.GetColorU32(new Vector4(1f, .58f, .12f, .30f)), title);
        draw.AddText(ImGui.GetFont(), ImGui.GetFontSize() * titleScale, titlePos, ImGui.GetColorU32(BibliognostTheme.GoldBright), title);
        draw.AddText(min + new Vector2(18, 70), ImGui.GetColorU32(BibliognostTheme.Dim), "CREATED BY  ");
        draw.AddText(min + new Vector2(104, 70), ImGui.GetColorU32(BibliognostTheme.Text), summary.Author.Length == 0 ? "UNKNOWN" : summary.Author);
        ImGui.Spacing();
    }

    internal static void DrawArchiveHeader(Plugin plugin, string subtitle, string id)
    {
        const float height = 126f;
        var min = ImGui.GetCursorScreenPos(); var width = ImGui.GetContentRegionAvail().X;
        ImGui.BeginChild("archive-header-" + id, new Vector2(width, height), false, ImGuiWindowFlags.NoScrollbar);
        var draw = ImGui.GetWindowDrawList(); var max = min + new Vector2(width, height);
        draw.AddRectFilledMultiColor(min, max, ImGui.GetColorU32(new Vector4(.025f, .035f, .065f, .98f)), ImGui.GetColorU32(new Vector4(.055f, .025f, .045f, .98f)), ImGui.GetColorU32(new Vector4(.015f, .018f, .035f, .99f)), ImGui.GetColorU32(new Vector4(.018f, .028f, .055f, .99f)));
        var time = BibliognostTheme.ReducedMotion ? 0f : (float)ImGui.GetTime(); var center = min.X + width * .5f;
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
        var version = $"v{typeof(Plugin).Assembly.GetName().Version?.ToString(3) ?? "unknown"}";
        var versionSize = ImGui.CalcTextSize(version);
        draw.AddText(max - versionSize - new Vector2(12, 8), ImGui.GetColorU32(new Vector4(.55f, .60f, .70f, .88f)), version);
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

    private void DrawImagePreview(Dalamud.Interface.Textures.TextureWraps.IDalamudTextureWrap wrap, IReadOnlyList<string> gallery)
    {
        ImGui.SetNextWindowSizeConstraints(new Vector2(520, 420), new Vector2(1500, 1200));
        if (!ImGui.BeginPopupModal("Full Image Preview", ImGuiWindowFlags.NoScrollbar)) return;
        if (wrap.Width > 0 && wrap.Height > 0)
        {
            var available = ImGui.GetContentRegionAvail() - new Vector2(0, 44);
            var scale = Math.Min(available.X / wrap.Width, available.Y / wrap.Height);
            ImGui.Image(wrap.Handle, new Vector2(wrap.Width, wrap.Height) * Math.Max(.1f, scale) * previewZoom);
        }
        ImGui.SetNextItemWidth(170); ImGui.SliderFloat("ZOOM", ref previewZoom, .5f, 3f, "%.1fx", ImGuiSliderFlags.AlwaysClamp);
        ImGui.SameLine(); if (BibliognostTheme.AccentButton("preview-reset-zoom", "FIT", new Vector2(65, 28))) previewZoom = 1f;
        ImGui.SameLine();
        if (gallery.Count > 1 && BibliognostTheme.AccentButton("preview-previous", "PREVIOUS", new Vector2(105, 28)))
            selectedImageIndex = (selectedImageIndex - 1 + gallery.Count) % gallery.Count;
        if (gallery.Count > 1) ImGui.SameLine();
        if (gallery.Count > 1 && BibliognostTheme.AccentButton("preview-next", "NEXT", new Vector2(85, 28)))
            selectedImageIndex = (selectedImageIndex + 1) % gallery.Count;
        ImGui.SameLine();
        if (BibliognostTheme.AccentButton("preview-close", "CLOSE", new Vector2(85, 28))) ImGui.CloseCurrentPopup();
        ImGui.EndPopup();
    }

    private async Task SearchAsync()
    {
        searchCancellation?.Cancel();
        searchCancellation?.Dispose();
        searchCancellation = new CancellationTokenSource();
        var token = searchCancellation.Token;
        SaveBrowsingSession();
        loading = true; status = providerSelection switch { 1 => "Contacting XIV Mod Archive…", 2 => "Contacting Heliosphere…", 3 => "Contacting Nexus Mods…", _ => "Contacting mod archives…" };
        var result = await plugin.Catalog.SearchAsync(new ModSearchQuery
        {
            SearchText = search, Name = name, Author = author, Races = races, Tags = tags, Affects = affects,
            Gender = gender switch { 1 => "male", 2 => "female", 3 => "unisex", _ => "" },
            Sort = (ModSort)sort, Types = selectedTypes.ToArray(), Page = page, PublishedTodayOnly = latestReleases,
            DawntrailCompatibleOnly = plugin.Configuration.DawntrailCompatibleOnly,
            // XMA's `nsfw=true` means "adult-only", not "include adult results".
            // Show and Follow therefore omit the restriction and return the mixed catalog.
            AdultContent = plugin.Configuration.AdultContent == AdultContentMode.HideAdult ? false : null,
        }, (ProviderSelection)providerSelection, token);
        if (token.IsCancellationRequested) return;
        mods.Clear();
        if (result.Success && result.Value is not null)
        {
            mods.AddRange(result.Value.Take(plugin.Configuration.ResultsPerPage));
            var count = result.TotalCount is { } total
                ? providerSelection == (int)ProviderSelection.All ? $"{mods.Count} shown · {total:N0} provider matches" : $"{mods.Count} shown · {total:N0} total matches"
                : $"{mods.Count} shown · total unavailable";
            status = latestReleases ? $"{count} published today" + (result.Error is null ? "." : $". One source reported: {result.Error}") : count + (result.Error is null ? "." : $". One source reported: {result.Error}");
        }
        else status = result.Error ?? "The archive could not be read.";
        loading = false;
    }

    private async Task LoadDetailsAsync(ModSummary mod)
    {
        plugin.Configuration.RecentlyViewedMods.RemoveAll(item => ModKey(item) == ModKey(mod));
        plugin.Configuration.RecentlyViewedMods.Insert(0, mod);
        if (plugin.Configuration.RecentlyViewedMods.Count > 50)
            plugin.Configuration.RecentlyViewedMods.RemoveRange(50, plugin.Configuration.RecentlyViewedMods.Count - 50);
        plugin.Configuration.Save();
        selectedSummary = mod;
        status = $"Reading {mod.Name}…";
        var result = await plugin.Catalog.GetAllSourceDetailsAsync(mod);
        if (result.Success && result.Value is { Count: > 0 })
        {
            sourceDetails = result.Value;
            details = sourceDetails.FirstOrDefault(item => item.Summary.ProviderId == mod.ProviderId) ?? sourceDetails[0];
            detailOverlayRequested = true;
            var merged = details.Summary;
            var index = mods.FindIndex(item => item.ProviderId == mod.ProviderId && item.RemoteId == mod.RemoteId);
            if (index >= 0) mods[index] = mods[index] with { Sources = merged.Sources };
            status = merged.Sources.Count > 1 ? $"Matched across {merged.Sources.Count} sources." : $"Reading {mod.Name}.";
            selectedImageIndex = 0; previewZoom = 1f; showDescription = false; descriptionExpansion = 0; selectionFlash = 1f;
            comparisonDetails = null;
            _ = LoadRelatedAsync(details.Summary);
        }
        else status = result.Error ?? "Details were unavailable.";
    }

    internal void OpenMod(ModSummary mod)
    {
        IsOpen = true;
        _ = LoadDetailsAsync(mod);
    }

    internal void ForceRefresh()
    {
        plugin.Catalog.ClearSearchCache();
        ResetCatalogScroll();
        _ = SearchAsync();
    }

    internal void ApplySavedSearch(SavedModSearch saved)
    {
        search = saved.SearchText; author = saved.Author; tags = saved.Tags; affects = saved.Affects;
        providerSelection = Math.Clamp(saved.Provider, 0, 3); sort = Math.Clamp(saved.Sort, 0, 5);
        selectedTypes.Clear(); foreach (var type in saved.Types) selectedTypes.Add(type);
        page = 1; highestVisitedPage = 1; libraryView = 0; saved.NewResultCount = 0; plugin.Configuration.Save();
        ResetCatalogScroll();
        _ = SearchAsync();
    }

    private void ResetCatalogScroll()
    {
        plugin.Configuration.LastCatalogScrollY = 0f;
        restoreCatalogScroll = true;
    }

    private void DrawSaveSearchPopup()
    {
        if (!ImGui.BeginPopup("Save Search")) return;
        ImGui.TextColored(BibliognostTheme.GoldBright, "SAVE CURRENT SEARCH AS WATCHLIST");
        ImGui.SetNextItemWidth(300); ImGui.InputTextWithHint("##saved-search-name", "Watchlist name", ref savedSearchName, 60);
        if (BibliognostTheme.AccentButton("confirm-save-search", "SAVE", new Vector2(90, 28)) && savedSearchName.Trim().Length > 0)
        {
            var saved = new SavedModSearch { Name = savedSearchName.Trim(), SearchText = search, Author = author, Tags = tags, Affects = affects, Provider = providerSelection, Sort = sort, Types = selectedTypes.ToList(), LastChecked = DateTimeOffset.Now, LastTopResultKey = mods.Count == 0 ? string.Empty : ModKey(mods[0]) };
            plugin.Configuration.SavedSearches.RemoveAll(item => item.Name.Equals(saved.Name, StringComparison.OrdinalIgnoreCase));
            plugin.Configuration.SavedSearches.Add(saved); plugin.Configuration.Save(); savedSearchName = string.Empty; ImGui.CloseCurrentPopup();
        }
        ImGui.EndPopup();
    }

    private void SaveBrowsingSession()
    {
        plugin.Configuration.LastSearchText = search; plugin.Configuration.LastProviderSelection = providerSelection;
        plugin.Configuration.LastSort = sort; plugin.Configuration.LastPage = page; plugin.Configuration.LastSelectedTypes = selectedTypes.ToList();
        plugin.Configuration.LastNameFilter = name; plugin.Configuration.LastAuthorFilter = author; plugin.Configuration.LastRaceFilter = races;
        plugin.Configuration.LastTagFilter = tags; plugin.Configuration.LastAffectsFilter = affects; plugin.Configuration.LastGenderFilter = gender;
        plugin.Configuration.Save();
    }

    private void HandleCatalogNavigation()
    {
        if (mods.Count == 0 || ImGui.GetIO().WantTextInput) return;
        var moved = false;
        if (ImGui.IsKeyPressed(ImGuiKey.LeftArrow) || ImGui.IsKeyPressed(ImGuiKey.GamepadDpadLeft)) { selectedCardIndex--; moved = true; }
        if (ImGui.IsKeyPressed(ImGuiKey.RightArrow) || ImGui.IsKeyPressed(ImGuiKey.GamepadDpadRight)) { selectedCardIndex++; moved = true; }
        if (ImGui.IsKeyPressed(ImGuiKey.UpArrow) || ImGui.IsKeyPressed(ImGuiKey.GamepadDpadUp)) { selectedCardIndex -= lastGridColumns; moved = true; }
        if (ImGui.IsKeyPressed(ImGuiKey.DownArrow) || ImGui.IsKeyPressed(ImGuiKey.GamepadDpadDown)) { selectedCardIndex += lastGridColumns; moved = true; }
        selectedCardIndex = Math.Clamp(selectedCardIndex, 0, mods.Count - 1);
        if (moved) status = $"Selected {mods[selectedCardIndex].Name}. Press Enter to open.";
        if ((ImGui.IsKeyPressed(ImGuiKey.Enter) || ImGui.IsKeyPressed(ImGuiKey.GamepadFaceDown)) && details is null) _ = LoadDetailsAsync(mods[selectedCardIndex]);
        if ((ImGui.IsKeyPressed(ImGuiKey.Escape) || ImGui.IsKeyPressed(ImGuiKey.GamepadFaceRight)) && details is not null) { details = null; ImGui.CloseCurrentPopup(); }
    }

    private async Task LoadRelatedAsync(ModSummary current)
    {
        var result = await plugin.Catalog.SearchAsync(new ModSearchQuery { SearchText = current.ModType, Author = string.Empty, Tags = string.Join(' ', current.Tags.Take(3)), Sort = ModSort.Relevance, DawntrailCompatibleOnly = plugin.Configuration.DawntrailCompatibleOnly }, ProviderSelection.All);
        relatedMods.Clear();
        if (result.Success && result.Value is not null) relatedMods.AddRange(result.Value.Where(item => ModKey(item) != ModKey(current)).OrderByDescending(item => RelatedScore(current, item)).Take(6));
    }

    private void DrawRelatedAndComparison(ModDetails current)
    {
        if (comparisonDetails is not null)
        {
            ImGui.TextColored(BibliognostTheme.Gold, "COMPARE MODS");
            if (ImGui.BeginTable("comparison", 2, ImGuiTableFlags.Borders | ImGuiTableFlags.SizingStretchSame))
            {
                foreach (var item in new[] { current, comparisonDetails }) { ImGui.TableNextColumn(); ImGui.TextWrapped(item.Summary.Name); ImGui.TextColored(BibliognostTheme.Dim, item.Summary.Author); ImGui.Text($"Version {item.Summary.Version}"); ImGui.TextWrapped(SourceList(item.Summary)); ImGui.TextWrapped(string.Join(", ", item.Summary.Tags.Take(8))); }
                ImGui.EndTable();
            }
            if (BibliognostTheme.AccentButton("close-comparison", "CLOSE COMPARISON", new Vector2(175, 27))) comparisonDetails = null;
        }
        if (relatedMods.Count == 0) return;
        ImGui.TextColored(BibliognostTheme.Gold, "RELATED MODS");
        ImGui.TextColored(BibliognostTheme.Dim, "Ranked by listing type, creator, tags, and affected metadata—not by page proximity.");
        foreach (var mod in relatedMods.Take(4))
        {
            ImGui.PushID("related-" + ModKey(mod)); ImGui.TextWrapped($"{mod.Name} · {mod.Author}");
            if (BibliognostTheme.AccentButton("open-related", "OPEN", new Vector2(78, 25))) _ = LoadDetailsAsync(mod);
            ImGui.SameLine(); if (BibliognostTheme.AccentButton("compare-related", "COMPARE", new Vector2(95, 25))) _ = LoadComparisonAsync(mod);
            ImGui.PopID();
        }
    }

    private async Task LoadComparisonAsync(ModSummary mod)
    {
        var result = await plugin.Catalog.GetDetailsAsync(mod); if (result.Success) comparisonDetails = result.Value;
    }

    private static float RelatedScore(ModSummary left, ModSummary right)
    {
        var score = left.ModType.Equals(right.ModType, StringComparison.OrdinalIgnoreCase) ? 3f : 0f;
        if (left.Author.Equals(right.Author, StringComparison.OrdinalIgnoreCase)) score += 2f;
        score += left.Tags.Intersect(right.Tags, StringComparer.OrdinalIgnoreCase).Count(); return score;
    }

    private static bool IsNewer(string available, string installed)
    {
        if (available.Length == 0 || installed.Length == 0) return false;
        return Version.TryParse(available.TrimStart('v', 'V'), out var a) && Version.TryParse(installed.TrimStart('v', 'V'), out var b) ? a > b : !available.Equals(installed, StringComparison.OrdinalIgnoreCase);
    }

    private static string FitText(string value, float maxWidth)
    {
        if (ImGui.CalcTextSize(value).X <= maxWidth) return value;
        while (value.Length > 2 && ImGui.CalcTextSize(value + "…").X > maxWidth) value = value[..^1];
        return value + "…";
    }
    private static string ModKey(ModSummary mod) => $"{mod.ProviderId}:{mod.RemoteId}";
    private static void DrawBackdrop()
    {
        var draw = ImGui.GetWindowDrawList();
        var min = ImGui.GetWindowPos(); var max = min + ImGui.GetWindowSize();
        draw.AddRectFilled(min, max, ImGui.GetColorU32(BibliognostTheme.Surface));
        for (var x = min.X; x < max.X; x += 42) draw.AddLine(new Vector2(x, min.Y), new Vector2(x, max.Y), ImGui.GetColorU32(BibliognostTheme.Gold with { W = .035f }));
    }
}
