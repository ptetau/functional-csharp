using HighlightReel.Core;

namespace HighlightReel.Tests;

public class ReelFileTests
{
    private const string ExampleFile =
        "# HighlightReel reel, version 1. One entry per line:\n" +
        "#   duration <video-id> <seconds>\n" +
        "#   milestone <order> <video-id> <seconds> <label>\n" +
        "duration 9bZkp7q19f0 252\n" +
        "milestone 1 dQw4w9WgXcQ 43 Chorus starts\n" +
        "milestone 2 dQw4w9WgXcQ 50 Dance move\n" +
        "milestone 3 9bZkp7q19f0 68 Horse dance\n" +
        "milestone 4 9bZkp7q19f0 3 Intro\n";

    [Fact]
    public void The_spec_example_is_saved_in_this_format() =>
        Assert.Equal(ExampleFile, Session.SpecExample.Text);

    [Fact]
    public void Reading_then_writing_gives_the_same_text() =>
        Assert.Equal(ExampleFile, ReelFile.Parse(ExampleFile).Map(ReelFile.Write).ValueOr("failed"));

    [Fact]
    public void Reels_with_the_same_contents_are_equal() =>
        Assert.Equal(ReelFile.Parse(ExampleFile), ReelFile.Parse(ExampleFile));

    [Fact]
    public void Durations_are_kept_in_video_order_so_the_order_they_were_set_in_does_not_matter() =>
        Assert.Equal(
            Reels.SetDuration(Reels.SetDuration(Reel.Empty, "aaaaaaaaaaa", 10), "bbbbbbbbbbb", 20),
            Reels.SetDuration(Reels.SetDuration(Reels.SetDuration(Reel.Empty, "bbbbbbbbbbb", 20), "aaaaaaaaaaa", 5), "aaaaaaaaaaa", 10));

    [Fact]
    public void The_last_duration_for_a_video_wins() =>
        Assert.Equal(
            Functional.Option.Some(20),
            Reels.DurationOf(ReelFile.Parse("duration dQw4w9WgXcQ 10\nduration dQw4w9WgXcQ 20\n").ValueOr(Reel.Empty), "dQw4w9WgXcQ"));

    [Fact]
    public void Blank_lines_comments_and_crlf_are_ignored() =>
        Assert.Equal(
            ExampleFile,
            ReelFile.Parse("\r\n# mine\r\nmilestone 4 9bZkp7q19f0 3 Intro\r\n\r\nduration 9bZkp7q19f0 252\r\n" +
                           "milestone 2 dQw4w9WgXcQ 50 Dance move\nmilestone 1 dQw4w9WgXcQ 43 Chorus starts\nmilestone 3 9bZkp7q19f0 68 Horse dance")
                .Map(ReelFile.Write).ValueOr("failed"));

    [Fact]
    public void Labels_keep_their_inner_spaces() =>
        Assert.Equal(
            "1. dQw4w9WgXcQ  0:00:10  a  b   c\n",
            new Session(Functional.Option.Some("milestone 1 dQw4w9WgXcQ 10 a  b   c\n")).Output("list"));

    [Theory]
    [InlineData("milestone 1 dQw4w9WgXcQ 10", "line 1: can't read 'milestone 1 dQw4w9WgXcQ 10' (expected 'duration <id> <seconds>' or 'milestone <order> <id> <seconds> <label>')")]
    [InlineData("\nduration abc 10", "line 2: 'abc' is not a valid video ID (expected 11 characters)")]
    [InlineData("duration dQw4w9WgXcQ ten", "line 1: 'ten' is not a whole number")]
    [InlineData("chapter 1", "line 1: can't read 'chapter 1' (expected 'duration <id> <seconds>' or 'milestone <order> <id> <seconds> <label>')")]
    [InlineData("milestone 1 dQw4w9WgXcQ 10 a\nmilestone 1 dQw4w9WgXcQ 20 b", "two milestones have the same order number")]
    public void Bad_reel_files_are_errors_naming_the_line(string text, string message) =>
        Assert.Equal(message, ReelFile.Parse(text).Match(_ => "parsed", e => e.Message));

    [Fact]
    public void A_bad_reel_file_error_names_the_file() =>
        Assert.Equal(
            "my.reel: line 1: can't read 'nonsense' (expected 'duration <id> <seconds>' or 'milestone <order> <id> <seconds> <label>')",
            App.Execute(new Invocation("my.reel", ["list"]), Functional.Option.Some("nonsense")).Match(_ => "ran", e => e.Message));
}
