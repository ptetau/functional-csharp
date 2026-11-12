using Functional;
using Ledger.Core;

namespace Ledger.Tests;

/// <summary>Runs the core the way the shell does, without touching the disk.</summary>
public static class Harness
{
    /// <summary>The journal from the spec, exactly as shown.</summary>
    public const string SpecJournal =
        "; Lines starting with ';' are comments. Blank lines are ignored.\n" +
        "open 2026-01-01 Assets:Bank:Everyday\n" +
        "open 2026-01-01 Income:Salary\n" +
        "open 2026-01-01 Expenses:Groceries\n" +
        "\n" +
        "2026-01-05 Countdown\n" +
        "  Expenses:Groceries     82.40\n" +
        "  Assets:Bank:Everyday\n" +
        "\n" +
        "2026-01-15 Pay\n" +
        "  Assets:Bank:Everyday   3200.00\n" +
        "  Income:Salary         -3200.00\n";

    /// <summary>The spec's unbalanced transaction, appended after a blank line.</summary>
    public const string TypoJournal = SpecJournal +
        "\n" +
        "2026-02-01 Typo\n" +
        "  Expenses:Groceries   10.00\n" +
        "  Assets:Bank:Everyday -1.00\n";

    public static Outcome Run(string? journal, params string[] args) =>
        CommandLine.Parse(args).Match(
            invocation => App.Execute(invocation, journal is null ? Option.None<string>() : Option.Some(journal)),
            App.Usage);

    /// <summary>The text that would be appended to the journal, or null if nothing would be.</summary>
    public static string? Appended(this Outcome outcome) => outcome.Append.Match<string?>(a => a.Text, () => null);

    /// <summary>Opens the given accounts on 2026-01-01, then adds the body.</summary>
    public static string With(string body, params string[] accounts) =>
        string.Concat(accounts.Select(a => $"open 2026-01-01 {a}\n")) + "\n" + body;
}
