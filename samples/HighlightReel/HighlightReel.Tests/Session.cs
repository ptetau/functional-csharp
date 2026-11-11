using Functional;
using HighlightReel.Core;

namespace HighlightReel.Tests;

/// <summary>Runs commands the way the CLI does, keeping the reel file's text in memory between them.</summary>
public sealed record Session(Option<string> ReelText)
{
    public static readonly Session New = new(Option.None<string>());

    public static readonly string[][] SpecExampleCommands =
    [
        ["add", "https://www.youtube.com/watch?v=dQw4w9WgXcQ&t=43s", "Chorus starts"],
        ["add", "https://youtu.be/dQw4w9WgXcQ", "0:50", "Dance move"],
        ["add", "https://youtu.be/9bZkp7q19f0", "1:08", "Horse dance"],
        ["add", "youtube.com/shorts/9bZkp7q19f0", "3", "Intro"],
        ["duration", "9bZkp7q19f0", "4:12"],
    ];

    public static Session SpecExample => New.ThenAll(SpecExampleCommands);

    public Result<(Session Next, string Output)> Try(params string[] args) =>
        App.ParseArgs(args)
            .Bind(invocation => App.Execute(invocation, ReelText))
            .Map(response => (
                new Session(response.NewReelText.Match(Option.Some, () => ReelText)),
                response.Output));

    /// <summary>The output of a command that must succeed.</summary>
    public string Output(params string[] args) =>
        Try(args).Match(ok => ok.Output, error => throw new Xunit.Sdk.XunitException($"expected success, got {error}"));

    /// <summary>The session after a command that must succeed.</summary>
    public Session Then(params string[] args) =>
        Try(args).Match(ok => ok.Next, error => throw new Xunit.Sdk.XunitException($"expected success, got {error}"));

    public Session ThenAll(IEnumerable<string[]> commands) => commands.Aggregate(this, (session, args) => session.Then(args));

    /// <summary>The error message of a command that must fail.</summary>
    public string Error(params string[] args) =>
        Try(args).Match(ok => throw new Xunit.Sdk.XunitException($"expected an error, got output: {ok.Output}"), error => error.Message);

    public string Text => ReelText.ValueOr("");
}
