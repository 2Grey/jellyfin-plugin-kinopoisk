using System.Net.Http;
using Jellyfin.Plugin.Kinopoisk.ProviderIdResolvers;
using PoiskKino.ApiClient;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Kinopoisk
{
    /// <summary>
    /// Registers services
    /// </summary>
    public class KinopoiskPluginServiceRegistrator : IPluginServiceRegistrator
    {
        public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
        {
            serviceCollection.AddHttpClient(RemoteImageUrlSanitizer.HttpClientName,
                client => client.Timeout = System.TimeSpan.FromSeconds(10))
                .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
            serviceCollection.AddHttpClient(PoiskKinoApiClient.HttpClientName,
                client => client.Timeout = System.TimeSpan.FromSeconds(20))
                .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
            serviceCollection.AddSingleton((sp) => new PoiskKinoApiClient(
                () => Plugin.Instance.Configuration.PoiskKinoApiToken,
                sp.GetRequiredService<ILogger<PoiskKinoApiClient>>(),
                sp.GetRequiredService<IHttpClientFactory>()
            ));
            serviceCollection.AddSingleton<IPoiskKinoApiClient>((sp) => new CachedPoiskKinoApiClient(
                sp.GetRequiredService<PoiskKinoApiClient>(),
                sp.GetRequiredService<IMemoryCache>()
            ));

            serviceCollection.AddSingleton<IProviderIdResolver<MovieInfo>, VideoResolver<MovieInfo>>();
            serviceCollection.AddSingleton<IProviderIdResolver<SeriesInfo>, VideoResolver<SeriesInfo>>();
            serviceCollection.AddSingleton<IProviderIdResolver<PersonLookupInfo>, CommonResolver<PersonLookupInfo>>();
            serviceCollection.AddSingleton<IProviderIdResolver<BaseItem>, CommonResolver<BaseItem>>();
        }
    }
}
