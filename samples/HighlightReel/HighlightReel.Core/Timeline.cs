using Functional;

namespace HighlightReel.Core;

/// <summary>A stretch of one video, with the labels of the milestones it covers in time order.</summary>
public sealed record Clip(string VideoId, int Start, int End, ValueList<string> Labels)
{
    public int Length => End - Start;
}

/// <summary>A clip at its place in the reel: numbered from 1, starting ReelStart seconds in (H-15).</summary>
public sealed record PlacedClip(int Index, Clip Clip, int ReelStart);

public sealed record Timeline(ValueList<PlacedClip> Clips, int TotalSeconds);

/// <summary>Seconds of video kept before and after each milestone (H-9, H-11).</summary>
public sealed record Padding(int Before, int After)
{
    public static readonly Padding Default = new(5, 5);
}

/// <summary>Building the pre-cut timeline (H-11 to H-15).</summary>
public static class Timelines
{
    public static Timeline Build(Reel reel, Padding padding) =>
        Place([.. Reels.ByVideo(reel).SelectMany(video => ClipsFor(video, padding, Reels.DurationOf(reel, video.Key)))]);

    private static IEnumerable<Clip> ClipsFor(IGrouping<string, Milestone> video, Padding padding, Option<int> duration) =>
        Merge(video.Key, [.. Reels.InTimeOrder(video).Select(m => ClipAround(m, padding, duration))]);

    // H-11, H-12. A milestone past the known end gives an empty clip at the end rather than a negative one.
    private static Clip ClipAround(Milestone milestone, Padding padding, Option<int> duration)
    {
        var end = duration.Match(
            length => Math.Min(length, milestone.Seconds + padding.After),
            () => milestone.Seconds + padding.After);
        var start = Math.Min(end, Math.Max(0, milestone.Seconds - padding.Before));
        return new Clip(milestone.VideoId, start, end, [milestone.Label]);
    }

    // H-13. The clips are in time order, so a clip joins the run before it if it starts no later than the
    // run's end so far, and otherwise starts a new run. Runs don't overlap, so a run's start names it.
    private static IEnumerable<Clip> Merge(string video, ValueList<Clip> clips) =>
        clips
            .Zip(clips.Scan(
                (Start: -1, End: -1),
                (run, clip) => clip.Start > run.End ? (clip.Start, clip.End) : (run.Start, Math.Max(run.End, clip.End))))
            .GroupBy(pair => pair.Second.Start, pair => pair.First)
            .Select(run => new Clip(
                video,
                run.Key,
                run.Select(clip => clip.End).DefaultIfEmpty(run.Key).Max(),
                [.. run.SelectMany(clip => clip.Labels)]));

    // H-15. Each clip starts where the ones before it end: a running total of the lengths.
    private static Timeline Place(ValueList<Clip> clips) =>
        new(
            [.. clips
                .Zip(clips.Scan(0, (end, clip) => end + clip.Length))
                .Select((pair, i) => new PlacedClip(i + 1, pair.First, pair.Second - pair.First.Length))],
            clips.Sum(clip => clip.Length));
}
