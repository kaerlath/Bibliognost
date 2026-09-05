using System.Numerics;
using System.Diagnostics;
using Bibliognost.Security;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Windowing;

namespace Bibliognost.UI;

public sealed class SettingsWindow : Window
{
    private readonly Plugin plugin;
    private string session = string.Empty;
    private string nexusApiKey = string.Empty;
    private string status;
    private string nexusStatus;
    private bool busy;
    private readonly FileDialogManager dialogs = new();

    public SettingsWindow(Plugin plugin) : base("Bibliognost Settings###BibliognostSettings")
    {
        this.plugin = plugin;
        status = HasXmaSession ? "Session saved securely for this Windows user." : "No XMA session is currently saved.";
        nexusStatus = HasNexusKey ? "API key saved securely for this Windows user." : "No Nexus Mods API key is currently saved.";
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(620, 600), MaximumSize = new Vector2(1200, 1400) };
    }

    public override void Draw()
    {
        DrawBackdrop();
        MainWindow.DrawArchiveHeader(plugin, "CONNECTIONS · PRIVACY · DISPLAY", "settings-banner");
        DrawSectionTitle("PROVIDERS", "CONNECTED ARCHIVES");
        DrawProviderState(true, "HELIOSPHERE", "Public GraphQL catalog · no sign-in required");
        DrawProviderState(HasXmaSession, "XIV MOD ARCHIVE", HasXmaSession ? "Public catalog · account session saved" : "Public catalog · optional account connection");
        DrawProviderState(HasNexusKey, "NEXUS MODS", HasNexusKey ? "Final Fantasy XIV catalog · API key saved" : "Final Fantasy XIV catalog · API key required");
        DrawSectionTitle("XIV MOD ARCHIVE", "SECURE SESSION CONNECTION");
        ImGui.TextWrapped("XMA signs players in through Discord. XMA does not provide Bibliognost with a safe password or app-login API, so your password should never be typed into this plugin.");
        ImGui.Spacing();
        if (BibliognostTheme.AccentButton("open-login", "1  SIGN IN TO XMA", new Vector2(180, 34)))
            Process.Start(new ProcessStartInfo("https://www.xivmodarchive.com/login") { UseShellExecute = true });
        ImGui.SameLine();
        ImGui.TextColored(BibliognostTheme.Dim, "Opens XMA's official Discord sign-in in your browser.");
        if (BibliognostTheme.AccentButton("xma-cookie-help", "?  COOKIE HELP — STEP BY STEP", new Vector2(240, 34))) plugin.Help.IsOpen = true;
        ImGui.SameLine(); ImGui.TextColored(BibliognostTheme.Gold, "New to Developer Tools? Start here.");
        ImGui.Spacing();
        ImGui.TextWrapped("2  After signing in, copy the value of the connect.sid cookie from xivmodarchive.com. This is an XMA limitation; browser credential databases are deliberately not read automatically.");
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("In your browser's developer tools: Storage or Application → Cookies → xivmodarchive.com → connect.sid");
        ImGui.TextWrapped("3  Paste that value below, then save the connection.");
        ImGui.Spacing();
        DrawSavedState(HasXmaSession, "XMA SESSION");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##session", HasXmaSession ? "Saved securely — paste here only to replace it" : "Paste connect.sid value", ref session, 4096, ImGuiInputTextFlags.Password);
        ImGui.Spacing();
        if (!busy && BibliognostTheme.AccentButton("save-session", "SAVE CONNECTION", new Vector2(160, 34))) _ = SaveAndVerifyAsync();
        ImGui.SameLine();
        if (!busy && BibliognostTheme.AccentButton("clear-session", "CLEAR SESSION", new Vector2(150, 34)))
        {
            session = string.Empty;
            plugin.SetXmaSession(null);
            status = "Stored XMA session cleared.";
        }
        ImGui.TextWrapped(status);
        DrawSectionTitle("NEXUS MODS", "SECURE API CONNECTION");
        ImGui.TextWrapped("Bibliognost restricts every Nexus request to Final Fantasy XIV. During private testing, Nexus uses a personal API key; a public release will use Nexus SSO after the application is registered.");
        if (BibliognostTheme.AccentButton("nexus-key-page", "1  GET API KEY", new Vector2(180, 34)))
            Process.Start(new ProcessStartInfo("https://www.nexusmods.com/users/myaccount?tab=api%20access") { UseShellExecute = true });
        ImGui.SameLine(); ImGui.TextColored(BibliognostTheme.Dim, "Opens your official Nexus Mods API access page.");
        ImGui.TextWrapped("2  Create or copy your personal key, paste it below, then verify it.");
        DrawSavedState(HasNexusKey, "NEXUS API KEY");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##nexus-api-key", HasNexusKey ? "Saved securely — paste here only to replace it" : "Paste personal API key", ref nexusApiKey, 4096, ImGuiInputTextFlags.Password);
        if (!busy && BibliognostTheme.AccentButton("save-nexus", "SAVE & VERIFY", new Vector2(160, 34))) _ = SaveAndVerifyNexusAsync();
        ImGui.SameLine();
        if (!busy && BibliognostTheme.AccentButton("clear-nexus", "CLEAR KEY", new Vector2(150, 34)))
        {
            nexusApiKey = string.Empty; plugin.SetNexusApiKey(null); nexusStatus = "Stored Nexus Mods key cleared.";
        }
        ImGui.TextWrapped(nexusStatus);
        DrawSectionTitle("CATALOG LAYOUT", "PRESENTATION");
        var cardWidth = plugin.Configuration.CardWidth;
        ImGui.SetNextItemWidth(280);
        if (ImGui.SliderFloat("Card size", ref cardWidth, 480, 900, "%.0f px", ImGuiSliderFlags.AlwaysClamp))
        {
            plugin.Configuration.CardWidth = cardWidth;
            plugin.Configuration.Save();
        }
        ImGui.TextColored(BibliognostTheme.Dim, "The grid automatically reflows as the window or cards change size.");
        var compactCards = plugin.Configuration.CompactCards;
        if (ImGui.Checkbox("Compact card layout", ref compactCards)) { plugin.Configuration.CompactCards = compactCards; plugin.Configuration.Save(); }
        DrawSectionTitle("DOWNLOADS", "DELIVERY & HISTORY");
        var downloadDirectory = EffectiveDownloadDirectory();
        ImGui.TextColored(BibliognostTheme.Dim, "DOWNLOAD LOCATION");
        ImGui.SameLine(); ImGui.TextWrapped(downloadDirectory);
        if (BibliognostTheme.AccentButton("choose-download-directory", "CHOOSE FOLDER…", new Vector2(150, 30)))
            dialogs.OpenFolderDialog("Choose Bibliognost Download Folder", SelectDownloadDirectory, downloadDirectory, true);
        ImGui.SameLine();
        if (BibliognostTheme.AccentButton("reset-download-directory", "USE WINDOWS DOWNLOADS", new Vector2(205, 30)))
        { plugin.Configuration.DownloadDirectory = string.Empty; plugin.Configuration.Save(); }
        var keep = plugin.Configuration.KeepDownloadedPackages;
        if (ImGui.Checkbox("Keep downloaded package after Penumbra import", ref keep)) { plugin.Configuration.KeepDownloadedPackages = keep; plugin.Configuration.Save(); }
        ImGui.TextColored(BibliognostTheme.Dim, "RECENT DELIVERY HISTORY");
        foreach (var entry in plugin.Configuration.DeliveryHistory.Take(5)) ImGui.TextWrapped(entry);
        if (plugin.Configuration.DeliveryHistory.Count > 0 && BibliognostTheme.AccentButton("clear-history", "CLEAR HISTORY", new Vector2(140, 28)))
        { plugin.Configuration.DeliveryHistory.Clear(); plugin.Configuration.Save(); }
        DrawSectionTitle("TITLE TYPOGRAPHY", "WINDOWS FONT LIBRARY");
        DrawTitleFontPicker();
        DrawSectionTitle("CONTENT VISIBILITY", "BROWSING POLICY");
        var mode = (int)plugin.Configuration.AdultContent;
        ImGui.RadioButton("Follow XMA account", ref mode, 0);
        ImGui.RadioButton("Hide adult results", ref mode, 1);
        ImGui.RadioButton("Show permitted adult results", ref mode, 2);
        var blur = plugin.Configuration.BlurAdultPreviews;
        ImGui.Checkbox("Obscure adult previews until hovered", ref blur);
        var dt = plugin.Configuration.DawntrailCompatibleOnly;
        ImGui.Checkbox("Dawntrail-compatible results only", ref dt);
        if (mode != (int)plugin.Configuration.AdultContent || blur != plugin.Configuration.BlurAdultPreviews || dt != plugin.Configuration.DawntrailCompatibleOnly)
        {
            plugin.Configuration.AdultContent = (AdultContentMode)mode;
            plugin.Configuration.BlurAdultPreviews = blur;
            plugin.Configuration.DawntrailCompatibleOnly = dt;
            plugin.Configuration.Save();
        }
        dialogs.Draw();
    }

    private string EffectiveDownloadDirectory() => string.IsNullOrWhiteSpace(plugin.Configuration.DownloadDirectory)
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")
        : plugin.Configuration.DownloadDirectory;

    private void SelectDownloadDirectory(bool success, string path)
    {
        if (!success || string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;
        plugin.Configuration.DownloadDirectory = Path.GetFullPath(path);
        plugin.Configuration.Save();
    }

    private void DrawTitleFontPicker()
    {
        ImGui.TextWrapped("Choose the display face used by the large BIBLIOGNOST wordmark. The preview updates immediately and your choice is remembered.");
        ImGui.Spacing();
        var selectedName = string.IsNullOrWhiteSpace(plugin.Configuration.TitleFontPath)
            ? "Charito — bundled default"
            : plugin.Configuration.TitleFontName ?? Path.GetFileNameWithoutExtension(plugin.Configuration.TitleFontPath);
        ImGui.SetNextItemWidth(Math.Min(440, ImGui.GetContentRegionAvail().X));
        ImGui.SetNextWindowSizeConstraints(new Vector2(360, 0), new Vector2(700, 420));
        if (ImGui.BeginCombo("##title-font", selectedName))
        {
            if (ImGui.Selectable("Charito — bundled default", string.IsNullOrWhiteSpace(plugin.Configuration.TitleFontPath)))
                plugin.SetTitleFont(null);
            ImGui.Separator();
            foreach (var font in plugin.TitleFonts.Fonts)
            {
                var selected = string.Equals(plugin.Configuration.TitleFontPath, font.Path, StringComparison.OrdinalIgnoreCase);
                if (ImGui.Selectable(font.Name, selected)) plugin.SetTitleFont(font);
                if (selected) ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }
        ImGui.SameLine();
        if (BibliognostTheme.AccentButton("reset-title-font", "RESET", new Vector2(92, 28))) plugin.SetTitleFont(null);

        ImGui.Spacing();
        var start = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        const float height = 92;
        ImGui.Dummy(new Vector2(width, height));
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(start, start + new Vector2(width, height), ImGui.GetColorU32(new Vector4(.012f, .018f, .033f, .94f)), 5);
        draw.AddRect(start, start + new Vector2(width, height), ImGui.GetColorU32(BibliognostTheme.Gold), 5, ImDrawFlags.None, 1.2f);
        var label = "BIBLIOGNOST";
        if (plugin.BannerFont is not null)
        {
            using (plugin.BannerFont.Push())
            {
                var size = ImGui.CalcTextSize(label);
                ImGui.SetCursorScreenPos(start + new Vector2(Math.Max(10, (width - size.X) * .5f), 10));
                ImGui.TextColored(new Vector4(.86f, .92f, 1f, 1f), label);
            }
        }
        ImGui.SetCursorScreenPos(start + new Vector2(0, height));
        ImGui.TextColored(BibliognostTheme.Dim, plugin.TitleFonts.Status);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("If this font disappears or cannot be opened, Bibliognost falls back to Segoe UI Black, Segoe UI Bold, Arial Black, then Dalamud's default font.");
    }

    private async Task SaveAndVerifyAsync()
    {
        busy = true;
        try
        {
            plugin.SetXmaSession(session);
            session = string.Empty;
            var result = await plugin.Provider.VerifyAuthenticationAsync();
            status = result.Message + (result.AccountName is null ? "" : $" Signed in as {result.AccountName}.");
        }
        finally { busy = false; }
    }

    private async Task SaveAndVerifyNexusAsync()
    {
        busy = true;
        try
        {
            plugin.SetNexusApiKey(nexusApiKey);
            nexusApiKey = string.Empty;
            var result = await plugin.Nexus.VerifyAuthenticationAsync();
            nexusStatus = result.Message + (result.AccountName is null ? "" : $" Connected as {result.AccountName}.");
        }
        finally { busy = false; }
    }

    private bool HasXmaSession => !string.IsNullOrWhiteSpace(plugin.Configuration.EncryptedXmaSession);
    private bool HasNexusKey => !string.IsNullOrWhiteSpace(plugin.Configuration.EncryptedNexusApiKey);

    private static void DrawProviderState(bool connected, string label, string description)
    {
        ImGui.TextColored(connected ? new Vector4(.42f, .90f, .60f, 1) : BibliognostTheme.Gold, $"●  {label}");
        ImGui.SameLine(); ImGui.TextColored(BibliognostTheme.Dim, description);
    }


    private static void DrawSavedState(bool saved, string label)
    {
        var color = saved ? new Vector4(.42f, .90f, .60f, 1f) : BibliognostTheme.Dim;
        ImGui.TextColored(color, saved ? $"●  {label} SAVED SECURELY" : $"○  {label} NOT SAVED");
        if (saved && ImGui.IsItemHovered()) ImGui.SetTooltip("The secret is encrypted for your current Windows user and automatically restored when Bibliognost starts.");
    }

    private static void DrawSectionTitle(string title, string subtitle)
    {
        ImGui.Spacing();
        var start = ImGui.GetCursorScreenPos(); var width = ImGui.GetContentRegionAvail().X;
        ImGui.Dummy(new Vector2(width, 38));
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilledMultiColor(start, start + new Vector2(width, 36), ImGui.GetColorU32(new Vector4(.08f, .055f, .025f, .78f)), ImGui.GetColorU32(new Vector4(.025f, .04f, .075f, .82f)), ImGui.GetColorU32(new Vector4(.018f, .024f, .045f, .88f)), ImGui.GetColorU32(new Vector4(.045f, .028f, .025f, .84f)));
        draw.AddLine(start + new Vector2(0, 35), start + new Vector2(width, 35), ImGui.GetColorU32(BibliognostTheme.Gold), 1.2f);
        draw.AddText(start + new Vector2(10, 6), ImGui.GetColorU32(BibliognostTheme.GoldBright), title);
        var subSize = ImGui.CalcTextSize(subtitle);
        draw.AddText(start + new Vector2(Math.Max(12, width - subSize.X - 10), 7), ImGui.GetColorU32(BibliognostTheme.Dim), subtitle);
    }

    private static void DrawBackdrop()
    {
        var draw = ImGui.GetWindowDrawList(); var min = ImGui.GetWindowPos(); var max = min + ImGui.GetWindowSize();
        draw.AddRectFilled(min, max, ImGui.GetColorU32(BibliognostTheme.Surface));
        for (var x = min.X; x < max.X; x += 42) draw.AddLine(new Vector2(x, min.Y), new Vector2(x, max.Y), ImGui.GetColorU32(new Vector4(.4f, .32f, .16f, .035f)));
    }
}
