using System;

namespace Memberra.Jellyfin;

internal static class MemberraProtocol
{
    public const string Version = "1.0.0";
    public const string HttpClientName = "Memberra";
    public const int MaximumResponseBytes = 64 * 1024;

    private static readonly Uri BaseUri = new("https://memberra.co.uk/", UriKind.Absolute);

    public static Uri RegisterUri { get; } = new(BaseUri, "api/public/jellyfin-plugin/register");
    public static Uri HeartbeatUri { get; } = new(BaseUri, "api/public/jellyfin-plugin/heartbeat");
    public static Uri EventsUri { get; } = new(BaseUri, "api/public/jellyfin-plugin/events");
}
