using System;
using System.Collections.Immutable;
using System.Linq;
using FunctionalLint.Adapters;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace FunctionalLint;

// FP0020: the subset of .NET's own library a core may use, as an allowlist. FP0018 decides which other libraries a
// core may use; this decides which parts of .NET. Anything not listed is reported where it's used. Side effects
// (FP0011) and mutable collections (FP0005) are left to their own rules, which give better advice.
public sealed partial class FunctionalAnalyzer
{
    public static readonly DiagnosticDescriptor OutsideApiSubset = Rule("FP0020",
        "Only the functional subset of .NET",
        "'{0}' is outside the .NET subset a core may use. Move it to a shell project, or ask the owner to allow it.",
        "A core may use the parts of .NET's own library listed in ApiSubset.cs: LINQ, immutable collections, strings, " +
        "numbers, dates, regular expressions, JSON and similar. Anything else (threads, the garbage collector, interop, " +
        "reflection, hash codes, enumerators) is reported where it's used. A policy can allow more " +
        "(functional_lint.allowed_apis: a namespace, a type or a member).");

    public const string AllowedApisKey = "functional_lint.allowed_apis";

    // Namespaces whose types a core may use (not their sub-namespaces, unless listed).
    private static readonly ImmutableHashSet<string> AllowedNamespaces = ImmutableHashSet.Create(StringComparer.Ordinal,
        "System.Linq", "System.Collections.Immutable", "System.Collections.Frozen",
        // Mutable collections live here too: FP0005 and FP0008 report them.
        "System.Collections.Generic",
        "System.Text", "System.Text.RegularExpressions", "System.Text.Json", "System.Text.Json.Serialization",
        "System.Text.Encodings.Web", "System.Text.Unicode",
        "System.Numerics", "System.Globalization", "System.Diagnostics.CodeAnalysis", "System.ComponentModel.DataAnnotations");

    // Types a core may use in namespaces that also hold things it may not (System has Console, GC and Environment).
    private static readonly ImmutableHashSet<string> AllowedTypes = ImmutableHashSet.Create(StringComparer.Ordinal,
        "System.Object", "System.String", "System.Boolean", "System.Char", "System.SByte", "System.Byte", "System.Int16",
        "System.UInt16", "System.Int32", "System.UInt32", "System.Int64", "System.UInt64", "System.Int128", "System.UInt128",
        "System.Half", "System.Single", "System.Double", "System.Decimal", "System.Math", "System.MathF",
        "System.DateTime", "System.DateOnly", "System.TimeOnly", "System.TimeSpan", "System.DateTimeOffset", "System.DayOfWeek",
        "System.DateTimeKind", "System.Guid", "System.Uri", "System.UriKind", "System.UriComponents", "System.UriFormat",
        "System.UriPartial", "System.Version", "System.Enum", "System.Array", "System.Convert", "System.BitConverter",
        "System.Nullable", "System.Nullable`1", "System.Index", "System.Range", "System.ReadOnlySpan`1", "System.ReadOnlyMemory`1",
        "System.MemoryExtensions", "System.StringComparison", "System.StringComparer", "System.StringSplitOptions",
        "System.MidpointRounding", "System.Base64FormattingOptions", "System.Lazy`1", "System.FormattableString",
        "System.IComparable", "System.IComparable`1", "System.IConvertible", "System.IEquatable`1", "System.IFormattable", "System.IFormatProvider",
        "System.ISpanFormattable", "System.IParsable`1", "System.ISpanParsable`1", "System.Comparison`1", "System.Predicate`1",
        "System.Converter`2", "System.Attribute", "System.AttributeUsageAttribute", "System.AttributeTargets",
        "System.FlagsAttribute", "System.ObsoleteAttribute", "System.Tuple", "System.ValueTuple",
        "System.Threading.CancellationToken",
        "System.Runtime.CompilerServices.IsExternalInit", "System.Runtime.CompilerServices.IUnion",
        "System.Runtime.CompilerServices.FormattableStringFactory");

    // Families allowed by name: every arity of Func, Action, Tuple and ValueTuple.
    private static readonly ImmutableArray<string> AllowedTypePrefixes =
        ["System.Func`", "System.Action`", "System.Action", "System.Tuple`", "System.ValueTuple`"];

    // Never allowed, even on an allowed type: hash codes change between runs (strings are hashed with a random seed),
    // and GetType is the way into reflection.
    private static readonly ImmutableHashSet<string> DeniedMembers = ImmutableHashSet.Create(StringComparer.Ordinal,
        "GetHashCode", "GetType", "MemberwiseClone");

    // Never allowed, even in an allowed namespace: stepping an enumerator by hand is mutable state.
    private static readonly ImmutableHashSet<string> DeniedTypes = ImmutableHashSet.Create(StringComparer.Ordinal,
        "System.Collections.Generic.IEnumerator`1", "System.HashCode");

    private static void AnalyzeApiUse(OperationAnalysisContext ctx, ImmutableArray<string> extra, string? coreFolder,
        Memo<IAssemblySymbol, bool> dotNet, Func<ISymbol, INamedTypeSymbol, bool> ownedElsewhere)
    {
        var (member, type) = Referenced(ctx.Operation);
        if (member is null || type is null || type.ContainingAssembly is not { } assembly) return;
        if (!dotNet.Get(assembly, a => ctx.Compilation.GetMetadataReference(a) is { } reference && IsDotNetBaseLibrary(reference, a, coreFolder)))
            return;
        if (ownedElsewhere(member, type) || InApiSubset(member, type, extra)) return;

        var isWholeType = member is INamedTypeSymbol || member is IMethodSymbol { MethodKind: MethodKind.Constructor };
        Report(ctx, OutsideApiSubset, ctx.Operation.Syntax, isWholeType ? type.Name : $"{type.Name}.{member.Name}");
    }

    private static INamedTypeSymbol Outermost(INamedTypeSymbol type) => type.ContainingType is { } outer ? Outermost(outer) : type;

    private static bool InApiSubset(ISymbol member, INamedTypeSymbol type, ImmutableArray<string> extra)
    {
        // Nested types (ImmutableArray<T>.Builder) belong to their outermost type.
        var top = Outermost(type.OriginalDefinition);
        var ns = top.ContainingNamespace?.ToDisplayString() ?? "";
        var typeName = ns.Length == 0 ? top.MetadataName : $"{ns}.{top.MetadataName}";
        var plainTypeName = ns.Length == 0 ? top.Name : $"{ns}.{top.Name}";
        var memberName = $"{plainTypeName}.{member.Name}";

        if (extra.Any(e => e == ns || e == typeName || e == plainTypeName || e == memberName)) return true;
        if (DeniedMembers.Contains(member.Name) || DeniedTypes.Contains(typeName)) return false;
        return AllowedNamespaces.Contains(ns)
            || AllowedTypes.Contains(typeName)
            || AllowedTypePrefixes.Any(p => typeName.StartsWith(p, StringComparison.Ordinal))
            // Attributes are declarations, not behaviour: [NotNullWhen], [CallerMemberName], [JsonPropertyName]...
            || top.Name.EndsWith("Attribute", StringComparison.Ordinal);
    }
}
