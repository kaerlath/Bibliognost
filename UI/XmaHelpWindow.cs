using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace Bibliognost.UI;

public sealed class XmaHelpWindow : Window
{
    private readonly Plugin plugin;

    public XmaHelpWindow(Plugin plugin) : base("Bibliognost Help — Connecting XIV Mod Archive###BibliognostXmaHelp")
    {
        this.plugin = plugin;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(640, 560), MaximumSize = new Vector2(1100, 1300) };
    }

    public override void Draw()
    {
        DrawBackdrop();
        MainWindow.DrawArchiveHeader(plugin, "GUIDANCE · SECURITY · CONNECTION", "xma-help-banner");
        ImGui.TextColored(BibliognostTheme.GoldBright, "CONNECTING XIV MOD ARCHIVE");
        ImGui.TextWrapped("This guide assumes you have never used browser Developer Tools before. Nothing in these steps changes the website or your browser.");
        ImGui.Spacing();
        Warning("Treat connect.sid exactly like a password. Never paste it into Discord, a support message, or a screenshot. Bibliognost encrypts it for your Windows user and sends it only to XIV Mod Archive.");

        Section("MICROSOFT EDGE OR GOOGLE CHROME");
        Step(1, "SIGN IN NORMALLY", "In Bibliognost Settings, choose SIGN IN TO XMA. Complete XMA's official Discord sign-in in your browser, then return to an XIV Mod Archive page.");
        Step(2, "OPEN DEVELOPER TOOLS", "Press F12. If your keyboard uses media keys, hold Fn while pressing F12. Ctrl + Shift + I also works. Seeing a Welcome page means you are in the correct place.");
        Step(3, "OPEN THE APPLICATION TOOL", "At the top of Developer Tools, choose the + button and select Application. If Application is already visible as a tab, select it directly.");
        Step(4, "OPEN XMA'S COOKIES", "In the left column, expand Storage, then Cookies. Select https://www.xivmodarchive.com. If both www and non-www addresses appear, check both.");
        Step(5, "FIND CONNECT.SID", "Use the cookie table's filter box and type connect.sid. Select the row whose Name is connect.sid.");
        Step(6, "COPY ONLY THE VALUE", "Double-click the cell in the Value column. Press Ctrl + A, then Ctrl + C. The value may be extremely long. Do not copy its name, domain, expiration date, or the entire row.");
        Step(7, "SAVE THE CONNECTION", "Return to Bibliognost Settings, paste the value into the XMA session field, then choose SAVE CONNECTION. The field clears afterward by design. The green saved-securely indicator confirms it was stored.");

        Section("MOZILLA FIREFOX");
        Step(1, "SIGN IN AND OPEN TOOLS", "Sign in to XIV Mod Archive, remain on that site, then press F12 or Ctrl + Shift + I.");
        Step(2, "OPEN STORAGE", "Select the Storage tab. If it is hidden, open the » overflow menu and select Storage.");
        Step(3, "FIND THE COOKIE", "Expand Cookies, select the XIV Mod Archive address, find connect.sid, and copy its complete Value.");
        Step(4, "SAVE IN BIBLIOGNOST", "Paste the value into the XMA session field and choose SAVE CONNECTION.");

        Section("IF CONNECT.SID IS MISSING");
        Bullet("Make certain Developer Tools belongs to the XIV Mod Archive tab—not Discord, a new tab, or browser Settings.");
        Bullet("Reload the XMA page once while Developer Tools remains open.");
        Bullet("Check both www.xivmodarchive.com and xivmodarchive.com under Cookies.");
        Bullet("Widen Developer Tools or use its + / » menus to reveal Application or Storage.");
        Bullet("If a saved connection stops working, sign in again and replace it. Signing out of XMA can invalidate the session.");

        Section("WHY THIS STEP IS MANUAL");
        ImGui.TextWrapped("XIV Mod Archive currently signs users in through Discord but does not provide Bibliognost with an official OAuth or plugin-login interface. Its session is intentionally HttpOnly. Bibliognost will not read browser credential databases and will never ask for your Discord password. If XMA provides an official authorization method later, this manual step can be replaced.");
        ImGui.Spacing();
        if (BibliognostTheme.AccentButton("close-help", "CLOSE HELP", new Vector2(140, 32))) IsOpen = false;
    }

    private static void Step(int number, string title, string body)
    {
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(.035f, .048f, .075f, .92f));
        ImGui.BeginChild($"help-step-{title}", new Vector2(0, 104), true, ImGuiWindowFlags.NoScrollbar);
        ImGui.TextColored(BibliognostTheme.GoldBright, $"{number:00}  {title}");
        ImGui.Separator(); ImGui.TextWrapped(body);
        ImGui.EndChild(); ImGui.PopStyleColor(); ImGui.Spacing();
    }

    private static void Section(string title)
    {
        ImGui.Spacing(); ImGui.TextColored(BibliognostTheme.Gold, title);
        ImGui.Separator(); ImGui.Spacing();
    }

    private static void Warning(string text)
    {
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(.16f, .09f, .025f, .92f));
        ImGui.BeginChild("security-warning", new Vector2(0, 100), true, ImGuiWindowFlags.NoScrollbar);
        ImGui.TextColored(new Vector4(1f, .72f, .28f, 1f), "SECURITY WARNING"); ImGui.TextWrapped(text);
        ImGui.EndChild(); ImGui.PopStyleColor();
    }

    private static void Bullet(string text) { ImGui.Bullet(); ImGui.SameLine(); ImGui.TextWrapped(text); }

    private static void DrawBackdrop()
    {
        var draw = ImGui.GetWindowDrawList(); var min = ImGui.GetWindowPos(); var max = min + ImGui.GetWindowSize();
        draw.AddRectFilled(min, max, ImGui.GetColorU32(BibliognostTheme.Surface));
        for (var x = min.X; x < max.X; x += 42) draw.AddLine(new Vector2(x, min.Y), new Vector2(x, max.Y), ImGui.GetColorU32(new Vector4(.4f, .32f, .16f, .035f)));
    }
}
