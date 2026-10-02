using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
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
            var searchResult = await _kinopoiskApiClient.SearchMovies(info.Name, 1, ct ?? CancellationToken.None);
            if (searchResult?.Docs is null || searchResult.Docs.Count == 0)
            {
                _logger.LogDebug($"Received empty search result");
                return (false, 0);
            }
            var candidates = searchResult.Docs.Where(movie => movie != null && movie.Id > 0).ToArray();
            _logger.LogDebug($"Received {candidates.Length} results, trying to filter and match...");

            var candidates_by_year = FilterByYear(info, candidates);

            // Check if there are single candidate filtered by year
            possibleResult = await TryResolveBySingleCandidateLeft(info, candidates_by_year, ct);
            if (possibleResult.IsSuccess)
                return possibleResult;

            // Try to resolve by ImdbId match filtered by year
            possibleResult = await TryResolveByImdbMatch(info, candidates_by_year, ct);
            if (possibleResult.IsSuccess)
                return possibleResult;

            // Try to resolve by ImdbId match without filtering
            possibleResult = await TryResolveByImdbMatch(info, candidates, ct);
            if (possibleResult.IsSuccess)
                return possibleResult;

            // TODO: Maybe check type?

            if (0 < candidates_by_year.Count)
            {
                var kinopoiskId = candidates_by_year.First().Id;
                _logger.LogDebug($"All other checks failed, use first result by year, setting KinopoiskProviderId to {kinopoiskId} ({info.Name})");
                return (true, kinopoiskId);
            }

            if (0 < candidates.Length)
            {
                var kinopoiskId = candidates.First().Id;
                _logger.LogDebug($"All other checks failed, use first result, setting KinopoiskProviderId to {kinopoiskId} ({info.Name})");
                return (true, kinopoiskId);
            }

            _logger.LogDebug($"Suitable result not found");
            return (false, 0);
        }

        public async Task<(bool IsSuccess, int ProviderId)> TryResolveByImdbMatch(T info, ICollection<PoiskKinoMovie> candidates, CancellationToken? ct = null)
        {
            if (info.TryGetProviderId(MetadataProvider.Imdb, out var imdbId))
            {
                _logger.LogDebug($"Trying to find result with ImdbId '{imdbId}'...");
                var index = 0;
                foreach (var candidate in candidates)
                {
                    try
                    {
                        if (!string.IsNullOrWhiteSpace(candidate.ExternalId?.Imdb))
                        {
                            if (imdbId == candidate.ExternalId.Imdb)
                                return (true, candidate.Id);
                            continue;
                        }
                        var film = await _kinopoiskApiClient.GetMovie(candidate.Id, ct ?? CancellationToken.None);

                        if (imdbId == film?.ExternalId?.Imdb)
                        {
                            _logger.LogDebug($"Found match: {candidate.Id} '{film.GetLocalName()}', ImdbId '{film?.ExternalId?.Imdb}', setting KinopoiskProviderId to {candidate.Id}");
                            return (true, candidate.Id);
                        }

                        _logger.LogDebug($"Film {candidate.Id} '{film.GetLocalName()}' has ImdbId '{film?.ExternalId?.Imdb}', skipping, {candidates.Count - ++index} candidates left...");
                    }
                    catch (OperationCanceledException) when ((ct ?? CancellationToken.None).IsCancellationRequested)
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
                        _logger.LogError(e, $"Error while retrieving film {candidate.Id}");
                        continue;
                    }
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
