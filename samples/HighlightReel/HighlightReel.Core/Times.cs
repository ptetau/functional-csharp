using System.Collections.Immutable;
using System.Globalization;
using System.Text.RegularExpressions;
using Functional;

namespace HighlightReel.Core;

/// <summary>Reading and writing times as whole seconds (H-3, H-4).</summary>
public static class Times
{
    private sealed record Form(Regex Pattern, ValueList<long> Units);

    // Each form's capture groups line up with its units. Digits are capped so a total can't overflow a long.
    private static readonly ImmutableArray<Form> Forms =
    [
        new(new Regex("^([0-9]{1,9})$"), [1]),
        new(new Regex("^([0-9]{1,9}):([0-5][0-9])$"), [60, 1]),
        new(new Regex("^([0-9]{1,9}):([0-5][0-9]):([0-5][0-9])$"), [3600, 60, 1]),
        new(new Regex("^(?=[0-9])(?:([0-9]{1,9})h)?(?:([0-9]{1,9})m)?(?:([0-9]{1,9})s)?$", RegexOptions.IgnoreCase), [3600, 60, 1]),
    ];

    /// <summary>Reads 95, 1:35, 1:01:35, 1h1m35s, 1m35s or 95s as a number of seconds.</summary>
    public static Result<int> Parse(string text) =>
        Forms
            .Select(form => (form.Units, Match: form.Pattern.Match(text.Trim())))
            .Where(candidate => candidate.Match.Success)
            .SelectMany(candidate => Total(candidate.Units, candidate.Match).ToValueList())
            .Where(total => total <= int.MaxValue)
            .Select(total => Result.Ok((int)total))
            .FirstOrDefault()
        ?? Result.Fail<int>("bad-time", $"'{text}' is not a valid time (use 95, 1:35, 1:01:35 or 1m35s)");

    /// <summary>Writes seconds as h:mm:ss, e.g. 0:01:35.</summary>
    public static string Format(int seconds) =>
        FormattableString.Invariant($"{seconds / 3600}:{seconds / 60 % 60:00}:{seconds % 60:00}");

    // Group 0 is the whole match; groups 1.. line up with the units. A group that didn't take part counts as 0.
    // The patterns only let digits into a group, so TryParse can't fail; if it did, the form wouldn't match.
    private static Option<long> Total(ValueList<long> units, Match match) =>
        units
            .Zip(match.Groups.Values.Skip(1), (unit, group) => group.Success ? Digits(group.Value).Map(n => unit * n) : Option.Some(0L))
            .Aggregate(Option.Some(0L), (sum, term) => from a in sum from b in term select a + b);

    private static Option<long> Digits(string text) =>
        long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? Option.Some(n) : Option.None<long>();
}
