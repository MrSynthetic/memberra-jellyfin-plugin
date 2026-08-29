using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Memberra.Jellyfin;

public sealed class MemberraClient
{
    private static readonly ConcurrentDictionary<string, DateTimeOffset> LastProgress = new();
    private readonly HttpClient _http;
    private readonly ILogger<MemberraClient> _log;

    public MemberraClient(HttpClient http, ILogger<MemberraClient> log)
    {
        _http = http;
        _log = log;
        _http.Timeout = TimeSpan.FromSeconds(10);
    }

    public async Task SendAsync(object payload, string sessionId, bool progress, CancellationToken ct = default)
    {
        var cfg = Plugin.Instance?.Configuration;
        if (cfg is null || !cfg.Enabled || string.IsNullOrWhiteSpace(cfg.InstallToken)) return;
        if (progress)
        {
            var now = DateTimeOffset.UtcNow;
            var previous = LastProgress.GetOrAdd(sessionId, DateTimeOffset.MinValue);
            if ((now - previous).TotalSeconds < Math.Clamp(cfg.ProgressIntervalSeconds, 5, 300)) return;
            LastProgress[sessionId] = now;
        }

        if (string.IsNullOrWhiteSpace(cfg.InstallId)) return;
        var url = cfg.MemberraUrl.TrimEnd('/') + "/api/public/jellyfin-plugin/events";
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(payload) };
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", cfg.InstallId + "." + cfg.InstallToken);
            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) _log.LogWarning("Memberra event rejected with HTTP {Status}", (int)response.StatusCode);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Memberra event delivery failed; Jellyfin playback is unaffected");
        }
    }
}
