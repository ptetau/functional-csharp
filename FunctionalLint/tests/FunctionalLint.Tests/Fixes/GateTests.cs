using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FunctionalLint.Heal;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace FunctionalLint.Tests.Fixes;

/// <summary>The strict gate: what heal reports under a functionallint.policy, beyond the build.</summary>
public class GateTests : IDisposable
{
    private readonly string _folder = TempFolder.Create("gate");

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private const string PolicyText = """
        is_global = true
        global_level = 200
        # comment
        functional_lint.cores = src/*.Core/*.csproj, lib/Shared.csproj
        functional_lint.may_lower = none
        functional_lint.legacy_namespaces = none
        functional_lint.adapter_namespaces = App.Adapters
        functional_lint.allowed_references = Functional, Acme.*   ; trailing comment
        functional_lint.gate_only = lib/Shared.csproj
        """;

    // An in-memory core project whose files sit in a temp folder under a policy.
    private async Task<Project> Core(Dictionary<string, string> files, string? editorconfig = null,
        Func<CSharpCompilationOptions, CSharpCompilationOptions>? options = null)
    {
        File.WriteAllText(Path.Combine(_folder, Policy.FileName), PolicyText);
        var dir = Path.Combine(_folder, "src", "App.Core");
        Directory.CreateDirectory(dir);

        var references = await Lint.References.ResolveAsync(LanguageNames.CSharp, default);
        var id = ProjectId.CreateNewId();
        var compilation = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable);
        var solution = new AdhocWorkspace().CurrentSolution.AddProject(ProjectInfo.Create(id, VersionStamp.Default, "App.Core", "App.Core",
            LanguageNames.CSharp, filePath: Path.Combine(dir, "App.Core.csproj"),
            compilationOptions: options?.Invoke(compilation) ?? compilation,
            parseOptions: new CSharpParseOptions(Lint.CSharp15), metadataReferences: references));
        foreach (var (name, text) in files)
            solution = solution.AddDocument(DocumentId.CreateNewId(id), name, SourceText.From(text), filePath: Path.Combine(dir, name));
        if (editorconfig is not null)
            solution = solution.AddAnalyzerConfigDocument(DocumentId.CreateNewId(id), ".editorconfig",
                SourceText.From("root = true\n\n[*.cs]\n" + editorconfig + "\n"), filePath: Path.Combine(dir, ".editorconfig"));
        return solution.GetProject(id)!;
    }

    private static async Task<List<string>> Messages(Project project) =>
        (await Healer.DiagnoseAsync(project)).Where(d => d.Id.StartsWith("FP", StringComparison.Ordinal))
            .Select(d => $"{d.Id}: {d.GetMessage()}").ToList();

    [Fact]
    public void The_policy_reads_lists_globs_and_comments()
    {
        var policy = Policy.Parse(Path.Combine(_folder, Policy.FileName), PolicyText);
        Assert.Equal(["Functional", "Acme.*"], policy.AllowedReferences.ToArray());
        Assert.Empty(policy.MayLower);
        Assert.Empty(policy.LegacyNamespaces);
        Assert.True(policy.IsCore(Path.Combine(_folder, "src", "Ledger.Core", "Ledger.Core.csproj")));
        Assert.True(policy.IsCore(Path.Combine(_folder, "lib", "Shared.csproj")));
        Assert.False(policy.IsCore(Path.Combine(_folder, "src", "Ledger.Cli", "Ledger.Cli.csproj")));
        Assert.False(policy.IsCore(Path.Combine(_folder, "src", "deep", "X.Core", "X.Core.csproj")));
        Assert.True(policy.IsGateOnly(Path.Combine(_folder, "lib", "Shared.csproj")));
        Assert.False(policy.IsGateOnly(Path.Combine(_folder, "src", "Ledger.Core", "Ledger.Core.csproj")));
    }

    [Fact]
    public void A_policy_from_elsewhere_still_reads_paths_from_the_repository()
    {
        // CI saves main's policy in a temp folder and names it. Its core paths are still the repository's.
        File.WriteAllText(Path.Combine(_folder, Policy.FileName), "functional_lint.cores = none\n");
        var mains = Path.Combine(TempFolder.Create("mains"), Policy.FileName);
        File.WriteAllText(mains, PolicyText);

        var policy = Policy.For(Path.Combine(_folder, "src", "App.Core", "App.Core.csproj"), named: mains)!;
        Assert.Equal(mains, policy.File);
        Assert.True(policy.IsCore(Path.Combine(_folder, "src", "App.Core", "App.Core.csproj")));
        File.Delete(mains);
    }

    [Fact]
    public async Task A_pragma_is_reported_even_when_it_also_switches_FP0016_off()
    {
        var project = await Core(new() { ["Prices.cs"] = """
            #pragma warning disable FP0016, FP0001
            public static class Prices { public static int Total(int[] xs) { var t = 0; foreach (var x in xs) t += x; return t; } }
            """ });
        Assert.Equal(["FP0016: '#pragma warning disable FP0016, FP0001' turns checks off. Remove it and fix the code it hides."],
            await Messages(project));
    }

    [Fact]
    public async Task Lowered_rules_nullable_off_and_generated_files_are_reported()
    {
        var project = await Core(
            new() { ["Prices.cs"] = "public static class Prices { }", ["Totals.g.cs"] = "public static class Totals { }" },
            editorconfig: "dotnet_diagnostic.FP0001.severity = warning",
            options: o => o.WithSpecificDiagnosticOptions([new("FP0002", ReportDiagnostic.Suppress)])
                           .WithNullableContextOptions(NullableContextOptions.Disable));

        Assert.Equal(new[]
        {
            "FP0016: 'Totals.g.cs' is skipped as generated code. Remove the marker (<auto-generated> header, .g.cs name or generated_code).",
            "FP0016: 'FP0001' is lowered to 'warning' in 'App.Core' (NoWarn, .editorconfig or .globalconfig). Remove that setting.",
            "FP0016: 'FP0002' is lowered to 'none' in 'App.Core' (NoWarn, .editorconfig or .globalconfig). Remove that setting.",
            "FP0016: 'App.Core' has nullable checking turned off. Remove the <Nullable> setting that turns it off.",
        }.Order(), (await Messages(project)).Order());
    }

    [Fact]
    public async Task The_policy_beats_the_project_config_for_exemptions_and_adapters()
    {
        // The project's .editorconfig calls its own namespace legacy (so every rule would skip it) and allows
        // adapters anywhere. Under the policy, neither holds.
        var project = await Core(new() { ["Prices.cs"] = Lint.AdapterAttribute + """

            namespace App.Core
            {
                public static class Prices { public static int Total(int[] xs) { var t = 0; foreach (var x in xs) t += x; return t; } }
                [ImperativeAdapter] public static class Clock { }
            }
            """ },
            editorconfig: "functional_lint.legacy_namespaces = App\nfunctional_lint.adapter_namespaces = App.Core");

        var messages = await Messages(project);
        Assert.Contains(messages, m => m.StartsWith("FP0001: 'foreach' loops are not allowed", StringComparison.Ordinal));
        Assert.Contains("FP0017: 'Clock' is an [ImperativeAdapter] outside 'App.Adapters'. Move it there, or pass the value in.", messages);
    }

    [Fact]
    public async Task Without_a_policy_only_the_builds_own_checks_apply()
    {
        // The build still reports the pragma; the gate's project-level checks need a policy.
        var project = await Core(new() { ["Prices.cs"] = "#pragma warning disable CS0168\npublic static class Prices { }", ["Totals.g.cs"] = "" },
            editorconfig: "dotnet_diagnostic.FP0001.severity = none");
        File.Delete(Path.Combine(_folder, Policy.FileName));
        Assert.Equal(["FP0016: '#pragma warning disable CS0168' turns checks off. Remove it and fix the code it hides."], await Messages(project));
    }
}
