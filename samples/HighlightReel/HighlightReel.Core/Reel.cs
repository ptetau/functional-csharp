using Functional;

namespace HighlightReel.Core;

/// <summary>A moment worth including. Order is the order it was added in; it never changes.</summary>
public sealed record Milestone(string VideoId, int Seconds, string Label, int Order);

/// <summary>How long a video is (H-6).</summary>
public sealed record VideoDuration(string VideoId, int Seconds);

/// <summary>
/// A project: the known video durations and the milestones. Durations hold one entry per video, in video ID
/// order (<see cref="Reels.Durations"/> keeps them that way), so two reels with the same contents are equal.
/// </summary>
public sealed record Reel(ValueList<VideoDuration> Durations, ValueList<Milestone> Milestones)
{
    public static readonly Reel Empty = new([], []);
}

/// <summary>Reading and changing a reel.</summary>
public static class Reels
{
    public static Option<int> DurationOf(Reel reel, string videoId) =>
        reel.Durations.FirstOrDefault(d => d.VideoId == videoId) is { } found
            ? Option.Some(found.Seconds)
            : Option.None<int>();

    /// <summary>One duration per video, the last one given winning, in video ID order.</summary>
    public static ValueList<VideoDuration> Durations(IEnumerable<VideoDuration> durations) =>
        [.. durations
            .Reverse()
            .DistinctBy(d => d.VideoId, StringComparer.Ordinal)
            .OrderBy(d => d.VideoId, StringComparer.Ordinal)];

    /// <summary>
    /// Milestones grouped by video, the videos in the order they were first used: by the earliest milestone
    /// still in the reel (H-14). GroupBy keeps the order in which each key first appears.
    /// </summary>
    public static IEnumerable<IGrouping<string, Milestone>> ByVideo(Reel reel) =>
        reel.Milestones
            .OrderBy(m => m.Order)
            .GroupBy(m => m.VideoId);

    /// <summary>Milestones in time order within a video; ties keep the order they were added in.</summary>
    public static IEnumerable<Milestone> InTimeOrder(IEnumerable<Milestone> milestones) =>
        milestones
            .OrderBy(m => m.Seconds)
            .ThenBy(m => m.Order);

    /// <summary>Milestones grouped by video and in time order within each: the numbering 'list' and 'remove' share (H-7, H-10).</summary>
    public static ValueList<Milestone> InListOrder(Reel reel) =>
        [.. ByVideo(reel).SelectMany(InTimeOrder)];

    public static Reel AddMilestone(Reel reel, string videoId, int seconds, string label) =>
        reel with
        {
            Milestones = reel.Milestones.Add(new Milestone(
                videoId,
                seconds,
                label,
                reel.Milestones.Select(m => m.Order).DefaultIfEmpty(0).Max() + 1)),
        };

    public static Reel SetDuration(Reel reel, string videoId, int seconds) =>
        reel with { Durations = Durations(reel.Durations.Append(new VideoDuration(videoId, seconds))) };

    public static Reel Remove(Reel reel, Milestone milestone) =>
        reel with { Milestones = reel.Milestones.Remove(milestone) };
}
