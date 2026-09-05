using Dalamud.Interface.ManagedFontAtlas;

namespace Bibliognost.Services;

internal sealed class TitleFontManager : IDisposable
{
    private IFontHandle? handle;
    internal IReadOnlyList<SystemFontChoice> Fonts { get; } = SystemFontCatalog.Discover();
    internal IFontHandle? Handle => handle;
    internal string Status { get; private set; } = string.Empty;

    internal TitleFontManager(Configuration configuration) => Apply(configuration.TitleFontName, configuration.TitleFontPath);

    internal bool Apply(string? name, string? path)
    {
        var bundledPath = BundledCharitoPath();
        var selectedPath = string.IsNullOrWhiteSpace(path) ? FirstAvailable(bundledPath, SystemFontCatalog.FindAutomaticFont()) : path;
        var selectedName = string.IsNullOrWhiteSpace(path) ? (File.Exists(bundledPath) ? "Charito (bundled)" : "Automatic") : name ?? Path.GetFileNameWithoutExtension(path);
        if (selectedPath is not null && !File.Exists(selectedPath))
        {
            selectedPath = FirstAvailable(bundledPath, SystemFontCatalog.FindAutomaticFont());
            selectedName = File.Exists(bundledPath) ? "Charito (saved font was unavailable)" : "Automatic (saved font was unavailable)";
        }

        try
        {
            var replacement = Plugin.PluginInterface.UiBuilder.FontAtlas.NewDelegateFontHandle(entry =>
            {
                entry.OnPreBuild(toolkit =>
                {
                    if (selectedPath is not null)
                        toolkit.AddFontFromFile(selectedPath, new SafeFontConfig { SizePx = 48f });
                    else
                        toolkit.AddDalamudDefaultFont(46f);
                });
            });
            _ = replacement.WaitAsync();
            var previous = handle;
            handle = replacement;
            previous?.Dispose();
            Status = selectedPath is null ? "Using the Dalamud default font." : $"Using {selectedName}.";
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Could not load title font {Font}; retaining the previous title font.", selectedName);
            Status = $"Could not load {selectedName}; the previous safe font remains active.";
            return false;
        }
    }

    private static string BundledCharitoPath()
        => Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location) ?? AppContext.BaseDirectory, "Assets", "Fonts", "Charito.ttf");

    private static string? FirstAvailable(params string?[] paths) => paths.FirstOrDefault(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path));

    public void Dispose() { handle?.Dispose(); handle = null; }
}
