using Microsoft.Win32;
using System.Text.RegularExpressions;

namespace Bibliognost.Services;

internal sealed record SystemFontChoice(string Name, string Path);

internal static partial class SystemFontCatalog
{
    private const string FontRegistryKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts";

    internal static IReadOnlyList<SystemFontChoice> Discover()
    {
        var choices = new Dictionary<string, SystemFontChoice>(StringComparer.OrdinalIgnoreCase);
        ReadHive(Registry.LocalMachine, choices);
        ReadHive(Registry.CurrentUser, choices);
        return choices.Values.OrderBy(choice => choice.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    internal static string? FindAutomaticFont()
    {
        var directory = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        return new[] { "seguibl.ttf", "segoeuib.ttf", "ariblk.ttf" }
            .Select(name => Path.Combine(directory, name)).FirstOrDefault(File.Exists);
    }

    private static void ReadHive(RegistryKey hive, IDictionary<string, SystemFontChoice> choices)
    {
        try
        {
            using var key = hive.OpenSubKey(FontRegistryKey);
            if (key is null) return;
            foreach (var valueName in key.GetValueNames())
            {
                if (key.GetValue(valueName) is not string registeredPath) continue;
                var extension = Path.GetExtension(registeredPath);
                if (extension is not (".ttf" or ".otf" or ".ttc")) continue;
                var path = Path.IsPathRooted(registeredPath)
                    ? registeredPath
                    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), registeredPath);
                if (!File.Exists(path)) continue;
                var name = FontSuffix().Replace(valueName, string.Empty).Trim();
                choices.TryAdd($"{name}\0{path}", new SystemFontChoice(name, path));
            }
        }
        catch { /* A locked registry hive must not prevent the plugin from loading. */ }
    }

    [GeneratedRegex(@"\s*\((?:TrueType|OpenType)\)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex FontSuffix();
}
