using System.Collections.Immutable;

namespace Functional;

/// <summary>Why something failed. Code is stable and machine-readable; Message is for people.</summary>
public sealed record Error(string Code, string Message)
{
    public override string ToString() => $"{Code}: {Message}";
}

/// <summary>
/// Either a value or an Error. It's a C# 15 closed record, so a switch expression over
/// <c>Ok</c> and <c>Failed</c> is checked for completeness by the compiler:
/// <code>r switch { Result&lt;int&gt;.Ok(var v) =&gt; v, Result&lt;int&gt;.Failed(var e) =&gt; 0 }</code>
/// </summary>
public closed record Result<T>
{
    public sealed record Ok(T Value) : Result<T>;

    public sealed record Failed(Error Error) : Result<T>;

    public TOut Match<TOut>(Func<T, TOut> ok, Func<Error, TOut> failed) => this switch
    {
        Ok(var value) => ok(value),
        Failed(var error) => failed(error),
    };

    public bool IsOk => Match(_ => true, _ => false);

    public Result<TOut> Map<TOut>(Func<T, TOut> f) => Match<Result<TOut>>(v => f(v), e => e);

    public Result<TOut> Bind<TOut>(Func<T, Result<TOut>> f) => Match(f, e => new Result<TOut>.Failed(e));

    public T ValueOr(T fallback) => Match(v => v, _ => fallback);

    public Result<T> Ensure(Func<T, bool> holds, Func<T, Error> otherwise) =>
        Bind(v => holds(v) ? this : new Failed(otherwise(v)));

    public Result<T> MapError(Func<Error, Error> f) => Match<Result<T>>(v => v, e => f(e));

    public Option<T> ToOption() => Match(Option.Some, _ => Option.None<T>());

    /// <summary>For shells and adapters: run one action or the other.</summary>
    public void Switch(Action<T> ok, Action<Error> failed)
    {
        Action run = this switch
        {
            Ok(var value) => () => ok(value),
            Failed(var error) => () => failed(error),
        };
        run();
    }

    public static implicit operator Result<T>(T value) => new Ok(value);
    public static implicit operator Result<T>(Error error) => new Failed(error);
}

public static class Result
{
    public static Result<T> Ok<T>(T value) => new Result<T>.Ok(value);

    public static Result<T> Fail<T>(string code, string message) => new Result<T>.Failed(new Error(code, message));

    /// <summary>All the values, or the first error. O(n).</summary>
    public static Result<ValueList<T>> All<T>(this IEnumerable<Result<T>> results)
    {
        var all = results.ToValueList();
        return all.OfType<Result<T>.Failed>().FirstOrDefault() is { } failed
            ? failed.Error
            : Ok(all.OfType<Result<T>.Ok>().Select(ok => ok.Value).ToValueList());
    }

    /// <summary>Applies f to each item: all the values, or the first error.</summary>
    public static Result<ValueList<TOut>> Traverse<T, TOut>(this IEnumerable<T> items, Func<T, Result<TOut>> f) =>
        items.Select(f).All();

    // LINQ query syntax: from a in ra from b in rb select a + b
    public static Result<TOut> Select<T, TOut>(this Result<T> r, Func<T, TOut> f) => r.Map(f);

    public static Result<TOut> SelectMany<T, TMid, TOut>(this Result<T> r, Func<T, Result<TMid>> bind, Func<T, TMid, TOut> project) =>
        r.Bind(a => bind(a).Map(b => project(a, b)));
}
