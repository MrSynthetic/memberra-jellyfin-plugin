using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Memberra.Jellyfin;

public sealed class MemberraClient
{
    private static readonly ConcurrentDictionary<string, DateTimeOffset> LastProgress = new();
    private static readonly object ProgressLock = new();
    private static readonly TimeSpan ProgressEntryLifetime = TimeSpan.FromMinutes(10);
    private const int MaximumTrackedSessions = 4096;
    private static int _cleanupCounter;
    private readonly DurableOutbox _outbox;
    private readonly ILogger<MemberraClient> _log;

    public MemberraClient(DurableOutbox outbox, ILogger<MemberraClient> log)
    {
        _outbox = outbox;
        _log = log;
    }

    public void ForgetSession(string sessionId)
    {
        lock (ProgressLock) LastProgress.TryRemove(sessionId, out _);
    }

    public async Task SendAsync(object payload, string sessionId, bool progress, CancellationToken ct = default)
    {
        var cfg = Plugin.Instance?.Configuration;
        if (cfg is null || !cfg.Enabled || string.IsNullOrWhiteSpace(cfg.InstallToken)) return;
        if (progress)
        {
            var now = DateTimeOffset.UtcNow;
            lock (ProgressLock)
            {
                if (Interlocked.Increment(ref _cleanupCounter) % 256 == 0 || LastProgress.Count >= MaximumTrackedSessions)
                {
                    foreach (var entry in LastProgress)
                    {
                        if (now - entry.Value > ProgressEntryLifetime)
                        {
                            ((ICollection<KeyValuePair<string, DateTimeOffset>>)LastProgress).Remove(entry);
                        }
                    }
                }

                if (LastProgress.Count >= MaximumTrackedSessions && !LastProgress.ContainsKey(sessionId))
                {
                    KeyValuePair<string, DateTimeOffset>? oldest = null;
                    foreach (var entry in LastProgress)
                    {
                        if (oldest is null || entry.Value < oldest.Value.Value) oldest = entry;
                    }
                    if (oldest is not null) LastProgress.TryRemove(oldest.Value.Key, out _);
                }

                var previous = LastProgress.GetOrAdd(sessionId, DateTimeOffset.MinValue);
                if ((now - previous).TotalSeconds < Math.Clamp(cfg.ProgressIntervalSeconds, 5, 300)) return;
                LastProgress[sessionId] = now;
            }
        }

        if (string.IsNullOrWhiteSpace(cfg.InstallId)) return;
        try { await _outbox.EnqueueAsync(ReadEventId(payload), payload, ct).ConfigureAwait(false); }
        catch (Exception ex)
        {
            _log.LogError(ex, "Memberra event could not be persisted; Jellyfin playback is unaffected");
        }
    }

    private static Guid ReadEventId(object payload)
    {
        var property = payload.GetType().GetProperty("EventId");
        return property?.GetValue(payload) is Guid id && id != Guid.Empty ? id : Guid.NewGuid();
    }
}
