using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Kinopoisk.ProviderIdResolvers;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PoiskKino.ApiClient;
using Xunit;

namespace Jellyfin.Plugin.Kinopoisk.Tests
{
    public class LookupMatchingTests
    {
        [Fact]
        public async Task AmbiguousTitlesAreNotAssignedAutomatically()
        {
            var api = Search(new PoiskKinoMovie() { Id = 1, Name = "Название", Year = 2026 },
                new() { Id = 2, Name = "Название", Year = 2026 });
            Assert.Equal((false, 0), await Resolver(api).TryResolve(new MovieInfo { Name = "Название", Year = 2026 }));
        }

        [Fact]
        public async Task UnrelatedTitleWithMatchingYearIsRejected()
        {
            var api = Search(new PoiskKinoMovie() { Id = 1, Name = "Другое название", Year = 2026 });
            Assert.Equal((false, 0), await Resolver(api).TryResolve(new MovieInfo { Name = "Название", Year = 2026 }));
        }

        [Fact]
        public async Task MoviesAndSeriesAreDistinguished()
        {
            var api = Search(new PoiskKinoMovie() { Id = 1, Name = "Название", Type = "movie" },
                new() { Id = 2, Name = "Название", Type = "tv-series" });
            Assert.Equal((true, 1), await Resolver(api).TryResolve(new MovieInfo { Name = "Название" }));
            var series = new VideoResolver<SeriesInfo>(api.Object, NullLogger<VideoResolver<SeriesInfo>>.Instance);
            Assert.Equal((true, 2), await series.TryResolve(new SeriesInfo { Name = "Название" }));
        }

        [Fact]
        public async Task AlternateTitleIgnoresPunctuationCaseAndYo()
        {
            var api = Search(new PoiskKinoMovie() { Id = 1, Name = "Original", AlternativeName = "Ёлки: Новые!" });
            Assert.Equal((true, 1), await Resolver(api).TryResolve(new MovieInfo { Name = "елки новые" }));
        }

        [Fact]
        public async Task ExternalIdTakesPriorityOverYearAndDoesNotFetchUnrelatedCards()
        {
            var api = Search(new PoiskKinoMovie() { Id = 1, Name = "Название", Year = 2026 },
                new() { Id = 2, Year = 2025, ExternalId = new() { Imdb = "tt222" } });
            var info = new MovieInfo { Name = "Название", Year = 2026 };
            info.SetProviderId(MetadataProvider.Imdb, "tt222");
            Assert.Equal((true, 2), await Resolver(api).TryResolve(info));
            api.Verify(client => client.GetMovie(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task ContradictingExternalIdDoesNotFallBackToTitle()
        {
            var api = Search(new PoiskKinoMovie() { Id = 1, Name = "Название", ExternalId = new() { Imdb = "tt111" } });
            var info = new MovieInfo { Name = "Название" };
            info.SetProviderId(MetadataProvider.Imdb, "tt222");
            Assert.Equal((false, 0), await Resolver(api).TryResolve(info));
        }

        [Fact]
        public async Task TmdbIdCanBeResolvedFromFullCard()
        {
            var api = Search(new PoiskKinoMovie() { Id = 1, Name = "Название" });
            api.Setup(client => client.GetMovie(1, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PoiskKinoMovie { Id = 1, ExternalId = new() { Tmdb = 222 } });
            var info = new MovieInfo { Name = "Название" };
            info.SetProviderId(MetadataProvider.Tmdb, "222");
            Assert.Equal((true, 1), await Resolver(api).TryResolve(info));
        }

        [Fact]
        public async Task MatchCanBeFoundOnSecondPage()
        {
            var api = Search(new PoiskKinoMovie() { Id = 1, Name = "Другое название" });
            api.Setup(client => client.SearchMovies("Название", 1, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MovieSearchResponse { Pages = 2, Docs = new() { new() { Id = 1, Name = "Другое название" } } });
            api.Setup(client => client.SearchMovies("Название", 2, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MovieSearchResponse { Pages = 2, Docs = new() { new() { Id = 2, Name = "Название" } } });
            Assert.Equal((true, 2), await Resolver(api).TryResolve(new MovieInfo { Name = "Название" }));
        }

        [Fact]
        public async Task TruncatedSearchCannotEstablishTitleUniqueness()
        {
            var api = new Mock<IPoiskKinoApiClient>(MockBehavior.Strict);
            api.Setup(client => client.SearchMovies("Название", It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MovieSearchResponse { Pages = 100, Docs = new() { new() { Id = 1, Name = "Название" } } });
            Assert.Equal((false, 0), await Resolver(api).TryResolve(new MovieInfo { Name = "Название" }));
            api.Verify(client => client.SearchMovies("Название", It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Exactly(5));
        }

        private static Mock<IPoiskKinoApiClient> Search(params PoiskKinoMovie[] movies)
        {
            var api = new Mock<IPoiskKinoApiClient>(MockBehavior.Strict);
            api.Setup(client => client.SearchMovies(It.IsAny<string>(), 1, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MovieSearchResponse { Docs = new List<PoiskKinoMovie>(movies) });
            return api;
        }

        private static VideoResolver<MovieInfo> Resolver(Mock<IPoiskKinoApiClient> api)
            => new(api.Object, NullLogger<VideoResolver<MovieInfo>>.Instance);
    }
}
