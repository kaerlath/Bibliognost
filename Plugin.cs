using Bibliognost.Images;
using Bibliognost.Downloads;
using Bibliognost.Models;
using Bibliognost.Providers.XivModArchive;
using Bibliognost.Providers.Heliosphere;
using Bibliognost.Providers.NexusMods;
using Bibliognost.Providers;
using Bibliognost.Security;
using Bibliognost.Services;
using Bibliognost.UI;
using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace Bibliognost;

public sealed class Plugin : IDalamudPlugin
{
    private const string Command = "/bibliognost";
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    private readonly XmaHttpClient http = new();
    private readonly HeliosphereHttpClient heliosphereHttp = new();
    private readonly NexusModsClient nexusHttp = new();
    internal Configuration Configuration { get; }
    internal XmaProvider Provider { get; }
    internal HeliosphereProvider Heliosphere { get; }
    internal NexusModsProvider Nexus { get; }
    internal ModCatalog Catalog { get; }
    internal ThumbnailCache Thumbnails { get; }
    internal WindowSystem Windows { get; } = new("Bibliognost");
    internal MainWindow Main { get; }
    internal SettingsWindow Settings { get; }
    internal XmaHelpWindow Help { get; }
    internal UpdatesWindow Updates { get; }
    internal TitleFontManager TitleFonts { get; }
    internal ModDeliveryService Delivery { get; }
    internal Dalamud.Interface.ManagedFontAtlas.IFontHandle? BannerFont => TitleFonts.Handle;

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        TitleFonts = new TitleFontManager(Configuration);
        Delivery = new ModDeliveryService(Configuration);
        if (Configuration.Version < 2)
        {
            Configuration.CardWidth = Math.Max(Configuration.CardWidth * 2f, 640f);
            Configuration.Version = 2;
            Configuration.Save();
        }
        Provider = new XmaProvider(http);
        Heliosphere = new HeliosphereProvider(heliosphereHttp);
        Nexus = new NexusModsProvider(nexusHttp);
        Catalog = new ModCatalog([Provider, Heliosphere, Nexus], Configuration);
        Provider.SetSession(ProtectedSecretStore.TryUnprotect(Configuration.EncryptedXmaSession));
        Nexus.SetApiKey(ProtectedSecretStore.TryUnprotectNexus(Configuration.EncryptedNexusApiKey));
        Thumbnails = new ThumbnailCache(Path.Combine(PluginInterface.GetPluginConfigDirectory(), "thumbnails"), TextureProvider);
        Main = new MainWindow(this);
        Settings = new SettingsWindow(this);
        Help = new XmaHelpWindow(this);
        Updates = new UpdatesWindow(this);
        Windows.AddWindow(Main); Windows.AddWindow(Settings); Windows.AddWindow(Help); Windows.AddWindow(Updates);
        CommandManager.AddHandler(Command, new CommandInfo((_, _) => Main.Toggle()) { HelpMessage = "Open Bibliognost." });
        PluginInterface.UiBuilder.Draw += Windows.Draw;
        PluginInterface.UiBuilder.OpenMainUi += Main.Toggle;
        PluginInterface.UiBuilder.OpenConfigUi += Settings.Toggle;
    }

    internal void SetNexusApiKey(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        Configuration.EncryptedNexusApiKey = normalized is null ? null : ProtectedSecretStore.ProtectNexus(normalized);
        Nexus.SetApiKey(normalized);
        Configuration.Save();
    }

    internal void SetXmaSession(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (normalized?.StartsWith("connect.sid=", StringComparison.OrdinalIgnoreCase) == true)
            normalized = normalized["connect.sid=".Length..].Trim();
        if (normalized?.Length >= 2 && normalized[0] == '"' && normalized[^1] == '"')
            normalized = normalized[1..^1];
        Configuration.EncryptedXmaSession = normalized is null ? null : ProtectedSecretStore.Protect(normalized);
        Provider.SetSession(normalized);
        Configuration.Save();
    }

    internal void SetTitleFont(SystemFontChoice? choice)
    {
        if (!TitleFonts.Apply(choice?.Name, choice?.Path)) return;
        Configuration.TitleFontName = choice?.Name;
        Configuration.TitleFontPath = choice?.Path;
        Configuration.Save();
    }

    internal Task DeliverAsync(ModDetails details, bool install)
        => Delivery.DeliverAsync(details, install, details.Summary.ProviderId == XmaProvider.ProviderId ? http.GetDownloadAsync : null);

    public void Dispose()
    {
        PluginInterface.UiBuilder.Draw -= Windows.Draw;
        PluginInterface.UiBuilder.OpenMainUi -= Main.Toggle;
        PluginInterface.UiBuilder.OpenConfigUi -= Settings.Toggle;
        CommandManager.RemoveHandler(Command);
        Windows.RemoveAllWindows();
        Updates.Dispose();
        TitleFonts.Dispose();
        Delivery.Dispose(); Thumbnails.Dispose(); http.Dispose(); heliosphereHttp.Dispose(); nexusHttp.Dispose();
    }
}
