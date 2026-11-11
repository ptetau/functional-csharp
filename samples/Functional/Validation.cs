namespace Functional;

/// <summary>
/// A value, or every problem found while building it. Unlike <see cref="Result{T}"/>, which stops at the first
/// error, this collects them all, so a form or a file parser can report everything at once. Combine independent
/// checks with <see cref="Validation.Combine{TError,T1,T2,TOut}"/> or <see cref="Validation.All{TError,T}"/>.
/// </summary>
public closed record Validation<TError, T>
{
    public sealed record Valid(T Value) : Validation<TError, T>;

    public sealed record Invalid(ValueList<TError> Errors) : Validation<TError, T>;

    public TOut Match<TOut>(Func<T, TOut> valid, Func<ValueList<TError>, TOut> invalid) => this switch
    {
        Valid(var value) => valid(value),
        Invalid(var errors) => invalid(errors),
    };

    public bool IsValid => this is Valid;

    // Not called "Errors": Invalid's positional Errors would bind to it and recurse forever (CS8907).
    public ValueList<TError> ErrorsOrEmpty => Match(_ => [], errors => errors);

    public Validation<TError, TOut> Map<TOut>(Func<T, TOut> f) =>
        Match<Validation<TError, TOut>>(v => new Validation<TError, TOut>.Valid(f(v)), e => new Validation<TError, TOut>.Invalid(e));

    /// <summary>Runs a check that needs this value. Stops here if this is already invalid.</summary>
    public Validation<TError, TOut> Bind<TOut>(Func<T, Validation<TError, TOut>> f) =>
        Match(f, e => new Validation<TError, TOut>.Invalid(e));

    public Result<T> ToResult(Func<ValueList<TError>, Error> toError) => Match<Result<T>>(v => v, e => toError(e));

    public Option<T> ToOption() => Match(Option.Some, _ => Option.None<T>());

    /// <summary>Changes the errors (sort them, add context), keeping the value if there is one.</summary>
    public Validation<TError, T> MapErrors(Func<ValueList<TError>, ValueList<TError>> f) =>
        Match<Validation<TError, T>>(v => v, e => new Invalid(f(e)));

    /// <summary>The value as a one-item list, or the empty list.</summary>
    public ValueList<T> ToValueList() => Match<ValueList<T>>(v => [v], _ => []);

    public static implicit operator Validation<TError, T>(T value) => new Valid(value);
}

public static class Validation
{
    public static Validation<TError, T> Valid<TError, T>(T value) => new Validation<TError, T>.Valid(value);

    public static Validation<TError, T> Invalid<TError, T>(params ReadOnlySpan<TError> errors) =>
        new Validation<TError, T>.Invalid(ValueList.Create(errors));

    /// <summary>The value if <paramref name="ok"/>, otherwise the error.</summary>
    public static Validation<TError, T> Check<TError, T>(T value, bool ok, TError error) =>
        ok ? Valid<TError, T>(value) : Invalid<TError, T>(error);

    /// <summary>Both values, or the errors from both.</summary>
    public static Validation<TError, TOut> Combine<TError, T1, T2, TOut>(
        Validation<TError, T1> a, Validation<TError, T2> b, Func<T1, T2, TOut> f) =>
        (a, b) switch
        {
            (Validation<TError, T1>.Valid(var x), Validation<TError, T2>.Valid(var y)) => Valid<TError, TOut>(f(x, y)),
            _ => new Validation<TError, TOut>.Invalid(a.ErrorsOrEmpty.AddRange(b.ErrorsOrEmpty)),
        };

    public static Validation<TError, TOut> Combine<TError, T1, T2, T3, TOut>(
        Validation<TError, T1> a, Validation<TError, T2> b, Validation<TError, T3> c, Func<T1, T2, T3, TOut> f) =>
        Combine(Combine(a, b, (x, y) => (x, y)), c, (xy, z) => f(xy.x, xy.y, z));

    public static Validation<TError, TOut> Combine<TError, T1, T2, T3, T4, TOut>(
        Validation<TError, T1> a, Validation<TError, T2> b, Validation<TError, T3> c, Validation<TError, T4> d,
        Func<T1, T2, T3, T4, TOut> f) =>
        Combine(Combine(a, b, c, (x, y, z) => (x, y, z)), d, (xyz, w) => f(xyz.x, xyz.y, xyz.z, w));

    /// <summary>All the values, or every error from every item.</summary>
    public static Validation<TError, ValueList<T>> All<TError, T>(this IEnumerable<Validation<TError, T>> items)
    {
        var all = items.ToValueList();
        var errors = all.SelectMany(v => v.ErrorsOrEmpty).ToValueList();
        return errors.IsEmpty
            ? Valid<TError, ValueList<T>>(all.OfType<Validation<TError, T>.Valid>().Select(v => v.Value).ToValueList())
            : new Validation<TError, ValueList<T>>.Invalid(errors);
    }

    /// <summary>Validates each item, keeping every error.</summary>
    public static Validation<TError, ValueList<TOut>> Traverse<TError, T, TOut>(
        this IEnumerable<T> items, Func<T, Validation<TError, TOut>> validate) =>
        items.Select(validate).All();
}
