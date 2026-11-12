using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace FunctionalLint.Tests.Rules;

public class FP0018_ReferencesOutsidePolicy
{
    // A library the core references: a project reference when Path is null, else a file at that path. DotNetKey
    // signs it with the key of .NET's own assemblies.
    private sealed record Library(string Name, string? Path = null, bool DotNetKey = false);

    // A core that references its base library plus the given libraries. Returns the FP0018 messages.
    private static async Task<string[]> Findings(string? allowed, ReferenceAssemblies? framework = null, params Library[] libraries)
    {
        var references = await (framework ?? Lint.References).ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        var dotNetKey = CSharpCompilation.Create("Probe", [], references)
            .GetSpecialType(SpecialType.System_Object).ContainingAssembly.Identity.PublicKey;

        MetadataReference Reference(Library library)
        {
            var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary);
            if (library.DotNetKey) options = options.WithCryptoPublicKey(dotNetKey).WithPublicSign(true);
            var compilation = CSharpCompilation.Create(library.Name,
                [CSharpSyntaxTree.ParseText($"namespace {library.Name} {{ public static class Api {{ }} }}")], references, options);
            if (library.Path is null) return compilation.ToMetadataReference();
            using var image = new System.IO.MemoryStream();
            Assert.True(compilation.Emit(image).Success);
            return MetadataReference.CreateFromImage(image.ToArray(), filePath: library.Path);
        }

        var uses = string.Join(" ", libraries.Select((l, i) => $"static object M{i}() => typeof({l.Name}.Api);"));
        var core = CSharpCompilation.Create("Core", [CSharpSyntaxTree.ParseText($"public static class C {{ {uses} }}")],
            references.AddRange(libraries.Select(Reference)), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var config = allowed is null ? "" : "functional_lint.allowed_references = " + allowed;
        var options = new AnalyzerOptions([], new GlobalConfig(config));
        var found = await core.WithAnalyzers([new FunctionalAnalyzer()], options).GetAnalyzerDiagnosticsAsync();
        return found.Where(d => d.Id == "FP0018").Select(d => d.GetMessage()).ToArray();
    }

    private static readonly Library[] OwnLibraries = [new("Acme.Db"), new("Functional")];

    [Fact]
    public async Task Libraries_outside_the_list_are_reported_and_the_base_library_is_not()
    {
        Assert.Equal(
            ["'Acme.Db' isn't on this core's list of allowed references. Move the code that uses it to a shell project."],
            await Findings("Functional", libraries: OwnLibraries));
    }

    [Fact]
    public async Task A_trailing_star_allows_a_family_and_no_key_means_no_check()
    {
        Assert.Empty(await Findings("Functional, Acme.*", libraries: OwnLibraries));
        Assert.Empty(await Findings(null, libraries: OwnLibraries));
        Assert.Equal(2, (await Findings("none", libraries: OwnLibraries)).Length);
    }

    [Fact]
    public async Task A_netstandard_core_may_use_the_NETStandard_Library_pack()
    {
        Assert.Equal(
            ["'Acme.Db' isn't on this core's list of allowed references. Move the code that uses it to a shell project."],
            await Findings("none", ReferenceAssemblies.NetStandard.NetStandard20, new Library("Acme.Db")));
    }

    [Fact]
    public async Task A_library_in_a_folder_named_like_the_pack_is_still_reported()
    {
        // The real pack folder, wherever this machine keeps it.
        var references = await Lint.References.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        var pack = System.IO.Path.GetDirectoryName(references.OfType<PortableExecutableReference>()
            .First(r => System.IO.Path.GetFileName(r.FilePath) == "System.Runtime.dll").FilePath)!;

        var found = await Findings("none", libraries:
        [
            // Copied into the repository, in a folder named after the pack.
            new("Newtonsoft.Json", "/repo/libs/microsoft.netcore.app.ref/Newtonsoft.Json.dll"),
            // Even signed with .NET's key: that folder isn't where this core's base library comes from.
            new("Acme.Signed", "/repo/libs/microsoft.netcore.app.ref/11.0.0/ref/net11.0/Acme.Signed.dll", DotNetKey: true),
            // Copied into the real pack, but not signed by .NET.
            new("Acme.Copied", System.IO.Path.Combine(pack, "Acme.Copied.dll")),
            // In the real pack and signed with .NET's key: as far as a compilation can tell, part of .NET.
            new("Acme.InPack", System.IO.Path.Combine(pack, "Acme.InPack.dll"), DotNetKey: true),
        ]);
        Assert.Equal(["Acme.Copied", "Acme.Signed", "Newtonsoft.Json"], found.Select(m => m.Split('\'')[1]).Order());
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
