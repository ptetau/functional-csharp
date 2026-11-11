using System.Collections.Immutable;
using System.Globalization;
using Functional;

namespace HighlightReel.Core;

/// <summary>
/// The reel file: UTF-8 text, one entry per line, fields separated by single spaces.
/// Blank lines and lines starting with '#' are ignored.
/// <code>
/// duration  &lt;video-id&gt; &lt;seconds&gt;
/// milestone &lt;order&gt; &lt;video-id&gt; &lt;seconds&gt; &lt;label, the rest of the line&gt;
/// </code>
/// Order is the order the milestone was added in (unique, from 1). Writing puts durations first (by video ID),
/// then milestones by order, so a reel always writes the same way.
/// </summary>
public static class ReelFile
{
    private static readonly ImmutableArray<string> Header =
    [
        "# HighlightReel reel, version 1. One entry per line:",
        "#   duration <video-id> <seconds>",
        "#   milestone <order> <video-id> <seconds> <label>",
    ];

    public static Result<Reel> Parse(string text) =>
        text
            .Split('\n')
            .Select((line, index) => (Number: index + 1, Text: line.TrimEnd('\r')))
            .Where(line => line.Text.Trim().Length > 0 && !line.Text.TrimStart().StartsWith('#'))
            .Select(line => Errors.Prefix(ParseLine(line.Text), $"line {line.Number}: "))
            .All()
            .Map(entries => entries.Aggregate(Reel.Empty, (reel, apply) => apply(reel)))
            .Ensure(
                reel => reel.Milestones.Select(m => m.Order).Distinct().Count() == reel.Milestones.Length,
                _ => new Error("bad-reel", "two milestones have the same order number"));

    public static string Write(Reel reel) =>
        string.Join('\n', Header
            .Concat(reel.Durations
                .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .Select(entry => Line($"duration {entry.Key} {entry.Value}")))
            .Concat(reel.Milestones
                .OrderBy(m => m.Order)
                .Select(m => Line($"milestone {m.Order} {m.VideoId} {m.Seconds} {m.Label}"))))
        + "\n";

    private static string Line(FormattableString line) => line.ToString(CultureInfo.InvariantCulture);

    private static Result<Func<Reel, Reel>> ParseLine(string line) =>
        line.Split(' ', 5) switch
        {
            ["duration", var id, var seconds] =>
                from video in VideoLinks.ParseId(id)
                from length in Count(seconds)
                select (Func<Reel, Reel>)(reel => Reels.SetDuration(reel, video, length)),
            ["milestone", var order, var id, var seconds, var label] when label.Length > 0 =>
                from number in Count(order)
                from video in VideoLinks.ParseId(id)
                from at in Count(seconds)
                select (Func<Reel, Reel>)(reel => reel with { Milestones = reel.Milestones.Add(new Milestone(video, at, label, number)) }),
            _ => new Error("bad-reel", $"can't read '{line}' (expected 'duration <id> <seconds>' or 'milestone <order> <id> <seconds> <label>')"),
        };

    private static Result<int> Count(string text) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? value
            : new Error("bad-reel", $"'{text}' is not a whole number");
}
