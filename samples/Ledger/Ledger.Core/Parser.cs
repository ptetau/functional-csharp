using System.Collections.Immutable;
using Functional;

namespace Ledger.Core;

/// <summary>What could be read from a journal, and every line that couldn't.</summary>
public sealed record Parsed(Journal Journal, ImmutableArray<Problem> Problems)
{
    public static readonly Parsed Empty = new(Journal.Empty, []);

    public Parsed Concat(Parsed other) => new(
        new Journal(Journal.Openings.AddRange(other.Journal.Openings), Journal.Transactions.AddRange(other.Journal.Transactions)),
        Problems.AddRange(other.Problems));
}

/// <summary>Turns journal text into openings and transactions (L-1, L-2). Checks only the syntax.</summary>
public static class Parser
{
    private sealed record SourceLine(int Number, string Text);

    /// <summary>A line that starts at column 0 and the indented lines under it.</summary>
    private sealed record Block(SourceLine Head, ImmutableArray<SourceLine> Body);

    private sealed record Grouping(ImmutableList<Block> Blocks, ImmutableArray<SourceLine> Orphans);

    public static Parsed Parse(string text)
    {
        var lines = text.Split('\n')
            .Select((line, index) => new SourceLine(index + 1, line.TrimEnd('\r')))
            .Where(line => !IsBlankOrComment(line.Text));

        var grouped = lines.Aggregate(new Grouping([], []), Group);

        var orphans = new Parsed(Journal.Empty,
            [.. grouped.Orphans.Select(line => new Problem(line.Number, "posting is not inside a transaction"))]);

        return grouped.Blocks.Select(ParseBlock).Aggregate(orphans, (all, next) => all.Concat(next));
    }

    private static bool IsBlankOrComment(string line) =>
        string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith(';');

    private static bool IsIndented(string line) => char.IsWhiteSpace(line[0]);

    private static Grouping Group(Grouping acc, SourceLine line) =>
        !IsIndented(line.Text) ? acc with { Blocks = acc.Blocks.Add(new Block(line, [])) }
        : acc.Blocks.IsEmpty ? acc with { Orphans = acc.Orphans.Add(line) }
        : acc with { Blocks = acc.Blocks.SetItem(acc.Blocks.Count - 1, acc.Blocks[^1] with { Body = acc.Blocks[^1].Body.Add(line) }) };

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
            new Journal(opening.Match(o => ImmutableArray.Create(o), _ => []), []),
            [.. ProblemsOf(opening, block.Head.Number), .. stray]);
    }

    private static Parsed ParseTransaction(Block block)
    {
        var header = ParseHeader(block.Head.Text);
        var postings = block.Body.Select(ParsePosting).ToImmutableArray();

        var transaction =
            from h in header
            from ps in postings.All()
            select new Transaction(block.Head.Number, h.Date, h.Description, ps);

        var problems = ProblemsOf(header, block.Head.Number)
            .AddRange(postings.SelectMany((posting, i) => ProblemsOf(posting, block.Body[i].Number)));

        return new Parsed(new Journal([], transaction.Match(t => ImmutableArray.Create(t), _ => [])), problems);
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

    private static ImmutableArray<Problem> ProblemsOf<T>(Result<T> result, int line) =>
        result.Match(_ => ImmutableArray<Problem>.Empty, error => [new Problem(line, error.Message)]);
}
