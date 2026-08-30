using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Memberra.Jellyfin;

public sealed class OutboxDeliveryService(
    IHttpClientFactory clients,
    DurableOutbox outbox,
    MemberraProtocolState protocolState,
    ILogger<OutboxDeliveryService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var delay = TimeSpan.FromSeconds(1);
        while (!stoppingToken.IsCancellationRequested)
        {
            var delivered = false;
            try
            {
                var cfg = Plugin.Instance?.Configuration;
                var pending = await outbox.PeekAsync(stoppingToken).ConfigureAwait(false);
                if (pending is not null && cfg?.Enabled == true &&
                    protocolState.AcceptsCurrentEventSchema &&
                    !string.IsNullOrWhiteSpace(cfg.InstallId) && !string.IsNullOrWhiteSpace(cfg.InstallToken))
                {
                    using var http = clients.CreateClient(MemberraProtocol.HttpClientName);
                    using var request = new HttpRequestMessage(HttpMethod.Post, MemberraProtocol.EventsUri)
                    {
                        Content = new StringContent(pending.Value.Item.Payload, Encoding.UTF8, "application/json")
                    };
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cfg.InstallId + "." + cfg.InstallToken);
                    request.Headers.TryAddWithoutValidation("X-Memberra-Protocol", MemberraProtocol.ProtocolVersion.ToString());
                    using var response = await http.SendAsync(request, stoppingToken).ConfigureAwait(false);
                    if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.Conflict)
                    {
                        outbox.Complete(pending.Value.Path);
                        delivered = true;
                        delay = TimeSpan.FromSeconds(1);
                    }
                    else if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.RequestEntityTooLarge or HttpStatusCode.UnprocessableEntity)
                    {
                        outbox.Quarantine(pending.Value.Path, ((int)response.StatusCode).ToString());
                        delivered = true;
                        delay = TimeSpan.FromSeconds(1);
                        log.LogError("Memberra permanently rejected queued event {EventId} with HTTP {Status}; it was quarantined", pending.Value.Item.EventId, (int)response.StatusCode);
                    }
                    else
                    {
                        log.LogWarning("Memberra queued event {EventId} rejected with HTTP {Status}", pending.Value.Item.EventId, (int)response.StatusCode);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { log.LogWarning(ex, "Memberra queued delivery failed; it will be retried"); }

            if (!delivered)
            {
                await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 60));
            }
        }
    }
}
