using Functional;
using HighlightReel.Core;

namespace HighlightReel.Tests;

public class TimelineTests
{
    private const string A = "aaaaaaaaaaa";
    private const string B = "bbbbbbbbbbb";

    private static Reel ReelOf(params (string Video, int Seconds, string Label)[] milestones) =>
        milestones.Aggregate(Reel.Empty, (reel, m) => Reels.AddMilestone(reel, m.Video, m.Seconds, m.Label));

    private static ValueList<(string, int, int, string, int)> Clips(Reel reel, Padding? padding = null) =>
        [.. Timelines.Build(reel, padding ?? Padding.Default).Clips
            .Select(p => (p.Clip.VideoId, p.Clip.Start, p.Clip.End, string.Join(" / ", p.Clip.Labels), p.ReelStart))];

    [Fact]
    public void H11_each_milestone_becomes_a_clip_from_t_minus_before_to_t_plus_after() =>
        Assert.Equal([(A, 90, 110, "x", 0)], Clips(ReelOf((A, 100, "x")), new Padding(10, 10)));

    [Fact]
    public void H11_before_and_after_can_differ() =>
        Assert.Equal([(A, 98, 130, "x", 0)], Clips(ReelOf((A, 100, "x")), new Padding(2, 30)));

    [Fact]
    public void H12_clips_never_start_before_zero() =>
        Assert.Equal([(A, 0, 8, "x", 0)], Clips(ReelOf((A, 3, "x"))));

    [Fact]
    public void H12_clips_end_at_the_known_duration() =>
        Assert.Equal([(A, 245, 252, "x", 0)], Clips(Reels.SetDuration(ReelOf((A, 250, "x")), A, 252)));

    [Fact]
    public void H12_without_a_duration_clips_run_their_full_length() =>
        Assert.Equal([(A, 245, 255, "x", 0)], Clips(ReelOf((A, 250, "x"))));

    [Fact]
    public void H12_a_milestone_past_the_end_in_a_hand_edited_reel_gives_an_empty_clip_not_a_negative_one() =>
        Assert.Equal([(A, 100, 100, "x", 0)], Clips(Reels.SetDuration(ReelOf((A, 300, "x")), A, 100)));

    [Fact]
    public void H13_overlapping_clips_merge() =>
        Assert.Equal([(A, 38, 55, "Chorus / Dance", 0)], Clips(ReelOf((A, 43, "Chorus"), (A, 50, "Dance"))));

    [Fact]
    public void H13_touching_clips_merge() =>
        Assert.Equal([(A, 5, 25, "one / two", 0)], Clips(ReelOf((A, 10, "one"), (A, 20, "two"))));

    [Fact]
    public void H13_clips_one_second_apart_do_not_merge() =>
        Assert.Equal([(A, 5, 15, "one", 0), (A, 16, 26, "two", 10)], Clips(ReelOf((A, 10, "one"), (A, 21, "two"))));

    [Fact]
    public void H13_labels_join_in_time_order_not_the_order_they_were_added() =>
        Assert.Equal([(A, 5, 28, "first / second / third", 0)], Clips(ReelOf((A, 23, "third"), (A, 10, "first"), (A, 16, "second"))));

    [Fact]
    public void H13_milestones_at_the_same_time_keep_the_order_they_were_added() =>
        Assert.Equal([(A, 5, 15, "added first / added second", 0)], Clips(ReelOf((A, 10, "added first"), (A, 10, "added second"))));

    [Fact]
    public void H13_clips_clamped_to_the_same_end_merge() =>
        Assert.Equal([(A, 42, 50, "a / b", 0)], Clips(Reels.SetDuration(ReelOf((A, 47, "a"), (A, 49, "b")), A, 50)));

    [Fact]
    public void H13_clips_from_different_videos_never_merge() =>
        Assert.Equal(
            [(A, 5, 15, "a", 0), (B, 6, 16, "b", 10)],
            Clips(ReelOf((A, 10, "a"), (B, 11, "b"))));

    [Fact]
    public void H14_clips_are_ordered_by_first_use_of_their_video_then_start() =>
        Assert.Equal(
            [(B, 5, 15, "b early", 0), (B, 95, 105, "b late", 10), (A, 5, 15, "a early", 20), (A, 195, 205, "a late", 30)],
            Clips(ReelOf((B, 100, "b late"), (A, 200, "a late"), (B, 10, "b early"), (A, 10, "a early"))));

    [Fact]
    public void H15_reel_start_is_the_sum_of_the_lengths_before_it() =>
        Assert.Equal(
            new[] { 0, 17, 25 },
            Timelines.Build(ReelFile.Parse(Session.SpecExample.Text).ValueOr(Reel.Empty), Padding.Default)
                .Clips.Select(p => p.ReelStart).ToArray());

    [Fact]
    public void H15_total_is_the_sum_of_all_clip_lengths() =>
        Assert.Equal(35, Timelines.Build(ReelFile.Parse(Session.SpecExample.Text).ValueOr(Reel.Empty), Padding.Default).TotalSeconds);

    [Fact]
    public void Timelines_built_from_equal_reels_are_equal() =>
        Assert.Equal(
            Timelines.Build(ReelFile.Parse(Session.SpecExample.Text).ValueOr(Reel.Empty), Padding.Default),
            Timelines.Build(ReelFile.Parse(Session.SpecExample.Text).ValueOr(Reel.Empty), Padding.Default));

    [Fact]
    public void Many_touching_clips_merge_into_one_keeping_every_label_in_order() =>
        Assert.Equal(
            [(A, 0, 1005, string.Join(" / ", Enumerable.Range(0, 1001).Select(i => $"m{i}")), 0)],
            Clips(Enumerable.Range(0, 1001).Aggregate(Reel.Empty, (reel, i) => Reels.AddMilestone(reel, A, i, $"m{i}"))));

    [Fact]
    public void An_empty_reel_gives_an_empty_timeline() =>
        Assert.Equal("Total: 0:00:00\n", Session.New.Output("timeline"));
}
