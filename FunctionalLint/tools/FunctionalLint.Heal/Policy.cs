using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace FunctionalLint.Heal;

/// <summary>
/// The repository owner's rules for the gate, read from <c>functionallint.policy</c> at the repository root (or the
/// file FUNCTIONALLINT_POLICY names, which CI sets to the base branch's copy). The file is a globalconfig, so the build
/// reads the same values. It says which projects are functional cores, where adapters and legacy code may live, what
/// cores may reference, and which rules may be lowered or suppressed (normally none).
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
    ImmutableArray<string> ImpureApis,
    ImmutableArray<string> GateOnly)
{
    public const string FileName = "functionallint.policy";

    public const string EnvironmentVariable = "FUNCTIONALLINT_POLICY";

    private static readonly ConcurrentDictionary<string, (DateTime Stamp, Policy? Policy)> Cache = new(StringComparer.Ordinal);

    // File systems on Windows and macOS ignore case by default; Linux's don't.
    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>The full path FUNCTIONALLINT_POLICY names, or null when it isn't set.</summary>
    public static string? Named() =>
        Environment.GetEnvironmentVariable(EnvironmentVariable) is { Length: > 0 } named ? Path.GetFullPath(named) : null;

    /// <summary>Throws <see cref="PolicyNotFoundException"/> if FUNCTIONALLINT_POLICY names a file that isn't there.</summary>
    public static void EnsureNamedExists()
    {
        if (Named() is { } named && !System.IO.File.Exists(named)) throw new PolicyNotFoundException(named);
    }

    /// <summary>
    /// The policy that governs <paramref name="path"/> (a project or source file), or null. Throws
    /// <see cref="PolicyNotFoundException"/> when a policy is named but missing: the gate fails closed.
    /// </summary>
    public static Policy? For(string? path, string? named = null)
    {
        var folder = path is null ? null : Path.GetDirectoryName(Path.GetFullPath(path))!;
        // The repository the path is in. Outside git, the topmost policy above the path stands in for it.
        var repository = folder is null ? null : RepositoryRoot(folder);
        var local = folder is null ? null : TopmostAbove(folder, repository);

        named = named is { Length: > 0 } ? Path.GetFullPath(named) : Named();
        if (named is not null && !System.IO.File.Exists(named)) throw new PolicyNotFoundException(named);
        var file = named ?? local;
        if (file is null) return null;

        // Paths in the policy are relative to the repository root, even when the rules come from another copy (CI uses
        // main's, saved in a temp folder). A policy file further down can't move it. Without git, the folder of the
        // topmost policy; without one, the folder heal runs in.
        var root = repository ?? (local is not null ? Path.GetDirectoryName(local)! : Environment.CurrentDirectory);
        var stamp = System.IO.File.GetLastWriteTimeUtc(file);
        var key = file + "|" + root;
        if (Cache.TryGetValue(key, out var cached) && cached.Stamp == stamp) return cached.Policy;
        var policy = Parse(file, System.IO.File.ReadAllText(file), root);
        Cache[key] = (stamp, policy);
        return policy;
    }

    public static Policy? For(Project project) => For(project.FilePath ?? project.Documents.FirstOrDefault()?.FilePath);

    /// <summary>The git top level at or above <paramref name="folder"/>: the nearest folder with a .git folder or file.</summary>
    public static string? RepositoryRoot(string folder)
    {
        for (var dir = new DirectoryInfo(folder); dir is not null; dir = dir.Parent)
        {
            var git = Path.Combine(dir.FullName, ".git");
            if (Directory.Exists(git) || System.IO.File.Exists(git)) return dir.FullName;
        }
        return null;
    }

    // The highest policy file at or above the folder, up to and including the repository root when there is one.
    private static string? TopmostAbove(string folder, string? repository)
    {
        string? found = null;
        for (var dir = new DirectoryInfo(folder); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, FileName);
            if (System.IO.File.Exists(candidate)) found = candidate;
            if (repository is not null && string.Equals(dir.FullName, repository, PathComparison)) break;
        }
        return found;
    }

    /// <summary>
    /// Policy files between <paramref name="projectPath"/>'s folder and the root, other than this policy itself. The
    /// build uses the nearest policy above a project, so one of these would replace the owner's rules there.
    /// </summary>
    public IEnumerable<string> PoliciesBelowRoot(string projectPath)
    {
        for (var dir = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(projectPath))!); dir is not null; dir = dir.Parent)
        {
            if (string.Equals(Path.TrimEndingDirectorySeparator(dir.FullName), Path.TrimEndingDirectorySeparator(Root), PathComparison)) yield break;
            var candidate = Path.Combine(dir.FullName, FileName);
            if (System.IO.File.Exists(candidate) && !string.Equals(candidate, File, PathComparison)) yield return candidate;
        }
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
            // 'key = value' or 'key : value', as Roslyn reads a globalconfig.
            var equals = line.IndexOfAny(['=', ':']);
            if (equals > 0) values[line[..equals].Trim()] = StripComment(line[(equals + 1)..]).Trim();
        }

        ImmutableArray<string> List(string key) => values.TryGetValue("functional_lint." + key, out var value)
            ? [.. value.Split(',').Select(v => v.Trim()).Where(v => v.Length > 0 && !v.Equals("none", StringComparison.OrdinalIgnoreCase))]
            : [];

        return new Policy(file, root ?? Path.GetDirectoryName(file)!, List("cores"), List("may_lower"), List("legacy_namespaces"), List("adapter_namespaces"),
            List("allowed_references"), List("impure_apis"), List("gate_only"));
    }

    private static string StripComment(string value)
    {
        var at = value.IndexOfAny(['#', ';']);
        return at >= 0 ? value[..at] : value;
    }

    /// <summary>True if the policy lists this project as a functional core (paths or globs, relative to the repository root).</summary>
    public bool IsCore(string? projectPath) => Matches(Cores, projectPath);

    /// <summary>
    /// True for a core the gate checks but whose build can't load FunctionalLint: FunctionalLint's own projects,
    /// which would have to analyze themselves. The gate doesn't ask them to import FunctionalCore.props.
    /// </summary>
    public bool IsGateOnly(string? projectPath) => Matches(GateOnly, projectPath);

    private bool Matches(ImmutableArray<string> globs, string? projectPath)
    {
        if (projectPath is null) return false;
        var relative = Path.GetRelativePath(Root, Path.GetFullPath(projectPath)).Replace('\\', '/');
        return globs.Any(glob => Glob(glob).IsMatch(relative));
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
            ["functional_lint.may_lower"] = Join(MayLower),
            ["functional_lint.legacy_namespaces"] = Join(LegacyNamespaces),
            ["functional_lint.adapter_namespaces"] = Join(AdapterNamespaces),
            ["functional_lint.allowed_references"] = Join(AllowedReferences),
            ["functional_lint.impure_apis"] = string.Join(",", ImpureApis.Append(impure).Where(v => v.Length > 0)),
        });
    }

    private sealed class OverridingProvider(AnalyzerConfigOptionsProvider inner, Policy policy) : AnalyzerConfigOptionsProvider
    {
        // Analyzers ask for a file's options on nearly every callback, and files share option sets, so each set is
        // wrapped once. (Wrapping on every call made the gate twice as slow on a large project.)
        private readonly System.Runtime.CompilerServices.ConditionalWeakTable<AnalyzerConfigOptions, OverridingOptions> wrapped = new();

        public override AnalyzerConfigOptions GlobalOptions => Wrap(inner.GlobalOptions);
        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => Wrap(inner.GetOptions(tree));
        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => Wrap(inner.GetOptions(textFile));
        private OverridingOptions Wrap(AnalyzerConfigOptions options) =>
            wrapped.GetValue(options, o => new OverridingOptions(o, policy.Overrides(o)));
    }

    private sealed class OverridingOptions(AnalyzerConfigOptions inner, ImmutableDictionary<string, string> overrides) : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? value) =>
            overrides.TryGetValue(key, out value) || inner.TryGetValue(key, out value);

        public override IEnumerable<string> Keys => inner.Keys.Concat(overrides.Keys).Distinct(StringComparer.OrdinalIgnoreCase);
    }
}

/// <summary>FUNCTIONALLINT_POLICY names a file that isn't there. The gate stops rather than run without the owner's rules.</summary>
public sealed class PolicyNotFoundException(string file) : Exception(
    $"FunctionalLint: the policy file '{file}' named by {Policy.EnvironmentVariable} doesn't exist, so the gate can't run. " +
    $"Fix the path, or unset {Policy.EnvironmentVariable} to use the repository's own policy.")
{
    public string File { get; } = file;
}
