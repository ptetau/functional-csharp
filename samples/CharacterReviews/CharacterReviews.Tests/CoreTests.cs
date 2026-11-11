using System.Collections.Immutable;
using CharacterReviews.Core;
using Functional;

namespace CharacterReviews.Tests;

/// <summary>Unit tests on the functional core. Each spec rule or example is a test named after its requirement ID.</summary>
public class CoreTests
{
    private static readonly DateOnly Day1 = new(2026, 11, 1);
    private static readonly DateOnly Day2 = new(2026, 11, 2);

    // ---- Helpers: run a transition and insist it worked ----

    private static T Valid<T>(Validated<T> v) =>
        v.Match(x => x, errors => throw new Xunit.Sdk.XunitException("Expected valid, got: " + string.Join("; ", errors)));

    private static T Ok<T>(Result<T> r) =>
        r.Match(x => x, e => throw new Xunit.Sdk.XunitException("Expected ok, got: " + e));

    private static ImmutableArray<FieldError> Errors<T>(Validated<T> v) =>
        v.Match(_ => throw new Xunit.Sdk.XunitException("Expected errors"), e => e);

    private static State WithTitle(State s, string name, string kind, string year) =>
        Valid(Library.AddTitle(s, new TitleForm(name, kind, year))).State;

    private static State WithCharacter(State s, string titleSlug, string name, string description = "") =>
        Valid(Ok(Library.AddCharacter(s, titleSlug, new CharacterForm(name, description)))).State;

    private static State WithReview(State s, string titleSlug, string characterSlug, int rating, DateOnly? day = null, string reviewer = "Sam", string text = "") =>
        Valid(Ok(Library.AddReview(s, titleSlug, characterSlug, new ReviewForm(reviewer, rating.ToString(), text), day ?? Day1))).State;

    /// <summary>The spec's example: Arrival (2016), Louise Banks, reviews 5, 4 and 4.</summary>
    private static State Arrival()
    {
        var s1 = WithTitle(State.Empty, "Arrival", "film", "2016");
        var s2 = WithCharacter(s1, "arrival", "Louise Banks");
        var s3 = WithReview(s2, "arrival", "louise-banks", 5);
        var s4 = WithReview(s3, "arrival", "louise-banks", 4);
        return WithReview(s4, "arrival", "louise-banks", 4);
    }

    private static Character Louise(State s) =>
        Queries.FindCharacter(s, "arrival", "louise-banks").Match(c => c, () => throw new Xunit.Sdk.XunitException("missing"));

    // ---- Slugs ----

    [Theory]
    [InlineData("Kendall Roy", "kendall-roy")]
    [InlineData("  Louise   Banks  ", "louise-banks")]
    [InlineData("Dr. Strangelove, or: How I Learned", "dr-strangelove-or-how-i-learned")]
    [InlineData("--Zoë--", "zoe")]
    [InlineData("R2-D2", "r2-d2")]
    [InlineData("!!!", "")]
    public void Slug_is_lower_case_letters_and_digits_with_single_dashes(string name, string slug) =>
        Assert.Equal(slug, Text.Slugify(name));

    [Fact]
    public void Slug_that_is_taken_gets_a_number() =>
        Assert.Equal("louise-banks-3", Text.UniqueSlug("louise-banks", ImmutableHashSet.Create("louise-banks", "louise-banks-2")));

    [Fact]
    public void Slug_example_second_Louise_Banks_in_another_title_keeps_the_plain_slug()
    {
        var s1 = Arrival();
        var s2 = WithTitle(s1, "Arrival Again", "film", "2030");
        var added = Valid(Ok(Library.AddCharacter(s2, "arrival-again", new CharacterForm("Louise Banks", ""))));
        Assert.Equal("louise-banks", added.Item.Slug);
        Assert.Equal("louise-banks", Louise(added.State).Slug);
    }

    [Fact]
    public void Slug_clash_within_a_title_from_different_names_gets_a_number()
    {
        var s = WithCharacter(Arrival(), "arrival", "Louise-Banks?");
        Assert.Equal(["louise-banks", "louise-banks-2"], s.Characters.Select(c => c.Slug));
    }

    [Fact]
    public void Slug_for_a_title_called_New_does_not_hide_the_add_title_form()
    {
        var added = Valid(Library.AddTitle(State.Empty, new TitleForm("New", "show", "2020")));
        Assert.Equal("new-2", added.Item.Slug);
    }

    [Fact]
    public void Slug_for_a_remake_with_the_same_name_gets_a_number()
    {
        var s = WithTitle(WithTitle(State.Empty, "Dune", "film", "1984"), "Dune", "film", "2021");
        Assert.Equal(["dune", "dune-2"], s.Titles.Select(t => t.Slug));
    }

    // ---- C-4, C-9: ratings ----

    [Fact]
    public void C4_example_average_is_4_3_with_breakdown()
    {
        var s = Arrival();
        var reviews = Queries.ReviewsOf(s, Louise(s));
        Assert.Equal(Option.Some(4.3m), Queries.Average(reviews));
        // ImmutableArray<T> compares by reference, so name the element type to get xUnit's sequence comparison.
        Assert.Equal<StarCount>(
            [new StarCount(5, 1), new StarCount(4, 2), new StarCount(3, 0), new StarCount(2, 0), new StarCount(1, 0)],
            Queries.Breakdown(reviews));
    }

    [Fact]
    public void C4_example_page_shows_average_and_breakdown()
    {
        var page = Html(Site.Character(Arrival(), "arrival", "louise-banks"), 200);
        Assert.Contains("4.3", page);
        Assert.Contains("5★ 1", page);
        Assert.Contains("4★ 2", page);
        Assert.Contains("3★ 0", page);
        Assert.Contains("2★ 0", page);
        Assert.Contains("1★ 0", page);
    }

    [Fact]
    public void C4_reviews_are_newest_first()
    {
        var s1 = WithCharacter(WithTitle(State.Empty, "Arrival", "film", "2016"), "arrival", "Louise Banks");
        var s2 = WithReview(s1, "arrival", "louise-banks", 3, Day2, reviewer: "Later day");
        var s3 = WithReview(s2, "arrival", "louise-banks", 3, Day1, reviewer: "Earlier day");
        var s4 = WithReview(s3, "arrival", "louise-banks", 3, Day2, reviewer: "Later day, saved last");
        Assert.Equal(
            ["Later day, saved last", "Later day", "Earlier day"],
            Queries.NewestFirst(s4.Reviews).Select(r => r.Reviewer));
    }

    [Theory]
    [InlineData(new[] { 1, 2 }, 1.5)]
    [InlineData(new[] { 4, 4, 5, 5, 5, 5 }, 4.7)]   // 4.666... rounds up
    [InlineData(new[] { 1, 1, 1, 1, 1, 1, 1, 1, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2 }, 1.6)]   // 1.6 exactly
    [InlineData(new[] { 1, 1, 1, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2 }, 1.9)]   // 1.85 rounds away from zero
    [InlineData(new[] { 3 }, 3.0)]
    public void C9_average_rounds_half_away_from_zero_to_one_decimal(int[] ratings, double expected)
    {
        var reviews = ratings.Select((r, i) => new Review(i + 1, "t", "c", "x", r, "", Day1)).ToImmutableArray();
        // Attributes can't hold decimals, and since .NET 11 (decimal)1.9 keeps the double's exact binary value
        // (1.8999999999999999...). Round to the one decimal place being tested to get the intended value.
        Assert.Equal(Option.Some(Math.Round((decimal)expected, 1)), Queries.Average(reviews));
    }

    [Fact]
    public void C9_no_reviews_shows_No_reviews_yet()
    {
        var s = WithCharacter(WithTitle(State.Empty, "Arrival", "film", "2016"), "arrival", "Ian Donnelly");
        Assert.Equal(Option.None<decimal>(), Queries.Average([]));
        Assert.Contains("No reviews yet", Html(Site.Character(s, "arrival", "ian-donnelly"), 200));
        Assert.Contains("No reviews yet", Html(Site.Title(s, "arrival"), 200));
    }

    [Fact]
    public void C9_average_shows_one_decimal_even_when_whole()
    {
        var s = WithReview(WithCharacter(WithTitle(State.Empty, "Arrival", "film", "2016"), "arrival", "Ian Donnelly"), "arrival", "ian-donnelly", 4);
        Assert.Contains("<strong>4.0</strong>", Html(Site.Character(s, "arrival", "ian-donnelly"), 200));
    }

    // ---- C-5: reviews ----

    [Fact]
    public void C5_example_rating_6_and_empty_name_give_both_errors_and_keep_the_text()
    {
        var result = Ok(Library.AddReview(Arrival(), "arrival", "louise-banks", new ReviewForm("", "6", "Kept text"), Day1));
        var errors = Errors(result);
        Assert.Contains(new FieldError("rating", "Rating must be between 1 and 5"), errors);
        Assert.Contains(new FieldError("reviewer", "Name is required"), errors);

        var outcome = Site.PostReview(Arrival(), "arrival", "louise-banks", new ReviewForm("", "6", "Kept text"), Day1);
        Assert.False(outcome.Saved.IsSome);
        var page = Html(outcome.Response, 400);
        Assert.Contains("Rating must be between 1 and 5", page);
        Assert.Contains("Name is required", page);
        Assert.Contains(">Kept text</textarea>", page);
    }

    [Theory]
    [InlineData("", "4", "", "reviewer", "Name is required")]
    [InlineData("   ", "4", "", "reviewer", "Name is required")]
    [InlineData("Sam", "", "", "rating", "Rating is required")]
    [InlineData("Sam", "0", "", "rating", "Rating must be between 1 and 5")]
    [InlineData("Sam", "4.5", "", "rating", "Rating must be a whole number between 1 and 5")]
    [InlineData("Sam", "four", "", "rating", "Rating must be a whole number between 1 and 5")]
    public void C5_invalid_review_fields(string reviewer, string rating, string text, string field, string message)
    {
        var errors = Errors(Ok(Library.AddReview(Arrival(), "arrival", "louise-banks", new ReviewForm(reviewer, rating, text), Day1)));
        Assert.Equal<FieldError>([new FieldError(field, message)], errors);
    }

    [Fact]
    public void C5_reviewer_name_is_1_to_40_characters()
    {
        var ok = Library.AddReview(Arrival(), "arrival", "louise-banks", new ReviewForm(new string('a', 40), "3", ""), Day1);
        var tooLong = Library.AddReview(Arrival(), "arrival", "louise-banks", new ReviewForm(new string('a', 41), "3", ""), Day1);
        Assert.True(Ok(ok).Match(_ => true, _ => false));
        Assert.Equal<FieldError>([new FieldError("reviewer", "Name must be at most 40 characters")], Errors(Ok(tooLong)));
    }

    [Fact]
    public void C5_review_text_is_at_most_2000_characters()
    {
        var ok = Library.AddReview(Arrival(), "arrival", "louise-banks", new ReviewForm("Sam", "3", new string('x', 2000)), Day1);
        var tooLong = Library.AddReview(Arrival(), "arrival", "louise-banks", new ReviewForm("Sam", "3", new string('x', 2001)), Day1);
        Assert.True(Ok(ok).Match(_ => true, _ => false));
        Assert.Equal("text", Assert.Single(Errors(Ok(tooLong))).Field);
    }

    [Fact]
    public void C5_valid_review_redirects_to_the_character_page()
    {
        var outcome = Site.PostReview(Arrival(), "arrival", "louise-banks", new ReviewForm("Ana", "2", "Meh"), Day2);
        Assert.Equal(new Response.SeeOther("/characters/arrival/louise-banks"), outcome.Response);
        Assert.True(outcome.Saved.IsSome);
    }

    [Fact]
    public void C5_review_for_unknown_character_is_404()
    {
        var outcome = Site.PostReview(Arrival(), "arrival", "nobody", new ReviewForm("Ana", "2", ""), Day2);
        Assert.False(outcome.Saved.IsSome);
        Html(outcome.Response, 404);
    }

    // ---- C-6, C-7: titles and characters ----

    [Fact]
    public void C6_duplicate_name_and_year_is_an_error()
    {
        var errors = Errors(Library.AddTitle(Arrival(), new TitleForm(" arrival ", "film", "2016")));
        Assert.Equal("name", Assert.Single(errors).Field);
    }

    [Fact]
    public void C6_same_name_different_year_is_fine() =>
        Valid(Library.AddTitle(Arrival(), new TitleForm("Arrival", "film", "1996")));

    [Theory]
    [InlineData("", "film", "2016", "name")]
    [InlineData("X", "", "2016", "kind")]
    [InlineData("X", "musical", "2016", "kind")]
    [InlineData("X", "film", "", "year")]
    [InlineData("X", "film", "twenty", "year")]
    [InlineData("X", "film", "1500", "year")]
    public void C6_invalid_title_fields(string name, string kind, string year, string field) =>
        Assert.Equal(field, Assert.Single(Errors(Library.AddTitle(State.Empty, new TitleForm(name, kind, year)))).Field);

    [Fact]
    public void C6_all_title_errors_are_reported_together() =>
        Assert.Equal(["name", "kind", "year"], Errors(Library.AddTitle(State.Empty, new TitleForm("", "", ""))).Select(e => e.Field));

    [Fact]
    public void C7_example_second_Louise_Banks_in_Arrival_is_refused()
    {
        var errors = Errors(Ok(Library.AddCharacter(Arrival(), "arrival", new CharacterForm("Louise Banks", ""))));
        Assert.Equal("name", Assert.Single(errors).Field);
    }

    [Fact]
    public void C7_duplicate_name_ignores_case_and_accents()
    {
        var s = WithCharacter(WithTitle(State.Empty, "Firefly", "show", "2002"), "firefly", "Zoë Washburne");
        Assert.Single(Errors(Ok(Library.AddCharacter(s, "firefly", new CharacterForm("ZOE WASHBURNE", "")))));
    }

    [Fact]
    public void C7_description_is_at_most_280_characters()
    {
        var s = WithTitle(State.Empty, "Arrival", "film", "2016");
        Valid(Ok(Library.AddCharacter(s, "arrival", new CharacterForm("A", new string('d', 280)))));
        Assert.Equal("description", Assert.Single(Errors(Ok(Library.AddCharacter(s, "arrival", new CharacterForm("A", new string('d', 281)))))).Field);
    }

    [Fact]
    public void C7_character_for_unknown_title_is_not_found() =>
        Assert.False(Library.AddCharacter(State.Empty, "nope", new CharacterForm("A", "")).IsOk);

    // ---- C-1, C-10: home page ----

    [Fact]
    public void C10_most_reviewed_ties_break_on_higher_average_then_name()
    {
        var s0 = WithTitle(State.Empty, "Show", "show", "2000");
        var s1 = new[] { "Cleo", "Bea", "Ava", "Dan" }.Aggregate(s0, (s, n) => WithCharacter(s, "show", n));
        // Dan: 3 reviews. Ava and Bea: 2 reviews averaging 4. Cleo: 2 reviews averaging 5.
        var s2 = new[] { ("dan", 1), ("dan", 1), ("dan", 1), ("ava", 4), ("ava", 4), ("bea", 4), ("bea", 4), ("cleo", 5), ("cleo", 5) }
            .Aggregate(s1, (s, r) => WithReview(s, "show", r.Item1, r.Item2));
        Assert.Equal(["Dan", "Cleo", "Ava", "Bea"], Queries.MostReviewed(s2).Select(x => x.Character.Name));
    }

    [Fact]
    public void C1_home_lists_at_most_10_most_reviewed_and_10_newest()
    {
        var s0 = WithTitle(State.Empty, "Show", "show", "2000");
        var names = Enumerable.Range(1, 12).Select(i => $"Person {i:00}").ToArray();
        var s1 = names.Aggregate(s0, (s, n) => WithCharacter(s, "show", n));
        var s2 = names.Aggregate(s1, (s, n) => WithReview(s, "show", Text.Slugify(n), 3));
        Assert.Equal(10, Queries.MostReviewed(s2).Length);
        var newest = Queries.NewestReviews(s2);
        Assert.Equal(10, newest.Length);
        Assert.Equal("Person 12", newest[0].About.Character.Name);
        var page = Html(Site.Home(s2), 200);
        Assert.Contains("name=\"q\"", page);
    }

    // ---- C-2: search ----

    [Fact]
    public void C2_example_LOUISE_finds_Louise_Banks() =>
        Assert.Equal(["Louise Banks"], Queries.Search(Arrival(), "LOUISE").Select(s => s.Character.Name));

    [Fact]
    public void C2_zoe_finds_Zoe_with_diaeresis_and_the_other_way_round()
    {
        var s = WithCharacter(WithTitle(State.Empty, "Firefly", "show", "2002"), "firefly", "Zoë Washburne");
        Assert.Single(Queries.Search(s, "zoe"));
        Assert.Single(Queries.Search(s, "ZOË"));
        Assert.Single(Queries.Search(s, "washb"));
    }

    [Fact]
    public void C2_results_are_by_name_and_at_most_50()
    {
        var s0 = WithTitle(State.Empty, "Big", "show", "2000");
        var s1 = Enumerable.Range(1, 60).Reverse().Aggregate(s0, (s, i) => WithCharacter(s, "big", $"Extra {i:00}"));
        var found = Queries.Search(s1, "extra");
        Assert.Equal(50, found.Length);
        Assert.Equal("Extra 01", found[0].Character.Name);
        Assert.Equal("Extra 50", found[49].Character.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void C2_empty_query_asks_for_one(string? q)
    {
        Assert.Empty(Queries.Search(Arrival(), q ?? ""));
        Assert.Contains("Type part of a character's name to search.", Html(Site.Search(Arrival(), q), 200));
    }

    // ---- C-3, C-8 ----

    [Fact]
    public void C3_title_page_lists_characters_with_average_and_count()
    {
        var page = Html(Site.Title(Arrival(), "arrival"), 200);
        Assert.Contains("Louise Banks", page);
        Assert.Contains("4.3 (3 reviews)", page);
    }

    [Fact]
    public void C8_unknown_title_or_character_is_404_with_a_link_home()
    {
        Assert.Contains("href=\"/\"", Html(Site.Title(Arrival(), "nope"), 404));
        Assert.Contains("href=\"/\"", Html(Site.Character(Arrival(), "arrival", "nope"), 404));
        Assert.Contains("href=\"/\"", Html(Site.Character(Arrival(), "nope", "louise-banks"), 404));
        Html(Site.NewCharacter(Arrival(), "nope"), 404);
    }

    // ---- C-11, C-12, C-13 ----

    [Fact]
    public void C11_example_script_in_review_text_is_shown_as_literal_text()
    {
        var s = WithReview(Arrival(), "arrival", "louise-banks", 3, text: "<script>alert(1)</script>");
        var page = Html(Site.Character(s, "arrival", "louise-banks"), 200);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", page);
        Assert.DoesNotContain("<script>", page);
    }

    [Fact]
    public void C11_every_user_value_is_escaped_on_every_page()
    {
        const string evil = "<b>\"x\"&'y'";
        var s1 = WithTitle(State.Empty, evil, "film", "2000");
        var titleSlug = s1.Titles[0].Slug;
        var s2 = WithCharacter(s1, titleSlug, evil, evil);
        var charSlug = s2.Characters[0].Slug;
        var s3 = WithReview(s2, titleSlug, charSlug, 4, reviewer: evil, text: evil);
        var pages = new[]
        {
            Site.Home(s3), Site.Search(s3, evil), Site.Search(s3, "b"), Site.Title(s3, titleSlug),
            Site.Character(s3, titleSlug, charSlug), Site.NewCharacter(s3, titleSlug),
            Site.PostTitle(s3, new TitleForm(evil, evil, evil)).Response,
            Site.PostReview(s3, titleSlug, charSlug, new ReviewForm(evil, evil, evil), Day1).Response,
        };
        Assert.All(pages, p => Assert.DoesNotContain("<b>", p.Match(page => page.Html, _ => "")));
        Assert.All(pages, p => Assert.DoesNotContain("\"x\"", p.Match(page => page.Html, _ => "")));
    }

    [Fact]
    public void C12_inputs_are_trimmed_before_validation_and_storage()
    {
        var added = Valid(Ok(Library.AddReview(Arrival(), "arrival", "louise-banks", new ReviewForm("  Ana  ", " 4 ", "  Good.  "), Day1)));
        Assert.Equal(("Ana", 4, "Good."), (added.Item.Reviewer, added.Item.Rating, added.Item.Text));
        var title = Valid(Library.AddTitle(State.Empty, new TitleForm("  Arrival ", " Film ", " 2016 "))).Item;
        Assert.Equal(new Title("arrival", "Arrival", TitleKind.Film, 2016), title);
    }

    [Fact]
    public void C13_review_date_is_the_date_passed_in()
    {
        var added = Valid(Ok(Library.AddReview(Arrival(), "arrival", "louise-banks", new ReviewForm("Ana", "4", ""), Day2)));
        Assert.Equal(Day2, added.Item.Date);
    }

    // ---- helpers ----

    private static string Html(Response response, int status) =>
        response.Match(
            page =>
            {
                Assert.Equal(status, page.Status);
                return page.Html;
            },
            redirect => throw new Xunit.Sdk.XunitException($"Expected a page, got a redirect to {redirect.Location}"));
}
