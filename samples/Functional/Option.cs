namespace Functional;

/// <summary>
/// A value that may be missing. Prefer it to null when "missing" is a normal answer.
/// A C# 15 closed record: a switch expression over <c>Some</c> and <c>None</c> is checked for completeness.
/// </summary>
public closed record Option<T>
{
    public sealed record Some(T Value) : Option<T>;

    public sealed record None : Option<T>;

    public TOut Match<TOut>(Func<T, TOut> some, Func<TOut> none) => this switch
    {
        Some(var value) => some(value),
        None => none(),
    };

    public bool IsSome => Match(_ => true, () => false);

    public Option<TOut> Map<TOut>(Func<T, TOut> f) => Match<Option<TOut>>(v => new Option<TOut>.Some(f(v)), () => new Option<TOut>.None());

    public T ValueOr(T fallback) => Match(v => v, () => fallback);

    public Result<T> OrError(Error error) => Match<Result<T>>(v => v, () => error);
}

public static class Option
{
    public static Option<T> Some<T>(T value) => new Option<T>.Some(value);
    public static Option<T> None<T>() => new Option<T>.None();
    public static Option<T> FromNullable<T>(T? value) where T : class => value is null ? None<T>() : Some(value);
    public static Option<T> FromNullable<T>(T? value) where T : struct => value is { } v ? Some(v) : None<T>();
}
