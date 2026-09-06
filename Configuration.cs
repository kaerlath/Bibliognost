using Dalamud.Configuration;
using Bibliognost.Models;

namespace Bibliognost;

public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 2;
    public string? EncryptedXmaSession { get; set; }
    public string? EncryptedNexusApiKey { get; set; }
    public AdultContentMode AdultContent { get; set; } = AdultContentMode.FollowXmaAccount;
    public bool BlurAdultPreviews { get; set; } = true;
    public bool DawntrailCompatibleOnly { get; set; } = true;
    public int ResultsPerPage { get; set; } = 24;
    public float CardWidth { get; set; } = 640f;
    public string? TitleFontName { get; set; }
    public string? TitleFontPath { get; set; }
    public string ThemeName { get; set; } = "Archive Gold";
    public string? CardTitleFontName { get; set; }
    public string? CardTitleFontPath { get; set; }
    public string? CardAuthorFontName { get; set; }
    public string? CardAuthorFontPath { get; set; }
    public string? CardTypeFontName { get; set; }
    public string? CardTypeFontPath { get; set; }
    public float CardTitleFontSize { get; set; } = 18f;
    public float CardAuthorFontSize { get; set; } = 14f;
    public float CardTypeFontSize { get; set; } = 14f;
    public bool CardTitleBold { get; set; } = true;
    public List<string> ConfirmedSourceMatches { get; set; } = [];
    public List<string> RejectedSourceMatches { get; set; } = [];
    public string DownloadDirectory { get; set; } = string.Empty;
    public bool KeepDownloadedPackages { get; set; } = true;
    public List<string> DeliveryHistory { get; set; } = [];
    public bool CompactCards { get; set; }
    public List<InstalledModReceipt> InstalledModReceipts { get; set; } = [];

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}

public enum AdultContentMode
{
    FollowXmaAccount,
    HideAdult,
    ShowAdult,
}
