using MediaBrowser.Model.Plugins;

namespace Memberra.Jellyfin.Configuration;

public sealed class PluginConfiguration : BasePluginConfiguration
{
    public bool Enabled { get; set; } = true;
    // Kept for backwards-compatible deserialization only. Production builds use
    // the fixed HTTPS origin in MemberraProtocol so credentials cannot be
    // redirected to another host through persisted plugin configuration.
    public string MemberraUrl { get; set; } = "https://memberra.co.uk";
    public string PairingCode { get; set; } = string.Empty;
    public string InstallId { get; set; } = string.Empty;
    public string InstallToken { get; set; } = string.Empty;
    public int ProgressIntervalSeconds { get; set; } = 15;
    public bool ReconciliationEnabled { get; set; } = true;
    public int ReconciliationIntervalSeconds { get; set; } = 30;
    public bool AllowRemoteStop { get; set; } = false;
}
