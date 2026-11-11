namespace Functional;

/// <summary>A value that may be missing. Prefer it to null when "missing" is a normal answer.</summary>
public abstract record Option<T>
{
    private Option() { }

    public sealed record Some(T Value) : Option<T>
    {
        public override TOut Match<TOut>(Func<T, TOut> some, Func<TOut> none) => some(Value);
    }

    public sealed record None : Option<T>
    {
        public override TOut Match<TOut>(Func<T, TOut> some, Func<TOut> none) => none();
    }

    public abstract TOut Match<TOut>(Func<T, TOut> some, Func<TOut> none);

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
