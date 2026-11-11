using System.Collections.Immutable;
using Functional;

namespace Ledger.Core;

/// <summary>Text to append to a file.</summary>
public sealed record Append(string Path, string Text);

/// <summary>Everything the shell has to do once a command has run (L-15).</summary>
public sealed record Outcome(string Stdout, string Stderr, int ExitCode, Option<Append> Append)
{
    public const int Success = 0;
    public const int Invalid = 1;
    public const int UsageError = 2;

    public static Outcome Printed(string stdout) => new(stdout, "", Success, Option.None<Append>());
    public static Outcome Failed(string stderr) => new("", stderr, Invalid, Option.None<Append>());
}

/// <summary>Runs a command against the journal's text. The shell reads the file and does what the Outcome says.</summary>
public static class App
{
    public static Outcome Usage(Error error) =>
        new("", $"error: {error.Message}\n{CommandLine.Usage}", Outcome.UsageError, Option.None<Append>());

    /// <summary>`journal` is the file's text, or None when the file doesn't exist.</summary>
    public static Outcome Execute(Invocation invocation, Option<string> journal) => invocation.Command.Match(
        check: _ => WithBook(invocation, journal, book =>
            Outcome.Printed($"OK: {book.TransactionCount} transactions, {book.Accounts.Count} accounts\n")),
        balance: b => WithBook(invocation, journal, book => Outcome.Printed(Reports.Balance(book, b.AsOf, b.Prefix))),
        register: r => WithBook(invocation, journal, book => Outcome.Printed(Reports.Register(book, r.Prefix))),
        add: a => Append_(invocation.File, journal.ValueOr(""), Render(a)),
        open: o => Append_(invocation.File, journal.ValueOr(""), $"open {o.Date} {o.Account}\n"));

    /// <summary>Loads the journal and runs `run` on it, or reports why it can't.</summary>
    private static Outcome WithBook(Invocation invocation, Option<string> journal, Func<Book, Outcome> run) =>
        journal.Match(
            text =>
            {
                var validated = Journals.Load(text);
                return validated.IsValid ? run(validated.Book) : Outcome.Failed(Lines(validated.Problems));
            },
            () => Outcome.Failed($"error: journal '{invocation.File}' not found\n"));

    /// <summary>
    /// L-13, L-14: appends `addition` only if the journal is valid before and after.
    /// Problems in the existing journal keep their line numbers; problems in the new text are reported as "error:".
    /// </summary>
    private static Outcome Append_(string file, string existing, string addition)
    {
        var before = Journals.Load(existing);
        var appended = Separator(existing, addition) + addition;
        var after = Journals.Load(existing + appended);
        var firstNewLine = LineCount(existing) + 1;

        return !before.IsValid ? Outcome.Failed(Lines(before.Problems))
            : !after.IsValid ? Outcome.Failed(string.Concat(after.Problems.Select(p =>
                $"{(p.Line >= firstNewLine ? "error" : $"line {p.Line}")}: {p.Message}\n")))
            : new Outcome("", "", Outcome.Success, Option.Some(new Append(file, appended)));
    }

    private static int LineCount(string text) =>
        text.Count(c => c == '\n') + (text.Length > 0 && !text.EndsWith('\n') ? 1 : 0);

    /// <summary>New text starts on its own line, after a blank line unless it's an `open` following another `open`.</summary>
    private static string Separator(string existing, string addition)
    {
        var lastLine = existing.TrimEnd('\n', '\r').Split('\n')[^1];
        var newLine = existing.EndsWith('\n') ? "" : "\n";
        var openAfterOpen = addition.StartsWith("open ", StringComparison.Ordinal) && lastLine.StartsWith("open ", StringComparison.Ordinal);
        return existing.Length == 0 ? "" : openAfterOpen ? newLine : newLine + "\n";
    }

    private static string Render(Command.Add add) =>
        $"{add.Date} {add.Description.ReplaceLineEndings(" ")}\n" +
        string.Concat(add.Postings.Select(p => $"  {p.Account}{p.Amount.Match(amount => "  " + amount, () => "")}\n"));

    private static string Lines(ImmutableArray<Problem> problems) =>
        string.Concat(problems.OrderBy(p => p.Line).Select(p => $"line {p.Line}: {p.Message}\n"));
}

/// <summary>Reading a journal: parse it, then check it.</summary>
public static class Journals
{
    public static Validated Load(string text)
    {
        var parsed = Parser.Parse(text);
        var validated = Validator.Validate(parsed.Journal);
        return validated with { Problems = parsed.Problems.AddRange(validated.Problems) };
    }
}
