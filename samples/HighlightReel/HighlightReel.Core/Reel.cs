using System.Collections.Immutable;
using Functional;

namespace HighlightReel.Core;

/// <summary>A moment worth including. Order is the order it was added in; it never changes.</summary>
public sealed record Milestone(string VideoId, int Seconds, string Label, int Order);

/// <summary>A project: the known video durations and the milestones.</summary>
public sealed record Reel(ImmutableDictionary<string, int> Durations, ImmutableArray<Milestone> Milestones)
{
    public static readonly Reel Empty = new(ImmutableDictionary<string, int>.Empty, []);
}

/// <summary>Reading and changing a reel.</summary>
public static class Reels
{
    public static Option<int> DurationOf(Reel reel, string videoId) =>
        reel.Durations.TryGetValue(videoId, out var seconds) ? Option.Some(seconds) : Option.None<int>();

    /// <summary>Videos in the order they were first used: by the earliest milestone still in the reel (H-14).</summary>
    public static ImmutableArray<string> VideosInFirstUseOrder(Reel reel) =>
        [.. reel.Milestones
            .GroupBy(m => m.VideoId)
            .OrderBy(group => group.Min(m => m.Order))
            .Select(group => group.Key)];

    /// <summary>Milestones grouped by video and in time order within each: the numbering 'list' and 'remove' share (H-7, H-10).</summary>
    public static ImmutableArray<Milestone> InListOrder(Reel reel) =>
        [.. VideosInFirstUseOrder(reel)
            .SelectMany(video => reel.Milestones
                .Where(m => m.VideoId == video)
                .OrderBy(m => m.Seconds)
                .ThenBy(m => m.Order))];

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
        reel with { Durations = reel.Durations.SetItem(videoId, seconds) };

    public static Reel Remove(Reel reel, Milestone milestone) =>
        reel with { Milestones = reel.Milestones.Remove(milestone) };
}
