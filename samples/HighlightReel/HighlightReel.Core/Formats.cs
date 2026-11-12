using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using Functional;

namespace HighlightReel.Core;

public enum OutputFormat { Text, Csv, Json, Edl }

/// <summary>Writing a timeline as text, CSV, JSON or a CMX 3600 EDL. Every format ends with a newline.</summary>
public static class Formats
{
    public static Result<OutputFormat> Parse(string text) =>
        text.ToLowerInvariant() switch
        {
            "text" => OutputFormat.Text,
            "csv" => OutputFormat.Csv,
            "json" => OutputFormat.Json,
            "edl" => OutputFormat.Edl,
            _ => new Error("bad-format", $"'{text}' is not a format (use text, csv, json or edl)"),
        };

    public static string Write(Timeline timeline, OutputFormat format) =>
        format switch
        {
            OutputFormat.Text => Text(timeline),
            OutputFormat.Csv => Csv(timeline),
            OutputFormat.Json => Json(timeline),
            OutputFormat.Edl => Edl(timeline),
        };

    /// <summary>An embed link that plays just this clip.</summary>
    public static string EmbedUrl(Clip clip) =>
        Invariant($"https://www.youtube.com/embed/{clip.VideoId}?start={clip.Start}&end={clip.End}");

    private static string Labels(Clip clip) => string.Join(" / ", clip.Labels);

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

    private static string Lines(IEnumerable<string> lines) => string.Concat(lines.Select(line => line + "\n"));

    // text: <n>. <reel start>  <id>  <start>–<end>  <labels>, then the total.
    private static string Text(Timeline timeline) =>
        Lines(timeline.Clips
            .Select(placed => Invariant($"{placed.Index}. {Times.Format(placed.ReelStart)}  {placed.Clip.VideoId}  {Times.Format(placed.Clip.Start)}–{Times.Format(placed.Clip.End)}  {Labels(placed.Clip)}"))
            .Append($"Total: {Times.Format(timeline.TotalSeconds)}"));

    // csv: a header, then one row per clip.
    private static string Csv(Timeline timeline) =>
        Lines(timeline.Clips
            .Select(placed => string.Join(',',
                Invariant($"{placed.Index}"),
                CsvField(placed.Clip.VideoId),
                Invariant($"{placed.Clip.Start}"),
                Invariant($"{placed.Clip.End}"),
                Invariant($"{placed.ReelStart}"),
                CsvField(Labels(placed.Clip)),
                CsvField(EmbedUrl(placed.Clip))))
            .Prepend("index,video_id,start_seconds,end_seconds,reel_start_seconds,labels,url"));

    // RFC 4180: quote a field holding a comma, quote or line break, and double any quotes inside it.
    private static string CsvField(string value) =>
        value.IndexOfAny([',', '"', '\r', '\n']) >= 0
            ? "\"" + value.Replace("\"", "\"\"") + "\""
            : value;

    // json: the shape in the spec. Serialising records keeps this a plain function from value to text.
    internal sealed record JsonClip(int Index, string VideoId, int Start, int End, int ReelStart, ValueList<string> Labels, string Url);

    internal sealed record JsonTimeline(ValueList<JsonClip> Clips, int TotalSeconds);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        // Keep '&' in URLs and non-ASCII labels readable; the output is not embedded in HTML.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        // Same bytes on every OS, like the other formats (the default is Environment.NewLine, "\r\n" on Windows).
        NewLine = "\n",
    };

    private static string Json(Timeline timeline) =>
        JsonSerializer.Serialize(
            new JsonTimeline(
                [.. timeline.Clips.Select(placed => new JsonClip(
                    placed.Index,
                    placed.Clip.VideoId,
                    placed.Clip.Start,
                    placed.Clip.End,
                    placed.ReelStart,
                    placed.Clip.Labels,
                    EmbedUrl(placed.Clip)))],
                timeline.TotalSeconds),
            JsonOptions) + "\n";

    // edl: CMX 3600, 30 fps non-drop. The record side starts at 00:00:00:00 so it lines up with reel start (H-15).
    private static string Edl(Timeline timeline) =>
        Lines(new[] { "TITLE: HIGHLIGHT REEL", "FCM: NON-DROP FRAME" }
            .Concat(timeline.Clips.SelectMany(placed => new[]
            {
                "",
                Invariant($"{placed.Index:000}  {ReelName(placed.Clip.VideoId),-8} V     C        {Timecode(placed.Clip.Start)} {Timecode(placed.Clip.End)} {Timecode(placed.ReelStart)} {Timecode(placed.ReelStart + placed.Clip.Length)}"),
                $"* FROM CLIP NAME: {Labels(placed.Clip)}",
            })));

    private static string ReelName(string videoId) => videoId.Length > 8 ? videoId[..8] : videoId;

    // Whole seconds, so the frame count is always 00.
    private static string Timecode(int seconds) =>
        Invariant($"{seconds / 3600:00}:{seconds / 60 % 60:00}:{seconds % 60:00}:00");
}
