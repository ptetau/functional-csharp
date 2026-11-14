using System.Collections.Immutable;

namespace Ledger.Core;

/// <summary>Account names: colon-separated paths whose first segment is the account's type.</summary>
public static class Accounts
{
    public static readonly ImmutableArray<string> Types = ["Assets", "Liabilities", "Equity", "Income", "Expenses"];

    public static ImmutableArray<string> Segments(string account) => [.. account.Split(':')];

    /// <summary>Why the name isn't a usable account (L-7), or nothing if it's fine.</summary>
    public static ImmutableArray<string> Problems(string account)
    {
        var segments = Segments(account);
        var emptySegment = segments.Any(string.IsNullOrWhiteSpace)
            ? [$"account '{account}' has an empty part"]
            : ImmutableArray<string>.Empty;
        // Split always gives at least one part.
        var type = account.Split(':')[0];
        var badType = Types.Contains(type)
            ? ImmutableArray<string>.Empty
            : [$"account '{account}' has unknown type '{type}' (expected {string.Join(", ", Types)})"];
        return emptySegment.AddRange(badType);
    }

    /// <summary>
    /// True when the account is the prefix itself or sits under it, so "Assets:Bank" matches
    /// "Assets:Bank:Everyday" but not "Assets:Banking".
    /// </summary>
    public static bool IsUnder(string account, string prefix) =>
        prefix.Length == 0 || account == prefix || account.StartsWith(prefix + ":", StringComparison.Ordinal);

    /// <summary>"Assets:Bank:Everyday" gives "Assets", "Assets:Bank", "Assets:Bank:Everyday".</summary>
    public static ImmutableArray<string> PathsTo(string account)
    {
        var segments = Segments(account);
        return [.. Enumerable.Range(1, segments.Length).Select(n => string.Join(':', segments.Take(n)))];
    }

    /// <summary>A key that sorts accounts as a tree: parents first, then children by name.</summary>
    public static string TreeOrder(string account) => account.Replace(':', '\0');
}
