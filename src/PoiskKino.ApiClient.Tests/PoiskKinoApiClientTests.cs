using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace PoiskKino.ApiClient.Tests
{
    public class PoiskKinoApiClientTests
    {
        [Fact]
        public async Task MovieRequestUsesNewEndpointHeaderAndNestedMetadata()
        {
            using var handler = new RecordingHandler(_ => JsonResponse(Fixture("movie.json")));
            var client = CreateClient(handler);
            var movie = await client.GetMovie(123);
            AssertRequest(handler, "https://api.poiskkino.dev/v1.5/movie/123");
            Assert.Equal(123, movie.Id);
            Assert.Equal("Тестовый фильм", movie.Name);
            Assert.Equal(8.5, movie.Rating.Kp);
            Assert.Equal("tt123", movie.ExternalId.Imdb);
            Assert.Equal("https://example.com/backdrop.jpg", movie.Backdrop.Url);
            Assert.Equal(3, movie.Persons.Count);
            Assert.Equal("actor", movie.Persons[0].EnProfession);
            Assert.Equal("translator", movie.Persons[2].EnProfession);
            Assert.Equal("https://www.youtube.com/embed/abc123", Assert.Single(movie.Videos.Trailers).Url);
        }

        [Fact]
        public async Task SearchEncodesQueryAndPassesRequestedPage()
        {
            using var handler = new RecordingHandler(_ => JsonResponse(Fixture("search.json")));
            var result = await CreateClient(handler).SearchMovies("Тест & кино/сериал?", 2);
            AssertRequest(handler, "https://api.poiskkino.dev/v1.5/movie/search?page=2&limit=20&query="
                + Uri.EscapeDataString("Тест & кино/сериал?"));
            Assert.Equal(22, result.Total);
            Assert.Equal(2, result.Page);
            Assert.Equal(2, result.Docs.Count);
            Assert.True(result.Docs[1].IsSeries);
        }

        [Fact]
        public async Task PersonRequestReadsNewSchemaAndIgnoresUnusedFields()
        {
            using var handler = new RecordingHandler(_ => JsonResponse(Fixture("person.json")));
            var person = await CreateClient(handler).GetPerson(456);
            AssertRequest(handler, "https://api.poiskkino.dev/v1.5/person/456");
            Assert.Equal("Тестовый актёр", person.Name);
            Assert.Equal("1980-02-03T00:00:00.000Z", person.Birthday);
            Assert.Null(person.Death);
            Assert.Equal(2, person.BirthPlace.Count);
        }

        [Fact]
        public async Task NullOptionalFieldsAndUnknownTypesAreAccepted()
        {
            using var handler = new RecordingHandler(_ => JsonResponse(
                """{"id":123,"name":null,"year":null,"rating":null,"persons":null,"videos":null,"type":"new-type"}"""));
            var movie = await CreateClient(handler).GetMovie(123);
            Assert.Null(movie.Year);
            Assert.Null(movie.Rating);
            Assert.Null(movie.Persons);
            Assert.Equal("new-type", movie.Type);
        }

        [Theory]
        [InlineData(401)]
        [InlineData(403)]
        [InlineData(429)]
        [InlineData(500)]
        public async Task ApiErrorsPropagateWithoutLeakingResponseBody(int status)
        {
            using var handler = new RecordingHandler(_ => new HttpResponseMessage((HttpStatusCode)status)
            {
                Content = new StringContent("secret-response-body")
            });
            var exception = await Assert.ThrowsAsync<HttpRequestException>(() => CreateClient(handler).GetMovie(123));
            Assert.Equal((HttpStatusCode)status, exception.StatusCode);
            Assert.DoesNotContain("secret-response-body", exception.Message);
            Assert.Single(handler.Requests);
        }

        [Fact]
        public async Task MissingMovieAndPersonReturnNull()
        {
            using var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
            var client = CreateClient(handler);
            Assert.Null(await client.GetMovie(123));
            Assert.Null(await client.GetPerson(456));
        }

        [Fact]
        public async Task MissingSearchEndpointIsAnError()
        {
            using var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
            await Assert.ThrowsAsync<HttpRequestException>(() => CreateClient(handler).SearchMovies("Название"));
        }

        [Theory]
        [InlineData("null")]
        [InlineData("not-json")]
        public async Task InvalidJsonIsReported(string body)
        {
            using var handler = new RecordingHandler(_ => JsonResponse(body));
            await Assert.ThrowsAsync<System.Text.Json.JsonException>(() => CreateClient(handler).GetMovie(123));
        }

        [Fact]
        public async Task MissingTokenFailsBeforeSendingRequestAndUpdatedTokenIsRead()
        {
            using var handler = new RecordingHandler(_ => JsonResponse(Fixture("movie.json")));
            var token = string.Empty;
            var client = CreateClient(handler, () => token);
            await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetMovie(123));
            Assert.Empty(handler.Requests);
            token = "first-test-token";
            await client.GetMovie(123);
            token = "second-test-token";
            await client.GetMovie(123);
            Assert.Equal(new[] { "first-test-token", "second-test-token" }, handler.Requests.Select(request => request.Token));
        }

        [Fact]
        public async Task CancelledRequestDoesNotReachServer()
        {
            using var handler = new RecordingHandler(_ => JsonResponse(Fixture("movie.json")));
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateClient(handler).GetMovie(123, cancellation.Token));
            Assert.Empty(handler.Requests);
        }

        [Fact]
        public async Task CancellationIsForwardedToHttpHandler()
        {
            using var cancellation = new CancellationTokenSource();
            using var handler = new CancellationHandler(cancellation);
            var client = new PoiskKinoApiClient(() => "test-token", NullLogger<PoiskKinoApiClient>.Instance, new ClientFactory(handler));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetPerson(456, cancellation.Token));
        }

        [Fact]
        public async Task ResponseContentIsDisposedAfterDeserialization()
        {
            using var content = new TrackingContent(Fixture("movie.json"));
            using var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
            await CreateClient(handler).GetMovie(123);
            Assert.True(content.IsDisposed);
        }

        [Fact]
        public async Task BlankSearchDoesNotSpendApiRequests()
        {
            using var handler = new RecordingHandler(_ => throw new InvalidOperationException("Unexpected request"));
            Assert.Empty((await CreateClient(handler).SearchMovies("   ")).Docs);
            Assert.Empty(handler.Requests);
        }

        private static void AssertRequest(RecordingHandler handler, string url)
        {
            var request = Assert.Single(handler.Requests);
            Assert.Equal(url, request.Uri.AbsoluteUri);
            Assert.Equal("test-token", request.Token);
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.DoesNotContain("test-token", request.Uri.AbsoluteUri);
        }

        private static PoiskKinoApiClient CreateClient(RecordingHandler handler, Func<string> getToken = null)
            => new(getToken ?? (() => "test-token"), NullLogger<PoiskKinoApiClient>.Instance, new ClientFactory(handler));

        private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
        private static HttpResponseMessage JsonResponse(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };

        private sealed class ClientFactory : IHttpClientFactory
        {
            private readonly HttpMessageHandler _handler;
            public ClientFactory(HttpMessageHandler handler) => _handler = handler;
            public HttpClient CreateClient(string name)
            {
                Assert.Equal(PoiskKinoApiClient.HttpClientName, name);
                return new HttpClient(_handler, disposeHandler: false);
            }
        }

        private sealed class RecordingHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
            public List<(Uri Uri, string Token, HttpMethod Method)> Requests { get; } = new();
            public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Requests.Add((request.RequestUri, Assert.Single(request.Headers.GetValues("X-API-KEY")), request.Method));
                return Task.FromResult(_respond(request));
            }
        }

        private sealed class CancellationHandler : HttpMessageHandler
        {
            private readonly CancellationTokenSource _cancellation;
            public CancellationHandler(CancellationTokenSource cancellation) => _cancellation = cancellation;
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                _cancellation.Cancel();
                await Task.Delay(Timeout.Infinite, cancellationToken);
                throw new InvalidOperationException("Cancellation was not propagated");
            }
        }

        private sealed class TrackingContent : StringContent
        {
            public bool IsDisposed { get; private set; }
            public TrackingContent(string body) : base(body) { }
            protected override void Dispose(bool disposing)
            {
                if (disposing) IsDisposed = true;
                base.Dispose(disposing);
            }
        }
    }
}
