using System.Collections.Generic;

namespace PoiskKino.ApiClient
{
    // Only fields used by the plugin are modeled; unknown API fields are ignored.
    public class PoiskKinoMovie
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string AlternativeName { get; set; }
        public string EnName { get; set; }
        public string Type { get; set; }
        public bool? IsSeries { get; set; }
        public int? Year { get; set; }
        public string Description { get; set; }
        public string ShortDescription { get; set; }
        public string Slogan { get; set; }
        public int? AgeRating { get; set; }
        public string RatingMpaa { get; set; }
        public ExternalIds ExternalId { get; set; }
        public MovieRating Rating { get; set; }
        public ApiImage Poster { get; set; }
        public ApiImage Backdrop { get; set; }
        public MovieVideos Videos { get; set; }
        public MoviePremiere Premiere { get; set; }
        public List<NamedValue> Genres { get; set; }
        public List<NamedValue> Countries { get; set; }
        public List<MoviePerson> Persons { get; set; }
    }

    public class MovieSearchResponse
    {
        public List<PoiskKinoMovie> Docs { get; set; } = new();
        public int Total { get; set; }
        public int Limit { get; set; }
        public int Page { get; set; }
        public int Pages { get; set; }
    }

    public class ExternalIds
    {
        public string Imdb { get; set; }
        public int? Tmdb { get; set; }
    }

    public class MovieRating
    {
        public double? Kp { get; set; }
        public double? Imdb { get; set; }
        public double? FilmCritics { get; set; }
        public double? RussianFilmCritics { get; set; }
    }

    public class ApiImage
    {
        public string Url { get; set; }
        public string PreviewUrl { get; set; }
    }

    public class NamedValue
    {
        public string Name { get; set; }
    }

    public class MovieVideos
    {
        public List<MovieVideo> Trailers { get; set; }
    }

    public class MovieVideo
    {
        public string Url { get; set; }
        public string Name { get; set; }
        public string Site { get; set; }
    }

    public class MoviePremiere
    {
        public string World { get; set; }
        public string Russia { get; set; }
    }

    public class MoviePerson
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string EnName { get; set; }
        public string Photo { get; set; }
        public string Description { get; set; }
        public string Profession { get; set; }
        public string EnProfession { get; set; }
    }

    public class PoiskKinoPerson
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string EnName { get; set; }
        public string Photo { get; set; }
        public string Birthday { get; set; }
        public string Death { get; set; }
        public List<PlaceValue> BirthPlace { get; set; }
    }

    public class PlaceValue
    {
        public string Value { get; set; }
    }
}
