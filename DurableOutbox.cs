using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;

namespace Memberra.Jellyfin;

public sealed record OutboxItem(Guid EventId, DateTimeOffset CreatedAt, string Payload);

/// <summary>
/// A deliberately simple file-backed queue. Each event is written to a temporary
/// file and atomically renamed before it can be delivered. A Jellyfin restart or
/// Memberra outage therefore cannot silently discard accepted playback events.
/// </summary>
public sealed class DurableOutbox
{
    private const int MaximumItems = 100_000;
    private const long MaximumPayloadBytes = 256 * 1024;
    private readonly string _path;
    private readonly string _quarantinePath;
    private readonly ILogger<DurableOutbox> _log;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public DurableOutbox(IApplicationPaths paths, ILogger<DurableOutbox> log)
        : this(Path.Combine(paths.PluginConfigurationsPath, "Memberra"), log)
    {
    }

    internal DurableOutbox(string memberraPath, ILogger<DurableOutbox> log)
    {
        _path = Path.Combine(memberraPath, "outbox");
        _quarantinePath = Path.Combine(memberraPath, "quarantine");
        _log = log;
        Directory.CreateDirectory(_path);
        Directory.CreateDirectory(_quarantinePath);
    }

    public int Count => Directory.EnumerateFiles(_path, "*.json").Take(MaximumItems + 1).Count();

    public async Task EnqueueAsync(Guid eventId, object payload, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(payload);
        if (Encoding.UTF8.GetByteCount(json) > MaximumPayloadBytes)
            throw new InvalidOperationException("Memberra event exceeds the durable queue payload limit.");

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (Count >= MaximumItems)
                throw new IOException("Memberra durable queue is full; refusing to discard an older event.");

            var item = new OutboxItem(eventId, DateTimeOffset.UtcNow, json);
            var timestamp = item.CreatedAt.UtcTicks.ToString("D20", CultureInfo.InvariantCulture);
            var finalPath = Path.Combine(_path, $"{timestamp}-{eventId:N}.json");
            var temporaryPath = finalPath + ".tmp";
            await File.WriteAllTextAsync(temporaryPath, JsonSerializer.Serialize(item), Encoding.UTF8, ct).ConfigureAwait(false);
            File.Move(temporaryPath, finalPath, false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<(string Path, OutboxItem Item)?> PeekAsync(CancellationToken ct)
    {
        var path = Directory.EnumerateFiles(_path, "*.json").OrderBy(x => x, StringComparer.Ordinal).FirstOrDefault();
        if (path is null) return null;
        try
        {
            var json = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
            var item = JsonSerializer.Deserialize<OutboxItem>(json);
            if (item is null || item.EventId == Guid.Empty || string.IsNullOrWhiteSpace(item.Payload))
                throw new JsonException("Invalid outbox item.");
            return (path, item);
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            var quarantine = Path.Combine(_quarantinePath, Path.GetFileName(path));
            File.Move(path, quarantine, true);
            _log.LogError(ex, "Quarantined corrupt Memberra outbox item {File}", Path.GetFileName(path));
            return null;
        }
    }

    public void Complete(string path)
    {
        if (path.StartsWith(_path + Path.DirectorySeparatorChar, StringComparison.Ordinal)) File.Delete(path);
    }

    public void Quarantine(string path, string reason)
    {
        if (!path.StartsWith(_path + Path.DirectorySeparatorChar, StringComparison.Ordinal)) return;
        var safeReason = new string(reason.Where(char.IsLetterOrDigit).Take(32).ToArray());
        var target = Path.Combine(_quarantinePath, Path.GetFileNameWithoutExtension(path) + "-" + safeReason + ".json");
        File.Move(path, target, true);
    }
}
