using System.Collections.Immutable;
using System.Globalization;
using Functional;

namespace HighlightReel.Core;

/// <summary>What a command did: the reel afterwards, whether it changed (so needs saving), and what to print.</summary>
public sealed record Outcome(Reel Reel, bool Changed, string Output);

/// <summary>The timeline command's options (H-9).</summary>
public sealed record TimelineOptions(Padding Padding, OutputFormat Format)
{
    public static readonly TimelineOptions Default = new(Padding.Default, OutputFormat.Text);
}

/// <summary>The commands (H-5 to H-10), each a function from a reel and its arguments to an outcome or an error.</summary>
public static class Commands
{
    public const string Usage =
        """
        usage: highlights [--reel <path>] <command>

          add <url> [<time>] <label>   add a milestone (time from the argument, else the link's t=)
          duration <url-or-id> <time>  record how long a video is
          list                         list milestones, numbered, grouped by video
          links                        print a link to each milestone
          timeline [--before <s>] [--after <s>] [--format text|csv|json|edl]
                                       print the pre-cut timeline
          remove <n>                   remove milestone n, as numbered by list

        Times: 95, 1:35, 1:01:35, 1h1m35s, 1m35s or 95s. The reel defaults to reel.txt.

        """;

    public static Result<Outcome> Run(Reel reel, ImmutableArray<string> args) =>
        args switch
        {
            [] or ["help" or "--help" or "-h"] => Unchanged(reel, Usage),
            ["add", .. var rest] => Add(reel, rest),
            ["duration", .. var rest] => Duration(reel, rest),
            ["list"] => Unchanged(reel, List(reel)),
            ["links"] => Unchanged(reel, Links(reel)),
            ["timeline", .. var rest] => TimelineCommand(reel, rest),
            ["remove", .. var rest] => Remove(reel, rest),
            ["list" or "links", ..] => UsageError($"{args[0]} takes no arguments"),
            [var other, ..] => new Error("usage", $"'{other}' is not a command (try: add, duration, list, links, timeline, remove)"),
        };

    // H-5
    private static Result<Outcome> Add(Reel reel, ImmutableArray<string> args) =>
        args switch
        {
            [var link, var label] => AddAt(reel, link, Option.None<string>(), label),
            [var link, var time, var label] => AddAt(reel, link, Option.Some(time), label),
            _ => UsageError("usage: highlights add <url> [<time>] <label>"),
        };

    private static Result<Outcome> AddAt(Reel reel, string link, Option<string> time, string label) =>
        from video in VideoLinks.Parse(link)
        from seconds in time
            .Match(Times.Parse, () => video.Time.OrError(new Error("no-time", "no time given and the link has no t= parameter")))
            .Bind(at => NotPastEnd(reel, video.Id, at))
        from text in Label(label)
        let updated = Reels.AddMilestone(reel, video.Id, seconds, text)
        select new Outcome(updated, true, $"Added {Describe(updated.Milestones[^1])}\n");

    // H-6
    private static Result<Outcome> Duration(Reel reel, ImmutableArray<string> args) =>
        args switch
        {
            [var video, var time] =>
                from id in VideoLinks.Parse(video).Map(found => found.Id)
                from seconds in Times.Parse(time)
                    .Ensure(s => s > 0, _ => new Error("bad-duration", "a video's duration must be more than 0 seconds"))
                    .Bind(s => NothingPast(reel, id, s))
                select new Outcome(Reels.SetDuration(reel, id, seconds), true, $"{id} is {Times.Format(seconds)} long\n"),
            _ => UsageError("usage: highlights duration <url-or-id> <time>"),
        };

    // H-7
    private static string List(Reel reel) =>
        Lines(Reels.InListOrder(reel).Select((m, i) => Invariant($"{i + 1}. {Describe(m)}")));

    // H-8
    private static string Links(Reel reel) =>
        Lines(Reels.InListOrder(reel).Select(m => Invariant($"https://youtu.be/{m.VideoId}?t={m.Seconds}")));

    // H-9
    private static Result<Outcome> TimelineCommand(Reel reel, ImmutableArray<string> args) =>
        TimelineOptionsFrom(args, TimelineOptions.Default)
            .Map(options => Unchanged(reel, Formats.Write(Timelines.Build(reel, options.Padding), options.Format)));

    private static Result<TimelineOptions> TimelineOptionsFrom(ImmutableArray<string> args, TimelineOptions options) =>
        args switch
        {
            [] => options,
            ["--before", var value, .. var rest] =>
                Times.Parse(value).Bind(s => TimelineOptionsFrom(rest, options with { Padding = options.Padding with { Before = s } })),
            ["--after", var value, .. var rest] =>
                Times.Parse(value).Bind(s => TimelineOptionsFrom(rest, options with { Padding = options.Padding with { After = s } })),
            ["--format", var value, .. var rest] =>
                Formats.Parse(value).Bind(format => TimelineOptionsFrom(rest, options with { Format = format })),
            ["--before" or "--after" or "--format"] => UsageError($"{args[0]} needs a value"),
            [var other, ..] => UsageError($"'{other}' is not a timeline option (use --before, --after or --format)"),
        };

    // H-10
    private static Result<Outcome> Remove(Reel reel, ImmutableArray<string> args) =>
        args switch
        {
            [var number] =>
                from n in MilestoneNumber(number, Reels.InListOrder(reel).Length)
                let milestone = Reels.InListOrder(reel)[n - 1]
                select new Outcome(Reels.Remove(reel, milestone), true, Invariant($"Removed {n}. {Describe(milestone)}\n")),
            _ => UsageError("usage: highlights remove <n>"),
        };

    private static Result<int> MilestoneNumber(string text, int count) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var n)
            ? n >= 1 && n <= count
                ? n
                : new Error("no-milestone", Invariant($"there is no milestone {n} (the reel has {count}; see 'list')"))
            : new Error("usage", $"'{text}' is not a milestone number");

    private static Result<string> Label(string label) =>
        label.Trim() switch
        {
            "" => new Error("bad-label", "the label is empty"),
            var text when text.Any(char.IsControl) => new Error("bad-label", "a label can't contain line breaks or tabs"),
            var text => text,
        };

    // A milestone can't be later than a video's known end (H-12 would give it an empty clip).
    private static Result<int> NotPastEnd(Reel reel, string videoId, int seconds) =>
        Reels.DurationOf(reel, videoId).Match<Result<int>>(
            length => seconds <= length
                ? seconds
                : new Error("past-end", $"{Times.Format(seconds)} is past the end of {videoId} ({Times.Format(length)})"),
            () => seconds);

    private static Result<int> NothingPast(Reel reel, string videoId, int length) =>
        reel.Milestones
            .Where(m => m.VideoId == videoId && m.Seconds > length)
            .OrderBy(m => m.Seconds)
            .Select(m => Result.Fail<int>("past-end", $"'{m.Label}' at {Times.Format(m.Seconds)} is past {Times.Format(length)}; remove it first"))
            .DefaultIfEmpty(length)
            .First();

    private static string Describe(Milestone m) => $"{m.VideoId}  {Times.Format(m.Seconds)}  {m.Label}";

    private static Outcome Unchanged(Reel reel, string output) => new(reel, false, output);

    private static Error UsageError(string message) => new("usage", message);

    private static string Lines(IEnumerable<string> lines) => string.Concat(lines.Select(line => line + "\n"));

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
