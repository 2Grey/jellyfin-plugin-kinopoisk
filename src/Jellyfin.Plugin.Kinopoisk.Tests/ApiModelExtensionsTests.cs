using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data.Enums;
using MediaBrowser.Model.Entities;
using PoiskKino.ApiClient;
using Xunit;

namespace Jellyfin.Plugin.Kinopoisk.Tests
{
    public class ApiModelExtensionsTests
    {
        [Fact]
        public void ToPersonInfosSkipsMissingNamesAndPreservesCastOrderAndRoles()
        {
            var staff = new List<MoviePerson>
            {
                new() { Id = 1, Name = "Иван Иванов", EnProfession = "actor", Description = "главный герой" },
                new() { Id = 2, Name = string.Empty },
                null,
                new() { Id = 3, Name = "   ", EnName = "   " },
                new() { Id = 4, EnName = "John Smith", EnProfession = "director" }
            };
            var result = staff.ToPersonInfos().ToArray();
            Assert.Equal(2, result.Length);
            Assert.Equal("Иван Иванов", result[0].Name);
            Assert.Equal("главный герой", result[0].Role);
            Assert.Equal(PersonKind.Actor, result[0].Type);
            Assert.Equal(1, result[0].SortOrder);
            Assert.Equal("John Smith", result[1].Name);
            Assert.Equal(PersonKind.Director, result[1].Type);
            Assert.Equal(2, result[1].SortOrder);
        }

        [Fact]
        public void MovieMappingIncludesImagesRatingsExternalIdsAndPremiere()
        {
            var movie = new PoiskKinoMovie
            {
                Id = 123, Name = "Название", AlternativeName = "Original title", Year = 2026,
                ShortDescription = "Описание", AgeRating = 0, Slogan = "Слоган",
                Rating = new MovieRating { Kp = 0, Imdb = 8.1, RussianFilmCritics = 7.5 },
                ExternalId = new ExternalIds { Imdb = "tt123", Tmdb = 456 },
                Premiere = new MoviePremiere { World = "2026-03-04T00:00:00.000Z" },
                Genres = new() { new NamedValue { Name = "драма" }, null },
                Countries = new() { new NamedValue { Name = "Россия" } },
                Poster = new ApiImage { Url = "https://example.com/poster.jpg" },
                Backdrop = new ApiImage { PreviewUrl = "https://example.com/backdrop.jpg" }
            };
            var result = movie.ToMovie();
            Assert.Equal("Original title", result.OriginalTitle);
            Assert.Equal("Описание", result.Overview);
            Assert.Equal("0+", result.OfficialRating);
            Assert.Equal(8.1f, result.CommunityRating);
            Assert.Equal(7.5f, result.CriticRating);
            Assert.Equal("456", result.GetProviderId(MetadataProvider.Tmdb));
            Assert.Equal(new DateTime(2026, 3, 4, 0, 0, 0, DateTimeKind.Utc), result.PremiereDate);
            Assert.Equal("драма", Assert.Single(result.Genres));
            Assert.Equal("Россия", Assert.Single(result.ProductionLocations));
            var images = movie.ToRemoteImageInfos().ToArray();
            Assert.Equal(ImageType.Primary, images[0].Type);
            Assert.Equal(ImageType.Backdrop, images[1].Type);
            Assert.Equal("https://example.com/backdrop.jpg", images[1].Url);
        }

        [Fact]
        public void MissingOptionalMetadataProducesEmptyCollections()
        {
            var movie = new PoiskKinoMovie { Id = 123, EnName = "English title" };
            var result = movie.ToMovie();
            Assert.Equal("English title", result.Name);
            Assert.Null(result.PremiereDate);
            Assert.Null(result.CommunityRating);
            Assert.Empty(result.RemoteTrailers);
            Assert.Empty(movie.ToRemoteImageInfos());
            Assert.Empty(movie.Persons.ToPersonInfos());
            Assert.Empty(new MovieSearchResponse { Docs = null }.ToRemoteSearchResults());
            Assert.Null(((PoiskKinoMovie)null).ToMovie());
        }

        [Fact]
        public void PersonMappingKeepsIdBirthplaceAndDates()
        {
            var person = new PoiskKinoPerson
            {
                Id = 456, EnName = "Person name", Birthday = "1980-02-03", Death = "01.05.2025",
                Photo = "https://example.com/person.jpg",
                BirthPlace = new() { new PlaceValue { Value = "Москва" }, null, new PlaceValue { Value = "Россия" } }
            };
            var result = person.ToPerson();
            Assert.Equal("456", result.GetProviderId(Constants.ProviderId));
            Assert.Equal("Person name", result.Name);
            Assert.Equal(new DateTime(1980, 2, 3, 0, 0, 0, DateTimeKind.Utc), result.PremiereDate);
            Assert.Equal(new DateTime(2025, 5, 1, 0, 0, 0, DateTimeKind.Utc), result.EndDate);
            Assert.Equal(new[] { "Москва", "Россия" }, result.ProductionLocations);
            Assert.Equal(person.Photo, person.ToRemoteImageInfo().Url);
        }

        [Theory]
        [InlineData("https://www.youtube.com/embed/abc-123_?rel=0", "https://www.youtube.com/watch?v=abc-123_")]
        [InlineData("http://youtu.be/abc123?t=30", "https://www.youtube.com/watch?v=abc123")]
        [InlineData("https://www.youtube.com/watch?v=abc123&list=456", "https://www.youtube.com/watch?v=abc123")]
        [InlineData("https://youtube.com/shorts/abc123", "https://www.youtube.com/watch?v=abc123")]
        [InlineData("https://www.youtube.com/v/abc123", "https://www.youtube.com/watch?v=abc123")]
        [InlineData("https://example.com/watch?v=abc123", null)]
        [InlineData("https://www.youtube.com/embed/", null)]
        [InlineData(null, null)]
        public void YoutubeTrailersAreNormalized(string url, string expected)
            => Assert.Equal(expected, url.SanitizeYoutubeLink());

        [Theory]
        [InlineData("actor", PersonKind.Actor)]
        [InlineData("producer_ussr", PersonKind.Producer)]
        [InlineData("translator", PersonKind.Translator)]
        [InlineData("монтажеры", PersonKind.Editor)]
        [InlineData("unknown-new-profession", PersonKind.Unknown)]
        [InlineData(null, PersonKind.Unknown)]
        public void ProfessionsAreMappedIncludingUnknownValues(string profession, PersonKind expected)
            => Assert.Equal(expected, profession.ToPersonType());
    }
}
