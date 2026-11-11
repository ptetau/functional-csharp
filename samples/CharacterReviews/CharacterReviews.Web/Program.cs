// The imperative shell: routes, the clock, form parsing and the data file. All decisions are in CharacterReviews.Core.
using CharacterReviews.Core;
using CharacterReviews.Web;
using Microsoft.Extensions.Primitives;

var builder = WebApplication.CreateBuilder(args);
var dataFile = builder.Configuration["DataFile"] ?? Path.Combine(builder.Environment.ContentRootPath, "data", "character-reviews.json");
builder.Services.AddSingleton(new Store(dataFile));
builder.Services.AddSingleton(TimeProvider.System);

var app = builder.Build();

app.MapGet("/", (Store store) => Send(Site.Home(store.Current)));

app.MapGet("/search", (Store store, string? q) => Send(Site.Search(store.Current, q)));

app.MapGet("/titles/new", () => Send(Site.NewTitle()));

app.MapPost("/titles", async (HttpRequest request, Store store) =>
{
    var form = await request.ReadFormAsync();
    var input = new TitleForm(Field(form, "name"), Field(form, "kind"), Field(form, "year"));
    return Send(store.Apply(state => Site.PostTitle(state, input)));
});

app.MapGet("/titles/{title}", (Store store, string title) => Send(Site.Title(store.Current, title)));

app.MapGet("/titles/{title}/characters/new", (Store store, string title) => Send(Site.NewCharacter(store.Current, title)));

app.MapPost("/titles/{title}/characters", async (HttpRequest request, Store store, string title) =>
{
    var form = await request.ReadFormAsync();
    var input = new CharacterForm(Field(form, "name"), Field(form, "description"));
    return Send(store.Apply(state => Site.PostCharacter(state, title, input)));
});

app.MapGet("/characters/{title}/{character}", (Store store, string title, string character) =>
    Send(Site.Character(store.Current, title, character)));

app.MapPost("/characters/{title}/{character}/reviews", async (HttpRequest request, Store store, TimeProvider clock, string title, string character) =>
{
    var form = await request.ReadFormAsync();
    var input = new ReviewForm(Field(form, "reviewer"), Field(form, "rating"), Field(form, "text"));
    var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);   // C-13
    return Send(store.Apply(state => Site.PostReview(state, title, character, input, today)));
});

// C-8: anything else is a 404 page with a link home.
app.MapFallback(() => Send(Site.NotFound("There's nothing at this address.")));

app.Run();

static string Field(IFormCollection form, string name) =>
    form.TryGetValue(name, out StringValues values) ? values.ToString() : "";

static IResult Send(Response response) => response.Match(
    page => Results.Content(page.Html, "text/html; charset=utf-8", statusCode: page.Status),
    seeOther => new SeeOtherResult(seeOther.Location));

/// <summary>Results.Redirect only offers 301, 302, 307 and 308; C-5 wants 303 See Other.</summary>
sealed class SeeOtherResult(string location) : IResult
{
    public Task ExecuteAsync(HttpContext httpContext)
    {
        httpContext.Response.StatusCode = StatusCodes.Status303SeeOther;
        httpContext.Response.Headers.Location = location;
        return Task.CompletedTask;
    }
}

/// <summary>Lets the end-to-end tests start the site with WebApplicationFactory.</summary>
public partial class Program;
