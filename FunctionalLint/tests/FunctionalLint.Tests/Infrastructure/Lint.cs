using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace FunctionalLint.Tests;

/// <summary>
/// Helpers shared by every test.
///
/// Markup in test sources: {|FP0001:text|} means "expect diagnostic FP0001 on exactly this text".
/// Any diagnostic that isn't marked fails the test, so unmarked code is asserted clean.
/// </summary>
public static class Lint
{
    public const string AdapterAttribute =
        "public sealed class ImperativeAdapterAttribute : System.Attribute { }";

    public static readonly ReferenceAssemblies References = ReferenceAssemblies.Net.Net80;

    /// <summary>Runs the analyzer and checks the marked diagnostics are exactly the ones reported.</summary>
    public static Task Verify(string source, string? config = null)
    {
        var test = new CSharpAnalyzerTest<FunctionalAnalyzer, DefaultVerifier>
        {
            TestCode = source,
            ReferenceAssemblies = References,
        };
        AddConfig(test.TestState, config);
        return test.RunAsync();
    }

    /// <summary>Runs the analyzer's code fixes (including fix-all) and checks the result.</summary>
    public static Task VerifyFix<TFix>(string before, string after, string? config = null)
        where TFix : CodeFixProvider, new()
    {
        var test = new CSharpCodeFixTest<FunctionalAnalyzer, TFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
            ReferenceAssemblies = References,
        };
        AddConfig(test.TestState, config);
        AddConfig(test.FixedState, config);
        return test.RunAsync();
    }

    /// <summary>A diagnostic as a developer or agent would see it: the rule, the code it points at, and the message.</summary>
    public sealed record Finding(string Id, string Code, string Message);

    /// <summary>
    /// Compiles the source and returns the analyzer's diagnostics, plus any analyzer crash (AD0001).
    /// Compiler errors are ignored, so this works on half-written code.
    /// </summary>
    public static async Task<ImmutableArray<Finding>> Diagnose(string source, string? config = null,
        OutputKind kind = OutputKind.DynamicallyLinkedLibrary)
    {
        var (compilation, analyzers) = await Prepare(source, config, kind);
        var diagnostics = await analyzers.GetAnalyzerDiagnosticsAsync();
        var text = compilation.SyntaxTrees.First().GetText();
        return diagnostics
            .OrderBy(d => d.Location.SourceSpan.Start)
            .Select(d => new Finding(d.Id, d.Location.IsInSource ? text.ToString(d.Location.SourceSpan) : "", d.GetMessage()))
            .ToImmutableArray();
    }

    public static async Task<(Compilation, CompilationWithAnalyzers)> Prepare(string source, string? config,
        OutputKind kind = OutputKind.DynamicallyLinkedLibrary, bool logTime = false, Action<Exception>? onCrash = null)
    {
        var references = await References.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest), path: "/0/Test0.cs");
        var compilation = CSharpCompilation.Create("Test", new[] { tree }, references,
            new CSharpCompilationOptions(kind, nullableContextOptions: NullableContextOptions.Enable));

        var options = new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty, new ConfigOptions(ParseConfig(config)));
        var analyzers = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(new FunctionalAnalyzer()),
            new CompilationWithAnalyzersOptions(options,
                onAnalyzerException: onCrash is null ? null : (e, _, _) => onCrash(e),
                concurrentAnalysis: true, logAnalyzerExecutionTime: logTime));
        return (compilation, analyzers);
    }

    private static void AddConfig(SolutionState state, string? config)
    {
        if (config is not null)
            state.AnalyzerConfigFiles.Add(("/.editorconfig", "root = true\n\n[*]\n" + config + "\n"));
    }

    private static ImmutableDictionary<string, string> ParseConfig(string? config) =>
        (config ?? "").Split('\n')
            .Select(line => line.Split('=', 2))
            .Where(parts => parts.Length == 2)
            .ToImmutableDictionary(parts => parts[0].Trim(), parts => parts[1].Trim());

    private sealed class ConfigOptions(ImmutableDictionary<string, string> values) : AnalyzerConfigOptionsProvider
    {
        private readonly Values _values = new(values);
        public override AnalyzerConfigOptions GlobalOptions => _values;
        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => _values;
        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => _values;
    }

    private sealed class Values(ImmutableDictionary<string, string> values) : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, out string value) => values.TryGetValue(key, out value!);
    }
}

/// <summary>Finds files in the repository, so tests can check the docs agree with the code.</summary>
public static class Repo
{
    public static string Root { get; } = FindRoot();

    public static string Read(params string[] path) => File.ReadAllText(Path.Combine(Root, Path.Combine(path)));

    public static bool Exists(params string[] path) => File.Exists(Path.Combine(Root, Path.Combine(path)));

    public static IEnumerable<string> TestSources() =>
        Directory.EnumerateFiles(Path.Combine(Root, "tests"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "FunctionalLint.slnx"))) return dir.FullName;
        throw new InvalidOperationException("Couldn't find FunctionalLint.slnx above " + AppContext.BaseDirectory);
    }
}
