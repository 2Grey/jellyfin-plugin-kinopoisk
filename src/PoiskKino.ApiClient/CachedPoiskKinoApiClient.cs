using System;
using System.Collections.Concurrent;
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
        private readonly ConcurrentDictionary<string, Lazy<Task<object>>> _pending = new();
        private readonly TimeSpan _documentLifetime;
        private readonly TimeSpan _searchLifetime;

        public CachedPoiskKinoApiClient(IPoiskKinoApiClient innerClient, IMemoryCache cache,
            TimeSpan? documentLifetime = null, TimeSpan? searchLifetime = null)
        {
            _innerClient = innerClient ?? throw new ArgumentNullException(nameof(innerClient));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _documentLifetime = documentLifetime ?? TimeSpan.FromMinutes(15);
            _searchLifetime = searchLifetime ?? TimeSpan.FromMinutes(2);
            if (_documentLifetime <= TimeSpan.Zero || _searchLifetime <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(documentLifetime), "Cache lifetimes must be positive.");
        }

        public Task<PoiskKinoMovie> GetMovie(int movieId, CancellationToken cancellationToken = default)
            => GetCached("movie:" + movieId, () => _innerClient.GetMovie(movieId), _documentLifetime, cancellationToken);

        public Task<PoiskKinoPerson> GetPerson(int personId, CancellationToken cancellationToken = default)
            => GetCached("person:" + personId, () => _innerClient.GetPerson(personId), _documentLifetime, cancellationToken);

        public Task<MovieSearchResponse> SearchMovies(string query, int page = 1, CancellationToken cancellationToken = default)
            => GetCached("search:" + page + ":" + query, () => _innerClient.SearchMovies(query, page), _searchLifetime, cancellationToken);

        private async Task<T> GetCached<T>(string key, Func<Task<T>> factory, TimeSpan lifetime, CancellationToken cancellationToken)
            where T : class
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_cache.TryGetValue(_cachePrefix + key, out T cached))
                return cached;

            var pending = _pending.GetOrAdd(key, _ => new Lazy<Task<object>>(
                () => Fetch(key, factory, lifetime), LazyThreadSafetyMode.ExecutionAndPublication));
            // Cancellation belongs to each waiter. A cancelled scan must not cancel
            // a shared request still needed by another provider (or poison its cache).
            return (T)await pending.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        private async Task<object> Fetch<T>(string key, Func<Task<T>> factory, TimeSpan lifetime) where T : class
        {
            try
            {
                if (_cache.TryGetValue(_cachePrefix + key, out T cached))
                    return cached;
                var result = await factory().ConfigureAwait(false);
                _cache.Set(_cachePrefix + key, result, result is null ? TimeSpan.FromMinutes(1) : lifetime);
                return result;
            }
            finally
            {
                _pending.TryRemove(key, out _);
            }
        }
    }
}
