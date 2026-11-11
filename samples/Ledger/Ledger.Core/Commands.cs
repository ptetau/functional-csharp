using System.Collections.Immutable;
using Functional;

namespace Ledger.Core;

/// <summary>A posting given on the command line to `ledger add`: an account and, optionally, an amount.</summary>
public sealed record PostingArg(string Account, Option<string> Amount);

/// <summary>
/// What the user asked for. Dates and amounts stay as typed; they're checked against the journal.
/// A closed record: a switch expression over the five cases is checked for completeness.
/// </summary>
public closed record Command
{
    public sealed record Check : Command;

    public sealed record Balance(Option<DateOnly> AsOf, string Prefix) : Command;

    public sealed record Register(string Prefix) : Command;

    public sealed record Add(string Date, string Description, ImmutableArray<PostingArg> Postings) : Command;

    public sealed record Open(string Date, string Account) : Command;
}

/// <summary>A command and the journal it works on.</summary>
public sealed record Invocation(string File, Command Command);

/// <summary>Reads the command line (L-10 to L-15). Every failure here is a usage error.</summary>
public static class CommandLine
{
    public const string DefaultFile = "ledger.journal";

    public const string Usage =
        "usage: ledger <command> [--file <path>]\n" +
        "  check\n" +
        "  balance [--as-of <date>] [<account-prefix>]\n" +
        "  register <account-prefix>\n" +
        "  add <date> <description> <account> [<amount>] <account> [<amount>]...\n" +
        "  open <date> <account>\n";

    private sealed record Words(ImmutableDictionary<string, string> Options, ImmutableArray<string> Positional);

    private static readonly ImmutableArray<string> KnownOptions = ["--file", "--as-of"];

    public static Result<Invocation> Parse(IReadOnlyList<string> args) =>
        Split([.. args]).Bind(words =>
            from command in ParseCommand(words)
            select new Invocation(words.Options.GetValueOrDefault("--file", DefaultFile), command));

    /// <summary>Separates `--name value` options from the positional words, wherever they appear.</summary>
    private static Result<Words> Split(ImmutableArray<string> args) => args switch
    {
        [] => new Words(ImmutableDictionary<string, string>.Empty, []),
        [var name, var value, .. var rest] when KnownOptions.Contains(name) =>
            Split([.. rest]).Map(words => words with { Options = words.Options.SetItem(name, value) }),
        [var name, ..] when name.StartsWith("--", StringComparison.Ordinal) => KnownOptions.Contains(name)
            ? Usage_($"{name} needs a value")
            : Usage_($"unknown option {name}"),
        [var word, .. var rest] =>
            Split([.. rest]).Map(words => words with { Positional = words.Positional.Insert(0, word) }),
    };

    private static Result<Command> ParseCommand(Words words)
    {
        var asOf = words.Options.TryGetValue("--as-of", out var text)
            ? Text.ParseDate(text).Map(Option.Some)
            : Option.None<DateOnly>();

        var command = words.Positional switch
        {
            [] => Usage_<Command>("no command given"),
            ["check"] => new Command.Check(),
            ["balance"] => asOf.Map(date => (Command)new Command.Balance(date, "")),
            ["balance", var prefix] => asOf.Map(date => (Command)new Command.Balance(date, prefix)),
            ["register", var prefix] => new Command.Register(prefix),
            ["add", var date, var description, .. var postings] when postings.Length > 0 =>
                PostingArgs([.. postings]).Map(ps => (Command)new Command.Add(date, description, ps)),
            ["open", var date, var account] => new Command.Open(date, account),
            [var name, ..] when name is "check" or "balance" or "register" or "add" or "open" =>
                Usage_<Command>($"wrong arguments for '{name}'"),
            [var name, ..] => Usage_<Command>($"unknown command '{name}'"),
        };

        return words.Options.ContainsKey("--as-of") && command.Match(c => c is not Command.Balance, _ => false)
            ? Usage_<Command>("--as-of only applies to 'balance'")
            : command;
    }

    /// <summary>`account [amount] account [amount] ...`: an amount belongs to the account before it.</summary>
    private static Result<ImmutableArray<PostingArg>> PostingArgs(ImmutableArray<string> words) => words switch
    {
        [] => ImmutableArray<PostingArg>.Empty,
        [var amount, ..] when Text.IsAmount(amount) => Usage_<ImmutableArray<PostingArg>>($"amount '{amount}' must follow an account"),
        [var account, var amount, .. var rest] when Text.IsAmount(amount) =>
            PostingArgs([.. rest]).Map(ps => ps.Insert(0, new PostingArg(account, Option.Some(amount)))),
        [var account, .. var rest] =>
            PostingArgs([.. rest]).Map(ps => ps.Insert(0, new PostingArg(account, Option.None<string>()))),
    };

    private static Result<Words> Usage_(string message) => Usage_<Words>(message);

    private static Result<T> Usage_<T>(string message) => Result.Fail<T>("usage", message);
}
