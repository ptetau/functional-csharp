using Xunit;

namespace FunctionalLint.Tests.Guidance;

/// <summary>The source setup (FunctionalCore.props) and the NuGet package must give a project the same setup.</summary>
public class PackagingTests
{
    [Fact]
    public void Props_file_and_package_share_one_settings_file()
    {
        Assert.Contains("""<Import Project="$(MSBuildThisFileDirectory)FunctionalCore.Settings.props" />""",
            Repo.Read("build", "FunctionalCore.props"));
        Assert.Contains("""<None Include="..\..\build\FunctionalCore.Settings.props" Pack="true" PackagePath="build\FunctionalLint.props" />""",
            Repo.Read("src", "FunctionalLint.Package", "FunctionalLint.Package.csproj"));
    }

    [Fact]
    public void Both_ship_the_analyzer_and_the_code_fixes()
    {
        var props = Repo.Read("build", "FunctionalCore.props");
        var package = Repo.Read("src", "FunctionalLint.Package", "FunctionalLint.Package.csproj");
        foreach (var assembly in new[] { "FunctionalLint", "FunctionalLint.CodeFixes" })
        {
            Assert.Contains($@"src\{assembly}\{assembly}.csproj", props);
            Assert.Contains($"""<TfmSpecificPackageFile Include="$(OutputPath){assembly}.dll" PackagePath="analyzers/dotnet/cs" />""", package);
        }
    }
}
