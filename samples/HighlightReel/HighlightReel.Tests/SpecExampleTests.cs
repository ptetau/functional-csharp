namespace HighlightReel.Tests;

/// <summary>Every example in specs/highlight-reel.md, with its exact output.</summary>
public class SpecExampleTests
{
    private const string ExampleTimeline =
        "1. 0:00:00  dQw4w9WgXcQ  0:00:38–0:00:55  Chorus starts / Dance move\n" +
        "2. 0:00:17  9bZkp7q19f0  0:00:00–0:00:08  Intro\n" +
        "3. 0:00:25  9bZkp7q19f0  0:01:03–0:01:13  Horse dance\n" +
        "Total: 0:00:35\n";

    [Fact]
    public void H9_example_timeline_matches_the_spec_exactly() =>
        Assert.Equal(ExampleTimeline, Session.SpecExample.Output("timeline"));

    [Fact]
    public void H9_explicit_defaults_give_the_same_timeline() =>
        Assert.Equal(ExampleTimeline, Session.SpecExample.Output("timeline", "--before", "5", "--after", "5", "--format", "text"));

    [Fact]
    public void H13_example_overlapping_clips_38_48_and_45_55_merge_into_38_55() =>
        Assert.StartsWith(
            "1. 0:00:00  dQw4w9WgXcQ  0:00:38–0:00:55  Chorus starts / Dance move\n",
            Session.SpecExample.Output("timeline"));

    [Fact]
    public void H12_example_intro_at_3s_is_clamped_to_0_8() =>
        Assert.Contains("9bZkp7q19f0  0:00:00–0:00:08  Intro", Session.SpecExample.Output("timeline"));

    [Fact]
    public void H4_example_short_video_id_is_rejected_by_name() =>
        Assert.Equal(
            "'abc' is not a valid video ID (expected 11 characters)",
            Session.New.Error("add", "youtu.be/abc", "1:00", "x"));

    [Fact]
    public void H5_example_link_without_time_and_no_time_argument_fails() =>
        Assert.Equal(
            "no time given and the link has no t= parameter",
            Session.New.Error("add", "https://youtu.be/dQw4w9WgXcQ", "No time"));
}
