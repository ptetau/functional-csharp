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
