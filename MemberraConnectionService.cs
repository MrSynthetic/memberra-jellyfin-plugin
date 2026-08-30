using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Session;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Memberra.Jellyfin;

public sealed class MemberraConnectionService : BackgroundService
{
    private readonly IHttpClientFactory _clients;
    private readonly IServerApplicationHost _host;
    private readonly DurableOutbox _outbox;
    private readonly MemberraCommandProcessor _commands;
    private readonly ISessionManager _sessions;
    private readonly ILibraryManager _libraries;
    private readonly ILogger<MemberraConnectionService> _log;
    public MemberraConnectionService(IHttpClientFactory clients, IServerApplicationHost host, DurableOutbox outbox, MemberraCommandProcessor commands, ISessionManager sessions, ILibraryManager libraries, ILogger<MemberraConnectionService> log) => (_clients, _host, _outbox, _commands, _sessions, _libraries, _log) = (clients, host, outbox, commands, sessions, libraries, log);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _log.LogInformation("Memberra connection service started");
        var firstCycle = true;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var cfg = Plugin.Instance?.Configuration;
                if (firstCycle)
                {
                    _log.LogInformation("Memberra connection state: enabled={Enabled}, paired={Paired}", cfg?.Enabled == true, !string.IsNullOrWhiteSpace(cfg?.InstallToken));
                    firstCycle = false;
                }
                if (cfg?.Enabled == true)
                {
                    if (string.IsNullOrWhiteSpace(cfg.InstallToken) && !string.IsNullOrWhiteSpace(cfg.PairingCode)) await PairAsync(cfg, stoppingToken).ConfigureAwait(false);
                    else if (!string.IsNullOrWhiteSpace(cfg.InstallToken)) await HeartbeatAsync(cfg, stoppingToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex) { _log.LogWarning(ex, "Memberra connection cycle failed"); }
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task PairAsync(Configuration.PluginConfiguration cfg, CancellationToken ct)
    {
        using var http = _clients.CreateClient(MemberraProtocol.HttpClientName);
        using var response = await http.PostAsJsonAsync(MemberraProtocol.RegisterUri, new
        {
            pairingCode = cfg.PairingCode.Trim(),
            serverId = _host.SystemId,
            serverName = "Jellyfin " + _host.SystemId[..8],
            jellyfinVersion = _host.ApplicationVersionString,
            pluginVersion = MemberraProtocol.Version,
            protocolVersion = MemberraProtocol.ProtocolVersion,
            capabilities = MemberraProtocol.Capabilities(cfg.AllowRemoteStop, cfg.AllowViewerMessages)
        }, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) { _log.LogWarning("Memberra pairing rejected with HTTP {Status}", (int)response.StatusCode); return; }
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        var installId = doc.RootElement.GetProperty("installId").GetString() ?? string.Empty;
        var installToken = doc.RootElement.GetProperty("installToken").GetString() ?? string.Empty;
        if (installId.Length is < 16 or > 128 || installToken.Length is < 32 or > 512)
        {
            _log.LogWarning("Memberra pairing returned invalid credentials");
            return;
        }
        cfg.MemberraUrl = "https://memberra.co.uk";
        cfg.InstallId = installId;
        cfg.InstallToken = installToken;
        cfg.PairingCode = string.Empty;
        Plugin.Instance!.SaveConfiguration(cfg);
        _log.LogInformation("Memberra pairing completed for install {InstallId}", cfg.InstallId);
    }

    private async Task HeartbeatAsync(Configuration.PluginConfiguration cfg, CancellationToken ct)
    {
        _log.LogDebug("Sending Memberra heartbeat");
        using var http = _clients.CreateClient(MemberraProtocol.HttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Post, MemberraProtocol.HeartbeatUri) { Content = JsonContent.Create(new
        {
            serverName = "Jellyfin " + _host.SystemId[..8],
            jellyfinVersion = _host.ApplicationVersionString,
            pluginVersion = MemberraProtocol.Version,
            protocolVersion = MemberraProtocol.ProtocolVersion,
            eventSchemaVersion = MemberraProtocol.EventSchemaVersion,
            capabilities = MemberraProtocol.Capabilities(cfg.AllowRemoteStop, cfg.AllowViewerMessages),
            outboxDepth = _outbox.Count,
            runtimeMetrics = new
            {
                activeSessions = _sessions.Sessions.Count(s => s.NowPlayingItem is not null),
                activeTranscodes = _sessions.Sessions.Count(s => s.NowPlayingItem is not null && s.TranscodingInfo is not null)
            },
            libraries = _libraries.GetVirtualFolders().Select(l => new
            {
                id = l.ItemId,
                name = l.Name,
                kind = l.CollectionType?.ToString()
            }).Take(500).ToArray()
        }) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cfg.InstallId + "." + cfg.InstallToken);
        request.Headers.TryAddWithoutValidation("X-Memberra-Protocol", MemberraProtocol.ProtocolVersion.ToString());
        using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) _log.LogWarning("Memberra heartbeat rejected with HTTP {Status}", (int)response.StatusCode);
        else
        {
            _log.LogDebug("Memberra heartbeat accepted");
            await _commands.ProcessHeartbeatAsync(response, cfg, ct).ConfigureAwait(false);
        }
    }
}
