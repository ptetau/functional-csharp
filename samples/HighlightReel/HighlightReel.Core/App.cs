using System.Collections.Immutable;
using Functional;

namespace HighlightReel.Core;

/// <summary>Where the reel lives and the command to run on it.</summary>
public sealed record Invocation(string ReelPath, ValueList<string> Command);

/// <summary>What the shell should do: write the reel file (only if it changed) and print the output.</summary>
public sealed record Response(Option<string> NewReelText, string Output);

/// <summary>Everything between reading the arguments and file, and writing the file and output.</summary>
public static class App
{
    public const string DefaultReelPath = "reel.txt";

    /// <summary>Takes '--reel &lt;path&gt;' out of the arguments, wherever it appears.</summary>
    public static Result<Invocation> ParseArgs(IReadOnlyList<string> args) =>
        Split([.. args], Option.None<string>(), []);

    /// <summary>Runs a command on the reel's text (None if the file doesn't exist yet: the reel is created when it first changes).</summary>
    public static Result<Response> Execute(Invocation invocation, Option<string> reelText) =>
        from reel in reelText.Match(
            text => ReelFile.Parse(text).MapError(e => e with { Message = $"{invocation.ReelPath}: {e.Message}" }),
            () => Result.Ok(Reel.Empty))
        from outcome in Commands.Run(reel, invocation.Command)
        select new Response(
            outcome.Changed ? Option.Some(ReelFile.Write(outcome.Reel)) : Option.None<string>(),
            outcome.Output);

    private static Result<Invocation> Split(ImmutableArray<string> args, Option<string> reelPath, ImmutableArray<string> command) =>
        args switch
        {
            [] => new Invocation(reelPath.ValueOr(DefaultReelPath), command),
            ["--reel"] => new Error("usage", "--reel needs a path"),
            ["--reel", var path, .. var rest] => reelPath.IsSome
                ? new Error("usage", "--reel is given more than once")
                : Split(rest, Option.Some(path), command),
            [var arg, .. var rest] => Split(rest, reelPath, command.Add(arg)),
        };
}
