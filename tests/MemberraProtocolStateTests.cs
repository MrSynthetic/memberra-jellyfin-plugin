using System.Text.Json;
using Xunit;

namespace Memberra.Jellyfin.Tests;

public sealed class MemberraProtocolStateTests
{
    [Fact]
    public void KeepsBackwardCompatibilityWhenOldServerOmitsNegotiation()
    {
        var state = new MemberraProtocolState();
        using var document = JsonDocument.Parse("{\"ok\":true}");
        state.Update(document.RootElement);
        Assert.True(state.AcceptsCurrentEventSchema);
        Assert.Equal(1, state.ServerProtocolVersion);
    }

    [Fact]
    public void PausesDeliveryWhenServerDoesNotAcceptCurrentSchema()
    {
        var state = new MemberraProtocolState();
        using var document = JsonDocument.Parse("{\"protocolVersion\":3,\"acceptedEventSchemaVersions\":[2,3]}");
        state.Update(document.RootElement);
        Assert.False(state.AcceptsCurrentEventSchema);
        Assert.Equal(3, state.ServerProtocolVersion);
    }
}
