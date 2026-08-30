using System;
using System.Linq;
using System.Threading.Tasks;
using System.IO;
using MediaBrowser.Controller.Events;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;

namespace Memberra.Jellyfin;

public sealed class PlaybackStartConsumer(MemberraClient client) : IEventConsumer<PlaybackStartEventArgs>
{
    public Task OnEvent(PlaybackStartEventArgs e) => Send(client, "PlaybackStart", e, false);
    internal static async Task Send(MemberraClient client, string type, PlaybackProgressEventArgs e, bool progress)
    {
        if (e.Item is null || e.Item.IsThemeMedia || e.Users.Count == 0) return;
        var user = e.Users.First();
        var sessionId = e.Session?.Id ?? $"{user.Id:N}:{e.DeviceId}:{e.Item.Id:N}";
        if (type == "PlaybackStart") client.ForgetSession(sessionId);
        try
        {
            string? posterBase64 = null;
            string? posterContentType = null;
            if (type == "PlaybackStart")
            {
                try
                {
                    var path = e.Item.GetImagePath(ImageType.Primary, 0);
                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        var info = new FileInfo(path);
                        if (info.Exists && info.Length > 0 && info.Length <= 524288)
                        {
                            posterBase64 = Convert.ToBase64String(await File.ReadAllBytesAsync(path).ConfigureAwait(false));
                            posterContentType = info.Extension.ToLowerInvariant() switch { ".jpg" or ".jpeg" => "image/jpeg", ".png" => "image/png", ".webp" => "image/webp", _ => null };
                            if (posterContentType is null) posterBase64 = null;
                        }
                    }
                }
                catch { }
            }
            await client.SendAsync(new
            {
                SchemaVersion = MemberraProtocol.EventSchemaVersion,
                NotificationType = type,
                EventId = Guid.NewGuid(),
                OccurredAt = DateTimeOffset.UtcNow,
                SessionId = sessionId,
                UserId = user.Id,
                Username = user.Username,
                ItemId = e.Item.Id,
                ItemName = e.Item.Name,
                ItemType = e.Item.GetType().Name,
                e.DeviceId,
                e.DeviceName,
                e.ClientName,
                e.IsPaused,
                PositionTicks = e.PlaybackPositionTicks ?? 0,
                DurationTicks = e.Item.RunTimeTicks ?? 0,
                PlayMethod = e.Session?.PlayState?.PlayMethod?.ToString(),
                VideoCodec = e.Session?.TranscodingInfo?.VideoCodec,
                AudioCodec = e.Session?.TranscodingInfo?.AudioCodec,
                Container = e.Session?.TranscodingInfo?.Container,
                BitrateKbps = e.Session?.TranscodingInfo?.Bitrate / 1000,
                VideoWidth = e.Session?.TranscodingInfo?.Width,
                VideoHeight = e.Session?.TranscodingInfo?.Height,
                TranscodeReasons = e.Session?.TranscodingInfo?.TranscodeReasons.ToString(),
                Source = "event",
                PosterBase64 = posterBase64,
                PosterContentType = posterContentType
            }, sessionId, progress).ConfigureAwait(false);
        }
        finally
        {
            if (type == "PlaybackStop") client.ForgetSession(sessionId);
        }
    }
}

public sealed class PlaybackProgressConsumer(MemberraClient client) : IEventConsumer<PlaybackProgressEventArgs>
{
    public Task OnEvent(PlaybackProgressEventArgs e) => PlaybackStartConsumer.Send(client, "PlaybackProgress", e, true);
}

public sealed class PlaybackStopConsumer(MemberraClient client) : IEventConsumer<PlaybackStopEventArgs>
{
    public Task OnEvent(PlaybackStopEventArgs e) => PlaybackStartConsumer.Send(client, "PlaybackStop", e, false);
}
