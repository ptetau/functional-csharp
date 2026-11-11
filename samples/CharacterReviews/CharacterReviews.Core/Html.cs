using System.Collections.Immutable;
using System.Globalization;
using Functional;

namespace CharacterReviews.Core;

/// <summary>
/// HTML for every page. Each function returns a string. Every user-supplied value goes through
/// <see cref="Text.Escape"/> (C-11) via <see cref="E"/>; anything not escaped is fixed markup.
/// </summary>
public static class Html
{
    private static string E(string s) => Text.Escape(s);

    private static string Join(IEnumerable<string> parts) => string.Concat(parts);

    public static string Layout(string pageTitle, string body) =>
        $"""
        <!doctype html>
        <html lang="en">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <title>{E(pageTitle)} · Character Reviews</title>
        <style>
        body {"{"} font-family: system-ui, sans-serif; max-width: 46rem; margin: 2rem auto; padding: 0 1rem; line-height: 1.5; {"}"}
        .error {"{"} color: #b00020; {"}"}
        .muted {"{"} color: #666; {"}"}
        label {"{"} display: block; margin-top: .75rem; font-weight: 600; {"}"}
        ul.reviews {"{"} list-style: none; padding: 0; {"}"}
        ul.reviews li {"{"} border-top: 1px solid #ddd; padding: .5rem 0; {"}"}
        </style>
        </head>
        <body>
        <header><a href="/">Character Reviews</a> · <a href="/titles/new">Add a title</a></header>
        <main>
        {body}
        </main>
        </body>
        </html>
        """;

    // ---- Small pieces ----

    public static string FormatAverage(decimal average) => average.ToString("0.0", CultureInfo.InvariantCulture);

    /// <summary>C-9: "4.3 (3 reviews)", or "No reviews yet".</summary>
    public static string RatingSummary(Option<decimal> average, int count) =>
        average.Match(
            a => $"{FormatAverage(a)} ({count} {(count == 1 ? "review" : "reviews")})",
            () => "No reviews yet");

    public static string TitleUrl(Title t) => $"/titles/{t.Slug}";

    public static string CharacterUrl(Character c) => $"/characters/{c.TitleSlug}/{c.Slug}";

    private static string KindLabel(TitleKind kind) => kind switch
    {
        TitleKind.Show => "TV show",
        TitleKind.Film => "Film",
    };

    private static string TitleLabel(Title t) => $"{E(t.Name)} ({t.Year})";

    private static string CharacterItem(CharacterSummary s) =>
        $"""<li><a href="{E(CharacterUrl(s.Character))}">{E(s.Character.Name)}</a> <span class="muted">in {TitleLabel(s.Title)}</span> · {RatingSummary(s.Average, s.ReviewCount)}</li>""" + "\n";

    private static string CharacterList(ImmutableArray<CharacterSummary> items, string whenEmpty) =>
        items.IsEmpty ? $"<p class=\"muted\">{whenEmpty}</p>\n" : $"<ul>\n{Join(items.Select(CharacterItem))}</ul>\n";

    private static string ErrorFor(ImmutableArray<FieldError> errors, string field) =>
        Join(errors.Where(e => e.Field == field)
            .Select(e => $"""<span class="error" id="{field}-error">{E(e.Message)}</span>"""));

    private static string SearchBox(string query) =>
        $"""
        <form action="/search" method="get" role="search">
        <label for="q">Find a character</label>
        <input id="q" name="q" type="search" value="{E(query)}">
        <button type="submit">Search</button>
        </form>
        """;

    private static string Stars(int rating) => new string('★', rating) + new string('☆', 5 - rating);

    // ---- Pages ----

    /// <summary>C-1: search box, most reviewed characters, newest reviews.</summary>
    public static string Home(
        ImmutableArray<CharacterSummary> mostReviewed,
        ImmutableArray<(Review Review, CharacterSummary About)> newest) =>
        Layout("Home",
            $"""
            <h1>Character Reviews</h1>
            {SearchBox("")}
            <h2>Most reviewed</h2>
            {CharacterList(mostReviewed, "Nobody has written a review yet.")}
            <h2>Newest reviews</h2>
            {(newest.IsEmpty
                ? "<p class=\"muted\">No reviews yet.</p>"
                : $"<ul class=\"reviews\">\n{Join(newest.Select(n => NewestItem(n.Review, n.About)))}</ul>")}
            """);

    private static string NewestItem(Review r, CharacterSummary about) =>
        $"""<li><a href="{E(CharacterUrl(about.Character))}">{E(about.Character.Name)}</a> <span class="muted">in {TitleLabel(about.Title)}</span>: <span aria-label="{r.Rating} out of 5">{Stars(r.Rating)}</span> by {E(r.Reviewer)} on {r.Date:yyyy-MM-dd}</li>""" + "\n";

    /// <summary>C-2: search results, or a prompt when the query is empty.</summary>
    public static string SearchResults(string query, ImmutableArray<CharacterSummary> found) =>
        Layout(query.Trim().Length == 0 ? "Search" : $"Search: {query.Trim()}",
            $"""
            <h1>Search</h1>
            {SearchBox(query)}
            {(query.Trim().Length == 0
                ? "<p>Type part of a character's name to search.</p>"
                : $"<p>{found.Length} {(found.Length == 1 ? "character" : "characters")} matching “{E(query.Trim())}”.</p>\n{CharacterList(found, "No characters match.")}")}
            """);

    /// <summary>C-3: a title with its characters.</summary>
    public static string TitlePage(Title title, ImmutableArray<CharacterSummary> characters) =>
        Layout($"{title.Name} ({title.Year})",
            $"""
            <h1>{TitleLabel(title)}</h1>
            <p class="muted">{KindLabel(title.Kind)}</p>
            <h2>Characters</h2>
            {CharacterList(characters, "No characters yet.")}
            <p><a href="{E(TitleUrl(title))}/characters/new">Add a character</a></p>
            """);

    /// <summary>C-4: a character, its rating breakdown, its reviews and a form to add one.</summary>
    public static string CharacterPage(
        Title title,
        Character character,
        ImmutableArray<Review> reviews,
        ReviewForm form,
        ImmutableArray<FieldError> errors) =>
        Layout($"{character.Name} in {title.Name}",
            $"""
            <h1>{E(character.Name)}</h1>
            <p class="muted">in <a href="{E(TitleUrl(title))}">{TitleLabel(title)}</a></p>
            {(character.Description.Length == 0 ? "" : $"<p>{E(character.Description)}</p>")}
            <h2>Rating</h2>
            <p class="average">{Queries.Average(reviews).Match(a => $"<strong>{FormatAverage(a)}</strong> out of 5 from {reviews.Length} {(reviews.Length == 1 ? "review" : "reviews")}", () => "No reviews yet")}</p>
            <ul class="breakdown">
            {Join(Queries.Breakdown(reviews).Select(s => $"<li>{s.Stars}★ {s.Count}</li>\n"))}</ul>
            <h2>Reviews</h2>
            {(reviews.IsEmpty
                ? "<p class=\"muted\">No reviews yet.</p>"
                : $"<ul class=\"reviews\">\n{Join(Queries.NewestFirst(reviews).Select(ReviewItem))}</ul>")}
            {ReviewFormHtml(character, form, errors)}
            """);

    private static string ReviewItem(Review r) =>
        $"""
        <li>
        <span aria-label="{r.Rating} out of 5">{Stars(r.Rating)}</span> by <strong>{E(r.Reviewer)}</strong> on {r.Date:yyyy-MM-dd}
        {(r.Text.Length == 0 ? "" : $"<p>{E(r.Text)}</p>")}
        </li>

        """;

    private static string ReviewFormHtml(Character character, ReviewForm form, ImmutableArray<FieldError> errors) =>
        $"""
        <h2>Add your review</h2>
        <form action="{E(CharacterUrl(character))}/reviews" method="post">
        <label for="reviewer">Your name</label>
        <input id="reviewer" name="reviewer" maxlength="{Forms.MaxReviewerLength}" value="{E(form.Reviewer)}">
        {ErrorFor(errors, "reviewer")}
        <label for="rating">Rating (1 to 5 stars)</label>
        <select id="rating" name="rating">
        {RatingOptions(form.Rating)}</select>
        {ErrorFor(errors, "rating")}
        <label for="text">Review (optional)</label>
        <textarea id="text" name="text" rows="6" cols="60">{E(form.Text)}</textarea>
        {ErrorFor(errors, "text")}
        <p><button type="submit">Add review</button></p>
        </form>
        """;

    /// <summary>
    /// The rating choices. A value the user sent that isn't 1 to 5 is kept as an extra option,
    /// so the form really does show their input again.
    /// </summary>
    private static string RatingOptions(string chosen)
    {
        var valid = Enumerable.Range(1, 5).Reverse().Select(n => n.ToString(CultureInfo.InvariantCulture)).ToImmutableArray();
        var extra = chosen.Length == 0 || valid.Contains(chosen) ? [] : ImmutableArray.Create(chosen);
        var placeholder = $"<option value=\"\"{(chosen.Length == 0 ? " selected" : "")}>Choose…</option>\n";
        return placeholder + Join(extra.Concat(valid).Select(v =>
            $"<option value=\"{E(v)}\"{(v == chosen ? " selected" : "")}>{E(v)}</option>\n"));
    }

    /// <summary>C-6: the add-title form.</summary>
    public static string NewTitlePage(TitleForm form, ImmutableArray<FieldError> errors) =>
        Layout("Add a title",
            $"""
            <h1>Add a title</h1>
            <form action="/titles" method="post">
            <label for="name">Name</label>
            <input id="name" name="name" value="{E(form.Name)}">
            {ErrorFor(errors, "name")}
            <label for="kind">Kind</label>
            <select id="kind" name="kind">
            <option value=""{(form.Kind.Length == 0 ? " selected" : "")}>Choose…</option>
            <option value="show"{(form.Kind.Equals("show", StringComparison.OrdinalIgnoreCase) ? " selected" : "")}>TV show</option>
            <option value="film"{(form.Kind.Equals("film", StringComparison.OrdinalIgnoreCase) ? " selected" : "")}>Film</option>
            </select>
            {ErrorFor(errors, "kind")}
            <label for="year">Year (the first year, for a show)</label>
            <input id="year" name="year" inputmode="numeric" value="{E(form.Year)}">
            {ErrorFor(errors, "year")}
            <p><button type="submit">Add title</button></p>
            </form>
            """);

    /// <summary>C-7: the add-character form.</summary>
    public static string NewCharacterPage(Title title, CharacterForm form, ImmutableArray<FieldError> errors) =>
        Layout($"Add a character to {title.Name}",
            $"""
            <h1>Add a character to <a href="{E(TitleUrl(title))}">{TitleLabel(title)}</a></h1>
            <form action="{E(TitleUrl(title))}/characters" method="post">
            <label for="name">Name</label>
            <input id="name" name="name" value="{E(form.Name)}">
            {ErrorFor(errors, "name")}
            <label for="description">Short description (optional, at most {Forms.MaxDescriptionLength} characters)</label>
            <textarea id="description" name="description" rows="3" cols="60">{E(form.Description)}</textarea>
            {ErrorFor(errors, "description")}
            <p><button type="submit">Add character</button></p>
            </form>
            """);

    /// <summary>C-8: page not found.</summary>
    public static string NotFound(string what) =>
        Layout("Not found",
            $"""
            <h1>Not found</h1>
            <p>{E(what)}</p>
            <p><a href="/">Go to the home page</a></p>
            """);
}
