using System.Collections.Immutable;

namespace RayTracer.Core;

/// <summary>A problem in a scene file. Line is 1-based; 0 means the problem is with the file as a whole.</summary>
public sealed record SceneError(int Line, string Message)
{
    public override string ToString() => Line > 0 ? $"line {Line}: {Message}" : $"scene: {Message}";
}

/// <summary>
/// Either a value or every problem found while building it. Unlike Result, which stops at the
/// first error, this keeps them all (R-2). Functional has no such type, so it lives here.
/// </summary>
public abstract record Validation<T>
{
    private Validation() { }

    public sealed record Valid(T Value) : Validation<T>
    {
        public override TOut Match<TOut>(Func<T, TOut> valid, Func<ImmutableArray<SceneError>, TOut> invalid) => valid(Value);
    }

    public sealed record Invalid(ImmutableArray<SceneError> Errors) : Validation<T>
    {
        public override TOut Match<TOut>(Func<T, TOut> valid, Func<ImmutableArray<SceneError>, TOut> invalid) => invalid(Errors);
    }

    public abstract TOut Match<TOut>(Func<T, TOut> valid, Func<ImmutableArray<SceneError>, TOut> invalid);

    public bool IsValid => Match(_ => true, _ => false);
}
