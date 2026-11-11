using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace CharacterReviews.Tests;

/// <summary>Starts the real site in memory with its own data file and a fixed clock.</summary>
public sealed class TestSite : WebApplicationFactory<Program>
{
    public string DataFile { get; } = Path.Combine(Path.GetTempPath(), $"character-reviews-{Guid.NewGuid():N}", "data.json");

    public FixedClock Clock { get; } = new(new DateTimeOffset(2026, 11, 11, 12, 0, 0, TimeSpan.Zero));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("DataFile", DataFile);
        builder.ConfigureTestServices(services => services.AddSingleton<TimeProvider>(Clock));
    }

    public HttpClient Browser() => CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (Directory.Exists(Path.GetDirectoryName(DataFile))) Directory.Delete(Path.GetDirectoryName(DataFile)!, recursive: true);
    }
}

/// <summary>A clock that only moves when told to.</summary>
public sealed class FixedClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

    public void Advance(TimeSpan by) => _now += by;
}

/// <summary>End-to-end tests through HTTP. Each spec example is a test named after its requirement ID.</summary>
public class WebTests
{
    private static async Task<HttpResponseMessage> Post(HttpClient client, string url, params (string Key, string Value)[] fields) =>
        await client.PostAsync(url, new FormUrlEncodedContent(fields.Select(f => KeyValuePair.Create(f.Key, f.Value))));

    private static async Task<string> Page(HttpClient client, string url, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var response = await client.GetAsync(url);
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        return await response.Content.ReadAsStringAsync();
    }

    private static async Task SeeOther(HttpResponseMessage response, string location)
    {
        Assert.True(response.StatusCode == HttpStatusCode.SeeOther, $"Expected 303, got {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        Assert.Equal(location, response.Headers.Location?.OriginalString);
    }

    /// <summary>The spec's example, set up through the forms as a person would.</summary>
    private static async Task Arrival(HttpClient client)
    {
        await SeeOther(await Post(client, "/titles", ("name", "Arrival"), ("kind", "film"), ("year", "2016")), "/titles/arrival");
        await SeeOther(await Post(client, "/titles/arrival/characters", ("name", "Louise Banks"), ("description", "A linguist.")), "/characters/arrival/louise-banks");
        foreach (var (who, rating) in new[] { ("Ana", "5"), ("Ben", "4"), ("Cy", "4") })
            await SeeOther(await Post(client, "/characters/arrival/louise-banks/reviews", ("reviewer", who), ("rating", rating), ("text", "")), "/characters/arrival/louise-banks");
    }

    [Fact]
    public async Task C1_home_page_has_search_box_most_reviewed_and_newest()
    {
        using var site = new TestSite();
        var client = site.Browser();
        await Arrival(client);
        var home = await Page(client, "/");
        Assert.Contains("<form action=\"/search\" method=\"get\"", home);
        Assert.Contains("Most reviewed", home);
        Assert.Contains("Newest reviews", home);
        Assert.Contains("Louise Banks", home);
        Assert.DoesNotContain("<script", home);
    }

    [Fact]
    public async Task C2_example_search_LOUISE_finds_Louise_Banks()
    {
        using var site = new TestSite();
        var client = site.Browser();
        await Arrival(client);
        var page = await Page(client, "/search?q=LOUISE");
        Assert.Contains("href=\"/characters/arrival/louise-banks\"", page);
        Assert.Contains("1 character matching", page);
    }

    [Fact]
    public async Task C2_zoe_finds_Zoe_with_diaeresis()
    {
        using var site = new TestSite();
        var client = site.Browser();
        await SeeOther(await Post(client, "/titles", ("name", "Firefly"), ("kind", "show"), ("year", "2002")), "/titles/firefly");
        await SeeOther(await Post(client, "/titles/firefly/characters", ("name", "Zoë Washburne")), "/characters/firefly/zoe-washburne");
        Assert.Contains("Zoë Washburne", await Page(client, "/search?q=zoe"));
    }

    [Fact]
    public async Task C2_empty_query_asks_for_one()
    {
        using var site = new TestSite();
        var client = site.Browser();
        Assert.Contains("Type part of a character's name to search.", await Page(client, "/search?q="));
        Assert.Contains("Type part of a character's name to search.", await Page(client, "/search"));
    }

    [Fact]
    public async Task C3_title_page_lists_characters_with_average_and_count()
    {
        using var site = new TestSite();
        var client = site.Browser();
        await Arrival(client);
        var page = await Page(client, "/titles/arrival");
        Assert.Contains("Arrival (2016)", page);
        Assert.Contains("Louise Banks", page);
        Assert.Contains("4.3 (3 reviews)", page);
    }

    [Fact]
    public async Task C4_example_character_page_shows_4_3_and_breakdown()
    {
        using var site = new TestSite();
        var client = site.Browser();
        await Arrival(client);
        var page = await Page(client, "/characters/arrival/louise-banks");
        Assert.Contains("4.3", page);
        Assert.Contains("5★ 1", page);
        Assert.Contains("4★ 2", page);
        Assert.Contains("3★ 0", page);
        Assert.Contains("2★ 0", page);
        Assert.Contains("1★ 0", page);
        Assert.Contains("action=\"/characters/arrival/louise-banks/reviews\" method=\"post\"", page);
        // Newest first: Cy wrote the last review.
        Assert.True(page.IndexOf("Cy", StringComparison.Ordinal) < page.IndexOf("Ana", StringComparison.Ordinal));
    }

    [Fact]
    public async Task C5_valid_review_redirects_303_to_the_character_page()
    {
        using var site = new TestSite();
        var client = site.Browser();
        await Arrival(client);
        var response = await Post(client, "/characters/arrival/louise-banks/reviews", ("reviewer", "Dee"), ("rating", "1"), ("text", "Not for me"));
        await SeeOther(response, "/characters/arrival/louise-banks");
        Assert.Contains("Not for me", await Page(client, "/characters/arrival/louise-banks"));
    }

    [Fact]
    public async Task C5_invalid_review_redisplays_form_with_errors()
    {
        using var site = new TestSite();
        var client = site.Browser();
        await Arrival(client);
        var response = await Post(client, "/characters/arrival/louise-banks/reviews", ("reviewer", ""), ("rating", "6"), ("text", "I want this kept"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var page = await response.Content.ReadAsStringAsync();
        Assert.Contains("Rating must be between 1 and 5", page);
        Assert.Contains("Name is required", page);
        Assert.Contains(">I want this kept</textarea>", page);
        Assert.Contains("<option value=\"6\" selected>6</option>", page);
        // Each problem is next to its own field.
        Assert.True(page.IndexOf("id=\"reviewer-error\"", StringComparison.Ordinal) < page.IndexOf("id=\"rating\"", StringComparison.Ordinal));
        Assert.True(page.IndexOf("id=\"rating-error\"", StringComparison.Ordinal) < page.IndexOf("id=\"text\"", StringComparison.Ordinal));
        // Nothing was saved.
        Assert.Contains("4.3", await Page(client, "/characters/arrival/louise-banks"));
    }

    [Fact]
    public async Task C5_review_for_unknown_character_is_404()
    {
        using var site = new TestSite();
        var client = site.Browser();
        var response = await Post(client, "/characters/arrival/nobody/reviews", ("reviewer", "A"), ("rating", "3"));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task C6_add_title_form_and_duplicate_name_and_year_is_an_error()
    {
        using var site = new TestSite();
        var client = site.Browser();
        Assert.Contains("action=\"/titles\" method=\"post\"", await Page(client, "/titles/new"));
        await SeeOther(await Post(client, "/titles", ("name", "Arrival"), ("kind", "film"), ("year", "2016")), "/titles/arrival");
        var duplicate = await Post(client, "/titles", ("name", "Arrival"), ("kind", "film"), ("year", "2016"));
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        Assert.Contains("Arrival (2016) has already been added", await duplicate.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task C6_invalid_title_redisplays_form_with_errors()
    {
        using var site = new TestSite();
        var client = site.Browser();
        var response = await Post(client, "/titles", ("name", "Severance"), ("kind", "podcast"), ("year", "soon"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var page = await response.Content.ReadAsStringAsync();
        Assert.Contains("Kind must be show or film", page);
        Assert.Contains("Year must be a whole number", page);
        Assert.Contains("value=\"Severance\"", page);
        Assert.Contains("value=\"soon\"", page);
    }

    [Fact]
    public async Task C7_example_duplicate_character_in_same_title_refused_but_fine_in_another()
    {
        using var site = new TestSite();
        var client = site.Browser();
        await Arrival(client);
        Assert.Contains("action=\"/titles/arrival/characters\" method=\"post\"", await Page(client, "/titles/arrival/characters/new"));

        var duplicate = await Post(client, "/titles/arrival/characters", ("name", "Louise Banks"));
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        Assert.Contains("already has a character called Louise Banks", await duplicate.Content.ReadAsStringAsync());

        await SeeOther(await Post(client, "/titles", ("name", "Story of Your Life"), ("kind", "film"), ("year", "2030")), "/titles/story-of-your-life");
        await SeeOther(await Post(client, "/titles/story-of-your-life/characters", ("name", "Louise Banks")), "/characters/story-of-your-life/louise-banks");
        await Page(client, "/characters/arrival/louise-banks");
        await Page(client, "/characters/story-of-your-life/louise-banks");
    }

    [Fact]
    public async Task C8_unknown_title_or_character_is_404_with_a_link_home()
    {
        using var site = new TestSite();
        var client = site.Browser();
        await Arrival(client);
        foreach (var url in new[] { "/titles/nope", "/characters/arrival/nope", "/characters/nope/louise-banks", "/titles/nope/characters/new", "/no/such/page" })
            Assert.Contains("<a href=\"/\">", await Page(client, url, HttpStatusCode.NotFound));
        Assert.Equal(HttpStatusCode.NotFound, (await Post(client, "/titles/nope/characters", ("name", "X"))).StatusCode);
    }

    [Fact]
    public async Task C11_example_script_in_review_is_shown_as_literal_text()
    {
        using var site = new TestSite();
        var client = site.Browser();
        await Arrival(client);
        await SeeOther(await Post(client, "/characters/arrival/louise-banks/reviews", ("reviewer", "<i>Eve</i>"), ("rating", "3"), ("text", "<script>alert(1)</script>")), "/characters/arrival/louise-banks");
        var page = await Page(client, "/characters/arrival/louise-banks");
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", page);
        Assert.Contains("&lt;i&gt;Eve&lt;/i&gt;", page);
        Assert.DoesNotContain("<script>", page);
        Assert.DoesNotContain("<i>", await Page(client, "/"));
    }

    [Fact]
    public async Task C12_inputs_are_trimmed()
    {
        using var site = new TestSite();
        var client = site.Browser();
        await SeeOther(await Post(client, "/titles", ("name", "  Arrival  "), ("kind", " film "), ("year", " 2016 ")), "/titles/arrival");
        Assert.Contains("<h1>Arrival (2016)</h1>", await Page(client, "/titles/arrival"));
    }

    [Fact]
    public async Task C13_review_date_is_the_server_date_when_saved()
    {
        using var site = new TestSite();
        var client = site.Browser();
        await Arrival(client);
        site.Clock.Advance(TimeSpan.FromDays(3));
        await SeeOther(await Post(client, "/characters/arrival/louise-banks/reviews", ("reviewer", "Dee"), ("rating", "2")), "/characters/arrival/louise-banks");
        var page = await Page(client, "/characters/arrival/louise-banks");
        Assert.Contains("on 2026-11-14", page);
        Assert.Contains("on 2026-11-11", page);
    }

    [Fact]
    public async Task C14_data_survives_a_restart()
    {
        using var first = new TestSite();
        await Arrival(first.Browser());
        Assert.True(File.Exists(first.DataFile));

        // A second site on the same file, as after a restart.
        using var second = new SiteOn(first.DataFile);
        Assert.Contains("4.3", await Page(second.Browser(), "/characters/arrival/louise-banks"));
    }

    private sealed class SiteOn(string dataFile) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseSetting("DataFile", dataFile);

        public HttpClient Browser() => CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }
}
