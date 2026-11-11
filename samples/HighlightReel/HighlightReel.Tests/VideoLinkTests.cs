using Functional;
using HighlightReel.Core;

namespace HighlightReel.Tests;

public class VideoLinkTests
{
    private const string Id = "dQw4w9WgXcQ";

    private static VideoRef Parsed(string text) =>
        VideoLinks.Parse(text).Match(v => v, e => throw new Xunit.Sdk.XunitException($"'{text}' failed: {e}"));

    private static string Failure(string text) =>
        VideoLinks.Parse(text).Match(v => throw new Xunit.Sdk.XunitException($"'{text}' parsed as {v}"), e => e.Message);

    private static Option<int> NoTime => Option.None<int>();

    public static TheoryData<string> AllPrefixes(string rest) =>
        [$"https://www.{rest}", $"https://{rest}", $"http://www.{rest}", $"www.{rest}", rest];

    public static TheoryData<string> WatchLinks => AllPrefixes($"youtube.com/watch?v={Id}");
    public static TheoryData<string> ShortLinks => AllPrefixes($"youtu.be/{Id}");
    public static TheoryData<string> ShortsLinks => AllPrefixes($"youtube.com/shorts/{Id}");
    public static TheoryData<string> EmbedLinks => AllPrefixes($"youtube.com/embed/{Id}");
    public static TheoryData<string> MobileLinks =>
        [$"https://m.youtube.com/watch?v={Id}", $"http://m.youtube.com/watch?v={Id}", $"m.youtube.com/watch?v={Id}"];

    [Theory, MemberData(nameof(WatchLinks))]
    public void H1_watch_links_are_read_with_or_without_scheme_and_www(string link) =>
        Assert.Equal(new VideoRef(Id, NoTime), Parsed(link));

    [Theory, MemberData(nameof(ShortLinks))]
    public void H1_youtu_be_links_are_read(string link) =>
        Assert.Equal(new VideoRef(Id, NoTime), Parsed(link));

    [Theory, MemberData(nameof(ShortsLinks))]
    public void H1_shorts_links_are_read(string link) =>
        Assert.Equal(new VideoRef(Id, NoTime), Parsed(link));

    [Theory, MemberData(nameof(EmbedLinks))]
    public void H1_embed_links_are_read(string link) =>
        Assert.Equal(new VideoRef(Id, NoTime), Parsed(link));

    [Theory, MemberData(nameof(MobileLinks))]
    public void H1_mobile_watch_links_are_read(string link) =>
        Assert.Equal(new VideoRef(Id, NoTime), Parsed(link));

    [Theory]
    [InlineData("youtube.com/watch?feature=share&v=dQw4w9WgXcQ")]
    [InlineData("youtube.com/watch?v=dQw4w9WgXcQ&feature=share&list=PL123")]
    [InlineData("youtube.com/watch?list=PL123&index=2&v=dQw4w9WgXcQ")]
    [InlineData("youtube.com/watch?v=dQw4w9WgXcQ#comments")]
    [InlineData("HTTPS://WWW.YOUTUBE.COM/watch?v=dQw4w9WgXcQ")]
    public void H1_watch_links_take_other_query_parameters_in_any_order(string link) =>
        Assert.Equal(Id, Parsed(link).Id);

    [Fact]
    public void H1_a_bare_video_id_is_accepted() =>
        Assert.Equal(new VideoRef(Id, NoTime), Parsed(Id));

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ&t=43s", 43)]
    [InlineData("https://www.youtube.com/watch?t=43&v=dQw4w9WgXcQ", 43)]
    [InlineData("https://youtu.be/dQw4w9WgXcQ?t=1m35s", 95)]
    [InlineData("https://youtu.be/dQw4w9WgXcQ?si=abc&t=95", 95)]
    [InlineData("youtube.com/shorts/dQw4w9WgXcQ?t=7", 7)]
    [InlineData("https://www.youtube.com/embed/dQw4w9WgXcQ?start=30", 30)]
    [InlineData("m.youtube.com/watch?v=dQw4w9WgXcQ&start=1:01:35", 3695)]
    public void H2_t_or_start_parameter_gives_the_time(string link, int seconds) =>
        Assert.Equal(new VideoRef(Id, Option.Some(seconds)), Parsed(link));

    [Fact]
    public void H2_t_wins_over_start_when_both_are_given() =>
        Assert.Equal(Option.Some(10), Parsed("youtube.com/watch?v=dQw4w9WgXcQ&start=20&t=10").Time);

    [Theory]
    [InlineData("youtu.be/abc", "'abc' is not a valid video ID (expected 11 characters)")]
    [InlineData("youtube.com/watch?v=dQw4w9WgXcQQ", "'dQw4w9WgXcQQ' is not a valid video ID (expected 11 characters)")]
    [InlineData("youtube.com/watch?v=dQw4w9WgX!Q", "'dQw4w9WgX!Q' is not a valid video ID (only letters, digits, '-' and '_' are allowed)")]
    [InlineData("youtube.com/watch?list=PL123", "'youtube.com/watch?list=PL123' has no v= parameter")]
    [InlineData("https://vimeo.com/12345", "'https://vimeo.com/12345' is not a YouTube video link")]
    [InlineData("youtube.com/playlist?list=PL123", "'youtube.com/playlist?list=PL123' is not a YouTube video link")]
    [InlineData("youtube.com/channel/UC123", "'youtube.com/channel/UC123' is not a YouTube video link")]
    [InlineData("not a link", "'not a link' is not a YouTube video link")]
    [InlineData("youtu.be/dQw4w9WgXcQ?t=soon", "'soon' is not a valid time (use 95, 1:35, 1:01:35 or 1m35s)")]
    public void H4_bad_links_are_errors_that_name_the_bad_input(string link, string message) =>
        Assert.Equal(message, Failure(link));
}
