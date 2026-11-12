using System.Diagnostics;
using System.Net.Sockets;
using System.Reflection;
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
/// It listens on a Unix domain socket named after this tool's location, answers one request per connection, and
/// re-reads only files whose timestamps changed. It reloads a project when files are added or removed or a project
/// file changes, quits if the tool itself is rebuilt, and quits after 30 idle minutes.
/// </summary>
public static class Server
{
    public sealed record Request(string Project, string? Focus, string Cwd);

    public sealed record Response(int Code, string Report, bool Restart = false);

    private static readonly TimeSpan IdleLimit = TimeSpan.FromMinutes(30);

    private static string ToolPath => Assembly.GetExecutingAssembly().Location;

    /// <summary>One socket per copy of the tool and per user, in the temp folder.</summary>
    public static string SocketPath
    {
        get
        {
            var key = Encoding.UTF8.GetBytes(Path.GetDirectoryName(ToolPath) + "|" + Environment.UserName);
            var hash = Convert.ToHexString(SHA256.HashData(key))[..12].ToLowerInvariant();
            return Path.Combine(Path.GetTempPath(), $"functionallint-{hash}.sock");
        }
    }

    public static bool Disabled => Environment.GetEnvironmentVariable("FUNCTIONALLINT_NO_SERVER") is { Length: > 0 } and not "0";

    // Client ----------------------------------------------------------------------------------

    /// <summary>Asks a running server. Null if there isn't one, or it's restarting: check in-process instead.</summary>
    public static async Task<Response?> TryAskAsync(Request request, TimeSpan timeout)
    {
        if (Disabled || !File.Exists(SocketPath)) return null;
        try
        {
            using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            using var cts = new CancellationTokenSource(timeout);
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(SocketPath), cts.Token);
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

    /// <summary>Starts a server in the background, if one isn't running. It outlives this process.</summary>
    public static void StartInBackground()
    {
        if (Disabled || File.Exists(SocketPath)) return;
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
    }

    /// <summary>Runs until idle for 30 minutes or the tool is rebuilt. Returns 0, or 2 if another server is running.</summary>
    public static async Task<int> RunAsync(CancellationToken stop = default)
    {
        var path = SocketPath;
        if (File.Exists(path))
        {
            if (await IsAliveAsync(path)) return 2;
            File.Delete(path);
        }

        using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(path));
        listener.Listen(16);

        var toolStamp = File.GetLastWriteTimeUtc(ToolPath);
        var loaded = new Dictionary<string, Entry>(StringComparer.Ordinal);
        try
        {
            while (true)
            {
                using var idle = CancellationTokenSource.CreateLinkedTokenSource(stop);
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
            File.Delete(path);
        }
    }

    private static async Task<Response> HandleAsync(Request request, Dictionary<string, Entry> loaded)
    {
        var clock = Stopwatch.StartNew();
        try
        {
            var project = Path.GetFullPath(request.Project);
            if (!loaded.TryGetValue(project, out var entry) || NeedsReload(entry))
            {
                entry?.Loaded.Workspace.Dispose();
                entry = new Entry(await Checker.LoadAsync(project));
                Stamp(entry, entry.Current);
                loaded[project] = entry;
            }
            else
            {
                entry.Current = Refresh(entry);
            }

            var (code, report) = await Checker.CheckAsync(entry.Current, entry.Loaded.Functional, request.Focus, request.Cwd, clock);
            return new Response(code, report);
        }
        catch (Exception e)
        {
            // Let the client fall back to checking in-process rather than report a server fault as a lint result.
            return new Response(2, $"server error: {e.Message}", Restart: true);
        }
    }

    // Files on disk that belong to a project folder: everything except build output.
    private static IEnumerable<string> SourceFiles(string folder) =>
        Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"));

    // Reload when a project file changed, or files were added or removed: only MSBuild knows what's in the project then.
    private static bool NeedsReload(Entry entry) =>
        entry.Current.Projects.Any(project =>
        {
            if (project.FilePath is not { } projectFile) return false;
            if (!entry.Stamps.TryGetValue(projectFile, out var stamp) || File.GetLastWriteTimeUtc(projectFile) != stamp) return true;

            var folder = Path.GetDirectoryName(projectFile)!;
            var known = project.Documents.Select(d => d.FilePath).OfType<string>()
                .Where(f => f.StartsWith(folder, StringComparison.Ordinal)
                            && !f.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
                .ToHashSet(StringComparer.Ordinal);
            return !known.SetEquals(SourceFiles(folder));
        });

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

    private static void Stamp(Entry entry, Solution solution)
    {
        foreach (var project in solution.Projects)
        {
            if (project.FilePath is { } projectFile) entry.Stamps[projectFile] = File.GetLastWriteTimeUtc(projectFile);
            foreach (var file in project.Documents.Select(d => d.FilePath).OfType<string>().Where(File.Exists))
                entry.Stamps[file] = File.GetLastWriteTimeUtc(file);
        }
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
}
