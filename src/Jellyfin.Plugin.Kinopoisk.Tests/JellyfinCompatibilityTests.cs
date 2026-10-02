using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.Kinopoisk.MetadataProviders;
using Jellyfin.Plugin.Kinopoisk.Model;
using KinopoiskUnofficialInfo.ApiClient;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.Kinopoisk.Tests
{
    public class JellyfinCompatibilityTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task RegisteredProvidersReturnVideoMetadata(bool isSeries)
        {
            var apiClient = new Mock<IKinopoiskApiClient>(MockBehavior.Strict);
            apiClient.Setup(api => api.GetSingleFilm(123, It.IsAny<CancellationToken?>()))
                .ReturnsAsync(new Film
                {
                    KinopoiskId = 123,
                    NameRu = "Тестовый фильм",
                    Year = 2026,
                    RatingKinopoisk = 8.5,
                    ImdbId = "tt123"
                });
            apiClient.Setup(api => api.GetStaff(123, It.IsAny<CancellationToken?>()))
                .ReturnsAsync(new List<StaffResponse>
                {
                    new StaffResponse
                    {
                        StaffId = 456,
                        NameRu = "Тестовый актёр",
                        ProfessionKey = StaffResponseProfessionKey.ACTOR
                    }
                });
            apiClient.Setup(api => api.GetTrailers(123, It.IsAny<CancellationToken?>()))
                .ReturnsAsync(new VideoResponse
                {
                    Items = new List<VideoResponse_items>
                    {
                        new VideoResponse_items
                        {
                            Name = "Трейлер",
                            Url = "https://youtu.be/example",
                            Site = VideoResponse_itemsSite.YOUTUBE
                        }
                    }
                });

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddHttpClient();
            services.AddMemoryCache();
            new KinopoiskPluginServiceRegistrator().RegisterServices(services, null);
            services.AddSingleton(apiClient.Object);
            using var serviceProvider = services.BuildServiceProvider();

            if (isSeries)
            {
                var provider = ActivatorUtilities.CreateInstance<SeriesMetadataProvider>(serviceProvider);
                var info = new SeriesInfo { Path = "/media/series/kp-123" };
                AssertVideoMetadata(await provider.GetMetadata(info, CancellationToken.None));
            }
            else
            {
                var provider = ActivatorUtilities.CreateInstance<MovieMetadataProvider>(serviceProvider);
                var info = new MovieInfo();
                info.SetProviderId(Constants.ProviderId, "123");
                AssertVideoMetadata(await provider.GetMetadata(info, CancellationToken.None));
            }

            apiClient.VerifyAll();
        }

        private static void AssertVideoMetadata<T>(MetadataResult<T> result) where T : BaseItem
        {
            Assert.True(result.HasMetadata);
            Assert.Equal("Тестовый фильм", result.Item.Name);
            Assert.Equal(2026, result.Item.ProductionYear);
            Assert.Equal(8.5f, result.Item.CommunityRating);
            Assert.Equal("123", result.Item.GetProviderId(Constants.ProviderId));
            Assert.Equal("tt123", result.Item.GetProviderId(MetadataProvider.Imdb));
            var person = Assert.Single(result.People);
            Assert.Equal("Тестовый актёр", person.Name);
            Assert.Equal(PersonKind.Actor, person.Type);
            Assert.Equal("456", person.GetProviderId(Constants.ProviderId));
            Assert.Equal("https://www.youtube.com/watch?v=example", Assert.Single(result.Item.RemoteTrailers).Url);
        }

        [Theory]
        [InlineData("film")]
        [InlineData("series")]
        [InlineData("name")]
        public void ExternalLinksSupportJellyfinEntities(string path)
        {
            BaseItem item = path switch
            {
                "film" => new Movie(),
                "series" => new Series(),
                _ => new Person()
            };
            item.SetProviderId(Constants.ProviderId, "123");

            Assert.True(new KinopoiskExternalId().Supports(item));
            Assert.Equal($"https://www.kinopoisk.ru/{path}/123/",
                Assert.Single(new KinopoiskExternalUrlProvider().GetExternalUrls(item)));
        }
    }
}
