using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Dto;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Memberra.Jellyfin.Tests;

public sealed class MemberraCommandProcessorTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "memberra-command-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ExecutesEnabledStopAndAcknowledgesSuccess()
    {
        const string sessionId = "active-session";
        var sessions = new Mock<ISessionManager>();
        sessions.SetupGet(x => x.Sessions).Returns(new[]
        {
            new SessionInfo(sessions.Object, NullLogger<SessionInfo>.Instance) { Id = sessionId, NowPlayingItem = new BaseItemDto() }
        });
        sessions.Setup(x => x.SendPlaystateCommand(sessionId, sessionId, It.IsAny<MediaBrowser.Model.Session.PlaystateRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var handler = new CapturingHandler();
        var processor = Create(sessions.Object, handler);
        var commandId = Guid.NewGuid();
        using var heartbeat = JsonResponse($"{{\"acceptedEventSchemaVersions\":[1],\"commands\":[{{\"id\":\"{commandId}\",\"type\":\"stop_session\",\"payload\":{{\"sessionId\":\"{sessionId}\"}}}}]}}");

        await processor.ProcessHeartbeatAsync(heartbeat, Configuration(true), CancellationToken.None);

        sessions.Verify(x => x.SendPlaystateCommand(sessionId, sessionId, It.IsAny<MediaBrowser.Model.Session.PlaystateRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Contains("succeeded", handler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefusesRemoteStopWhenOperatorHasNotEnabledIt()
    {
        var sessions = new Mock<ISessionManager>();
        var handler = new CapturingHandler();
        var processor = Create(sessions.Object, handler);
        using var heartbeat = JsonResponse($"{{\"commands\":[{{\"id\":\"{Guid.NewGuid()}\",\"type\":\"stop_session\",\"payload\":{{\"sessionId\":\"session\"}}}}]}}");

        await processor.ProcessHeartbeatAsync(heartbeat, Configuration(false), CancellationToken.None);

        sessions.Verify(x => x.SendPlaystateCommand(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<MediaBrowser.Model.Session.PlaystateRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Contains("failed", handler.Body, StringComparison.Ordinal);
    }

    private MemberraCommandProcessor Create(ISessionManager sessions, CapturingHandler handler)
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(new HttpClient(handler));
        return new MemberraCommandProcessor(
            sessions,
            factory.Object,
            new CommandReceiptStore(_path, NullLogger<CommandReceiptStore>.Instance),
            new MemberraProtocolState(),
            NullLogger<MemberraCommandProcessor>.Instance);
    }

    private static Configuration.PluginConfiguration Configuration(bool allow) => new()
    {
        Enabled = true,
        InstallId = Guid.NewGuid().ToString(),
        InstallToken = new string('x', 40),
        AllowRemoteStop = allow
    };

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    public void Dispose()
    {
        if (Directory.Exists(_path)) Directory.Delete(_path, true);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string Body { get; private set; } = string.Empty;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
        }
    }
}
