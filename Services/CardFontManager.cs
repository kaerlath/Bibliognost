using Dalamud.Interface.ManagedFontAtlas;

namespace Bibliognost.Services;

internal enum CardFontRole { Title, Author, Type }

internal sealed class CardFontManager : IDisposable
{
    private readonly Dictionary<CardFontRole, IFontHandle> handles = [];
    internal IReadOnlyList<SystemFontChoice> Fonts { get; } = SystemFontCatalog.Discover();

    internal CardFontManager(Configuration configuration) => Apply(configuration);

    internal IDisposable? Push(CardFontRole role) => handles.TryGetValue(role, out var handle) ? handle.Push() : null;

    internal bool Apply(Configuration configuration)
    {
        var replacements = new Dictionary<CardFontRole, IFontHandle>();
        try
        {
            replacements[CardFontRole.Title] = Create(configuration.CardTitleFontPath, configuration.CardTitleFontSize);
            replacements[CardFontRole.Author] = Create(configuration.CardAuthorFontPath, configuration.CardAuthorFontSize);
            replacements[CardFontRole.Type] = Create(configuration.CardTypeFontPath, configuration.CardTypeFontSize);
            foreach (var handle in replacements.Values) _ = handle.WaitAsync();
            foreach (var handle in handles.Values) handle.Dispose();
            handles.Clear();
            foreach (var pair in replacements) handles[pair.Key] = pair.Value;
            return true;
        }
        catch (Exception ex)
        {
            foreach (var handle in replacements.Values) handle.Dispose();
            Plugin.Log.Warning(ex, "Could not rebuild catalogue fonts; retaining the previous safe fonts.");
            return false;
        }
    }

    private static IFontHandle Create(string? path, float size)
    {
        var safePath = !string.IsNullOrWhiteSpace(path) && File.Exists(path) ? path : null;
        return Plugin.PluginInterface.UiBuilder.FontAtlas.NewDelegateFontHandle(entry => entry.OnPreBuild(toolkit =>
        {
            if (safePath is not null) toolkit.AddFontFromFile(safePath, new SafeFontConfig { SizePx = Math.Clamp(size, 11f, 30f) });
            else toolkit.AddDalamudDefaultFont(Math.Clamp(size, 11f, 30f));
        }));
    }

    public void Dispose()
    {
        foreach (var handle in handles.Values) handle.Dispose();
        handles.Clear();
    }
}
