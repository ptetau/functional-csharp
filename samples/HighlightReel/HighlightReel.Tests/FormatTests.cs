using System.Text.Json;
using HighlightReel.Core;

namespace HighlightReel.Tests;

public class FormatTests
{
    private static string Timeline(string format) => Session.SpecExample.Output("timeline", "--format", format);

    [Fact]
    public void Text_format_is_one_line_per_clip_then_the_total() =>
        Assert.Equal(
            "1. 0:00:00  dQw4w9WgXcQ  0:00:38\u20130:00:55  Chorus starts / Dance move\n" +
            "2. 0:00:17  9bZkp7q19f0  0:00:00\u20130:00:08  Intro\n" +
            "3. 0:00:25  9bZkp7q19f0  0:01:03\u20130:01:13  Horse dance\n" +
            "Total: 0:00:35\n",
            Timeline("text"));

    [Fact]
    public void Csv_format_has_the_spec_header_and_embed_urls() =>
        Assert.Equal(
            "index,video_id,start_seconds,end_seconds,reel_start_seconds,labels,url\n" +
            "1,dQw4w9WgXcQ,38,55,0,Chorus starts / Dance move,https://www.youtube.com/embed/dQw4w9WgXcQ?start=38&end=55\n" +
            "2,9bZkp7q19f0,0,8,17,Intro,https://www.youtube.com/embed/9bZkp7q19f0?start=0&end=8\n" +
            "3,9bZkp7q19f0,63,73,25,Horse dance,https://www.youtube.com/embed/9bZkp7q19f0?start=63&end=73\n",
            Timeline("csv"));

    [Fact]
    public void Csv_fields_with_commas_or_quotes_are_quoted_per_RFC_4180() =>
        Assert.Equal(
            "index,video_id,start_seconds,end_seconds,reel_start_seconds,labels,url\n" +
            "1,dQw4w9WgXcQ,5,16,0,\"Hello, \"\"world\"\" / plain\",https://www.youtube.com/embed/dQw4w9WgXcQ?start=5&end=16\n",
            Session.New
                .Then("add", "dQw4w9WgXcQ", "10", "Hello, \"world\"")
                .Then("add", "dQw4w9WgXcQ", "11", "plain")
                .Output("timeline", "--format", "csv"));

    [Fact]
    public void Csv_of_an_empty_reel_is_just_the_header() =>
        Assert.Equal(
            "index,video_id,start_seconds,end_seconds,reel_start_seconds,labels,url\n",
            Session.New.Output("timeline", "--format", "csv"));

    [Fact]
    public void Json_format_matches_the_spec_shape_exactly() =>
        Assert.Equal(
            """
            {
              "clips": [
                {
                  "index": 1,
                  "videoId": "dQw4w9WgXcQ",
                  "start": 38,
                  "end": 55,
                  "reelStart": 0,
                  "labels": [
                    "Chorus starts",
                    "Dance move"
                  ],
                  "url": "https://www.youtube.com/embed/dQw4w9WgXcQ?start=38&end=55"
                },
                {
                  "index": 2,
                  "videoId": "9bZkp7q19f0",
                  "start": 0,
                  "end": 8,
                  "reelStart": 17,
                  "labels": [
                    "Intro"
                  ],
                  "url": "https://www.youtube.com/embed/9bZkp7q19f0?start=0&end=8"
                },
                {
                  "index": 3,
                  "videoId": "9bZkp7q19f0",
                  "start": 63,
                  "end": 73,
                  "reelStart": 25,
                  "labels": [
                    "Horse dance"
                  ],
                  "url": "https://www.youtube.com/embed/9bZkp7q19f0?start=63&end=73"
                }
              ],
              "totalSeconds": 35
            }

            """.ReplaceLineEndings("\n"),
            Timeline("json"));

    [Fact]
    public void Json_escapes_quotes_and_keeps_non_ascii_labels_readable()
    {
        var json = Session.New.Then("add", "dQw4w9WgXcQ", "10", "She said \"Café\" \\o/").Output("timeline", "--format", "json");
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("She said \"Café\" \\o/", doc.RootElement.GetProperty("clips")[0].GetProperty("labels")[0].GetString());
        Assert.Contains("Café", json);
    }

    [Fact]
    public void Json_of_an_empty_reel_has_no_clips() =>
        Assert.Equal("{\n  \"clips\": [],\n  \"totalSeconds\": 0\n}\n", Session.New.Output("timeline", "--format", "json"));

    [Fact]
    public void Edl_format_is_cmx_3600_at_30_fps_non_drop() =>
        Assert.Equal(
            "TITLE: HIGHLIGHT REEL\n" +
            "FCM: NON-DROP FRAME\n" +
            "\n" +
            "001  dQw4w9Wg V     C        00:00:38:00 00:00:55:00 00:00:00:00 00:00:17:00\n" +
            "* FROM CLIP NAME: Chorus starts / Dance move\n" +
            "\n" +
            "002  9bZkp7q1 V     C        00:00:00:00 00:00:08:00 00:00:17:00 00:00:25:00\n" +
            "* FROM CLIP NAME: Intro\n" +
            "\n" +
            "003  9bZkp7q1 V     C        00:01:03:00 00:01:13:00 00:00:25:00 00:00:35:00\n" +
            "* FROM CLIP NAME: Horse dance\n",
            Timeline("edl"));

    [Fact]
    public void Edl_timecodes_carry_hours() =>
        Assert.Contains(
            "001  dQw4w9Wg V     C        01:01:30:00 01:01:40:00 00:00:00:00 00:00:10:00\n",
            Session.New.Then("add", "dQw4w9WgXcQ", "1:01:35", "late").Output("timeline", "--format", "edl"));

    [Fact]
    public void Format_names_ignore_case() =>
        Assert.Equal(Timeline("csv"), Timeline("CSV"));

    [Fact]
    public void Embed_url_plays_just_the_clip() =>
        Assert.Equal(
            "https://www.youtube.com/embed/dQw4w9WgXcQ?start=38&end=55",
            Formats.EmbedUrl(new Clip("dQw4w9WgXcQ", 38, 55, ["x"])));
}
