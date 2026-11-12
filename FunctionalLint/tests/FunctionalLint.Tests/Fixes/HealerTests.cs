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
                parseOptions: new CSharpParseOptions(Lint.CSharp15),
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
    public async Task Never_keeps_a_fix_that_breaks_the_build()
    {
        // Making `calls` readonly would turn `calls++` into CS0198, so that one fix is thrown away
        // and its FP0003 stays in the report. The setter next to it is still fixed.
        var (project, doc) = await Load("""
            public static class Counter
            {
                private static int calls;
                public static int Next() => ++calls;
            }

            public record Box { public int Size { get; set; } }
            """);

        var result = await Healer.HealAsync(project);
        var text = await Text(result, doc);

        Assert.Contains("private static int calls;", text);
        Assert.Contains("public int Size { get; init; }", text);
        Assert.Equal(new[] { ("FP0003", 1) }, result.Fixed.Select(kv => (kv.Key, kv.Value)));
        Assert.Contains(result.Remaining, d => d.Id == "FP0003");
        Assert.DoesNotContain(result.Remaining, d => d.Id.StartsWith("CS"));
    }

    [Fact]
    public async Task Never_breaks_a_project_that_depends_on_the_healed_one()
    {
        // Core's setter would become `init`, which compiles in Core but breaks Host's `s.Retries = 3`.
        var references = await Lint.References.ResolveAsync(LanguageNames.CSharp, default);
        var core = ProjectId.CreateNewId();
        var host = ProjectId.CreateNewId();
        var coreDoc = DocumentId.CreateNewId(core);
        ProjectInfo Project(ProjectId id, string name, OutputKind kind) => ProjectInfo.Create(id, VersionStamp.Default, name, name,
            LanguageNames.CSharp, compilationOptions: new CSharpCompilationOptions(kind),
            parseOptions: new CSharpParseOptions(Lint.CSharp15), metadataReferences: references);

        var solution = new AdhocWorkspace().CurrentSolution
            .AddProject(Project(core, "Core", OutputKind.DynamicallyLinkedLibrary))
            .AddDocument(coreDoc, "Settings.cs", SourceText.From("public record Settings { public int Retries { get; set; } }"), filePath: "/core/Settings.cs")
            .AddProject(Project(host, "Host", OutputKind.DynamicallyLinkedLibrary))
            .AddProjectReference(host, new ProjectReference(core))
            .AddDocument(DocumentId.CreateNewId(host), "Use.cs",
                SourceText.From("public static class Use { public static void M(Settings s) { s.Retries = 3; } }"), filePath: "/host/Use.cs");

        var result = await Healer.HealAsync(solution.GetProject(core)!);

        Assert.Contains("set;", await Text(result, coreDoc));
        Assert.Empty(result.Fixed);
        Assert.Contains(result.Remaining, d => d.Id == "FP0003");
        Assert.False(await Healer.IsFixableAsync(solution.GetProject(core)!,
            (await Healer.DiagnoseAsync(solution.GetProject(core)!)).Single(d => d.Id == "FP0003")));
    }

    [Fact]
    public async Task Leaves_C_sharp_15_code_alone()
    {
        var source = """
            public closed record Shape;
            public sealed record Circle(double Radius) : Shape;
            public static class Use { public static double R(Shape s) => s switch { Circle c => c.Radius }; }
            """;
        var (project, doc) = await Load(source);

        var result = await Healer.HealAsync(project);

        Assert.Equal(source, await Text(result, doc));
        Assert.Empty(result.Remaining);
    }

    [Fact]
    public async Task Does_not_fix_a_file_it_cannot_parse()
    {
        var source = "public class Order { public int Id { get; set; }";
        var (project, doc) = await Load(source);

        var result = await Healer.HealAsync(project);

        Assert.Equal(source, await Text(result, doc));
        Assert.Empty(result.Fixed);
        Assert.Contains("syntax error", result.NotFixedBecause);
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

    [Fact]
    public async Task Leaves_fixes_that_would_change_meaning_and_does_not_mark_them()
    {
        // Host compares Tags, so making Tag a record would change Host's Distinct(). The enumerator is a mutable
        // struct: readonly would make MoveNext() run on a copy.
        var references = await Lint.References.ResolveAsync(LanguageNames.CSharp, default);
        var core = ProjectId.CreateNewId();
        var host = ProjectId.CreateNewId();
        var coreDoc = DocumentId.CreateNewId(core);
        ProjectInfo Project(ProjectId id, string name) => ProjectInfo.Create(id, VersionStamp.Default, name, name,
            LanguageNames.CSharp, compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
            parseOptions: new CSharpParseOptions(Lint.CSharp15), metadataReferences: references);
        var source = """
            public class Tag { public string Name { get; init; } = ""; }
            public sealed record Walker
            {
                private System.Collections.Immutable.ImmutableArray<int>.Enumerator _items = default;
                public bool Next() => _items.MoveNext();
            }
            """;

        var solution = new AdhocWorkspace().CurrentSolution
            .AddProject(Project(core, "Core"))
            .AddDocument(coreDoc, "Tag.cs", SourceText.From(source), filePath: "/core/Tag.cs")
            .AddProject(Project(host, "Host"))
            .AddProjectReference(host, new ProjectReference(core))
            .AddDocument(DocumentId.CreateNewId(host), "Use.cs",
                SourceText.From("using System.Linq; public static class Use { public static int M(Tag[] tags) => tags.Distinct().Count(); }"),
                filePath: "/host/Use.cs");

        var result = await Healer.HealAsync(solution.GetProject(core)!);

        Assert.Equal(source, await Text(result, coreDoc));
        Assert.Empty(result.Fixed);
        var remaining = (await Healer.DiagnoseAsync(solution.GetProject(core)!)).Where(d => d.Id is "FP0003" or "FP0004").ToList();
        Assert.Equal(["FP0003", "FP0004"], remaining.Select(d => d.Id).Order());
        foreach (var diagnostic in remaining)
            Assert.False(await Healer.IsFixableAsync(solution.GetProject(core)!, diagnostic));
    }
}
