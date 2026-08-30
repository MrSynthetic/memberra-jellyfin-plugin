using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Session;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Memberra.Jellyfin;

/// <summary>
/// Repairs missing client callbacks by periodically comparing Jellyfin's
/// authoritative active-session inventory with the events already observed.
/// </summary>
public sealed class SessionReconciliationService(
    ISessionManager sessions,
    MemberraClient client,
    ILogger<SessionReconciliationService> log) : BackgroundService
{
    private readonly Dictionary<string, SessionSnapshot> _known = new(StringComparer.Ordinal);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var cfg = Plugin.Instance?.Configuration;
            try
            {
                if (cfg?.Enabled == true && cfg.ReconciliationEnabled && !string.IsNullOrWhiteSpace(cfg.InstallToken))
                    await ReconcileAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { log.LogWarning(ex, "Memberra session reconciliation failed; it will retry"); }

            var seconds = Math.Clamp(cfg?.ReconciliationIntervalSeconds ?? 30, 15, 300);
            await Task.Delay(TimeSpan.FromSeconds(seconds), stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task ReconcileAsync(CancellationToken ct)
    {
        var active = sessions.Sessions
            .Where(s => s.NowPlayingItem is not null && s.UserId != Guid.Empty)
            .Select(SessionSnapshot.From)
            .Where(s => s is not null)
            .Cast<SessionSnapshot>()
            .ToDictionary(s => s.SessionId, StringComparer.Ordinal);

        foreach (var snapshot in active.Values)
        {
            var type = _known.ContainsKey(snapshot.SessionId) ? "PlaybackProgress" : "PlaybackStart";
            await client.SendAsync(snapshot.ToPayload(type), snapshot.SessionId, type == "PlaybackProgress", ct).ConfigureAwait(false);
            _known[snapshot.SessionId] = snapshot;
        }

        foreach (var ended in _known.Keys.Except(active.Keys, StringComparer.Ordinal).ToArray())
        {
            var snapshot = _known[ended];
            await client.SendAsync(snapshot.ToPayload("PlaybackStop"), snapshot.SessionId, false, ct).ConfigureAwait(false);
            client.ForgetSession(snapshot.SessionId);
            _known.Remove(ended);
        }
    }

    private sealed record SessionSnapshot(
        string SessionId,
        Guid UserId,
        string Username,
        Guid ItemId,
        string ItemName,
        string ItemType,
        string? DeviceId,
        string? DeviceName,
        string? ClientName,
        bool IsPaused,
        long PositionTicks,
        long DurationTicks,
        string? PlayMethod,
        string? VideoCodec,
        string? AudioCodec,
        string? Container,
        int? BitrateKbps,
        int? VideoWidth,
        int? VideoHeight,
        string? TranscodeReasons)
    {
        public static SessionSnapshot? From(SessionInfo session)
        {
            var item = session.NowPlayingItem;
            if (item is null || string.IsNullOrWhiteSpace(session.Id)) return null;
            return new SessionSnapshot(
                session.Id,
                session.UserId,
                session.UserName ?? "Unknown",
                item.Id,
                item.Name ?? "Unknown item",
                item.Type.ToString(),
                session.DeviceId,
                session.DeviceName,
                session.Client,
                session.PlayState?.IsPaused == true,
                session.PlayState?.PositionTicks ?? 0,
                item.RunTimeTicks ?? 0,
                session.PlayState?.PlayMethod?.ToString(),
                session.TranscodingInfo?.VideoCodec,
                session.TranscodingInfo?.AudioCodec,
                session.TranscodingInfo?.Container,
                session.TranscodingInfo?.Bitrate / 1000,
                session.TranscodingInfo?.Width,
                session.TranscodingInfo?.Height,
                session.TranscodingInfo?.TranscodeReasons.ToString());
        }

        public object ToPayload(string type) => new
        {
            SchemaVersion = MemberraProtocol.EventSchemaVersion,
            NotificationType = type,
            EventId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
            SessionId,
            UserId,
            Username,
            ItemId,
            ItemName,
            ItemType,
            DeviceId,
            DeviceName,
            ClientName,
            IsPaused,
            PositionTicks,
            DurationTicks,
            PlayMethod,
            VideoCodec,
            AudioCodec,
            Container,
            BitrateKbps,
            VideoWidth,
            VideoHeight,
            TranscodeReasons,
            Source = "reconciliation"
        };
    }
}
