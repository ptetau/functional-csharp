using System.Collections.Immutable;
using Functional;

namespace Ledger.Core;

/// <summary>The book built from a journal, and every rule the journal breaks. The book is only meaningful when there are no problems.</summary>
public sealed record Validated(Book Book, ImmutableArray<Problem> Problems)
{
    public bool IsValid => Problems.IsEmpty;
}

/// <summary>Checks a parsed journal against the bookkeeping rules (L-3 to L-9, L-14).</summary>
public static class Validator
{
    public static Validated Validate(Journal journal)
    {
        var accounts = journal.Openings
            .GroupBy(o => o.Account)
            .ToImmutableSortedDictionary(g => g.Key, g => g.First().Date, StringComparer.Ordinal);

        var problems = OpeningProblems(journal.Openings)
            .AddRange(journal.Transactions.SelectMany(t => TransactionProblems(t, accounts)))
            .AddRange(OrderProblems(journal.Transactions));

        var entries = journal.Transactions
            .OrderBy(t => t.Date)
            .SelectMany(Resolve)
            .ToImmutableArray();

        return new Validated(new Book(accounts, entries, journal.Transactions.Length), problems);
    }

    private static ImmutableArray<Problem> OpeningProblems(ImmutableArray<Opening> openings)
    {
        var badNames = openings.SelectMany(o => Accounts.Problems(o.Account).Select(message => new Problem(o.Line, message)));
        var duplicates = openings
            .GroupBy(o => o.Account)
            .SelectMany(g => g.Skip(1).Select(o =>
                new Problem(o.Line, $"account '{o.Account}' is already open (line {g.First().Line})")));
        return [.. badNames, .. duplicates];
    }

    private static ImmutableArray<Problem> TransactionProblems(Transaction t, ImmutableSortedDictionary<string, DateOnly> accounts)
    {
        var missing = t.Postings.Count(p => !p.Amount.IsSome);
        var sum = t.Postings.Sum(p => p.Amount.ValueOr(0m));

        var tooFew = t.Postings.Length < 2
            ? [new Problem(t.Line, $"transaction '{t.Description}' needs at least two postings")]
            : ImmutableArray<Problem>.Empty;

        var tooManyMissing = missing > 1
            ? [new Problem(t.Line, $"transaction '{t.Description}' has {missing} postings without an amount (at most one is allowed)")]
            : ImmutableArray<Problem>.Empty;

        var unbalanced = missing == 0 && sum != 0
            ? [new Problem(t.Line, $"transaction '{t.Description}' does not balance: off by {Text.FormatAmount(sum)}")]
            : ImmutableArray<Problem>.Empty;

        var precision = t.Postings
            .Where(p => !p.Amount.Match(Text.HasAtMostTwoDecimals, () => true))
            .Select(p => new Problem(p.Line, $"amount {p.Amount.ValueOr(0m)} has more than two decimal places"));

        var notOpen = t.Postings
            .Where(p => !IsOpen(accounts, p.Account, t.Date))
            .Select(p => new Problem(p.Line, NotOpen(p.Account, t.Date)));

        return [.. tooFew, .. tooManyMissing, .. unbalanced, .. precision, .. notOpen];
    }

    public static string NotOpen(string account, DateOnly date) =>
        $"account '{account}' is not open on {Text.FormatDate(date)}";

    private static bool IsOpen(ImmutableSortedDictionary<string, DateOnly> accounts, string account, DateOnly on) =>
        accounts.TryGetValue(account, out var opened) && opened <= on;

    private sealed record Order(DateOnly Latest, ImmutableArray<Problem> Problems);

    /// <summary>L-9: each transaction must be on or after every transaction above it.</summary>
    private static ImmutableArray<Problem> OrderProblems(ImmutableArray<Transaction> transactions) =>
        transactions.Aggregate(new Order(DateOnly.MinValue, []), (order, t) => t.Date < order.Latest
            ? order with
            {
                Problems = order.Problems.Add(new Problem(t.Line,
                    $"transaction '{t.Description}' on {Text.FormatDate(t.Date)} is out of order: it comes after a transaction on {Text.FormatDate(order.Latest)}")),
            }
            : order with { Latest = t.Date }).Problems;

    /// <summary>Fills in the one missing amount (L-3) so every entry has an amount.</summary>
    private static IEnumerable<Entry> Resolve(Transaction t)
    {
        var balance = -t.Postings.Sum(p => p.Amount.ValueOr(0m));
        return t.Postings.Select(p => new Entry(t.Date, t.Description, p.Account, p.Amount.ValueOr(balance)));
    }
}
