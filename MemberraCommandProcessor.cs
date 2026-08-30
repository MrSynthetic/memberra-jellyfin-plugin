using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Session;
using Microsoft.Extensions.Logging;

namespace Memberra.Jellyfin;

public sealed class MemberraCommandProcessor(
    ISessionManager sessions,
    IHttpClientFactory clients,
    CommandReceiptStore receipts,
    MemberraProtocolState protocolState,
    ILogger<MemberraCommandProcessor> log)
{
    public async Task ProcessHeartbeatAsync(HttpResponseMessage response, Configuration.PluginConfiguration cfg, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        protocolState.Update(doc.RootElement);
        if (!doc.RootElement.TryGetProperty("commands", out var commands) || commands.ValueKind != JsonValueKind.Array) return;
        foreach (var command in commands.EnumerateArray().Take(20))
        {
            if (!command.TryGetProperty("id", out var idValue) || !Guid.TryParse(idValue.GetString(), out var id)) continue;
            var type = command.TryGetProperty("type", out var typeValue) ? typeValue.GetString() : null;
            var error = string.Empty;
            var succeeded = false;
            try
            {
                if (receipts.Contains(id))
                {
                    await AcknowledgeAsync(id, true, string.Empty, cfg, ct).ConfigureAwait(false);
                    continue;
                }
                if (!string.Equals(type, "stop_session", StringComparison.Ordinal))
                    throw new InvalidOperationException("Unsupported command type.");
                if (!cfg.AllowRemoteStop)
                    throw new InvalidOperationException("Remote stop is disabled in the Jellyfin plugin settings.");
                if (!command.TryGetProperty("payload", out var payload) ||
                    !payload.TryGetProperty("sessionId", out var sessionValue))
                    throw new InvalidOperationException("Command is missing a session id.");
                var sessionId = sessionValue.GetString();
                if (string.IsNullOrWhiteSpace(sessionId) || sessionId.Length > 255)
                    throw new InvalidOperationException("Command contains an invalid session id.");
                if (!sessions.Sessions.Any(s => string.Equals(s.Id, sessionId, StringComparison.Ordinal) && s.NowPlayingItem is not null))
                    throw new InvalidOperationException("The playback session is no longer active.");

                await sessions.SendPlaystateCommand(
                    sessionId,
                    sessionId,
                    new PlaystateRequest { Command = PlaystateCommand.Stop },
                    ct).ConfigureAwait(false);
                receipts.MarkSucceeded(id);
                succeeded = true;
            }
            catch (Exception ex)
            {
                error = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message;
                log.LogWarning(ex, "Memberra command {CommandId} failed", id);
            }
            await AcknowledgeAsync(id, succeeded, error, cfg, ct).ConfigureAwait(false);
        }
    }

    private async Task AcknowledgeAsync(Guid commandId, bool succeeded, string error, Configuration.PluginConfiguration cfg, CancellationToken ct)
    {
        using var http = clients.CreateClient(MemberraProtocol.HttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Post, MemberraProtocol.CommandAckUri)
        {
            Content = JsonContent.Create(new
            {
                commandId,
                status = succeeded ? "succeeded" : "failed",
                error = succeeded ? null : error
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cfg.InstallId + "." + cfg.InstallToken);
        request.Headers.TryAddWithoutValidation("X-Memberra-Protocol", MemberraProtocol.ProtocolVersion.ToString());
        using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            log.LogWarning("Memberra command acknowledgement rejected with HTTP {Status}", (int)response.StatusCode);
    }
}
