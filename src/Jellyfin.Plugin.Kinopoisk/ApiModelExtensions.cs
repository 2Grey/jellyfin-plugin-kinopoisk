using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using PoiskKino.ApiClient;

namespace Jellyfin.Plugin.Kinopoisk
{
    public static class ApiModelExtensions
    {
        public static RemoteSearchResult ToRemoteSearchResult(this PoiskKinoMovie src)
        {
            if (src is null || src.Id <= 0)
                return null;

            var result = new RemoteSearchResult
            {
                Name = src.GetLocalName(),
                ImageUrl = GetImageUrl(src.Poster),
                PremiereDate = src.GetPremiereDate(),
                ProductionYear = src.Year,
                Overview = src.Description ?? src.ShortDescription,
                SearchProviderName = Constants.ProviderName
            };
            result.SetProviderId(Constants.ProviderId, src.Id.ToString(CultureInfo.InvariantCulture));
            return result;
        }

        public static IEnumerable<RemoteSearchResult> ToRemoteSearchResults(this MovieSearchResponse src)
            => (src?.Docs ?? Enumerable.Empty<PoiskKinoMovie>())
                .Select(movie => movie.ToRemoteSearchResult()).Where(result => result != null);

        public static Movie ToMovie(this PoiskKinoMovie src)
        {
            if (src is null || src.Id <= 0)
                return null;
            var result = new Movie();
            FillCommonMovieInfo(src, result);
            return result;
        }

        public static Series ToSeries(this PoiskKinoMovie src)
        {
            if (src is null || src.Id <= 0)
                return null;
            var result = new Series();
            FillCommonMovieInfo(src, result);
            return result;
        }

        private static void FillCommonMovieInfo(PoiskKinoMovie src, BaseItem dst)
        {
            dst.SetProviderId(Constants.ProviderId, src.Id.ToString(CultureInfo.InvariantCulture));
            dst.Name = src.GetLocalName();
            var originalName = FirstNonEmpty(src.AlternativeName, src.EnName);
            dst.OriginalTitle = originalName == dst.Name ? string.Empty : originalName;
            dst.PremiereDate = src.GetPremiereDate();
            dst.ProductionYear = src.Year;
            dst.Tagline = src.Slogan;
            dst.Overview = src.Description ?? src.ShortDescription;
            dst.ProductionLocations = (src.Countries ?? Enumerable.Empty<NamedValue>())
                .Select(country => country?.Name).Where(name => !string.IsNullOrWhiteSpace(name)).ToArray();
            foreach (var genre in src.Genres ?? Enumerable.Empty<NamedValue>())
                if (!string.IsNullOrWhiteSpace(genre?.Name))
                    dst.AddGenre(genre.Name);
            dst.OfficialRating = src.AgeRating.HasValue
                ? src.AgeRating.Value.ToString(CultureInfo.InvariantCulture) + "+"
                : src.RatingMpaa?.ToUpperInvariant();
            dst.CommunityRating = PositiveRating(src.Rating?.Kp) ?? PositiveRating(src.Rating?.Imdb);
            dst.CriticRating = PositiveRating(src.Rating?.RussianFilmCritics) ?? PositiveRating(src.Rating?.FilmCritics);
            if (!string.IsNullOrWhiteSpace(src.ExternalId?.Imdb))
                dst.SetProviderId(MetadataProvider.Imdb, src.ExternalId.Imdb);
            if (src.ExternalId?.Tmdb > 0)
                dst.SetProviderId(MetadataProvider.Tmdb, src.ExternalId.Tmdb.Value.ToString(CultureInfo.InvariantCulture));
            dst.RemoteTrailers = src.Videos.ToMediaUrls();
        }

        private static float? PositiveRating(double? rating) => rating >= 0.1 ? (float?)rating.Value : null;

        public static string GetLocalName(this PoiskKinoMovie src)
            => FirstNonEmpty(src?.Name, src?.AlternativeName, src?.EnName);

        public static DateTime? GetPremiereDate(this PoiskKinoMovie src)
        {
            var premiere = src?.Premiere?.World.ParseDate() ?? src?.Premiere?.Russia.ParseDate();
            if (premiere.HasValue)
                return premiere;
            return src?.Year is > 0 and <= 9999 ? new DateTime(src.Year.Value, 1, 1, 0, 0, 0, DateTimeKind.Utc) : null;
        }

        public static IEnumerable<RemoteImageInfo> ToRemoteImageInfos(this PoiskKinoMovie src)
        {
            var poster = GetImageUrl(src?.Poster);
            if (!string.IsNullOrWhiteSpace(poster))
                yield return CreateImage(poster, ImageType.Primary);
            var backdrop = GetImageUrl(src?.Backdrop);
            if (!string.IsNullOrWhiteSpace(backdrop))
                yield return CreateImage(backdrop, ImageType.Backdrop);
        }

        private static string GetImageUrl(ApiImage image) => FirstNonEmpty(image?.Url, image?.PreviewUrl);

        private static RemoteImageInfo CreateImage(string url, ImageType type) => new()
        {
            Type = type,
            Url = url,
            Language = Constants.ProviderMetadataLanguage,
            ProviderName = Constants.ProviderName
        };

        public static IReadOnlyList<MediaUrl> ToMediaUrls(this MovieVideos src)
            => (src?.Trailers ?? Enumerable.Empty<MovieVideo>())
                .Where(video => video != null)
                .Select(video => new MediaUrl { Name = video.Name, Url = video.Url.SanitizeYoutubeLink() })
                .Where(video => video.Url != null)
                .ToArray();

        public static string SanitizeYoutubeLink(this string src)
        {
            if (!Uri.TryCreate(src, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
                return null;
            string videoId = null;
            if (uri.Host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase))
                videoId = uri.AbsolutePath.Trim('/');
            else if (new[] { "youtube.com", "www.youtube.com", "m.youtube.com", "youtube-nocookie.com", "www.youtube-nocookie.com" }
                .Contains(uri.Host, StringComparer.OrdinalIgnoreCase))
            {
                var parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2 && (parts[0] == "embed" || parts[0] == "v" || parts[0] == "shorts"))
                    videoId = parts[1];
                else if (uri.AbsolutePath == "/watch")
                    videoId = uri.Query.TrimStart('?').Split('&').Select(part => part.Split('=', 2))
                        .FirstOrDefault(part => part.Length == 2 && part[0] == "v")?.ElementAt(1);
            }
            if (string.IsNullOrWhiteSpace(videoId) || videoId.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_'))
                return null;
            return "https://www.youtube.com/watch?v=" + videoId;
        }

        public static PersonInfo ToPersonInfo(this MoviePerson src)
        {
            var name = FirstNonEmpty(src?.Name, src?.EnName);
            if (src is null || src.Id <= 0 || name is null)
                return null;
            var result = new PersonInfo
            {
                Name = name,
                ImageUrl = src.Photo,
                Role = FirstNonEmpty(src.Description, src.Profession),
                Type = FirstNonEmpty(src.EnProfession, src.Profession).ToPersonType()
            };
            result.SetProviderId(Constants.ProviderId, src.Id.ToString(CultureInfo.InvariantCulture));
            return result;
        }

        public static IEnumerable<PersonInfo> ToPersonInfos(this IEnumerable<MoviePerson> src)
        {
            var people = (src ?? Enumerable.Empty<MoviePerson>()).Select(person => person.ToPersonInfo())
                .Where(person => person != null).ToArray();
            for (var i = 0; i < people.Length; i++)
                people[i].SortOrder = i + 1;
            return people;
        }

        public static PersonKind ToPersonType(this string profession) => profession?.Trim().ToLowerInvariant() switch
        {
            "actor" or "voice_actor" or "актеры" or "актёры" or "актер" or "актёр" => PersonKind.Actor,
            "director" or "voice_director" or "operator" or "режиссеры" or "режиссёры" or "операторы" => PersonKind.Director,
            "writer" or "сценаристы" => PersonKind.Writer,
            "composer" or "композиторы" => PersonKind.Composer,
            "producer" or "producer_ussr" or "продюсеры" or "директора фильма" => PersonKind.Producer,
            "editor" or "монтажеры" or "монтажёры" => PersonKind.Editor,
            "translator" or "переводчики" => PersonKind.Translator,
            _ => PersonKind.Unknown
        };

        public static Person ToPerson(this PoiskKinoPerson src)
        {
            if (src is null || src.Id <= 0)
                return null;
            var result = new Person
            {
                Name = FirstNonEmpty(src.Name, src.EnName),
                PremiereDate = src.Birthday.ParseDate(),
                EndDate = src.Death.ParseDate(),
                ProductionLocations = (src.BirthPlace ?? Enumerable.Empty<PlaceValue>())
                    .Select(place => place?.Value).Where(value => !string.IsNullOrWhiteSpace(value)).ToArray()
            };
            result.SetProviderId(Constants.ProviderId, src.Id.ToString(CultureInfo.InvariantCulture));
            return result;
        }

        public static RemoteImageInfo ToRemoteImageInfo(this PoiskKinoPerson src)
            => string.IsNullOrWhiteSpace(src?.Photo) ? null : CreateImage(src.Photo, ImageType.Primary);

        public static DateTime? ParseDate(this string src)
        {
            var formats = new[] { "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK", "yyyy-MM-dd", "dd.MM.yyyy" };
            return DateTime.TryParseExact(src, formats, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var value) ? value : null;
        }

        private static string FirstNonEmpty(params string[] values) => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }
}
