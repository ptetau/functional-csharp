using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace FunctionalLint.Heal;

/// <summary>
/// The repository owner's rules for the gate, read from <c>functionallint.policy</c> in a folder at or above the
/// project (or the file FUNCTIONALLINT_POLICY names, which CI sets to the base branch's copy). The file is a
/// globalconfig, so the build reads the same values. It says which projects are functional cores, where adapters
/// and legacy code may live, what cores may reference, and which rules may be lowered or suppressed (normally none).
/// Without a policy file nothing here applies, and heal behaves as before.
/// </summary>
public sealed record Policy(
    string File,
    string Root,
    ImmutableArray<string> Cores,
    ImmutableArray<string> MayLower,
    ImmutableArray<string> LegacyNamespaces,
    ImmutableArray<string> AdapterNamespaces,
    ImmutableArray<string> AllowedReferences,
    ImmutableArray<string> ImpureApis)
{
    public const string FileName = "functionallint.policy";

    private static readonly ConcurrentDictionary<string, (DateTime Stamp, Policy? Policy)> Cache = new(StringComparer.Ordinal);

    /// <summary>The policy that governs <paramref name="path"/> (a project or source file), or null.</summary>
    public static Policy? For(string? path, string? named = null)
    {
        var local = path is null ? null : FindAbove(Path.GetDirectoryName(Path.GetFullPath(path))!);
        named ??= Environment.GetEnvironmentVariable("FUNCTIONALLINT_POLICY");
        var file = named is { Length: > 0 } ? Path.GetFullPath(named) : local;
        if (file is null || !System.IO.File.Exists(file)) return null;

        // Paths in the policy are relative to the repository: where its own copy lives, even when the rules come
        // from another copy (CI uses main's, saved in a temp folder). Without one, the folder heal runs in.
        var root = local is not null ? Path.GetDirectoryName(local)! : Environment.CurrentDirectory;
        var stamp = System.IO.File.GetLastWriteTimeUtc(file);
        var key = file + "|" + root;
        if (Cache.TryGetValue(key, out var cached) && cached.Stamp == stamp) return cached.Policy;
        var policy = Parse(file, System.IO.File.ReadAllText(file), root);
        Cache[key] = (stamp, policy);
        return policy;
    }

    public static Policy? For(Project project) => For(project.FilePath ?? project.Documents.FirstOrDefault()?.FilePath);

    private static string? FindAbove(string folder)
    {
        for (var dir = new DirectoryInfo(folder); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, FileName);
            if (System.IO.File.Exists(candidate)) return candidate;
        }
        return null;
    }

    /// <summary>Reads the globalconfig's <c>functional_lint.*</c> keys. Other keys, comments and sections are ignored.</summary>
    public static Policy Parse(string file, string text, string? root = null)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith('[')) break;   // a section: only the global part counts
            if (line.Length == 0 || line[0] is '#' or ';') continue;
            var equals = line.IndexOf('=');
            if (equals > 0) values[line[..equals].Trim()] = StripComment(line[(equals + 1)..]).Trim();
        }

        ImmutableArray<string> List(string key) => values.TryGetValue("functional_lint." + key, out var value)
            ? [.. value.Split(',').Select(v => v.Trim()).Where(v => v.Length > 0 && !v.Equals("none", StringComparison.OrdinalIgnoreCase))]
            : [];

        return new Policy(file, root ?? Path.GetDirectoryName(file)!, List("cores"), List("may_lower"), List("legacy_namespaces"), List("adapter_namespaces"),
            List("allowed_references"), List("impure_apis"));
    }

    private static string StripComment(string value)
    {
        var at = value.IndexOfAny(['#', ';']);
        return at >= 0 ? value[..at] : value;
    }

    /// <summary>True if the policy lists this project as a functional core (paths or globs, relative to the policy file).</summary>
    public bool IsCore(string? projectPath)
    {
        if (projectPath is null) return false;
        var relative = Path.GetRelativePath(Root, Path.GetFullPath(projectPath)).Replace('\\', '/');
        return Cores.Any(glob => Glob(glob).IsMatch(relative));
    }

    private static readonly ConcurrentDictionary<string, Regex> Globs = new(StringComparer.Ordinal);

    private static Regex Glob(string glob) => Globs.GetOrAdd(glob, g =>
    {
        var pattern = Regex.Escape(g.Replace('\\', '/').TrimStart('/'))
            .Replace(@"\*\*/", "(.*/)?").Replace(@"\*\*", ".*").Replace(@"\*", "[^/]*").Replace(@"\?", "[^/]");
        var options = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? RegexOptions.IgnoreCase : RegexOptions.None;
        return new Regex("^" + pattern + "$", options);
    });

    /// <summary>
    /// The project's analyzer options, with the policy's values in place of whatever the project sets for the same
    /// keys: an .editorconfig section beats a globalconfig at build time, but not here. Extra impure APIs only add.
    /// </summary>
    public AnalyzerOptions Apply(AnalyzerOptions options) =>
        new(options.AdditionalFiles, new OverridingProvider(options.AnalyzerConfigOptionsProvider, this));

    private ImmutableDictionary<string, string> Overrides(AnalyzerConfigOptions inner)
    {
        var impure = inner.TryGetValue("functional_lint.impure_apis", out var own) ? own : "";
        string Join(ImmutableArray<string> list) => list.IsEmpty ? "none" : string.Join(",", list);
        return ImmutableDictionary.CreateRange(StringComparer.OrdinalIgnoreCase, new Dictionary<string, string>
        {
            ["functional_lint.legacy_namespaces"] = Join(LegacyNamespaces),
            ["functional_lint.adapter_namespaces"] = Join(AdapterNamespaces),
            ["functional_lint.allowed_references"] = Join(AllowedReferences),
            ["functional_lint.impure_apis"] = string.Join(",", ImpureApis.Append(impure).Where(v => v.Length > 0)),
        });
    }

    private sealed class OverridingProvider(AnalyzerConfigOptionsProvider inner, Policy policy) : AnalyzerConfigOptionsProvider
    {
        public override AnalyzerConfigOptions GlobalOptions => Wrap(inner.GlobalOptions);
        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => Wrap(inner.GetOptions(tree));
        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => Wrap(inner.GetOptions(textFile));
        private OverridingOptions Wrap(AnalyzerConfigOptions options) => new(options, policy.Overrides(options));
    }

    private sealed class OverridingOptions(AnalyzerConfigOptions inner, ImmutableDictionary<string, string> overrides) : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? value) =>
            overrides.TryGetValue(key, out value) || inner.TryGetValue(key, out value);

        public override IEnumerable<string> Keys => inner.Keys.Concat(overrides.Keys).Distinct(StringComparer.OrdinalIgnoreCase);
    }
}
