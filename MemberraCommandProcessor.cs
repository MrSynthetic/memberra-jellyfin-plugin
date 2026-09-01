using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Session;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Session;
using Microsoft.Extensions.Logging;

namespace Memberra.Jellyfin;

public sealed class MemberraCommandProcessor(
    ISessionManager sessions,
    IHttpClientFactory clients,
    CommandReceiptStore receipts,
    MemberraProtocolState protocolState,
    ILogger<MemberraCommandProcessor> log,
    IUserManager? userManager = null)
{
    private IUserManager Users => userManager ?? throw new InvalidOperationException("Jellyfin user management is unavailable.");
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
            object result = new { };
            try
            {
                if (receipts.Contains(id))
                {
                    await AcknowledgeAsync(id, true, string.Empty, new { duplicate = true }, cfg, ct).ConfigureAwait(false);
                    continue;
                }
                if (!command.TryGetProperty("payload", out var payload)) throw new InvalidOperationException("Command payload is missing.");

                if (string.Equals(type, "stop_session", StringComparison.Ordinal))
                {
                    var sessionId = RequireActiveSession(payload);
                    if (!cfg.AllowRemoteStop)
                        throw new InvalidOperationException("Remote stop is disabled in the Jellyfin plugin settings.");
                    await sessions.SendPlaystateCommand(
                        sessionId,
                        sessionId,
                        new PlaystateRequest { Command = PlaystateCommand.Stop },
                        ct).ConfigureAwait(false);
                }
                else if (string.Equals(type, "display_message", StringComparison.Ordinal))
                {
                    var sessionId = RequireActiveSession(payload);
                    if (!cfg.AllowViewerMessages)
                        throw new InvalidOperationException("Viewer messages are disabled in the Jellyfin plugin settings.");
                    var header = payload.TryGetProperty("header", out var headerValue) ? headerValue.GetString() : "Message from your provider";
                    var text = payload.TryGetProperty("text", out var textValue) ? textValue.GetString() : null;
                    var timeoutMs = payload.TryGetProperty("timeoutMs", out var timeoutValue) && timeoutValue.TryGetInt64(out var timeout) ? timeout : 10000;
                    if (string.IsNullOrWhiteSpace(text) || text.Length > 240)
                        throw new InvalidOperationException("Message text is missing or too long.");
                    await sessions.SendMessageCommand(
                        sessionId,
                        sessionId,
                        new MessageCommand { Header = string.IsNullOrWhiteSpace(header) ? "Message from your provider" : header[..Math.Min(header.Length, 60)], Text = text, TimeoutMs = Math.Clamp(timeoutMs, 3000, 30000) },
                        ct).ConfigureAwait(false);
                }
                else if (string.Equals(type, "provision_user", StringComparison.Ordinal)) result = await ProvisionAsync(payload).ConfigureAwait(false);
                else if (string.Equals(type, "suspend_user", StringComparison.Ordinal)) result = await SetDisabledAsync(payload, true).ConfigureAwait(false);
                else if (string.Equals(type, "restore_user", StringComparison.Ordinal)) result = await SetDisabledAsync(payload, false).ConfigureAwait(false);
                else if (string.Equals(type, "reset_password", StringComparison.Ordinal)) result = await ResetPasswordAsync(payload).ConfigureAwait(false);
                else if (string.Equals(type, "delete_user", StringComparison.Ordinal)) result = await DeleteUserAsync(payload).ConfigureAwait(false);
                else if (string.Equals(type, "update_libraries", StringComparison.Ordinal)) result = await UpdateLibrariesAsync(payload).ConfigureAwait(false);
                else if (string.Equals(type, "list_users", StringComparison.Ordinal)) result = ListUsers();
                else throw new InvalidOperationException("Unsupported command type.");
                receipts.MarkSucceeded(id);
                succeeded = true;
            }
            catch (Exception ex)
            {
                error = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message;
                log.LogWarning(ex, "Memberra command {CommandId} failed", id);
            }
            await AcknowledgeAsync(id, succeeded, error, result, cfg, ct).ConfigureAwait(false);
        }
    }

    private string RequireActiveSession(JsonElement payload)
    {
        var sessionId = payload.TryGetProperty("sessionId", out var value) ? value.GetString() : null;
        if (string.IsNullOrWhiteSpace(sessionId) || sessionId.Length > 255) throw new InvalidOperationException("Command contains an invalid session id.");
        if (!sessions.Sessions.Any(s => string.Equals(s.Id, sessionId, StringComparison.Ordinal) && s.NowPlayingItem is not null)) throw new InvalidOperationException("The playback session is no longer active.");
        return sessionId;
    }

    private async Task<object> ProvisionAsync(JsonElement payload)
    {
        var username = payload.GetProperty("username").GetString() ?? throw new InvalidOperationException("Username missing.");
        var password = payload.GetProperty("password").GetString() ?? throw new InvalidOperationException("Password missing.");
        var user = Users.GetUserByName(username) ?? await Users.CreateUserAsync(username).ConfigureAwait(false);
        await Users.ChangePassword(user.Id, password).ConfigureAwait(false);
        await ApplyPolicyAsync(user.Id, payload, false).ConfigureAwait(false);
        return new { providerUserId = user.Id.ToString("N"), providerUsername = user.Username };
    }

    private async Task<object> SetDisabledAsync(JsonElement payload, bool disabled)
    {
        var user = RequireUser(payload);
        await ApplyPolicyAsync(user.Id, payload, disabled).ConfigureAwait(false);
        return new { providerUserId = user.Id.ToString("N"), providerUsername = user.Username };
    }

    private async Task<object> ResetPasswordAsync(JsonElement payload)
    {
        var user = RequireUser(payload);
        var password = payload.GetProperty("password").GetString() ?? throw new InvalidOperationException("Password missing.");
        await Users.ChangePassword(user.Id, password).ConfigureAwait(false);
        return new { providerUserId = user.Id.ToString("N") };
    }

    private async Task<object> DeleteUserAsync(JsonElement payload)
    {
        var user = RequireUser(payload);
        await Users.DeleteUserAsync(user.Id).ConfigureAwait(false);
        return new { providerUserId = user.Id.ToString("N") };
    }

    private async Task<object> UpdateLibrariesAsync(JsonElement payload)
    {
        var user = RequireUser(payload);
        await ApplyPolicyAsync(user.Id, payload, null).ConfigureAwait(false);
        return new { providerUserId = user.Id.ToString("N") };
    }

    private object ListUsers()
    {
        var users = Users.GetUsers().Select(user =>
        {
            var dto = Users.GetUserDto(user);
            return new
            {
                id = user.Id.ToString("N"),
                username = user.Username,
                email = (string?)null,
                enabled = !dto.Policy.IsDisabled
            };
        }).ToArray();
        return new { users };
    }

    private global::Jellyfin.Database.Implementations.Entities.User RequireUser(JsonElement payload)
    {
        var raw = payload.GetProperty("userId").GetString();
        if (!Guid.TryParse(raw, out var id)) throw new InvalidOperationException("Invalid Jellyfin user id.");
        return Users.GetUserById(id) ?? throw new InvalidOperationException("Jellyfin user not found.");
    }

    private async Task ApplyPolicyAsync(Guid userId, JsonElement payload, bool? disabled)
    {
        var user = Users.GetUserById(userId) ?? throw new InvalidOperationException("Jellyfin user not found.");
        var policy = Users.GetUserDto(user).Policy;
        if (disabled.HasValue) policy.IsDisabled = disabled.Value;
        if (payload.TryGetProperty("libraries", out var libs) && libs.ValueKind == JsonValueKind.Array)
        {
            var ids = libs.EnumerateArray().Select(x => x.GetString()).Where(x => Guid.TryParse(x, out _)).Select(x => Guid.Parse(x!)).ToArray();
            policy.EnableAllFolders = ids.Length == 0;
            policy.EnabledFolders = ids;
        }
        await Users.UpdatePolicyAsync(userId, policy).ConfigureAwait(false);
    }

    private async Task AcknowledgeAsync(Guid commandId, bool succeeded, string error, object result, Configuration.PluginConfiguration cfg, CancellationToken ct)
    {
        using var http = clients.CreateClient(MemberraProtocol.HttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Post, MemberraProtocol.CommandAckUri)
        {
            Content = JsonContent.Create(new
            {
                commandId,
                status = succeeded ? "succeeded" : "failed",
                error = succeeded ? null : error,
                result
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cfg.InstallId + "." + cfg.InstallToken);
        request.Headers.TryAddWithoutValidation("X-Memberra-Protocol", MemberraProtocol.ProtocolVersion.ToString());
        using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            log.LogWarning("Memberra command acknowledgement rejected with HTTP {Status}", (int)response.StatusCode);
    }
}
