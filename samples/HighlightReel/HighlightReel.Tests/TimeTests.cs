using Functional;
using HighlightReel.Core;

namespace HighlightReel.Tests;

public class TimeTests
{
    [Theory]
    [InlineData("95", 95)]
    [InlineData("0", 0)]
    [InlineData("1:35", 95)]
    [InlineData("0:50", 50)]
    [InlineData("75:00", 4500)]
    [InlineData("1:01:35", 3695)]
    [InlineData("0:00:08", 8)]
    [InlineData("1h1m35s", 3695)]
    [InlineData("1m35s", 95)]
    [InlineData("95s", 95)]
    [InlineData("1h", 3600)]
    [InlineData("2m", 120)]
    [InlineData("1h5s", 3605)]
    [InlineData("1M35S", 95)]
    [InlineData(" 1:35 ", 95)]
    public void H3_time_formats_are_read_as_seconds(string text, int seconds) =>
        Assert.Equal(Result.Ok(seconds), Times.Parse(text));

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("1:5")]
    [InlineData("1:60")]
    [InlineData("1:60:00")]
    [InlineData("1:00:5")]
    [InlineData("1:2:3:4")]
    [InlineData("-5")]
    [InlineData("1.5")]
    [InlineData("1s1m")]
    [InlineData("1h1h")]
    [InlineData("h")]
    [InlineData("1d")]
    [InlineData("9999999999")]
    public void H4_anything_else_is_an_error_naming_the_input(string text) =>
        Assert.Equal(
            $"'{text}' is not a valid time (use 95, 1:35, 1:01:35 or 1m35s)",
            Times.Parse(text).Match(v => $"parsed as {v}", e => e.Message));

    [Theory]
    [InlineData(0, "0:00:00")]
    [InlineData(95, "0:01:35")]
    [InlineData(3695, "1:01:35")]
    [InlineData(36000, "10:00:00")]
    public void Times_are_written_as_h_mm_ss(int seconds, string text) =>
        Assert.Equal(text, Times.Format(seconds));
}
