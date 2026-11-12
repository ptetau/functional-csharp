using HighlightReel.Core;

namespace HighlightReel.Tests;

public class CommandTests
{
    private const string Rick = "dQw4w9WgXcQ";
    private const string Gangnam = "9bZkp7q19f0";

    [Fact]
    public void H5_add_takes_the_time_from_the_argument() =>
        Assert.Equal(
            "1. dQw4w9WgXcQ  0:00:50  Dance move\n",
            Session.New.Then("add", $"https://youtu.be/{Rick}", "0:50", "Dance move").Output("list"));

    [Fact]
    public void H5_add_takes_the_time_from_the_links_t_parameter() =>
        Assert.Equal(
            "1. dQw4w9WgXcQ  0:00:43  Chorus starts\n",
            Session.New.Then("add", $"https://www.youtube.com/watch?v={Rick}&t=43s", "Chorus starts").Output("list"));

    [Fact]
    public void H5_the_time_argument_wins_over_the_links_t_parameter() =>
        Assert.Equal(
            "1. dQw4w9WgXcQ  0:00:10  x\n",
            Session.New.Then("add", $"youtu.be/{Rick}?t=99", "10", "x").Output("list"));

    [Fact]
    public void H5_add_reports_what_it_added() =>
        Assert.Equal("Added dQw4w9WgXcQ  0:00:50  Dance move\n", Session.New.Output("add", Rick, "50", "Dance move"));

    [Theory]
    [InlineData(new[] { "add", "youtu.be/dQw4w9WgXcQ", "soon", "x" }, "'soon' is not a valid time (use 95, 1:35, 1:01:35 or 1m35s)")]
    [InlineData(new[] { "add", "youtu.be/dQw4w9WgXcQ", "10", "  " }, "the label is empty")]
    [InlineData(new[] { "add", "youtu.be/dQw4w9WgXcQ", "10", "two\nlines" }, "a label can't contain line breaks or tabs")]
    [InlineData(new[] { "add", "youtu.be/dQw4w9WgXcQ" }, "usage: highlights add <url> [<time>] <label>")]
    [InlineData(new[] { "add", "youtu.be/dQw4w9WgXcQ", "10", "Dance", "move" }, "usage: highlights add <url> [<time>] <label>")]
    public void H5_bad_add_commands_are_errors(string[] args, string message) =>
        Assert.Equal(message, Session.New.Error(args));

    [Fact]
    public void H6_duration_accepts_a_link_or_an_id() =>
        Assert.Equal(
            Session.New.Then("duration", Gangnam, "4:12").Text,
            Session.New.Then("duration", $"https://youtu.be/{Gangnam}", "252").Text);

    [Fact]
    public void H6_duration_reports_the_length() =>
        Assert.Equal("9bZkp7q19f0 is 0:04:12 long\n", Session.New.Output("duration", Gangnam, "4:12"));

    [Fact]
    public void H6_a_milestone_past_the_known_end_is_refused() =>
        Assert.Equal(
            "0:05:00 is past the end of 9bZkp7q19f0 (0:04:12)",
            Session.New.Then("duration", Gangnam, "4:12").Error("add", Gangnam, "5:00", "Too late"));

    [Fact]
    public void H6_a_duration_shorter_than_an_existing_milestone_is_refused() =>
        Assert.Equal(
            "'Horse dance' at 0:01:08 is past 0:01:00; remove it first",
            Session.New.Then("add", Gangnam, "1:08", "Horse dance").Error("duration", Gangnam, "1:00"));

    [Fact]
    public void H6_a_zero_duration_is_refused() =>
        Assert.Equal("a video's duration must be more than 0 seconds", Session.New.Error("duration", Gangnam, "0"));

    [Fact]
    public void H7_list_groups_by_video_in_first_use_order_and_time_order_within() =>
        Assert.Equal(
            "1. dQw4w9WgXcQ  0:00:43  Chorus starts\n" +
            "2. dQw4w9WgXcQ  0:00:50  Dance move\n" +
            "3. 9bZkp7q19f0  0:00:03  Intro\n" +
            "4. 9bZkp7q19f0  0:01:08  Horse dance\n",
            Session.SpecExample.Output("list"));

    [Fact]
    public void H7_list_of_an_empty_reel_prints_nothing() =>
        Assert.Equal("", Session.New.Output("list"));

    [Fact]
    public void H8_links_open_each_milestone_at_its_moment() =>
        Assert.Equal(
            "https://youtu.be/dQw4w9WgXcQ?t=43\n" +
            "https://youtu.be/dQw4w9WgXcQ?t=50\n" +
            "https://youtu.be/9bZkp7q19f0?t=3\n" +
            "https://youtu.be/9bZkp7q19f0?t=68\n",
            Session.SpecExample.Output("links"));

    [Fact]
    public void H9_before_and_after_change_the_clip_padding() =>
        Assert.Equal(
            "1. 0:00:00  dQw4w9WgXcQ  0:00:42–0:00:45  Chorus starts\n" +
            "2. 0:00:03  dQw4w9WgXcQ  0:00:49–0:00:52  Dance move\n" +
            "3. 0:00:06  9bZkp7q19f0  0:00:02–0:00:05  Intro\n" +
            "4. 0:00:09  9bZkp7q19f0  0:01:07–0:01:10  Horse dance\n" +
            "Total: 0:00:12\n",
            Session.SpecExample.Output("timeline", "--before", "1", "--after", "2"));

    [Theory]
    [InlineData(new[] { "timeline", "--format", "xml" }, "'xml' is not a format (use text, csv, json or edl)")]
    [InlineData(new[] { "timeline", "--before" }, "--before needs a value")]
    [InlineData(new[] { "timeline", "--before", "lots" }, "'lots' is not a valid time (use 95, 1:35, 1:01:35 or 1m35s)")]
    [InlineData(new[] { "timeline", "--speed", "2" }, "'--speed' is not a timeline option (use --before, --after or --format)")]
    public void H9_bad_timeline_options_are_errors(string[] args, string message) =>
        Assert.Equal(message, Session.SpecExample.Error(args));

    [Fact]
    public void H10_remove_takes_the_number_shown_by_list() =>
        Assert.Equal(
            "1. dQw4w9WgXcQ  0:00:43  Chorus starts\n" +
            "2. dQw4w9WgXcQ  0:00:50  Dance move\n" +
            "3. 9bZkp7q19f0  0:01:08  Horse dance\n",
            Session.SpecExample.Then("remove", "3").Output("list"));

    [Fact]
    public void H10_remove_reports_what_it_removed() =>
        Assert.Equal("Removed 3. 9bZkp7q19f0  0:00:03  Intro\n", Session.SpecExample.Output("remove", "3"));

    [Theory]
    [InlineData("0", "there is no milestone 0 (the reel has 4; see 'list')")]
    [InlineData("5", "there is no milestone 5 (the reel has 4; see 'list')")]
    [InlineData("two", "'two' is not a milestone number")]
    public void H10_remove_rejects_numbers_list_does_not_show(string n, string message) =>
        Assert.Equal(message, Session.SpecExample.Error("remove", n));

    [Fact]
    public void H14_removing_a_videos_first_milestone_moves_it_by_its_next_earliest_one()
    {
        // Rick was used first (milestones 1, 2); Gangnam second (3, 4). Remove both Rick milestones,
        // add Rick back: Gangnam is now first.
        var session = Session.SpecExample.Then("remove", "1").Then("remove", "1").Then("add", Rick, "43", "Again");
        Assert.Equal(
            "1. 9bZkp7q19f0  0:00:03  Intro\n" +
            "2. 9bZkp7q19f0  0:01:08  Horse dance\n" +
            "3. dQw4w9WgXcQ  0:00:43  Again\n",
            session.Output("list"));
    }

    [Theory]
    [InlineData(new[] { "play" }, "'play' is not a command (try: add, duration, list, links, timeline, remove)")]
    [InlineData(new[] { "list", "extra" }, "list takes no arguments")]
    [InlineData(new[] { "--reel" }, "--reel needs a path")]
    [InlineData(new[] { "--reel", "a.txt", "--reel", "b.txt", "list" }, "--reel is given more than once")]
    public void Bad_commands_are_errors(string[] args, string message) =>
        Assert.Equal(message, Session.New.Error(args));

    [Fact]
    public void No_arguments_prints_usage() =>
        Assert.StartsWith("usage: highlights [--reel <path>] <command>", Session.New.Output());

    [Theory]
    [InlineData("list")]
    [InlineData("links")]
    [InlineData("timeline")]
    [InlineData("help")]
    public void Read_only_commands_ask_for_no_reel_write(string command) =>
        Assert.False(App.Execute(new Invocation("reel.txt", [command]), Session.SpecExample.ReelText)
            .Match(response => response.NewReelText.IsSome, _ => true));

    [Fact]
    public void Changing_commands_ask_for_a_reel_write_even_when_the_file_is_missing() =>
        Assert.True(App.Execute(new Invocation("reel.txt", ["add", Rick, "10", "x"]), Functional.Option.None<string>())
            .Match(response => response.NewReelText.IsSome, _ => false));

    [Fact]
    public void Reel_path_defaults_to_reel_txt_and_can_appear_anywhere()
    {
        Assert.Equal("reel.txt", App.ParseArgs(["list"]).Match(i => i.ReelPath, e => e.Message));
        var parsed = App.ParseArgs(["timeline", "--reel", "my.reel", "--format", "csv"]);
        Assert.Equal("my.reel", parsed.Match(i => i.ReelPath, e => e.Message));
        Assert.Equal(["timeline", "--format", "csv"], parsed.Match(i => i.Command.ToArray(), _ => []));
    }
}
