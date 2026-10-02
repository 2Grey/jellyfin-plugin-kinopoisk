using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;

namespace PoiskKino.ApiClient
{
    public class CachedPoiskKinoApiClient : IPoiskKinoApiClient
    {
        private readonly IPoiskKinoApiClient _innerClient;
        private readonly IMemoryCache _cache;
        private readonly string _cachePrefix = "PoiskKino:" + Guid.NewGuid() + ":";

        public CachedPoiskKinoApiClient(IPoiskKinoApiClient innerClient, IMemoryCache cache)
        {
            _innerClient = innerClient ?? throw new ArgumentNullException(nameof(innerClient));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        }

        public Task<PoiskKinoMovie> GetMovie(int movieId, CancellationToken cancellationToken = default)
            => GetCached("movie:" + movieId, () => _innerClient.GetMovie(movieId, cancellationToken), cancellationToken);

        public Task<PoiskKinoPerson> GetPerson(int personId, CancellationToken cancellationToken = default)
            => GetCached("person:" + personId, () => _innerClient.GetPerson(personId, cancellationToken), cancellationToken);

        public Task<MovieSearchResponse> SearchMovies(string query, int page = 1, CancellationToken cancellationToken = default)
            => GetCached("search:" + page + ":" + query, () => _innerClient.SearchMovies(query, page, cancellationToken), cancellationToken);

        private Task<T> GetCached<T>(string key, Func<Task<T>> factory, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return _cache.GetOrCreateAsync(_cachePrefix + key, entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1);
                return factory();
            });
        }
    }
}
