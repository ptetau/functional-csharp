using Functional;
using HighlightReel.Core;
using Samples.TestSupport;
using Xunit;

namespace HighlightReel.Tests;

/// <summary>Any reel file or command line gives a result or an error message. Nothing throws.</summary>
public class FuzzTests
{
    private static readonly string[][] Reads =
        [["list"], ["urls"], ["timeline"], ["timeline", "--format", "json"], ["timeline", "--format", "csv"], ["timeline", "--format", "edl"]];

    [Fact]
    public void Every_command_handles_a_mangled_reel_file() =>
        Fuzz.NeverThrows(Fuzz.Around(Session.SpecExample.Text, 1500, seed: 3), text =>
        {
            var session = new Session(Option.Some(text));
            foreach (var args in Reads) session.Try(args);
            session.Try("remove", "2");
            session.Try("add", "https://youtu.be/dQw4w9WgXcQ", "0:50", "Dance move");
        });

    [Fact]
    public void Commands_handle_mangled_arguments() =>
        Fuzz.NeverThrows(Session.SpecExampleCommands.SelectMany((args, i) => Fuzz.Around(string.Join("|", args), 300, seed: 10 + i)),
            args => Session.SpecExample.Try(args.Split('|')));

    [Fact]
    public void Times_and_links_parse_anything() =>
        Fuzz.NeverThrows(Fuzz.Around("https://www.youtube.com/watch?v=dQw4w9WgXcQ&t=1h2m3s", 1500, seed: 4)
                .Concat(Fuzz.Around("1:02:03", 500, seed: 5)),
            text => { Times.Parse(text); VideoLinks.Parse(text); VideoLinks.ParseId(text); Formats.Parse(text); });
}
