using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Memberra.Jellyfin.Tests;

public sealed class DurableOutboxTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "memberra-outbox-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task EventSurvivesOutboxRecreationUntilAcknowledged()
    {
        var eventId = Guid.NewGuid();
        var first = Create();
        await first.EnqueueAsync(eventId, new { EventId = eventId, Value = "persisted" }, CancellationToken.None);

        var restarted = Create();
        var pending = await restarted.PeekAsync(CancellationToken.None);

        Assert.NotNull(pending);
        Assert.Equal(eventId, pending.Value.Item.EventId);
        Assert.Contains("persisted", pending.Value.Item.Payload, StringComparison.Ordinal);
        restarted.Complete(pending.Value.Path);
        Assert.Equal(0, restarted.Count);
    }

    [Fact]
    public async Task EventsAreReadInCreationOrder()
    {
        var outbox = Create();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        await outbox.EnqueueAsync(first, new { EventId = first }, CancellationToken.None);
        await Task.Delay(2);
        await outbox.EnqueueAsync(second, new { EventId = second }, CancellationToken.None);

        var pending = await outbox.PeekAsync(CancellationToken.None);
        Assert.Equal(first, pending!.Value.Item.EventId);
        outbox.Complete(pending.Value.Path);
        pending = await outbox.PeekAsync(CancellationToken.None);
        Assert.Equal(second, pending!.Value.Item.EventId);
    }

    [Fact]
    public async Task CorruptItemIsQuarantinedInsteadOfBlockingForever()
    {
        var outbox = Create();
        var outboxPath = Path.Combine(_path, "outbox");
        await File.WriteAllTextAsync(Path.Combine(outboxPath, "00000000000000000000-corrupt.json"), "not-json");

        Assert.Null(await outbox.PeekAsync(CancellationToken.None));
        Assert.Empty(Directory.EnumerateFiles(outboxPath, "*.json"));
        Assert.Single(Directory.EnumerateFiles(Path.Combine(_path, "quarantine"), "*.json"));
    }

    private DurableOutbox Create() => new(_path, NullLogger<DurableOutbox>.Instance);

    public void Dispose()
    {
        if (Directory.Exists(_path)) Directory.Delete(_path, true);
    }
}
