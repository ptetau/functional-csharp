using System.Collections.Immutable;
using Functional;

namespace CharacterReviews.Core;

/// <summary>What to send back: a page with a status code, or a 303 redirect.</summary>
public closed record Response
{
    public sealed record Page(int Status, string Html) : Response;

    public sealed record SeeOther(string Location) : Response;
}

/// <summary>The response to a form post, and the state to keep. Saved is None when nothing changed.</summary>
public sealed record Outcome(Option<State> Saved, Response Response);

/// <summary>
/// One function per route. The web host parses the request, calls one of these with the current state
/// (and the date, for reviews), saves Outcome.Saved if there is one, and sends the Response.
/// </summary>
public static class Site
{
    private static Response Ok(string html) => new Response.Page(200, html);

    private static Response BadRequest(string html) => new Response.Page(400, html);

    public static Response NotFound(string what) => new Response.Page(404, Html.NotFound(what));

    private static Response NotFound(Error error) => NotFound(error.Message);

    private static Outcome Unchanged(Response response) => new(Option.None<State>(), response);

    /// <summary>C-1</summary>
    public static Response Home(State state) =>
        Ok(Html.Home(Queries.MostReviewed(state), Queries.NewestReviews(state)));

    /// <summary>C-2</summary>
    public static Response Search(State state, string? query) =>
        Ok(Html.SearchResults(query ?? "", Queries.Search(state, query ?? "")));

    /// <summary>C-3</summary>
    public static Response Title(State state, string titleSlug) =>
        Queries.FindTitle(state, titleSlug).Match(
            title => Ok(Html.TitlePage(title, Queries.CharactersOf(state, title))),
            () => NotFound("There's no title at this address."));

    /// <summary>C-4</summary>
    public static Response Character(State state, string titleSlug, string characterSlug) =>
        CharacterPage(state, titleSlug, characterSlug, ReviewForm.Blank, [], 200);

    private static Response CharacterPage(
        State state, string titleSlug, string characterSlug, ReviewForm form, ImmutableArray<FieldError> errors, int status) =>
        Queries.FindCharacter(state, titleSlug, characterSlug)
            .Match(
                character => Queries.FindTitle(state, titleSlug).Match<Response>(
                    title => new Response.Page(status, Html.CharacterPage(title, character, Queries.ReviewsOf(state, character), form, errors)),
                    () => NotFound("There's no title at this address.")),
                () => NotFound("There's no character at this address."));

    /// <summary>C-5: add a review, then redirect to the character page; or show the form again with the problems.</summary>
    public static Outcome PostReview(State state, string titleSlug, string characterSlug, ReviewForm form, DateOnly today) =>
        Library.AddReview(state, titleSlug, characterSlug, form, today).Match(
            validated => validated.Match(
                added => new Outcome(Option.Some(added.State), new Response.SeeOther($"/characters/{titleSlug}/{characterSlug}")),
                errors => Unchanged(CharacterPage(state, titleSlug, characterSlug, Forms.Trimmed(form), errors, 400))),
            error => Unchanged(NotFound(error)));

    /// <summary>C-6</summary>
    public static Response NewTitle() => Ok(Html.NewTitlePage(TitleForm.Blank, []));

    /// <summary>C-6: add a title, then redirect to it.</summary>
    public static Outcome PostTitle(State state, TitleForm form) =>
        Library.AddTitle(state, form).Match(
            added => new Outcome(Option.Some(added.State), new Response.SeeOther(Html.TitleUrl(added.Item))),
            errors => Unchanged(BadRequest(Html.NewTitlePage(Forms.Trimmed(form), errors))));

    /// <summary>C-7</summary>
    public static Response NewCharacter(State state, string titleSlug) =>
        Queries.FindTitle(state, titleSlug).Match(
            title => Ok(Html.NewCharacterPage(title, CharacterForm.Blank, [])),
            () => NotFound("There's no title at this address."));

    /// <summary>C-7: add a character, then redirect to its page.</summary>
    public static Outcome PostCharacter(State state, string titleSlug, CharacterForm form) =>
        Library.AddCharacter(state, titleSlug, form).Match(
            validated => validated.Match(
                added => new Outcome(Option.Some(added.State), new Response.SeeOther(Html.CharacterUrl(added.Item))),
                errors => Unchanged(Queries.FindTitle(state, titleSlug).Match(
                    title => BadRequest(Html.NewCharacterPage(title, Forms.Trimmed(form), errors)),
                    () => NotFound("There's no title at this address.")))),
            error => Unchanged(NotFound(error)));
}
