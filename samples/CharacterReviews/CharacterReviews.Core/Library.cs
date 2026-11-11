using System.Collections.Immutable;
using Functional;

namespace CharacterReviews.Core;

/// <summary>A change that worked: the new state and the thing that was added.</summary>
public sealed record Added<T>(State State, T Item);

/// <summary>
/// The state transitions. Each takes the old state and returns the new one, or what was wrong.
/// The outer Result fails only when the title or character doesn't exist (C-8);
/// the inner Validation holds the form errors (C-5, C-6, C-7).
/// </summary>
public static class Library
{
    /// <summary>Slugs that would clash with a fixed route such as /titles/new.</summary>
    private static readonly ImmutableHashSet<string> ReservedTitleSlugs = ["new"];

    public static Error NotFound(string what) => new("not_found", $"{what} not found");

    /// <summary>C-6: add a title. A second title with the same name and year is refused.</summary>
    public static Validation<FieldError, Added<Title>> AddTitle(State state, TitleForm form) =>
        Forms.Validate(form).Bind(t =>
        {
            var duplicate = state.Titles.Any(x => x.Year == t.Year && SameName(x.Name, t.Name));
            if (duplicate)
                return Fields.Invalid<Added<Title>>("name", $"{t.Name} ({t.Year}) has already been added");

            var taken = state.Titles.Select(x => x.Slug).ToImmutableHashSet().Union(ReservedTitleSlugs);
            var slug = Text.UniqueSlug(SlugOr(t.Name, "title"), taken);
            var title = new Title(slug, t.Name, t.Kind, t.Year);
            return Validation.Valid<FieldError, Added<Title>>(new Added<Title>(state with { Titles = state.Titles.Add(title) }, title));
        });

    /// <summary>C-7: add a character to a title. A second character with the same name in the same title is refused.</summary>
    public static Result<Validation<FieldError, Added<Character>>> AddCharacter(State state, string titleSlug, CharacterForm form) =>
        Queries.FindTitle(state, titleSlug)
            .OrError(NotFound("Title"))
            .Map(title => Forms.Validate(form).Bind(c =>
            {
                var siblings = state.Characters.Where(x => x.TitleSlug == title.Slug).ToImmutableArray();
                if (siblings.Any(x => SameName(x.Name, c.Name)))
                    return Fields.Invalid<Added<Character>>("name", $"{title.Name} already has a character called {c.Name}");

                var taken = siblings.Select(x => x.Slug).ToImmutableHashSet();
                var slug = Text.UniqueSlug(SlugOr(c.Name, "character"), taken);
                var character = new Character(title.Slug, slug, c.Name, c.Description);
                return Validation.Valid<FieldError, Added<Character>>(new Added<Character>(state with { Characters = state.Characters.Add(character) }, character));
            }));

    /// <summary>C-5, C-13: add a review, dated today.</summary>
    public static Result<Validation<FieldError, Added<Review>>> AddReview(
        State state, string titleSlug, string characterSlug, ReviewForm form, DateOnly today) =>
        Queries.FindCharacter(state, titleSlug, characterSlug)
            .OrError(NotFound("Character"))
            .Map(character => Forms.Validate(form).Map(r =>
            {
                var review = new Review(
                    Seq: state.Reviews.Length + 1,
                    character.TitleSlug,
                    character.Slug,
                    r.Reviewer,
                    r.Rating,
                    r.Text,
                    today);
                return new Added<Review>(state with { Reviews = state.Reviews.Add(review) }, review);
            }));

    /// <summary>Names match ignoring case and accents, so "Zoë" and "zoe" are the same character.</summary>
    private static bool SameName(string a, string b) => Text.Fold(a) == Text.Fold(b);

    /// <summary>A name with no letters or digits (say "!!!") still needs a slug.</summary>
    private static string SlugOr(string name, string fallback) =>
        Text.Slugify(name) is { Length: > 0 } slug ? slug : fallback;
}
