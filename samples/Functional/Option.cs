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

    public Option<TOut> Bind<TOut>(Func<T, Option<TOut>> f) => Match(f, () => new Option<TOut>.None());

    public Option<T> Where(Func<T, bool> keep) => Bind(v => keep(v) ? this : new None());

    public Option<T> OrElse(Func<Option<T>> other) => Match(_ => this, other);

    public ValueList<T> ToValueList() => Match<ValueList<T>>(v => [v], () => []);

    /// <summary>For shells and adapters: do something only when there's a value.</summary>
    public void IfSome(Action<T> action)
    {
        Action run = this switch
        {
            Some(var value) => () => action(value),
            None => () => { },
        };
        run();
    }

    public Result<T> OrError(Error error) => Match<Result<T>>(v => v, () => error);
}

public static class Option
{
    public static Option<T> Some<T>(T value) => new Option<T>.Some(value);
    public static Option<T> None<T>() => new Option<T>.None();
    public static Option<T> FromNullable<T>(T? value) where T : class => value is null ? None<T>() : Some(value);
    public static Option<T> FromNullable<T>(T? value) where T : struct => value is { } v ? Some(v) : None<T>();

    // LINQ query syntax: from a in oa from b in ob select a + b
    public static Option<TOut> Select<T, TOut>(this Option<T> o, Func<T, TOut> f) => o.Map(f);

    public static Option<TOut> SelectMany<T, TMid, TOut>(this Option<T> o, Func<T, Option<TMid>> bind, Func<T, TMid, TOut> project) =>
        o.Bind(a => bind(a).Map(b => project(a, b)));
}
