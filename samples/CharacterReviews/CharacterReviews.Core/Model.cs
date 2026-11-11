using System.Collections.Immutable;

namespace CharacterReviews.Core;

public enum TitleKind
{
    Show,
    Film,
}

/// <summary>A TV show or film. Year is the first year, for a show.</summary>
public sealed record Title(string Slug, string Name, TitleKind Kind, int Year);

/// <summary>Someone in a title. The slug is unique within the title.</summary>
public sealed record Character(string TitleSlug, string Slug, string Name, string Description);

/// <summary>
/// One person's rating of one character. Seq orders reviews written on the same date:
/// it is one more than the number of reviews that existed when this one was saved.
/// </summary>
public sealed record Review(
    int Seq,
    string TitleSlug,
    string CharacterSlug,
    string Reviewer,
    int Rating,
    string Text,
    DateOnly Date);

/// <summary>Everything the site knows. Each change makes a new State.</summary>
public sealed record State(
    ImmutableArray<Title> Titles,
    ImmutableArray<Character> Characters,
    ImmutableArray<Review> Reviews)
{
    public static readonly State Empty = new([], [], []);
}
