using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace PoiskKino.ApiClient
{
    public class PoiskKinoApiClient : IPoiskKinoApiClient
    {
        public const string HttpClientName = "PoiskKino";
        private const string BaseUrl = "https://api.poiskkino.dev/v1.5/";
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
        private readonly Func<string> _getApiToken;
        private readonly ILogger<PoiskKinoApiClient> _logger;
        private readonly IHttpClientFactory _httpClientFactory;

        public PoiskKinoApiClient(Func<string> getApiToken, ILogger<PoiskKinoApiClient> logger, IHttpClientFactory httpClientFactory)
        {
            _getApiToken = getApiToken ?? throw new ArgumentNullException(nameof(getApiToken));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        }

        public Task<PoiskKinoMovie> GetMovie(int movieId, CancellationToken cancellationToken = default)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(movieId);
            return Get<PoiskKinoMovie>("movie/" + movieId.ToString(CultureInfo.InvariantCulture), cancellationToken);
        }

        public Task<PoiskKinoPerson> GetPerson(int personId, CancellationToken cancellationToken = default)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(personId);
            return Get<PoiskKinoPerson>("person/" + personId.ToString(CultureInfo.InvariantCulture), cancellationToken);
        }

        public async Task<MovieSearchResponse> SearchMovies(string query, int page = 1, CancellationToken cancellationToken = default)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(page);
            if (string.IsNullOrWhiteSpace(query))
                return new MovieSearchResponse();

            var path = "movie/search?page=" + page.ToString(CultureInfo.InvariantCulture)
                + "&limit=20&query=" + Uri.EscapeDataString(query);
            // A missing search endpoint is an API error, not an empty set of results.
            return await Get<MovieSearchResponse>(path, cancellationToken, allowNotFound: false);
        }

        private async Task<T> Get<T>(string path, CancellationToken cancellationToken, bool allowNotFound = true) where T : class
        {
            cancellationToken.ThrowIfCancellationRequested();
            var token = _getApiToken()?.Trim();
            if (string.IsNullOrEmpty(token))
                throw new InvalidOperationException("Укажите API-ключ PoiskKino в настройках плагина КиноПоиск.");

            using var httpClient = _httpClientFactory.CreateClient(HttpClientName);
            // HttpClient.Timeout stops at the headers when using ResponseHeadersRead.
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(httpClient.Timeout);
            using var request = new HttpRequestMessage(HttpMethod.Get, BaseUrl + path);
            request.Headers.Add("X-API-KEY", token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            try
            {
                using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
                if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
                    return null;

                if (!response.IsSuccessStatusCode)
                    _logger.LogWarning("PoiskKino returned HTTP {StatusCode}", (int)response.StatusCode);
                response.EnsureSuccessStatusCode();

                using var stream = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
                var result = await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, deadline.Token).ConfigureAwait(false);
                return result ?? throw new JsonException("PoiskKino returned an empty JSON document.");
            }
            catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested
                && (deadline.IsCancellationRequested || exception.InnerException is TimeoutException))
            {
                throw new TimeoutException($"PoiskKino request exceeded the timeout of {httpClient.Timeout.TotalSeconds} seconds.", exception);
            }
        }
    }
}
