using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Kinopoisk.MetadataProviders;
using Jellyfin.Plugin.Kinopoisk.ProviderIdResolvers;
using MediaBrowser.Controller.Providers;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PoiskKino.ApiClient;
using Xunit;

namespace Jellyfin.Plugin.Kinopoisk.Tests
{
    public class ImageValidationTests
    {
        [Fact]
        public async Task RelativeRedirectsAreResolved()
        {
            using var client = new HttpClient(new Handler((request, _) => Task.FromResult(
                request.RequestUri.AbsolutePath == "/start"
                    ? new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = new Uri("/photo.jpg", UriKind.Relative) } }
                    : new HttpResponseMessage(HttpStatusCode.OK))));
            Assert.Equal("https://example.com/photo.jpg", await new RemoteImageUrlSanitizer(client)
                .SanitizeRemoteImageUrl("https://example.com/start"));
        }

        [Theory]
        [InlineData("/start")]
        [InlineData("/no-poster.jpg")]
        [InlineData("file:///tmp/photo.jpg")]
        public async Task LoopsPlaceholdersAndNonHttpRedirectsAreRejected(string location)
        {
            using var client = new HttpClient(new Handler((_, _) => Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = new Uri(location, UriKind.RelativeOrAbsolute) } })));
            Assert.Null(await new RemoteImageUrlSanitizer(client).SanitizeRemoteImageUrl("https://example.com/start"));
        }

        [Fact]
        public async Task RedirectChainsHaveFiniteLength()
        {
            var calls = 0;
            using var client = new HttpClient(new Handler((_, _) => Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = new Uri("/" + ++calls, UriKind.Relative) } })));
            Assert.Null(await new RemoteImageUrlSanitizer(client).SanitizeRemoteImageUrl("https://example.com/start"));
            Assert.Equal(6, calls);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task NetworkErrorsAndTimeoutsOnlyRemoveImage(bool timeout)
        {
            using var client = new HttpClient(new Handler((_, _) => timeout
                ? Task.FromException<HttpResponseMessage>(new TaskCanceledException())
                : Task.FromException<HttpResponseMessage>(new HttpRequestException("unreachable"))));
            Assert.Null(await new RemoteImageUrlSanitizer(client).SanitizeRemoteImageUrl("https://example.com/image"));
        }

        [Fact]
        public async Task CancellationReachesHttpRequest()
        {
            using var cancellation = new CancellationTokenSource();
            using var client = new HttpClient(new Handler(async (_, token) =>
            {
                cancellation.Cancel();
                await Task.Delay(Timeout.Infinite, token);
                return new HttpResponseMessage(HttpStatusCode.OK);
            }));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new RemoteImageUrlSanitizer(client)
                .SanitizeRemoteImageUrl("https://example.com/image", cancellation.Token));
        }

        [Fact]
        public async Task BrokenActorPhotoDoesNotDiscardMetadataOrOtherActors()
        {
            var api = new Mock<IPoiskKinoApiClient>(MockBehavior.Strict);
            api.Setup(client => client.GetMovie(123, It.IsAny<CancellationToken>())).ReturnsAsync(new PoiskKinoMovie
            {
                Id = 123, Name = "Название", Persons = new()
                {
                    new() { Id = 1, Name = "First", Photo = "https://example.com/missing" },
                    new() { Id = 2, Name = "Second", Photo = "https://example.com/photo" }
                }
            });
            var factory = new Mock<IHttpClientFactory>();
            factory.Setup(f => f.CreateClient(RemoteImageUrlSanitizer.HttpClientName)).Returns(() =>
                new HttpClient(new Handler((request, _) => Task.FromResult(new HttpResponseMessage(
                    request.RequestUri.AbsolutePath == "/missing" ? HttpStatusCode.NotFound : HttpStatusCode.OK)))));
            var provider = new MovieMetadataProvider(api.Object,
                new VideoResolver<MovieInfo>(api.Object, NullLogger<VideoResolver<MovieInfo>>.Instance),
                NullLogger<MovieMetadataProvider>.Instance, factory.Object);
            var result = await provider.GetMetadata(new MovieInfo { Path = "/kp-123.mkv" }, CancellationToken.None);
            Assert.True(result.HasMetadata);
            Assert.Equal(2, result.People.Count);
            Assert.Null(result.People.First().ImageUrl);
            Assert.Equal("https://example.com/photo", result.People.Last().ImageUrl);
        }

        [Fact]
        public async Task ConcurrentValidationsAreBoundedAcrossInstances()
        {
            var active = 0;
            var maximum = 0;
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var client = new HttpClient(new Handler(async (_, token) =>
            {
                var count = Interlocked.Increment(ref active);
                int previous;
                do { previous = maximum; } while (count > previous && Interlocked.CompareExchange(ref maximum, count, previous) != previous);
                if (count == 4) entered.TrySetResult();
                await release.Task.WaitAsync(token);
                Interlocked.Decrement(ref active);
                return new HttpResponseMessage(HttpStatusCode.OK);
            }));
            var tasks = Enumerable.Range(0, 12).Select(index => new RemoteImageUrlSanitizer(client)
                .SanitizeRemoteImageUrl("https://example.com/" + index)).ToArray();
            try { await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); }
            finally { release.TrySetResult(); }
            await Task.WhenAll(tasks);
            Assert.True(maximum <= 4);
        }

        private sealed class Handler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _send;
            public Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) => _send = send;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
                => _send(request, cancellationToken);
        }
    }
}
