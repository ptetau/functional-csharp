using System.Diagnostics;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace FunctionalLint.Heal;

/// <summary>
/// A check server: keeps projects loaded between checks, so a per-edit check costs a recompile of what changed
/// (well under a second) instead of loading the project again (about 5 seconds).
///
/// It listens on a Unix domain socket in a folder only this user can open, answers one request per connection,
/// and re-reads only files whose timestamps changed. It reloads a project when a project file or analyzer config
/// (.editorconfig, .globalconfig, Directory.Build.*) changes, or when .cs files are added or removed. It quits
/// when the tool itself is rebuilt, on SIGTERM or SIGINT, or after 30 idle minutes.
/// </summary>
public static class Server
{
    public sealed record Request(string Project, string? Focus, string Cwd);

    public sealed record Response(int Code, string Report, bool Restart = false);

    private static readonly TimeSpan IdleLimit = TimeSpan.FromMinutes(30);

    // Config files MSBuild and the compiler read from a project's folder and every folder above it.
    private static readonly string[] ConfigFileNames =
        [".editorconfig", ".globalconfig", "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props"];

    private static string ToolPath => Assembly.GetExecutingAssembly().Location;

    public static bool Disabled => Environment.GetEnvironmentVariable("FUNCTIONALLINT_NO_SERVER") is { Length: > 0 } and not "0";

    /// <summary>
    /// One socket per copy of the tool and per user, in a folder only this user can open:
    /// $XDG_RUNTIME_DIR/functionallint, or ~/.cache/functionallint. Null if that folder can't be made private,
    /// in which case there's no server. (A socket in the shared temp folder could be answered by another user.)
    /// </summary>
    public static string? SocketPath
    {
        get
        {
            var key = Encoding.UTF8.GetBytes(Path.GetDirectoryName(ToolPath) + "|" + Environment.UserName);
            var hash = Convert.ToHexString(SHA256.HashData(key))[..12].ToLowerInvariant();
            return PrivateFolder() is { } folder ? Path.Combine(folder, $"{hash}.sock") : null;
        }
    }

    private static string? PrivateFolder()
    {
        var root = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") is { Length: > 0 } runtime
            ? runtime
            : OperatingSystem.IsWindows()
                ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache");
        var folder = Path.Combine(root, "functionallint");
        try
        {
            if (OperatingSystem.IsWindows())
            {
                Directory.CreateDirectory(folder);   // under the user's own profile
                return folder;
            }

            const UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
            Directory.CreateDirectory(folder, OwnerOnly);
            if (File.GetUnixFileMode(folder) != OwnerOnly) File.SetUnixFileMode(folder, OwnerOnly);
            return File.GetUnixFileMode(folder) == OwnerOnly ? folder : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // Client ----------------------------------------------------------------------------------

    /// <summary>Asks a running server. Null if there isn't one, or it's restarting: check in-process instead.</summary>
    public static async Task<Response?> TryAskAsync(Request request, TimeSpan timeout)
    {
        if (Disabled || SocketPath is not { } path || !File.Exists(path)) return null;
        try
        {
            using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            using var cts = new CancellationTokenSource(timeout);
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(path), cts.Token);
            await using var stream = new NetworkStream(socket, ownsSocket: false);
            await JsonSerializer.SerializeAsync(stream, request, cancellationToken: cts.Token);
            socket.Shutdown(SocketShutdown.Send);
            var response = await JsonSerializer.DeserializeAsync<Response>(stream, cancellationToken: cts.Token);
            return response is { Restart: false } ? response : null;
        }
        catch (Exception e) when (e is SocketException or IOException or JsonException or OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>
    /// Starts a server in the background unless one is running. It outlives this process. A socket file left
    /// by a server that was killed (SIGKILL can't be caught) is removed first, or no server would ever start again.
    /// </summary>
    public static async Task StartInBackgroundAsync()
    {
        if (Disabled || SocketPath is not { } path) return;
        if (File.Exists(path))
        {
            if (await IsAliveAsync(path)) return;
            TryDelete(path);
        }

        var start = new ProcessStartInfo("dotnet", [ToolPath, "--serve"])
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        try { Process.Start(start); }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { }
    }

    // Server ----------------------------------------------------------------------------------

    private sealed class Entry(Checker.Loaded loaded)
    {
        public Checker.Loaded Loaded { get; } = loaded;
        public Solution Current { get; set; } = loaded.Workspace.CurrentSolution;
        public Dictionary<string, DateTime> Stamps { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, HashSet<string>> Listings { get; } = new(StringComparer.Ordinal);
        public string ConfigSignature { get; set; } = "";
    }

    /// <summary>Runs until stopped, idle for 30 minutes, or the tool is rebuilt. Returns 0, or 2 if it can't serve.</summary>
    public static async Task<int> RunAsync(CancellationToken stop = default)
    {
        if (SocketPath is not { } path) return 2;
        if (File.Exists(path))
        {
            if (await IsAliveAsync(path)) return 2;
            TryDelete(path);
        }

        // SIGTERM and SIGINT stop the loop, so `finally` removes the socket file.
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(stop);
        void Stop(PosixSignalContext context) { context.Cancel = true; stopping.Cancel(); }
        using var term = PosixSignalRegistration.Create(PosixSignal.SIGTERM, Stop);
        using var interrupt = PosixSignalRegistration.Create(PosixSignal.SIGINT, Stop);

        using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(path));
        listener.Listen(16);

        var toolStamp = File.GetLastWriteTimeUtc(ToolPath);
        var loaded = new Dictionary<string, Entry>(StringComparer.Ordinal);
        try
        {
            while (true)
            {
                using var idle = CancellationTokenSource.CreateLinkedTokenSource(stopping.Token);
                idle.CancelAfter(IdleLimit);
                Socket client;
                try { client = await listener.AcceptAsync(idle.Token); }
                catch (OperationCanceledException) { return 0; }

                using (client)
                {
                    var restart = File.GetLastWriteTimeUtc(ToolPath) != toolStamp;
                    try
                    {
                        await using var stream = new NetworkStream(client, ownsSocket: false);
                        var request = await JsonSerializer.DeserializeAsync<Request>(stream);
                        var response = restart || request is null
                            ? new Response(2, "", Restart: true)
                            : await HandleAsync(request, loaded);
                        await JsonSerializer.SerializeAsync(stream, response);
                    }
                    catch (Exception e) when (e is JsonException or IOException or SocketException)
                    {
                        // A client that connected only to see if we're alive, or went away mid-request.
                    }
                    if (restart) return 0;
                }
            }
        }
        finally
        {
            foreach (var entry in loaded.Values) entry.Loaded.Workspace.Dispose();
            TryDelete(path);
        }
    }

    private static async Task<Response> HandleAsync(Request request, Dictionary<string, Entry> loaded)
    {
        var clock = Stopwatch.StartNew();
        try
        {
            // Relative paths are relative to the client's folder, not the server's.
            var project = Path.GetFullPath(request.Project, request.Cwd);
            if (!loaded.TryGetValue(project, out var entry) || NeedsReload(entry))
            {
                entry?.Loaded.Workspace.Dispose();
                entry = new Entry(await Checker.LoadAsync(project));
                Remember(entry);
                loaded[project] = entry;
            }
            else
            {
                entry.Current = Refresh(entry);
            }

            var focus = request.Focus is { } file ? Path.GetFullPath(file, request.Cwd) : null;
            var (code, report) = await Checker.CheckAsync(entry.Current, entry.Loaded.Functional, focus, request.Cwd, clock);
            return new Response(code, report);
        }
        catch (Exception e)
        {
            // Let the client fall back to checking in-process rather than report a server fault as a lint result.
            return new Response(2, $"server error: {e.Message}", Restart: true);
        }
    }

    // Source files in a project's folder: everything except build output.
    private static HashSet<string> Listing(string folder) =>
        Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories)
            .Where(f => !Path.GetRelativePath(folder, f).Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            .ToHashSet(StringComparer.Ordinal);

    // Every config file that can affect the analysis, whether or not it exists yet, with its timestamp: analyzer
    // config, the .editorconfig/.globalconfig/Directory.Build.* files above each project, every .props/.targets
    // those and the project import (FunctionalCore.props sets NoWarn and WarningsAsErrors), and NuGet's imports.
    private static string ConfigSignature(Solution solution)
    {
        var candidates = solution.Projects
            .SelectMany(p => p.AnalyzerConfigDocuments.Select(d => d.FilePath).OfType<string>()
                .Concat(p.FilePath is { } projectFile ? MsBuildInputs(projectFile) : []))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);
        return string.Join("\n", candidates.Select(f => File.Exists(f) ? $"{f}|{File.GetLastWriteTimeUtc(f).Ticks}" : $"{f}|-"));
    }

    private static IEnumerable<string> MsBuildInputs(string projectFile)
    {
        var folder = Path.GetDirectoryName(projectFile)!;
        var nearby = Ancestors(folder).SelectMany(dir => ConfigFileNames.Select(name => Path.Combine(dir, name))).ToList();
        var obj = Path.Combine(folder, "obj");
        var generated = Directory.Exists(obj)
            ? Directory.EnumerateFiles(obj, "*.g.props").Concat(Directory.EnumerateFiles(obj, "*.g.targets"))
            : [];
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var imports = nearby.Prepend(projectFile).Where(File.Exists).SelectMany(f => Imports(f, folder, seen)).ToList();
        return nearby.Concat(generated).Concat(imports);
    }

    private static readonly System.Text.RegularExpressions.Regex ImportPattern =
        new("<Import\\s+Project\\s*=\\s*\"([^\"]+)\"", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    // Files imported with <Import Project="..."/>, followed recursively. Paths using properties other than
    // $(MSBuildThisFileDirectory) and $(MSBuildProjectDirectory) can't be resolved without MSBuild and are skipped.
    private static IEnumerable<string> Imports(string file, string projectFolder, HashSet<string> seen)
    {
        if (!seen.Add(file) || !File.Exists(file)) return [];
        var folder = Path.GetDirectoryName(file)!;
        var direct = ImportPattern.Matches(File.ReadAllText(file))
            .Select(m => m.Groups[1].Value
                .Replace("$(MSBuildThisFileDirectory)", folder + Path.DirectorySeparatorChar)
                .Replace("$(MSBuildProjectDirectory)", projectFolder)
                .Replace('\\', Path.DirectorySeparatorChar))
            .Where(path => !path.Contains("$("))
            .Select(path => Path.GetFullPath(path, folder))
            .ToList();
        return direct.Concat(direct.SelectMany(path => Imports(path, projectFolder, seen)));
    }

    private static IEnumerable<string> Ancestors(string folder)
    {
        for (var dir = new DirectoryInfo(folder); dir is not null; dir = dir.Parent) yield return dir.FullName;
    }

    // Reload when a project file or analyzer config changed, or .cs files were added or removed: only MSBuild knows
    // what the project contains then. Listings are compared with the previous listing, not with the documents, so a
    // .cs file the project doesn't compile (a nested test project, <Compile Remove>) doesn't force a reload each time.
    private static bool NeedsReload(Entry entry)
    {
        if (ConfigSignature(entry.Current) != entry.ConfigSignature) return true;
        return entry.Current.Projects.Any(project =>
        {
            if (project.FilePath is not { } projectFile) return false;
            if (!entry.Stamps.TryGetValue(projectFile, out var stamp) || !File.Exists(projectFile) || File.GetLastWriteTimeUtc(projectFile) != stamp)
                return true;
            var folder = Path.GetDirectoryName(projectFile)!;
            return !entry.Listings.TryGetValue(folder, out var known) || !known.SetEquals(Listing(folder));
        });
    }

    // Re-read only the documents whose files changed since they were last read.
    private static Solution Refresh(Entry entry)
    {
        var solution = entry.Current;
        foreach (var document in solution.Projects.SelectMany(p => p.Documents))
        {
            if (document.FilePath is not { } file || !File.Exists(file)) continue;
            var stamp = File.GetLastWriteTimeUtc(file);
            if (entry.Stamps.TryGetValue(file, out var known) && known == stamp) continue;

            solution = solution.WithDocumentText(document.Id, SourceText.From(File.ReadAllText(file), Encoding.UTF8), PreservationMode.PreserveIdentity);
            entry.Stamps[file] = stamp;
        }
        return solution;
    }

    private static void Remember(Entry entry)
    {
        foreach (var project in entry.Current.Projects)
        {
            if (project.FilePath is { } projectFile)
            {
                entry.Stamps[projectFile] = File.GetLastWriteTimeUtc(projectFile);
                var folder = Path.GetDirectoryName(projectFile)!;
                entry.Listings[folder] = Listing(folder);
            }
            foreach (var file in project.Documents.Select(d => d.FilePath).OfType<string>().Where(File.Exists))
                entry.Stamps[file] = File.GetLastWriteTimeUtc(file);
        }
        entry.ConfigSignature = ConfigSignature(entry.Current);
    }

    private static async Task<bool> IsAliveAsync(string path)
    {
        try
        {
            using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(path), cts.Token);
            return true;
        }
        catch (Exception e) when (e is SocketException or OperationCanceledException)
        {
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
