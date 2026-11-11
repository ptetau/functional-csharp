using System.Globalization;
using Functional;

namespace Ledger.Core;

/// <summary>Reading and writing the small values the journal is made of: dates, amounts and account names.</summary>
public static class Text
{
    public static Result<DateOnly> ParseDate(string text) =>
        DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : new Error("date", $"'{text}' is not a valid date (expected YYYY-MM-DD)");

    /// <summary>Far beyond any personal account, and small enough that no sum of them can overflow a decimal.</summary>
    public const decimal Largest = 1_000_000_000_000m;

    public static Result<decimal> ParseAmount(string text) =>
        !decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount)
            ? new Error("amount", $"'{text}' is not a valid amount")
            : Math.Abs(amount) >= Largest ? new Error("amount", $"amount {text} is too large")
            : amount;

    /// <summary>True for amounts like 82.4 or 3200.00, false for 1.005 (L-8).</summary>
    public static bool HasAtMostTwoDecimals(decimal amount) => decimal.Round(amount, 2) == amount;

    public static string FormatDate(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string FormatAmount(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    public static bool IsAmount(string text) => ParseAmount(text).IsOk;
}
