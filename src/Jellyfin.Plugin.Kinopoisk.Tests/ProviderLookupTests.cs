using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Kinopoisk.Configuration;
using Jellyfin.Plugin.Kinopoisk.MetadataProviders;
using Jellyfin.Plugin.Kinopoisk.ProviderIdResolvers;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PoiskKino.ApiClient;
using Xunit;

namespace Jellyfin.Plugin.Kinopoisk.Tests
{
    public class ProviderLookupTests
    {
        [Fact]
        public void NewBackendDoesNotReuseTheOldToken()
        {
            Assert.Empty(new PluginConfiguration().PoiskKinoApiToken);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task ExplicitIdAndFilenameDoNotTriggerSearch(bool fromPath)
        {
            var api = new Mock<IPoiskKinoApiClient>(MockBehavior.Strict);
            var info = new MovieInfo { Path = fromPath ? "/movies/Example.kp-123.mkv" : null };
            if (!fromPath) info.SetProviderId(Constants.ProviderId, "123");
            Assert.Equal((true, 123), await CreateResolver(api.Object).TryResolve(info));
            api.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task SearchMatchesYearUsingNumericApiYear()
        {
            var api = new Mock<IPoiskKinoApiClient>(MockBehavior.Strict);
            api.Setup(client => client.SearchMovies("Название", 1, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MovieSearchResponse { Docs = new()
                {
                    new PoiskKinoMovie { Id = 1, Year = 2020 },
                    new PoiskKinoMovie { Id = 2, Year = 2026 }
                } });
            Assert.Equal((true, 2), await CreateResolver(api.Object).TryResolve(new MovieInfo { Name = "Название", Year = 2026 }));
            api.Verify(client => client.SearchMovies("Название", 1, It.IsAny<CancellationToken>()), Times.Once);
            api.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task SearchUsesEmbeddedImdbIdsWithoutRequestingEachMovie()
        {
            var api = new Mock<IPoiskKinoApiClient>(MockBehavior.Strict);
            api.Setup(client => client.SearchMovies("Название", 1, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MovieSearchResponse { Docs = new()
                {
                    new PoiskKinoMovie { Id = 1, ExternalId = new ExternalIds { Imdb = "tt111" } },
                    new PoiskKinoMovie { Id = 2, ExternalId = new ExternalIds { Imdb = "tt222" } }
                } });
            var info = new MovieInfo { Name = "Название" };
            info.SetProviderId(MetadataProvider.Imdb, "tt222");
            Assert.Equal((true, 2), await CreateResolver(api.Object).TryResolve(info));
            api.Verify(client => client.SearchMovies("Название", 1, It.IsAny<CancellationToken>()), Times.Once);
            api.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task MissingMoviesReturnNoMetadataOrSearchResults()
        {
            var api = new Mock<IPoiskKinoApiClient>(MockBehavior.Strict);
            api.Setup(client => client.GetMovie(123, It.IsAny<CancellationToken>())).ReturnsAsync((PoiskKinoMovie)null);
            var provider = new MovieMetadataProvider(api.Object, CreateResolver(api.Object),
                NullLogger<MovieMetadataProvider>.Instance, Mock.Of<IHttpClientFactory>());
            var info = new MovieInfo();
            info.SetProviderId(Constants.ProviderId, "123");
            Assert.False((await provider.GetMetadata(info, CancellationToken.None)).HasMetadata);
            Assert.Empty(await provider.GetSearchResults(info, CancellationToken.None));
        }

        [Fact]
        public async Task MissingPersonImageReturnsEmptyImages()
        {
            var api = new Mock<IPoiskKinoApiClient>(MockBehavior.Strict);
            api.Setup(client => client.GetPerson(456, It.IsAny<CancellationToken>())).ReturnsAsync((PoiskKinoPerson)null);
            var resolver = new CommonResolver<BaseItem>(NullLogger<CommonResolver<BaseItem>>.Instance);
            var provider = new PersonImageProvider(api.Object, resolver,
                NullLogger<PersonImageProvider>.Instance, Mock.Of<IHttpClientFactory>());
            var person = new Person();
            person.SetProviderId(Constants.ProviderId, "456");
            Assert.Empty(await provider.GetImages(person, CancellationToken.None));
        }

        [Fact]
        public async Task PersonMetadataUsesKinopoiskId()
        {
            var api = new Mock<IPoiskKinoApiClient>(MockBehavior.Strict);
            api.Setup(client => client.GetPerson(456, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PoiskKinoPerson { Id = 456, Name = "Имя" });
            var resolver = new CommonResolver<PersonLookupInfo>(NullLogger<CommonResolver<PersonLookupInfo>>.Instance);
            var provider = new PersonMetadataProvider(api.Object, resolver,
                NullLogger<PersonMetadataProvider>.Instance, Mock.Of<IHttpClientFactory>());
            var info = new PersonLookupInfo();
            info.SetProviderId(Constants.ProviderId, "456");
            var result = await provider.GetMetadata(info, CancellationToken.None);
            Assert.True(result.HasMetadata);
            Assert.Equal("456", result.Item.GetProviderId(Constants.ProviderId));
        }

        [Fact]
        public async Task CancelledImdbLookupDoesNotFallBackToWrongMovie()
        {
            using var cancellation = new CancellationTokenSource();
            var api = new Mock<IPoiskKinoApiClient>(MockBehavior.Strict);
            api.Setup(client => client.GetMovie(123, cancellation.Token))
                .Returns(() =>
                {
                    cancellation.Cancel();
                    return Task.FromCanceled<PoiskKinoMovie>(cancellation.Token);
                });
            var info = new MovieInfo();
            info.SetProviderId(MetadataProvider.Imdb, "tt123");
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateResolver(api.Object).TryResolveByImdbMatch(
                info, new List<PoiskKinoMovie> { new() { Id = 123 } }, cancellation.Token));
        }

        private static VideoResolver<MovieInfo> CreateResolver(IPoiskKinoApiClient api)
            => new(api, NullLogger<VideoResolver<MovieInfo>>.Instance);

        [Theory]
        [InlineData(401)]
        [InlineData(403)]
        [InlineData(429)]
        public async Task ImdbLookupStopsOnAuthAndQuotaErrors(int status)
        {
            var api = new Mock<IPoiskKinoApiClient>(MockBehavior.Strict);
            api.Setup(client => client.GetMovie(123, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new HttpRequestException("API error", null, (HttpStatusCode)status));
            var info = new MovieInfo();
            info.SetProviderId(MetadataProvider.Imdb, "tt123");
            var exception = await Assert.ThrowsAsync<HttpRequestException>(() => CreateResolver(api.Object).TryResolveByImdbMatch(
                info, new List<PoiskKinoMovie> { new() { Id = 123 }, new() { Id = 124 } }));
            Assert.Equal((HttpStatusCode)status, exception.StatusCode);
            api.Verify(client => client.GetMovie(123, It.IsAny<CancellationToken>()), Times.Once);
            api.VerifyNoOtherCalls();
        }
    }
}
