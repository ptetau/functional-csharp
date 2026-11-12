using System.Text;

namespace Samples.TestSupport;

/// <summary>
/// Seeded random inputs for "never throws" tests. Most are a valid example with a few random edits, so they get past
/// the first check and reach the deeper code; some are extreme values or plain noise. The same seed gives the same
/// inputs, so a failure reproduces. Linked into each sample's test project.
/// </summary>
public static class Fuzz
{
    private static readonly string[] Extremes =
    [
        "", " ", "0", "-0", "-1", "+1", "1.", ".5", "1e400", "-1e400", "NaN", "Infinity", "∞", "١٢٣", "0x1F",
        "99999999999999999999999999999999", "-99999999999999999999999999999999", "79228162514264337593543950336",
        "2147483648", "-2147483649", "9999-12-31", "0001-01-01", "2026-02-30", "2026-13-01", "24:60:60", "99:99",
        "-", ":", ",", ";", "#", "\"", "'", "\\", "/", "?", "&", "=", "\t", "\r", "\u0000", "\uFFFF", "😀", "é",
        new string('9', 400), new string('a', 2000),
    ];

    private const string Characters = "abcxyzABCXYZ0123456789 .,:;-+#&=?/\\'\"()[]{}_\t\r\n\u00e9\u0000";

    /// <summary><paramref name="count"/> inputs near <paramref name="valid"/>, the same every run for a given seed.</summary>
    public static IEnumerable<string> Around(string valid, int count, int seed)
    {
        var random = new Random(seed);
        yield return "";
        yield return valid;
        for (var i = 0; i < count; i++)
            yield return random.Next(10) switch
            {
                0 => Noise(random),
                _ => Enumerable.Range(0, 1 + random.Next(4)).Aggregate(valid, (text, _) => Edit(text, random)),
            };
    }

    /// <summary>Runs <paramref name="check"/> on each input and fails with the input that threw, if any.</summary>
    public static void NeverThrows(IEnumerable<string> inputs, Action<string> check)
    {
        foreach (var input in inputs)
        {
            try { check(input); }
            catch (Exception e)
            {
                throw new Xunit.Sdk.XunitException($"Threw {e.GetType().Name} ({e.Message}) for input:\n{Show(input)}\n{e.StackTrace}");
            }
        }
    }

    private static string Show(string input) =>
        input.Length > 600 ? Escape(input[..600]) + "..." : Escape(input);

    private static string Escape(string text) =>
        text.Replace("\\", "\\\\").Replace("\r", "\\r").Replace("\t", "\\t").Replace("\u0000", "\\0").Replace("\n", "\\n\n");

    private static string Noise(Random random) =>
        new(Enumerable.Range(0, random.Next(200)).Select(_ => Characters[random.Next(Characters.Length)]).ToArray());

    private static string Edit(string text, Random random)
    {
        if (text.Length == 0) return Extremes[random.Next(Extremes.Length)];
        var at = random.Next(text.Length);
        var lines = text.Split('\n');
        return random.Next(7) switch
        {
            0 => text.Insert(at, Characters[random.Next(Characters.Length)].ToString()),
            1 => text.Remove(at, Math.Min(1 + random.Next(8), text.Length - at)),
            2 => text[..at],
            3 => ReplaceToken(text, random),
            4 => string.Join('\n', lines.Take(random.Next(lines.Length)).Concat(lines.Skip(random.Next(lines.Length)))),
            5 => text.Insert(at, Extremes[random.Next(Extremes.Length)]),
            _ => string.Join('\n', lines.Select(l => random.Next(4) == 0 ? ReplaceToken(l, random) : l)),
        };
    }

    // Swaps one whitespace-separated token for an extreme value: a number too big, a date that doesn't exist...
    private static string ReplaceToken(string text, Random random)
    {
        var tokens = text.Split(' ');
        var i = random.Next(tokens.Length);
        var builder = new StringBuilder();
        for (var t = 0; t < tokens.Length; t++)
        {
            if (t > 0) builder.Append(' ');
            builder.Append(t == i ? Extremes[random.Next(Extremes.Length)] : tokens[t]);
        }
        return builder.ToString();
    }
}
