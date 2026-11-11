using System.Collections.Immutable;

namespace CharacterReviews.Core;

/// <summary>A problem with one form field, shown next to that field.</summary>
public sealed record FieldError(string Field, string Message);

/// <summary>
/// A value, or every problem found while building it. Unlike Result, which stops at the first error,
/// this collects them all, so a form can show each problem next to its field.
/// </summary>
public closed record Validated<T>
{
    public sealed record Valid(T Value) : Validated<T>;

    public sealed record Invalid(ImmutableArray<FieldError> Errors) : Validated<T>;

    public TOut Match<TOut>(Func<T, TOut> valid, Func<ImmutableArray<FieldError>, TOut> invalid) => this switch
    {
        Valid(var value) => valid(value),
        Invalid(var errors) => invalid(errors),
    };

    public ImmutableArray<FieldError> ErrorsOrEmpty => Match(_ => ImmutableArray<FieldError>.Empty, e => e);

    public Validated<TOut> Map<TOut>(Func<T, TOut> f) =>
        Match<Validated<TOut>>(v => new Validated<TOut>.Valid(f(v)), e => new Validated<TOut>.Invalid(e));

    public Validated<TOut> Bind<TOut>(Func<T, Validated<TOut>> f) =>
        Match(f, e => new Validated<TOut>.Invalid(e));
}

public static class Validated
{
    public static Validated<T> Valid<T>(T value) => new Validated<T>.Valid(value);

    public static Validated<T> Invalid<T>(string field, string message) =>
        new Validated<T>.Invalid([new FieldError(field, message)]);

    /// <summary>Valid when the check holds, otherwise one error on the field.</summary>
    public static Validated<T> Check<T>(T value, bool holds, string field, string message) =>
        holds ? Valid(value) : Invalid<T>(field, message);

    /// <summary>Combine independent checks, keeping every error from every one of them.</summary>
    public static Validated<TOut> Combine<T1, T2, TOut>(Validated<T1> a, Validated<T2> b, Func<T1, T2, TOut> f) =>
        a.Match(
            va => b.Map(vb => f(va, vb)),
            ea => new Validated<TOut>.Invalid(ea.AddRange(b.ErrorsOrEmpty)));

    public static Validated<TOut> Combine<T1, T2, T3, TOut>(
        Validated<T1> a, Validated<T2> b, Validated<T3> c, Func<T1, T2, T3, TOut> f) =>
        Combine(Combine(a, b, (x, y) => (x, y)), c, (xy, z) => f(xy.x, xy.y, z));
}
