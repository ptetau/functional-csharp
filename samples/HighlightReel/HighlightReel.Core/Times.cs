using System.Collections.Immutable;
using System.Globalization;
using System.Text.RegularExpressions;
using Functional;

namespace HighlightReel.Core;

/// <summary>Reading and writing times as whole seconds (H-3, H-4).</summary>
public static class Times
{
    private sealed record Form(Regex Pattern, ImmutableArray<long> Units);

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
            .Select(candidate => Total(candidate.Units, candidate.Match))
            .Where(total => total <= int.MaxValue)
            .Select(total => Result.Ok((int)total))
            .DefaultIfEmpty(Result.Fail<int>("bad-time", $"'{text}' is not a valid time (use 95, 1:35, 1:01:35 or 1m35s)"))
            .First();

    /// <summary>Writes seconds as h:mm:ss, e.g. 0:01:35.</summary>
    public static string Format(int seconds) =>
        string.Create(CultureInfo.InvariantCulture, $"{seconds / 3600}:{seconds / 60 % 60:00}:{seconds % 60:00}");

    private static long Total(ImmutableArray<long> units, Match match) =>
        units
            .Select((unit, i) => match.Groups[i + 1] is { Success: true } group
                ? unit * long.Parse(group.Value, CultureInfo.InvariantCulture)
                : 0)
            .Sum();
}
