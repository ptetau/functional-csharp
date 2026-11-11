using System.Linq;
using System.Threading.Tasks;
using FunctionalLint.Heal;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace FunctionalLint.Tests.Fixes;

/// <summary>The engine behind heal.sh, run on an in-memory project.</summary>
public class HealerTests
{
    private static async Task<(Project Project, DocumentId Document)> Load(string source, string? config = null)
    {
        var references = await Lint.References.ResolveAsync(LanguageNames.CSharp, default);
        var projectId = ProjectId.CreateNewId();
        var documentId = DocumentId.CreateNewId(projectId);

        var solution = new AdhocWorkspace().CurrentSolution
            .AddProject(ProjectInfo.Create(projectId, VersionStamp.Default, "Core", "Core", LanguageNames.CSharp,
                compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                    nullableContextOptions: NullableContextOptions.Enable),
                parseOptions: new CSharpParseOptions(LanguageVersion.Latest),
                metadataReferences: references))
            .AddDocument(documentId, "Orders.cs", SourceText.From(source), filePath: "/src/Orders.cs");

        if (config is not null)
            solution = solution.AddAnalyzerConfigDocument(DocumentId.CreateNewId(projectId), ".editorconfig",
                SourceText.From("root = true\n\n[*.cs]\n" + config + "\n"), filePath: "/src/.editorconfig");

        return (solution.GetProject(projectId)!, documentId);
    }

    private static async Task<string> Text(HealResult result, DocumentId id) =>
        (await result.Solution.GetDocument(id)!.GetTextAsync()).ToString();

    [Fact]
    public async Task Fixes_every_fixable_rule_in_one_run_and_reports_the_rest()
    {
        var (project, doc) = await Load("""
            using System.Collections.Generic;

            public class Order
            {
                public int Id { get; set; }
            }

            public class Pricing
            {
                private static int rate = 5;
                public static int Total(IList<int> prices)
                {
                    IList<int> copy = [1];
                    var total = 0;
                    total = prices.Count + copy.Count + rate;
                    return total;
                }
                public static async void Notify() => await System.Threading.Tasks.Task.Yield();
            }
            """);

        var result = await Healer.HealAsync(project);

        Assert.Equal("""
            using System.Collections.Generic;
            using System.Threading.Tasks;

            public record Order
            {
                public int Id { get; init; }
            }

            public static class Pricing
            {
                private static readonly int rate = 5;
                public static int Total(IReadOnlyList<int> prices)
                {
                    IReadOnlyList<int> copy = [1];
                    var total = 0;
                    total = prices.Count + copy.Count + rate;
                    return total;
                }
                public static async Task Notify() => await System.Threading.Tasks.Task.Yield();
            }
            """, await Text(result, doc));

        Assert.Equal(new[] { ("FP0003", 2), ("FP0004", 2), ("FP0005", 1), ("FP0008", 1), ("FP0010", 1) },
            result.Fixed.Select(kv => (kv.Key, kv.Value)));
        Assert.Equal(["FP0002"], result.Remaining.Select(d => d.Id));
    }

    [Fact]
    public async Task Follows_the_projects_config()
    {
        var (project, doc) = await Load("""
            namespace Legacy { public class Old { public int Count { get; set; } } }
            """, "functional_lint.legacy_namespaces = Legacy");

        var result = await Healer.HealAsync(project);

        Assert.Empty(result.Fixed);
        Assert.Empty(result.Remaining);
        Assert.Contains("public class Old", await Text(result, doc));
    }

    [Fact]
    public async Task Reports_compiler_errors_it_did_not_cause()
    {
        var (project, _) = await Load("public static class C { public static int M() => Missing(); }");

        var result = await Healer.HealAsync(project);

        Assert.Equal(["CS0103"], result.Remaining.Select(d => d.Id));
    }

    [Fact]
    public async Task Says_which_problems_it_can_fix()
    {
        var (project, _) = await Load("""
            using System.Collections.Generic;
            public static class C
            {
                public static int M()
                {
                    IList<int> a = [1];
                    var b = new List<int>();
                    return a.Count + b.Count;
                }
            }
            """);

        var diagnostics = await Healer.DiagnoseAsync(project);
        var fixable = await Task.WhenAll(diagnostics.Select(async d => (d.Id, Fixable: await Healer.IsFixableAsync(project, d))));

        Assert.Equal(new[] { ("FP0005", true), ("FP0005", false) }, fixable);
    }
}
