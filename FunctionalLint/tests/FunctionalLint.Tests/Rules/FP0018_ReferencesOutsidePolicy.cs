using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace FunctionalLint.Tests.Rules;

public class FP0018_ReferencesOutsidePolicy
{
    // A core that references the .NET base library plus two libraries of its own: Acme.Db and Functional.
    private static async Task<ImmutableArray<Diagnostic>> Findings(string? allowed)
    {
        var references = await Lint.References.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        MetadataReference Library(string name) => CSharpCompilation.Create(name,
            [CSharpSyntaxTree.ParseText($"namespace {name} {{ public static class Api {{ }} }}")], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)).ToMetadataReference();

        var compilation = CSharpCompilation.Create("Core",
            [CSharpSyntaxTree.ParseText("public static class C { static object A() => typeof(Acme.Db.Api); static object B() => typeof(Functional.Api); }")],
            references.Add(Library("Acme.Db")).Add(Library("Functional")),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var config = allowed is null ? "" : "functional_lint.allowed_references = " + allowed;
        var options = new AnalyzerOptions([], new GlobalConfig(config));
        return await compilation.WithAnalyzers([new FunctionalAnalyzer()], options).GetAnalyzerDiagnosticsAsync();
    }

    [Fact]
    public async Task Libraries_outside_the_list_are_reported_and_the_base_library_is_not()
    {
        var found = (await Findings("Functional")).Where(d => d.Id == "FP0018").ToList();
        Assert.Equal(
            ["'Acme.Db' isn't on this core's list of allowed references. Move the code that uses it to a shell project."],
            found.Select(d => d.GetMessage()));
    }

    [Fact]
    public async Task A_trailing_star_allows_a_family_and_no_key_means_no_check()
    {
        Assert.DoesNotContain(await Findings("Functional, Acme.*"), d => d.Id == "FP0018");
        Assert.DoesNotContain(await Findings(null), d => d.Id == "FP0018");
        Assert.Equal(2, (await Findings("none")).Count(d => d.Id == "FP0018"));
    }

    private sealed class GlobalConfig(string line) : AnalyzerConfigOptionsProvider
    {
        private readonly Values _values = new(line);
        public override AnalyzerConfigOptions GlobalOptions => _values;
        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => _values;
        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => _values;
    }

    private sealed class Values(string line) : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, out string value)
        {
            var parts = line.Split('=', 2);
            var found = parts.Length == 2 && parts[0].Trim() == key;
            value = found ? parts[1].Trim() : null!;
            return found;
        }
    }
}
