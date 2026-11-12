using Functional;

namespace Ledger.Core;

/// <summary>What could be read from a journal, and every line that couldn't.</summary>
public sealed record Parsed(Journal Journal, ValueList<Problem> Problems)
{
    public static readonly Parsed Empty = new(Journal.Empty, []);

    /// <summary>Everything in all the parts, in order.</summary>
    public static Parsed Concat(IEnumerable<Parsed> parts)
    {
        var all = parts.ToValueList();
        return new Parsed(
            new Journal(
                all.SelectMany(p => p.Journal.Openings).ToValueList(),
                all.SelectMany(p => p.Journal.Transactions).ToValueList()),
            all.SelectMany(p => p.Problems).ToValueList());
    }
}

/// <summary>Turns journal text into openings and transactions (L-1, L-2). Checks only the syntax.</summary>
public static class Parser
{
    private sealed record SourceLine(int Number, string Text);

    /// <summary>A line that starts at column 0 and the indented lines under it.</summary>
    private sealed record Block(SourceLine Head, ValueList<SourceLine> Body);

    public static Parsed Parse(string text)
    {
        var lines = text.Split('\n')
            .Select((line, index) => new SourceLine(index + 1, line.TrimEnd('\r')))
            .Where(line => !IsBlankOrComment(line.Text))
            .ToValueList();

        // Each line belongs to the nearest unindented line at or above it. Lines before the first one belong to none.
        var heads = lines.Scan(Option.None<SourceLine>(), (head, line) => IsIndented(line.Text) ? head : Option.Some(line));
        var groups = lines.Zip(heads, (line, head) => (Line: line, Head: head))
            .GroupBy(x => x.Head, x => x.Line)
            .ToValueList();

        var orphans = new Parsed(Journal.Empty, groups
            .Where(g => !g.Key.IsSome)
            .SelectMany(g => g.Select(line => new Problem(line.Number, "posting is not inside a transaction")))
            .ToValueList());

        // A head's group starts with the head itself.
        var blocks = groups.SelectMany(g => g.Key.Map(head => new Block(head, g.Skip(1).ToValueList())).ToValueList());

        return Parsed.Concat(blocks.Select(ParseBlock).Prepend(orphans));
    }

    private static bool IsBlankOrComment(string line) =>
        string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith(';');

    private static bool IsIndented(string line) => char.IsWhiteSpace(line[0]);

    private static string[] Words(string text) => text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    private static Parsed ParseBlock(Block block) =>
        Words(block.Head.Text)[0] == "open" ? ParseOpening(block) : ParseTransaction(block);

    private static Parsed ParseOpening(Block block)
    {
        var words = Words(block.Head.Text);
        var opening = words.Length == 3
            ? Text.ParseDate(words[1]).Map(date => new Opening(block.Head.Number, date, words[2]))
            : new Error("open", "expected 'open <date> <account>'");
        var stray = block.Body.Select(line => new Problem(line.Number, "an 'open' line can't have postings under it"));

        return new Parsed(
            new Journal(opening.ToOption().ToValueList(), []),
            [.. At(block.Head.Number, opening).ErrorsOrEmpty, .. stray]);
    }

    private static Parsed ParseTransaction(Block block)
    {
        var header = At(block.Head.Number, ParseHeader(block.Head.Text));
        var postings = block.Body.Traverse(line => At(line.Number, ParsePosting(line)));
        var transaction = Validation.Combine(header, postings,
            (h, ps) => new Transaction(block.Head.Number, h.Date, h.Description, ps));

        return new Parsed(
            new Journal([], transaction.Match<ValueList<Transaction>>(t => [t], _ => [])),
            transaction.ErrorsOrEmpty);
    }

    private static Result<(DateOnly Date, string Description)> ParseHeader(string text)
    {
        var words = Words(text);
        var description = text.Trim()[words[0].Length..].Trim();
        return Text.ParseDate(words[0]).Bind(date => description.Length > 0
            ? Result.Ok((date, description))
            : Result.Fail<(DateOnly, string)>("description", $"transaction on {Text.FormatDate(date)} has no description"));
    }

    /// <summary>`account` or `account  amount`.</summary>
    private static Result<Posting> ParsePosting(SourceLine line)
    {
        var words = Words(line.Text);
        return words.Length switch
        {
            1 => new Posting(line.Number, words[0], Option.None<decimal>()),
            2 => Text.ParseAmount(words[1]).Map(amount => new Posting(line.Number, words[0], Option.Some(amount))),
            _ => new Error("posting", "expected '<account>  <amount>' or just '<account>'"),
        };
    }

    /// <summary>The value, or its error as a problem on the given line.</summary>
    private static Validation<Problem, T> At<T>(int line, Result<T> result) =>
        result.Match(Validation.Valid<Problem, T>, error => Validation.Invalid<Problem, T>(new Problem(line, error.Message)));
}
