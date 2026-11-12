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

    /// <summary>One line of the file.</summary>
    private closed record Entry
    {
        public sealed record Duration(VideoDuration Value) : Entry;

        public sealed record Moment(Milestone Value) : Entry;
    }

    public static Result<Reel> Parse(string text) =>
        text
            .Split('\n')
            .Select((line, index) => (Number: index + 1, Text: line.TrimEnd('\r')))
            .Where(line => line.Text.Trim().Length > 0 && !line.Text.TrimStart().StartsWith('#'))
            .Traverse(line => ParseLine(line.Text).MapError(e => e with { Message = $"line {line.Number}: {e.Message}" }))
            .Map(entries => new Reel(
                Reels.Durations(entries.OfType<Entry.Duration>().Select(entry => entry.Value)),
                entries.OfType<Entry.Moment>().Select(entry => entry.Value).ToValueList()))
            .Ensure(
                reel => reel.Milestones.Select(m => m.Order).Distinct().Count() == reel.Milestones.Length,
                _ => new Error("bad-reel", "two milestones have the same order number"));

    public static string Write(Reel reel) =>
        string.Join('\n', Header
            .Concat(reel.Durations
                .OrderBy(d => d.VideoId, StringComparer.Ordinal)
                .Select(d => Line($"duration {d.VideoId} {d.Seconds}")))
            .Concat(reel.Milestones
                .OrderBy(m => m.Order)
                .Select(m => Line($"milestone {m.Order} {m.VideoId} {m.Seconds} {m.Label}"))))
        + "\n";

    private static string Line(FormattableString line) => line.ToString(CultureInfo.InvariantCulture);

    private static Result<Entry> ParseLine(string line) =>
        line.Split(' ', 5) switch
        {
            ["duration", var id, var seconds] =>
                from video in VideoLinks.ParseId(id)
                from length in Count(seconds)
                select (Entry)new Entry.Duration(new VideoDuration(video, length)),
            ["milestone", var order, var id, var seconds, var label] when label.Length > 0 =>
                from number in Count(order)
                from video in VideoLinks.ParseId(id)
                from at in Count(seconds)
                select (Entry)new Entry.Moment(new Milestone(video, at, label, number)),
            _ => new Error("bad-reel", $"can't read '{line}' (expected 'duration <id> <seconds>' or 'milestone <order> <id> <seconds> <label>')"),
        };

    private static Result<int> Count(string text) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? value
            : new Error("bad-reel", $"'{text}' is not a whole number");
}
