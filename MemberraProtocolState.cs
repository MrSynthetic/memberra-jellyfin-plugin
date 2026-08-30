using System;
using System.Linq;
using System.Text.Json;

namespace Memberra.Jellyfin;

/// <summary>Tracks the server-negotiated protocol without persisting secrets or feature payloads.</summary>
public sealed class MemberraProtocolState
{
    public bool AcceptsCurrentEventSchema { get; private set; } = true;
    public int ServerProtocolVersion { get; private set; } = 1;

    public void Update(JsonElement heartbeat)
    {
        if (heartbeat.TryGetProperty("protocolVersion", out var protocol) && protocol.TryGetInt32(out var value))
            ServerProtocolVersion = value;
        if (heartbeat.TryGetProperty("acceptedEventSchemaVersions", out var schemas) && schemas.ValueKind == JsonValueKind.Array)
            AcceptsCurrentEventSchema = schemas.EnumerateArray().Any(x => x.TryGetInt32(out var schema) && schema == MemberraProtocol.EventSchemaVersion);
    }
}
