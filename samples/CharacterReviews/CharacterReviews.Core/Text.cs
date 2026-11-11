using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CharacterReviews.Core;

/// <summary>Text helpers: accent folding for search and slugs, and HTML escaping.</summary>
public static partial class Text
{
    /// <summary>Lower case with accents removed, so "Zoë" and "ZOE" compare equal.</summary>
    public static string Fold(string s) =>
        string.Concat(s.Normalize(NormalizationForm.FormD)
                .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark))
            .Normalize(NormalizationForm.FormC)
            .ToLowerInvariant();

    public static bool ContainsFolded(string haystack, string needle) =>
        Fold(haystack).Contains(Fold(needle), StringComparison.Ordinal);

    /// <summary>Escape a value for use in HTML text or a quoted attribute.</summary>
    public static string Escape(string s) =>
        s.Replace("&", "&amp;")
         .Replace("<", "&lt;")
         .Replace(">", "&gt;")
         .Replace("\"", "&quot;")
         .Replace("'", "&#39;");

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NotSlugChars();

    /// <summary>"Kendall Roy" becomes "kendall-roy". Accents are dropped first, so "Zoë" becomes "zoe".</summary>
    public static string Slugify(string name) =>
        NotSlugChars().Replace(Fold(name), "-").Trim('-');

    /// <summary>The slug itself if it's free, otherwise the first free one of slug-2, slug-3, ...</summary>
    public static string UniqueSlug(string slug, IReadOnlySet<string> taken) =>
        Enumerable.Range(1, int.MaxValue)
            .Select(n => n == 1 ? slug : $"{slug}-{n}")
            .First(candidate => !taken.Contains(candidate));
}
