using System;
using System.Collections.Generic;

namespace Memberra.Jellyfin;

internal static class MemberraProtocol
{
    public const string Version = "1.5.0";
    public const int ProtocolVersion = 3;
    public const int EventSchemaVersion = 1;
    public const string HttpClientName = "Memberra";
    public const int MaximumResponseBytes = 64 * 1024;

    private static readonly Uri BaseUri = new("https://memberra.co.uk/", UriKind.Absolute);

    public static Uri RegisterUri { get; } = new(BaseUri, "api/public/jellyfin-plugin/register");
    public static Uri HeartbeatUri { get; } = new(BaseUri, "api/public/jellyfin-plugin/heartbeat");
    public static Uri EventsUri { get; } = new(BaseUri, "api/public/jellyfin-plugin/events");
    public static Uri CommandAckUri { get; } = new(BaseUri, "api/public/jellyfin-plugin/command-ack");

    public static IReadOnlyDictionary<string, bool> Capabilities(bool allowRemoteStop, bool allowViewerMessages) =>
        new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["playback_events"] = true,
            ["playback_reconciliation"] = true,
            ["durable_outbox"] = true,
            ["ordered_delivery"] = true,
            ["protocol_negotiation"] = true,
            ["rich_playback_telemetry"] = true,
            ["runtime_metrics"] = true,
            ["library_inventory"] = true,
            ["remote_stop"] = allowRemoteStop,
            ["display_message"] = allowViewerMessages,
            ["plugin_provisioning"] = true,
            ["suspend_user"] = true,
            ["password_reset"] = true,
            ["rename_user"] = true,
            ["library_scoping"] = true,
            ["secure_posters"] = true,
            ["user_inventory"] = true,
            ["server_metrics"] = false,
            ["library_events"] = false
        };
}
