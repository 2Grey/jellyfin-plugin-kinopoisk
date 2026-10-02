using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Moq;
using Xunit;

namespace PoiskKino.ApiClient.Tests
{
    public class CachedPoiskKinoApiClientTests
    {
        [Fact]
        public async Task RepeatedMovieRequestsShareTheFullDocument()
        {
            var inner = new Mock<IPoiskKinoApiClient>(MockBehavior.Strict);
            var movie = new PoiskKinoMovie { Id = 123 };
            inner.Setup(client => client.GetMovie(123, It.IsAny<CancellationToken>())).ReturnsAsync(movie);
            using var cache = new MemoryCache(new MemoryCacheOptions());
            var client = new CachedPoiskKinoApiClient(inner.Object, cache);
            Assert.Same(movie, await client.GetMovie(123));
            Assert.Same(movie, await client.GetMovie(123));
            inner.Verify(api => api.GetMovie(123, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task SearchPagesAndQueriesHaveSeparateCacheEntries()
        {
            var inner = new Mock<IPoiskKinoApiClient>(MockBehavior.Strict);
            inner.Setup(client => client.SearchMovies(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MovieSearchResponse());
            using var cache = new MemoryCache(new MemoryCacheOptions());
            var client = new CachedPoiskKinoApiClient(inner.Object, cache);
            await client.SearchMovies("фильм", 1);
            await client.SearchMovies("фильм", 1);
            await client.SearchMovies("фильм", 2);
            await client.SearchMovies("сериал", 1);
            inner.Verify(api => api.SearchMovies("фильм", 1, It.IsAny<CancellationToken>()), Times.Once);
            inner.Verify(api => api.SearchMovies("фильм", 2, It.IsAny<CancellationToken>()), Times.Once);
            inner.Verify(api => api.SearchMovies("сериал", 1, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task FailedRequestsAreNotCached()
        {
            var inner = new Mock<IPoiskKinoApiClient>(MockBehavior.Strict);
            inner.SetupSequence(client => client.GetMovie(123, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new HttpRequestException("quota", null, HttpStatusCode.Forbidden))
                .ReturnsAsync(new PoiskKinoMovie { Id = 123 });
            using var cache = new MemoryCache(new MemoryCacheOptions());
            var client = new CachedPoiskKinoApiClient(inner.Object, cache);
            await Assert.ThrowsAsync<HttpRequestException>(() => client.GetMovie(123));
            Assert.Equal(123, (await client.GetMovie(123)).Id);
            inner.Verify(api => api.GetMovie(123, It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        [Fact]
        public async Task CacheHitsStillRespectCancellation()
        {
            var inner = new Mock<IPoiskKinoApiClient>(MockBehavior.Strict);
            inner.Setup(client => client.GetPerson(456, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PoiskKinoPerson { Id = 456 });
            using var cache = new MemoryCache(new MemoryCacheOptions());
            var client = new CachedPoiskKinoApiClient(inner.Object, cache);
            await client.GetPerson(456);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetPerson(456, cancellation.Token));
            inner.Verify(api => api.GetPerson(456, It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}
