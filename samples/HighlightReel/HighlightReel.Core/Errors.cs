using Functional;

namespace HighlightReel.Core;

/// <summary>Helpers Functional.Result doesn't have (yet).</summary>
public static class Errors
{
    /// <summary>Puts context in front of a failure's message, e.g. "line 3: ". Leaves a success alone.</summary>
    public static Result<T> Prefix<T>(Result<T> result, string prefix) =>
        result.Match<Result<T>>(
            value => value,
            error => error with { Message = prefix + error.Message });
}
