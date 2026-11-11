using System.Globalization;

namespace CharacterReviews.Core;

/// <summary>What the user typed into the add-title form, untrimmed and unchecked.</summary>
public sealed record TitleForm(string Name, string Kind, string Year)
{
    public static readonly TitleForm Blank = new("", "", "");
}

/// <summary>What the user typed into the add-character form.</summary>
public sealed record CharacterForm(string Name, string Description)
{
    public static readonly CharacterForm Blank = new("", "");
}

/// <summary>What the user typed into the add-review form.</summary>
public sealed record ReviewForm(string Reviewer, string Rating, string Text)
{
    public static readonly ReviewForm Blank = new("", "", "");
}

/// <summary>Checked form input, ready to become part of the state.</summary>
public sealed record NewTitle(string Name, TitleKind Kind, int Year);

public sealed record NewCharacter(string Name, string Description);

public sealed record NewReview(string Reviewer, int Rating, string Text);

/// <summary>Field rules for each form (C-5, C-6, C-7, C-12). Inputs are trimmed before they're checked.</summary>
public static class Forms
{
    public const int MaxReviewerLength = 40;
    public const int MaxReviewTextLength = 2000;
    public const int MaxDescriptionLength = 280;
    public const int MaxNameLength = 200;
    public const int MinYear = 1870;
    public const int MaxYear = 2200;

    /// <summary>C-12: every input is trimmed before it's validated. Missing fields count as empty.</summary>
    public static TitleForm Trimmed(TitleForm f) => new(Trim(f.Name), Trim(f.Kind), Trim(f.Year));

    public static CharacterForm Trimmed(CharacterForm f) => new(Trim(f.Name), Trim(f.Description));

    public static ReviewForm Trimmed(ReviewForm f) => new(Trim(f.Reviewer), Trim(f.Rating), Trim(f.Text));

    private static string Trim(string? s) => (s ?? "").Trim();

    public static Validated<NewTitle> Validate(TitleForm raw)
    {
        var f = Trimmed(raw);
        return Validated.Combine(
            RequiredName(f.Name, "name"),
            ParseKind(f.Kind),
            ParseYear(f.Year),
            (name, kind, year) => new NewTitle(name, kind, year));
    }

    public static Validated<NewCharacter> Validate(CharacterForm raw)
    {
        var f = Trimmed(raw);
        return Validated.Combine(
            RequiredName(f.Name, "name"),
            Validated.Check(
                f.Description,
                f.Description.Length <= MaxDescriptionLength,
                "description",
                $"Description must be at most {MaxDescriptionLength} characters"),
            (name, description) => new NewCharacter(name, description));
    }

    public static Validated<NewReview> Validate(ReviewForm raw)
    {
        var f = Trimmed(raw);
        return Validated.Combine(
            f.Reviewer.Length == 0
                ? Validated.Invalid<string>("reviewer", "Name is required")
                : Validated.Check(
                    f.Reviewer,
                    f.Reviewer.Length <= MaxReviewerLength,
                    "reviewer",
                    $"Name must be at most {MaxReviewerLength} characters"),
            ParseRating(f.Rating),
            Validated.Check(
                f.Text,
                f.Text.Length <= MaxReviewTextLength,
                "text",
                $"Review must be at most {MaxReviewTextLength} characters"),
            (reviewer, rating, text) => new NewReview(reviewer, rating, text));
    }

    private static Validated<string> RequiredName(string name, string field) =>
        name.Length == 0
            ? Validated.Invalid<string>(field, "Name is required")
            : Validated.Check(name, name.Length <= MaxNameLength, field, $"Name must be at most {MaxNameLength} characters");

    private static Validated<TitleKind> ParseKind(string kind) =>
        kind.ToLowerInvariant() switch
        {
            "show" => Validated.Valid(TitleKind.Show),
            "film" => Validated.Valid(TitleKind.Film),
            "" => Validated.Invalid<TitleKind>("kind", "Kind is required"),
            _ => Validated.Invalid<TitleKind>("kind", "Kind must be show or film"),
        };

    private static Validated<int> ParseYear(string year) =>
        year.Length == 0
            ? Validated.Invalid<int>("year", "Year is required")
            : int.TryParse(year, NumberStyles.None, CultureInfo.InvariantCulture, out var y)
                ? Validated.Check(y, y is >= MinYear and <= MaxYear, "year", $"Year must be between {MinYear} and {MaxYear}")
                : Validated.Invalid<int>("year", "Year must be a whole number");

    private static Validated<int> ParseRating(string rating) =>
        rating.Length == 0
            ? Validated.Invalid<int>("rating", "Rating is required")
            : int.TryParse(rating, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var r)
                ? Validated.Check(r, r is >= 1 and <= 5, "rating", "Rating must be between 1 and 5")
                : Validated.Invalid<int>("rating", "Rating must be a whole number between 1 and 5");
}
