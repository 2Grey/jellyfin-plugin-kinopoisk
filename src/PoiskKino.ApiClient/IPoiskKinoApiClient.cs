using System.Threading;
using System.Threading.Tasks;

namespace PoiskKino.ApiClient
{
    public interface IPoiskKinoApiClient
    {
        Task<PoiskKinoMovie> GetMovie(int movieId, CancellationToken cancellationToken = default);
        Task<PoiskKinoPerson> GetPerson(int personId, CancellationToken cancellationToken = default);
        Task<MovieSearchResponse> SearchMovies(string query, int page = 1, CancellationToken cancellationToken = default);
    }
}
