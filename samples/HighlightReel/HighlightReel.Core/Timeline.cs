using System.Collections.Immutable;
using Functional;

namespace HighlightReel.Core;

/// <summary>A stretch of one video, with the labels of the milestones it covers in time order.</summary>
public sealed record Clip(string VideoId, int Start, int End, ImmutableArray<string> Labels)
{
    public int Length => End - Start;
}

/// <summary>A clip at its place in the reel: numbered from 1, starting ReelStart seconds in (H-15).</summary>
public sealed record PlacedClip(int Index, Clip Clip, int ReelStart);

public sealed record Timeline(ImmutableArray<PlacedClip> Clips, int TotalSeconds);

/// <summary>Seconds of video kept before and after each milestone (H-9, H-11).</summary>
public sealed record Padding(int Before, int After)
{
    public static readonly Padding Default = new(5, 5);
}

/// <summary>Building the pre-cut timeline (H-11 to H-15).</summary>
public static class Timelines
{
    public static Timeline Build(Reel reel, Padding padding) =>
        Place(Reels.VideosInFirstUseOrder(reel)
            .SelectMany(video => ClipsFor(reel, video, padding)));

    private static ImmutableArray<Clip> ClipsFor(Reel reel, string video, Padding padding) =>
        reel.Milestones
            .Where(m => m.VideoId == video)
            .OrderBy(m => m.Seconds)
            .ThenBy(m => m.Order)
            .Select(m => ClipAround(m, padding, Reels.DurationOf(reel, video)))
            .Aggregate(ImmutableArray<Clip>.Empty, MergeInto);

    // H-11, H-12. A milestone past the known end gives an empty clip at the end rather than a negative one.
    private static Clip ClipAround(Milestone milestone, Padding padding, Option<int> duration)
    {
        var end = duration.Match(
            length => Math.Min(length, milestone.Seconds + padding.After),
            () => milestone.Seconds + padding.After);
        var start = Math.Min(end, Math.Max(0, milestone.Seconds - padding.Before));
        return new Clip(milestone.VideoId, start, end, [milestone.Label]);
    }

    // H-13. Clips arrive in time order, so only the last merged clip can overlap or touch the next one.
    private static ImmutableArray<Clip> MergeInto(ImmutableArray<Clip> merged, Clip next) =>
        merged.IsEmpty || merged[^1].End < next.Start
            ? merged.Add(next)
            : merged.SetItem(merged.Length - 1, merged[^1] with
            {
                End = Math.Max(merged[^1].End, next.End),
                Labels = merged[^1].Labels.AddRange(next.Labels),
            });

    // H-15. Each clip starts where the ones before it end.
    private static Timeline Place(IEnumerable<Clip> clips) =>
        clips.Aggregate(
            new Timeline([], 0),
            (timeline, clip) => new Timeline(
                timeline.Clips.Add(new PlacedClip(timeline.Clips.Length + 1, clip, timeline.TotalSeconds)),
                timeline.TotalSeconds + clip.Length));
}
