using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.Kinopoisk
{
    public class RemoteImageUrlSanitizer
    {
        public const string HttpClientName = "KinopoiskImages";
        // Shared across providers so a library scan cannot flood image hosts.
        private static readonly SemaphoreSlim Requests = new(4, 4);
        private readonly HttpClient _httpClient;
        private readonly ILogger _logger;

        public RemoteImageUrlSanitizer(HttpClient httpClient, ILogger logger = null)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger ?? NullLogger.Instance;
        }

        public async Task<string> SanitizeRemoteImageUrl(string url, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Uri.TryCreate(url, UriKind.Absolute, out var currentUrl) || !IsHttp(currentUrl))
                return null;

            await Requests.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var visited = new HashSet<Uri>();
                for (var redirects = 0; redirects <= 5; redirects++)
                {
                    if (!visited.Add(currentUrl) || currentUrl.AbsoluteUri.Contains("no-poster", StringComparison.OrdinalIgnoreCase))
                        return null;
                    using var request = new HttpRequestMessage(HttpMethod.Get, currentUrl);
                    using var response = await _httpClient.SendAsync(request,
                        HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                    if (response.IsSuccessStatusCode)
                        return currentUrl.AbsoluteUri;
                    if (response.StatusCode is not (HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect
                        or HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
                        || response.Headers.Location is null)
                        return null;
                    currentUrl = new Uri(currentUrl, response.Headers.Location);
                    if (!IsHttp(currentUrl))
                        return null;
                }
                return null;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogDebug("Image validation timed out");
                return null;
            }
            catch (HttpRequestException exception)
            {
                _logger.LogDebug(exception, "Image validation failed");
                return null;
            }
            catch (UriFormatException)
            {
                return null;
            }
            finally
            {
                Requests.Release();
            }
        }

        private static bool IsHttp(Uri uri) => uri.Scheme is "http" or "https";
    }
}
