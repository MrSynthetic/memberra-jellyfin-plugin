using MediaBrowser.Model.Plugins;

namespace Memberra.Jellyfin.Configuration;

public sealed class PluginConfiguration : BasePluginConfiguration
{
    public bool Enabled { get; set; } = true;
    public string MemberraUrl { get; set; } = "https://memberra.co.uk";
    public string PairingCode { get; set; } = string.Empty;
    public string InstallId { get; set; } = string.Empty;
    public string InstallToken { get; set; } = string.Empty;
    public int ProgressIntervalSeconds { get; set; } = 15;
}
