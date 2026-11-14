using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PoiskKino.ApiClient;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Kinopoisk.ProviderIdResolvers
{
    public class VideoResolver<T> : CommonLookupInfoResolver<T>
        where T : ItemLookupInfo
    {
        private readonly IPoiskKinoApiClient _kinopoiskApiClient;

        public VideoResolver(IPoiskKinoApiClient kinopoiskApiClient, ILogger<VideoResolver<T>> logger) : base(logger)
        {
            _kinopoiskApiClient = kinopoiskApiClient ?? throw new ArgumentNullException(nameof(kinopoiskApiClient));
        }

        public override async Task<(bool IsSuccess, int ProviderId)> TryResolve(T info, CancellationToken? ct = null)
        {
            // Try to get from standart sources
            var possibleResult = await base.TryResolve(info, ct);
            if (possibleResult.IsSuccess)
                return possibleResult;

            // Trying to find empirically on kinopoisk
            if (string.IsNullOrWhiteSpace(info.Name))
            {
                _logger.LogDebug($"Film name is empty, skipping KinopoiskProviderId search");
                return (false, 0);
            }

            _logger.LogDebug($"Trying to get suitable film with name '{info.Name}'...");
            const int maxPages = 5;
            var candidates = new List<PoiskKinoMovie>();
            var seen = new HashSet<int>();
            var complete = false;
            for (var page = 1; page <= maxPages; page++)
            {
                var response = await _kinopoiskApiClient.SearchMovies(info.Name, page, ct ?? CancellationToken.None);
                foreach (var movie in response?.Docs ?? Enumerable.Empty<PoiskKinoMovie>())
                    if (movie != null && movie.Id > 0 && MatchesType(info, movie) && seen.Add(movie.Id))
                        candidates.Add(movie);
                if (response is null || response.Pages <= page || response.Docs is null || response.Docs.Count == 0)
                {
                    complete = true;
                    break;
                }
            }

            // External IDs are stronger evidence than a title or release year.
            if (info.TryGetProviderId(MetadataProvider.Imdb, out var imdbId) && !string.IsNullOrWhiteSpace(imdbId))
                return await TryResolveByImdbMatch(info, candidates, ct);
            if (info.TryGetProviderId(MetadataProvider.Tmdb, out var tmdbId) && !string.IsNullOrWhiteSpace(tmdbId))
                return await TryResolveByExternalId(info, candidates, tmdbId,
                    ids => ids?.Tmdb?.ToString(CultureInfo.InvariantCulture), ct);

            // A truncated search cannot establish uniqueness by title.
            if (!complete)
                return (false, 0);
            var title = NormalizeTitle(info.Name);
            var matchingTitles = candidates.Where(movie => title.Length > 0 &&
                new[] { movie.Name, movie.AlternativeName, movie.EnName }.Any(name => NormalizeTitle(name) == title)).ToArray();
            var matchesByTitleAndYear = info.Year.HasValue ? FilterByYear(info, matchingTitles) : matchingTitles;
            return await TryResolveBySingleCandidateLeft(info, matchesByTitleAndYear, ct);
        }

        private static bool MatchesType(T info, PoiskKinoMovie movie)
        {
            var isSeries = movie.IsSeries ?? movie.Type switch
            {
                "tv-series" or "animated-series" => (bool?)true,
                "movie" or "cartoon" => false,
                _ => null
            };
            return !isSeries.HasValue || isSeries.Value == (info is SeriesInfo);
        }

        private static string NormalizeTitle(string title)
        {
            var result = new StringBuilder();
            foreach (var character in (title ?? string.Empty).Normalize().ToLowerInvariant())
                if (char.IsLetterOrDigit(character))
                    result.Append(character == 'ё' ? 'е' : character);
            return result.ToString();
        }

        public Task<(bool IsSuccess, int ProviderId)> TryResolveByImdbMatch(T info, ICollection<PoiskKinoMovie> candidates, CancellationToken? ct = null)
            => info.TryGetProviderId(MetadataProvider.Imdb, out var imdbId) && !string.IsNullOrWhiteSpace(imdbId)
                ? TryResolveByExternalId(info, candidates, imdbId, ids => ids?.Imdb, ct)
                : Task.FromResult((false, 0));

        private async Task<(bool IsSuccess, int ProviderId)> TryResolveByExternalId(T info,
            ICollection<PoiskKinoMovie> candidates, string externalId, Func<ExternalIds, string> getId, CancellationToken? ct)
        {
            var token = ct ?? CancellationToken.None;
            token.ThrowIfCancellationRequested();
            var embeddedMatches = candidates.Where(movie =>
                string.Equals(getId(movie.ExternalId), externalId, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (embeddedMatches.Length > 0)
                return await TryResolveBySingleCandidateLeft(info, embeddedMatches, ct);

            foreach (var candidate in candidates.Where(movie => string.IsNullOrWhiteSpace(getId(movie.ExternalId))))
            {
                try
                {
                    var film = await _kinopoiskApiClient.GetMovie(candidate.Id, token);
                    if (film != null && MatchesType(info, film) &&
                        string.Equals(getId(film.ExternalId), externalId, StringComparison.OrdinalIgnoreCase))
                        return (true, candidate.Id);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (HttpRequestException e) when (e.StatusCode is HttpStatusCode.Unauthorized
                    or HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
                {
                    throw;
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "Error while retrieving film {MovieId}", candidate.Id);
                }
            }
            return (false, 0);
        }

        public Task<(bool IsSuccess, int ProviderId)> TryResolveBySingleCandidateLeft(T info, ICollection<PoiskKinoMovie> candidates, CancellationToken? ct = null)
        {
            if (candidates.Count == 1)
            {
                var kinopoiskId = candidates.Single().Id;
                _logger.LogDebug($"There is single candidate left, setting KinopoiskProviderId to {kinopoiskId} ({info.Name})");
                return Task.FromResult((true, kinopoiskId));
            }

            return Task.FromResult((false, 0));
        }

        public ICollection<PoiskKinoMovie> FilterByYear(T info, ICollection<PoiskKinoMovie> candidates)
        {
            if (!info.Year.HasValue)
            {
                _logger.LogDebug($"Can't filter by year, no year set in metadata...");
                return Array.Empty<PoiskKinoMovie>();
            }

            var res = candidates.Where(f => f.Year == info.Year.Value).ToArray();
            _logger.LogDebug($"Filtered by year {info.Year.Value}, {res.Length} results left...");
            return res;
        }
    }
}
