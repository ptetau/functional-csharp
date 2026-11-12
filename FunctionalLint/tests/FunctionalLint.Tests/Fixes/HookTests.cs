using System.Threading.Tasks;
using System.Diagnostics;
using System.IO;
using FunctionalLint.Heal;
using Xunit;

namespace FunctionalLint.Tests.Fixes;

/// <summary>How the agent hook reads Claude Code's PostToolUse input and finds the project.</summary>
public class HookTests
{
    [Fact]
    public void Reads_the_edited_file_from_the_hook_input() =>
        Assert.Equal("/repo/src/Core/Orders.cs",
            Hook.EditedFile("""{"tool_name":"Edit","cwd":"/repo","tool_input":{"file_path":"/repo/src/Core/Orders.cs","old_string":"a"}}"""));

    [Fact]
    public void Resolves_a_relative_path_against_cwd() =>
        Assert.Equal(Path.Combine("/repo", "src/Orders.cs"),
            Hook.EditedFile("""{"cwd":"/repo","tool_input":{"file_path":"src/Orders.cs"}}"""));

    [Theory]
    [InlineData("""{"tool_name":"Bash","tool_input":{"command":"ls"}}""")]
    [InlineData("""{"tool_input":"not an object"}""")]
    [InlineData("not json")]
    [InlineData("")]
    public void Ignores_input_without_a_file(string json) => Assert.Null(Hook.EditedFile(json));

    [Fact]
    public void Finds_the_nearest_project_above_the_file()
    {
        var root = Directory.CreateTempSubdirectory("hook").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "Core", "Domain"));
            File.WriteAllText(Path.Combine(root, "App.csproj"), "");
            File.WriteAllText(Path.Combine(root, "Core", "Core.csproj"), "");

            Assert.Equal(Path.Combine(root, "Core", "Core.csproj"), Hook.ProjectFor(Path.Combine(root, "Core", "Domain", "Order.cs")));
            Assert.Equal(Path.Combine(root, "App.csproj"), Hook.ProjectFor(Path.Combine(root, "Program.cs")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

public class HookExitTests
{
    [Fact]
    public async Task Warnings_elsewhere_dont_fail_an_edit_but_problems_in_the_edited_file_do()
    {
        // FP0003 lowered to a warning while migrating, as the README suggests.
        var references = await Lint.References.ResolveAsync(Microsoft.CodeAnalysis.LanguageNames.CSharp, default);
        var id = Microsoft.CodeAnalysis.ProjectId.CreateNewId();
        var clean = Microsoft.CodeAnalysis.DocumentId.CreateNewId(id);
        var solution = new Microsoft.CodeAnalysis.AdhocWorkspace().CurrentSolution
            .AddProject(Microsoft.CodeAnalysis.ProjectInfo.Create(id, Microsoft.CodeAnalysis.VersionStamp.Default, "Core", "Core",
                Microsoft.CodeAnalysis.LanguageNames.CSharp,
                compilationOptions: new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary),
                parseOptions: new Microsoft.CodeAnalysis.CSharp.CSharpParseOptions(Lint.CSharp15), metadataReferences: references))
            .AddDocument(clean, "Clean.cs", Microsoft.CodeAnalysis.Text.SourceText.From("public sealed record Ok(int X);"), filePath: "/src/Clean.cs")
            .AddDocument(Microsoft.CodeAnalysis.DocumentId.CreateNewId(id), "Old.cs",
                Microsoft.CodeAnalysis.Text.SourceText.From("public record Old { public int Y { get; set; } }"), filePath: "/src/Old.cs")
            .AddAnalyzerConfigDocument(Microsoft.CodeAnalysis.DocumentId.CreateNewId(id), ".editorconfig",
                Microsoft.CodeAnalysis.Text.SourceText.From("root = true\n[*.cs]\ndotnet_diagnostic.FP0003.severity = warning\n"), filePath: "/src/.editorconfig");

        var editingClean = await Checker.CheckAsync(solution, [id], "/src/Clean.cs", "/src", Stopwatch.StartNew());
        Assert.Equal(0, editingClean.Code);

        var editingOld = await Checker.CheckAsync(solution, [id], "/src/Old.cs", "/src", Stopwatch.StartNew());
        Assert.Equal(1, editingOld.Code);
        Assert.Contains("warning FP0003", editingOld.Report);
    }
}
