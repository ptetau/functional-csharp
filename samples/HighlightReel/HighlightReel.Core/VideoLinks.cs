using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Functional;

namespace HighlightReel.Core;

/// <summary>A video named by a link or ID, and the time the link points at, if any.</summary>
public sealed record VideoRef(string Id, Option<int> Time);

/// <summary>Reading YouTube links and video IDs (H-1, H-2, H-4).</summary>
public static class VideoLinks
{
    private static readonly Regex IdPattern = new("^[A-Za-z0-9_-]{11}$");

    // scheme? (www. | m.)? host path? query? fragment?
    private static readonly Regex LinkPattern = new(
        "^(?:https?://)?(?:www\\.|m\\.)?(?<host>youtube\\.com|youtu\\.be)(?<path>/[^?#]*)?(?:\\?(?<query>[^#]*))?(?:#.*)?$",
        RegexOptions.IgnoreCase);

    /// <summary>Checks that text is an 11-character video ID.</summary>
    public static Result<string> ParseId(string text) =>
        IdPattern.IsMatch(text)
            ? text
            : new Error("bad-video-id", text.Length == 11
                ? $"'{text}' is not a valid video ID (only letters, digits, '-' and '_' are allowed)"
                : $"'{text}' is not a valid video ID (expected 11 characters)");

    /// <summary>Reads a bare video ID or any of the YouTube link forms in H-1, with the time from its t= or start= parameter.</summary>
    public static Result<VideoRef> Parse(string text) =>
        IdPattern.IsMatch(text.Trim())
            ? new VideoRef(text.Trim(), Option.None<int>())
            : LinkPattern.Match(text.Trim()) is { Success: true } link
                ? FromLink(text, link.Groups["host"].Value.ToLowerInvariant(), Segments(link.Groups["path"].Value), Query(link.Groups["query"].Value))
                : new Error("bad-link", $"'{text}' is not a YouTube video link");

    private static Result<VideoRef> FromLink(string text, string host, ImmutableArray<string> path, ImmutableDictionary<string, string> query) =>
        from id in IdFrom(text, host, path, query)
        from time in TimeFrom(query)
        select new VideoRef(id, time);

    private static Result<string> IdFrom(string text, string host, ImmutableArray<string> path, ImmutableDictionary<string, string> query) =>
        (host, path) switch
        {
            ("youtu.be", [var id]) => ParseId(id),
            ("youtube.com", ["watch"]) => query.TryGetValue("v", out var id)
                ? ParseId(id)
                : new Error("bad-link", $"'{text}' has no v= parameter"),
            ("youtube.com", ["shorts" or "embed", var id]) => ParseId(id),
            _ => new Error("bad-link", $"'{text}' is not a YouTube video link"),
        };

    private static Result<Option<int>> TimeFrom(ImmutableDictionary<string, string> query) =>
        query.TryGetValue("t", out var t) || query.TryGetValue("start", out t)
            ? Times.Parse(t).Map(seconds => Option.Some(seconds))
            : Option.None<int>();

    private static ImmutableArray<string> Segments(string path) =>
        [.. path.Split('/', StringSplitOptions.RemoveEmptyEntries)];

    // Later duplicates are ignored, like a browser's URLSearchParams.get.
    private static ImmutableDictionary<string, string> Query(string query) =>
        query
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .Select(parts => (Key: Uri.UnescapeDataString(parts[0]), Value: parts.Length > 1 ? Uri.UnescapeDataString(parts[1].Replace('+', ' ')) : ""))
            .Aggregate(
                ImmutableDictionary<string, string>.Empty,
                (found, pair) => found.ContainsKey(pair.Key) ? found : found.Add(pair.Key, pair.Value));
}
