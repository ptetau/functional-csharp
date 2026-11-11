using System.Collections.Immutable;
using Functional;

namespace CharacterReviews.Core;

/// <summary>A character with its title and the numbers shown beside it.</summary>
public sealed record CharacterSummary(Character Character, Title Title, int ReviewCount, Option<decimal> Average);

/// <summary>Number of reviews at each star level, 5 down to 1.</summary>
public sealed record StarCount(int Stars, int Count);

/// <summary>Read-only questions about the state: lookups, ratings, search and the home page lists.</summary>
public static class Queries
{
    public const int HomeListSize = 10;
    public const int MaxSearchResults = 50;

    public static Option<Title> FindTitle(State state, string titleSlug) =>
        Option.FromNullable(state.Titles.FirstOrDefault(t => t.Slug == titleSlug));

    public static Option<Character> FindCharacter(State state, string titleSlug, string characterSlug) =>
        Option.FromNullable(state.Characters.FirstOrDefault(c => c.TitleSlug == titleSlug && c.Slug == characterSlug));

    public static ImmutableArray<Review> ReviewsOf(State state, Character character) =>
        state.Reviews
            .Where(r => r.TitleSlug == character.TitleSlug && r.CharacterSlug == character.Slug)
            .ToImmutableArray();

    /// <summary>C-9: the mean of all ratings, rounded half away from zero to one decimal. None when there are no reviews.</summary>
    public static Option<decimal> Average(IReadOnlyCollection<Review> reviews) =>
        reviews.Count == 0
            ? Option.None<decimal>()
            : Option.Some(Math.Round((decimal)reviews.Sum(r => r.Rating) / reviews.Count, 1, MidpointRounding.AwayFromZero));

    /// <summary>C-4: how many reviews gave each number of stars, from 5 down to 1, including zeros.</summary>
    public static ImmutableArray<StarCount> Breakdown(IReadOnlyCollection<Review> reviews) =>
        Enumerable.Range(1, 5)
            .Reverse()
            .Select(stars => new StarCount(stars, reviews.Count(r => r.Rating == stars)))
            .ToImmutableArray();

    /// <summary>C-4: newest first. Reviews from the same day are ordered by when they were saved.</summary>
    public static ImmutableArray<Review> NewestFirst(IEnumerable<Review> reviews) =>
        reviews.OrderByDescending(r => r.Date).ThenByDescending(r => r.Seq).ToImmutableArray();

    public static CharacterSummary Summarize(State state, Character character)
    {
        var reviews = ReviewsOf(state, character);
        var title = state.Titles.First(t => t.Slug == character.TitleSlug);
        return new CharacterSummary(character, title, reviews.Length, Average(reviews));
    }

    /// <summary>C-3: a title's characters, by name.</summary>
    public static ImmutableArray<CharacterSummary> CharactersOf(State state, Title title) =>
        state.Characters
            .Where(c => c.TitleSlug == title.Slug)
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .Select(c => Summarize(state, c))
            .ToImmutableArray();

    /// <summary>
    /// C-1, C-10: the characters with the most reviews. Ties go to the higher average, then the name.
    /// Characters with no reviews aren't listed.
    /// </summary>
    public static ImmutableArray<CharacterSummary> MostReviewed(State state) =>
        state.Characters
            .Select(c => Summarize(state, c))
            .Where(s => s.ReviewCount > 0)
            .OrderByDescending(s => s.ReviewCount)
            .ThenByDescending(s => s.Average.ValueOr(0m))
            .ThenBy(s => s.Character.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => s.Title.Name, StringComparer.OrdinalIgnoreCase)
            .Take(HomeListSize)
            .ToImmutableArray();

    /// <summary>C-1: the newest reviews, with the character each one is about.</summary>
    public static ImmutableArray<(Review Review, CharacterSummary About)> NewestReviews(State state) =>
        NewestFirst(state.Reviews)
            .Take(HomeListSize)
            .SelectMany(r => FindCharacter(state, r.TitleSlug, r.CharacterSlug)
                .Match(c => [(r, Summarize(state, c))], () => ImmutableArray<(Review, CharacterSummary)>.Empty))
            .ToImmutableArray();

    /// <summary>
    /// C-2: characters whose name contains the query, ignoring case and accents, by name, at most 50.
    /// The query is trimmed; an empty query finds nothing.
    /// </summary>
    public static ImmutableArray<CharacterSummary> Search(State state, string query)
    {
        var q = query.Trim();
        if (q.Length == 0) return [];
        return state.Characters
            .Where(c => Text.ContainsFolded(c.Name, q))
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.TitleSlug, StringComparer.Ordinal)
            .Take(MaxSearchResults)
            .Select(c => Summarize(state, c))
            .ToImmutableArray();
    }
}
