using System.Linq;
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
    public void Package_ships_the_rule_settings_next_to_its_props()
    {
        Assert.Contains("""<GlobalAnalyzerConfigFiles Include="$(MSBuildThisFileDirectory)FunctionalCore.globalconfig" />""",
            Repo.Read("build", "FunctionalCore.Settings.props"));
        Assert.Contains("""PackagePath="build\FunctionalCore.globalconfig" />""",
            Repo.Read("src", "FunctionalLint.Package", "FunctionalLint.Package.csproj"));
    }

    [Fact]
    public void Rules_you_lower_to_warnings_stay_warnings()
    {
        // TreatWarningsAsErrors is on, so every rule a project might set to "warning" while migrating must be
        // listed in WarningsNotAsErrors, or lowering it would do nothing.
        var settings = Repo.Read("build", "FunctionalCore.Settings.props");
        var exempt = System.Text.RegularExpressions.Regex.Match(settings, "<WarningsNotAsErrors>(.*?)</WarningsNotAsErrors>").Groups[1].Value.Split(';');
        var configured = System.Text.RegularExpressions.Regex.Matches(Repo.Read("build", "FunctionalCore.globalconfig"), @"dotnet_diagnostic\.(\w+)\.severity")
            .Select(m => m.Groups[1].Value);
        var ours = new FunctionalAnalyzer().SupportedDiagnostics.Select(d => d.Id);

        Assert.All(ours.Concat(configured), id => Assert.Contains(id, exempt));
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
