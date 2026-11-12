using Functional;

namespace CharacterReviews.Core;

/// <summary>A problem with one form field, shown next to that field.</summary>
public sealed record FieldError(string Field, string Message);

/// <summary>
/// Shorthands for <see cref="Validation{TError,T}"/> with <see cref="FieldError"/>, so the form rules
/// don't repeat the type arguments and the FieldError constructor on every check.
/// </summary>
public static class Fields
{
    public static Validation<FieldError, T> Invalid<T>(string field, string message) =>
        Validation.Invalid<FieldError, T>(new FieldError(field, message));

    /// <summary>Valid when the check holds, otherwise one error on the field.</summary>
    public static Validation<FieldError, T> Check<T>(T value, bool holds, string field, string message) =>
        Validation.Check(value, holds, new FieldError(field, message));
}
