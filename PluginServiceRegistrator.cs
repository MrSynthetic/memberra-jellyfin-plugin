using System;
using System.Net.Http;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Events;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Memberra.Jellyfin;

public sealed class PluginServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection services, IServerApplicationHost host)
    {
        services.AddHttpClient(MemberraProtocol.HttpClientName, ConfigureClient)
            .ConfigurePrimaryHttpMessageHandler(CreateHandler);
        services.AddHttpClient<MemberraClient>(ConfigureClient)
            .ConfigurePrimaryHttpMessageHandler(CreateHandler);
        services.AddHostedService<MemberraConnectionService>();
        services.AddScoped<IEventConsumer<PlaybackStartEventArgs>, PlaybackStartConsumer>();
        services.AddScoped<IEventConsumer<PlaybackProgressEventArgs>, PlaybackProgressConsumer>();
        services.AddScoped<IEventConsumer<PlaybackStopEventArgs>, PlaybackStopConsumer>();
    }

    private static void ConfigureClient(HttpClient client)
    {
        client.Timeout = TimeSpan.FromSeconds(10);
        client.MaxResponseContentBufferSize = MemberraProtocol.MaximumResponseBytes;
    }

    private static HttpMessageHandler CreateHandler() => new HttpClientHandler
    {
        AllowAutoRedirect = false
    };
}
