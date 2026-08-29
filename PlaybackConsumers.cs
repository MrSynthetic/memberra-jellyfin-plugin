using System;
using System.Linq;
using System.Threading.Tasks;
using MediaBrowser.Controller.Events;
using MediaBrowser.Controller.Library;

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
            await client.SendAsync(new
            {
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
                PlayMethod = e.Session?.PlayState?.PlayMethod?.ToString()
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
