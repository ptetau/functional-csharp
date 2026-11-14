using System.Collections.Immutable;
using Functional;

namespace Ledger.Core;

/// <summary>The text of the balance (L-11) and register (L-12) reports. Each line ends with "\n".</summary>
public static class Reports
{
    // The spec's examples use these widths. Columns grow when an amount doesn't fit.
    private const int BalanceAmountWidth = 18;
    private const int RegisterAmountWidth = 8;

    public static string Balance(Book book, Option<DateOnly> asOf, string prefix)
    {
        var inRange = (DateOnly date) => asOf.Match(limit => date <= limit, () => true);

        var entries = book.Entries
            .Where(e => inRange(e.Date) && Accounts.IsUnder(e.Account, prefix))
            .ToImmutableArray();

        var rows = book.Accounts
            .Where(a => inRange(a.Date) && Accounts.IsUnder(a.Account, prefix))
            .SelectMany(a => Accounts.PathsTo(a.Account))
            .Distinct()
            .OrderBy(Accounts.TreeOrder, StringComparer.Ordinal)
            .Select(path => (Path: path, Amount: Text.FormatAmount(entries.Where(e => Accounts.IsUnder(e.Account, path)).Sum(e => e.Amount))))
            .ToImmutableArray();

        var total = Text.FormatAmount(entries.Sum(e => e.Amount));
        var width = rows.Select(r => r.Amount.Length).Append(total.Length).Aggregate(BalanceAmountWidth, Math.Max);

        var lines = rows
            .Select(r => $"{r.Amount.PadLeft(width)}  {Indent(r.Path)}{r.Path.Split(':')[^1]}")
            .Append(new string('-', width + 2))
            .Append(total.PadLeft(width));

        return Lines(lines);
    }

    public static string Register(Book book, string prefix)
    {
        var entries = book.Entries.Where(e => Accounts.IsUnder(e.Account, prefix)).ToImmutableArray();

        var totals = entries.Scan(0m, (total, e) => total + e.Amount);

        var rows = entries
            .Zip(totals, (e, total) => (Entry: e, Amount: Text.FormatAmount(e.Amount), Total: Text.FormatAmount(total)))
            .ToImmutableArray();

        var descriptionWidth = rows.Select(r => r.Entry.Description.Length).DefaultIfEmpty(0).Max();
        var accountWidth = rows.Select(r => r.Entry.Account.Length).DefaultIfEmpty(0).Max();
        var amountWidth = rows.SelectMany(r => new[] { r.Amount.Length, r.Total.Length }).Aggregate(RegisterAmountWidth, Math.Max);

        return Lines(rows.Select(r =>
            $"{Text.FormatDate(r.Entry.Date)}  {r.Entry.Description.PadRight(descriptionWidth)}  " +
            $"{r.Entry.Account.PadRight(accountWidth)}  {r.Amount.PadLeft(amountWidth)}  {r.Total.PadLeft(amountWidth)}"));
    }

    private static string Indent(string path) => new(' ', 2 * (Accounts.Segments(path).Length - 1));

    private static string Lines(IEnumerable<string> lines) => string.Concat(lines.Select(line => line + "\n"));
}
