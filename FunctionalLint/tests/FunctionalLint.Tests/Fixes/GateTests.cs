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

    // An in-memory core project whose files sit in a temp folder under a policy. Its compiler options are the ones
    // FunctionalCore.props sets: nullable on, and every warning an error.
    private async Task<Project> Core(Dictionary<string, string> files, string? editorconfig = null,
        Func<CSharpCompilationOptions, CSharpCompilationOptions>? options = null, string? projectFile = null)
    {
        File.WriteAllText(Path.Combine(_folder, Policy.FileName), PolicyText);
        var dir = Path.Combine(_folder, "src", "App.Core");
        Directory.CreateDirectory(dir);
        if (projectFile is not null) File.WriteAllText(Path.Combine(dir, "App.Core.csproj"), projectFile);

        var references = await Lint.References.ResolveAsync(LanguageNames.CSharp, default);
        var id = ProjectId.CreateNewId();
        var compilation = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable,
            generalDiagnosticOption: ReportDiagnostic.Error);
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

    [Fact]
    public void The_policy_reads_colons_like_a_globalconfig()
    {
        var policy = Policy.Parse(Path.Combine(_folder, Policy.FileName), "functional_lint.cores : src/*.Core/*.csproj\nfunctional_lint.may_lower : FP0001");
        Assert.Equal(["FP0001"], policy.MayLower.ToArray());
        Assert.True(policy.IsCore(Path.Combine(_folder, "src", "App.Core", "App.Core.csproj")));
    }

    [Fact]
    public async Task The_policy_beats_the_project_config_for_may_lower()
    {
        // The project's .editorconfig allows FP0001 to be suppressed; the policy allows nothing.
        var project = await Core(new() { ["Prices.cs"] = "public static class Prices { }" }, editorconfig: "functional_lint.may_lower = FP0001");
        var tree = (await project.GetCompilationAsync())!.SyntaxTrees.First();
        var options = Policy.For(project)!.Apply(project.AnalyzerOptions).AnalyzerConfigOptionsProvider.GetOptions(tree);
        Assert.True(options.TryGetValue("functional_lint.may_lower", out var mayLower));
        Assert.Equal("none", mayLower);
    }

    [Fact]
    public void A_named_policy_that_is_missing_fails_closed()
    {
        // FUNCTIONALLINT_POLICY pointing nowhere mustn't quietly drop the repository's own policy.
        File.WriteAllText(Path.Combine(_folder, Policy.FileName), PolicyText);
        var missing = Path.Combine(_folder, "nowhere", Policy.FileName);
        var error = Assert.Throws<PolicyNotFoundException>(() => Policy.For(Path.Combine(_folder, "src", "App.Core", "App.Core.csproj"), named: missing));
        Assert.Contains(missing, error.Message);
    }

    [Fact]
    public async Task Heal_exits_2_when_the_named_policy_is_missing()
    {
        File.WriteAllText(Path.Combine(_folder, "App.csproj"), "<Project />");
        File.WriteAllText(Path.Combine(_folder, "Prices.cs"), "public static class Prices { }");
        var heal = Path.Combine(AppContext.BaseDirectory, "FunctionalLint.Heal.dll");

        async Task<(int Code, string Error)> Run(string arguments, string input)
        {
            var start = new System.Diagnostics.ProcessStartInfo("dotnet", $"\"{heal}\" {arguments}")
            {
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = _folder,
            };
            start.Environment[Policy.EnvironmentVariable] = Path.Combine(_folder, "nowhere.policy");
            start.Environment["FUNCTIONALLINT_NO_SERVER"] = "1";
            using var process = System.Diagnostics.Process.Start(start)!;
            await process.StandardInput.WriteAsync(input);
            process.StandardInput.Close();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            return (process.ExitCode, error);
        }

        var check = await Run("App.csproj --check", "");
        Assert.Equal(2, check.Code);
        Assert.Contains("nowhere.policy' named by FUNCTIONALLINT_POLICY doesn't exist", check.Error);

        var edited = Path.Combine(_folder, "Prices.cs").Replace('\\', '/');
        var hook = await Run("--hook", "{\"tool_input\":{\"file_path\":\"" + edited + "\"}}");
        Assert.Equal(2, hook.Code);
        Assert.Contains("doesn't exist", hook.Error);
    }

    [Fact]
    public async Task Suppressions_through_an_alias_generated_code_and_hidden_lines_are_reported()
    {
        var project = await Core(new()
        {
            ["Aliases.cs"] = "global using Quiet = System.Diagnostics.CodeAnalysis.SuppressMessageAttribute;",
            ["Prices.cs"] = """
                public static class Prices
                {
                    [Quiet("Functional", "FP0001")]
                    public static int A() => 1;

                    [System.CodeDom.Compiler.GeneratedCode("tool", "1.0")]
                    public static int B() => 2;

                #line hidden
                    public static int C() => 3;
                #line default
                }
                """,
        });
        Assert.Equal(new[]
        {
            "FP0016: '[Quiet(\"Functional\", \"FP0001\")]' turns checks off. Remove it and fix the code it hides.",
            "FP0016: '[System.CodeDom.Compiler.GeneratedCode(\"tool\", \"1.0\")]' makes every analyzer skip what it marks. Remove it and fix the code it hides.",
            "FP0016: '#line hidden' hides the code after it from every analyzer. Remove it and fix the code it hides.",
        }.Order(), (await Messages(project)).Order());
    }

    [Fact]
    public async Task Lowered_compiler_checks_and_culture_rules_are_reported_unless_the_policy_allows_them()
    {
        var project = await Core(new() { ["Prices.cs"] = "public static class Prices { }" },
            editorconfig: "dotnet_diagnostic.CA1305.severity = none",
            options: o => o.WithSpecificDiagnosticOptions([new("CS8509", ReportDiagnostic.Suppress), new("CS8602", ReportDiagnostic.Suppress),
                new("CS8618", ReportDiagnostic.Warn), new("CS8625", ReportDiagnostic.Suppress), new("CA1304", ReportDiagnostic.Suppress)]));
        Assert.Equal(new[]
        {
            "FP0016: 'CA1304' is lowered to 'none' in 'App.Core' (NoWarn, .editorconfig or .globalconfig). Remove that setting.",
            "FP0016: 'CA1305' is lowered to 'none' in 'App.Core' (NoWarn, .editorconfig or .globalconfig). Remove that setting.",
            "FP0016: 'CS8509' is lowered to 'none' in 'App.Core' (NoWarn, .editorconfig or .globalconfig). Remove that setting.",
            "FP0016: 'CS8602, CS8625' is lowered to 'none' in 'App.Core' (NoWarn, .editorconfig or .globalconfig). Remove that setting.",
            "FP0016: 'CS8618' is lowered to 'warning' in 'App.Core' (NoWarn, .editorconfig or .globalconfig). Remove that setting.",
        }.Order(), (await Messages(project)).Order());

        // "nullable" in may_lower covers every nullable warning; a rule ID covers itself.
        var policyFile = Path.Combine(_folder, Policy.FileName);
        File.WriteAllText(policyFile, PolicyText.Replace("may_lower = none", "may_lower = nullable, CS8509, CA1304, CA1305"));
        File.SetLastWriteTimeUtc(policyFile, DateTime.UtcNow.AddSeconds(1));
        Assert.Empty(await Messages(project));
    }

    [Fact]
    public async Task Warnings_that_are_no_longer_errors_are_reported()
    {
        // TreatWarningsAsErrors and the props file's WarningsAsErrors taken away: CS8509 and nullable warnings pass.
        var project = await Core(new() { ["Prices.cs"] = "public static class Prices { }" },
            options: o => o.WithGeneralDiagnosticOption(ReportDiagnostic.Default));
        Assert.Equal(new[]
        {
            "FP0016: 'CS8509' isn't an error in 'App.Core'. Restore TreatWarningsAsErrors and the WarningsAsErrors that FunctionalCore.props sets.",
            "FP0016: 'nullable' isn't an error in 'App.Core'. Restore TreatWarningsAsErrors and the WarningsAsErrors that FunctionalCore.props sets.",
        }, await Messages(project));
    }

    [Fact]
    public async Task Turning_analyzers_off_is_reported()
    {
        var project = await Core(new() { ["Prices.cs"] = "public static class Prices { }" }, projectFile: """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <RunAnalyzersDuringBuild>false</RunAnalyzersDuringBuild>
                <RunAnalyzers>true</RunAnalyzers>
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(_folder, "src", "Directory.Build.props"),
            "<Project><PropertyGroup><RunAnalyzers>$(Quiet)</RunAnalyzers></PropertyGroup></Project>");
        Assert.Equal(new[]
        {
            "FP0016: 'RunAnalyzersDuringBuild' is 'false' for 'App.Core', so its build runs no analyzers. Remove that setting.",
            "FP0016: 'RunAnalyzers' is '$(Quiet)' for 'App.Core', so its build runs no analyzers. Remove that setting.",
        }.Order(), (await Messages(project)).Order());
    }

    [Fact]
    public async Task A_policy_file_below_the_repository_root_is_reported_and_doesnt_move_the_root()
    {
        // The repository is _folder (it has .git). An empty policy next to the core would make the build use it, and
        // used to make the gate read core paths from there, so App.Core stopped being a core.
        Directory.CreateDirectory(Path.Combine(_folder, ".git"));
        var project = await Core(new() { ["Prices.cs"] = "public static class Prices { }" });
        var nested = Path.Combine(_folder, "src", Policy.FileName);
        File.WriteAllText(nested, "is_global = true\n");

        var projectFile = Path.Combine(_folder, "src", "App.Core", "App.Core.csproj");
        foreach (var named in new[] { null, Path.Combine(_folder, Policy.FileName) })
        {
            var policy = Policy.For(projectFile, named)!;
            Assert.Equal(_folder, policy.Root);
            Assert.True(policy.IsCore(projectFile));
        }
        Assert.Equal(["FP0016: 'src/functionallint.policy' would replace the root policy for 'App.Core' in the build. Remove it, and change the root policy instead."],
            await Messages(project));

    }
}
