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
        services.AddHttpClient<MemberraClient>();
        services.AddHostedService<MemberraConnectionService>();
        services.AddScoped<IEventConsumer<PlaybackStartEventArgs>, PlaybackStartConsumer>();
        services.AddScoped<IEventConsumer<PlaybackProgressEventArgs>, PlaybackProgressConsumer>();
        services.AddScoped<IEventConsumer<PlaybackStopEventArgs>, PlaybackStopConsumer>();
    }
}
