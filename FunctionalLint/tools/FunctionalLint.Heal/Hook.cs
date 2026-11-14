using System.Text.Json;

namespace FunctionalLint.Heal;

/// <summary>
/// Glue for running as an agent hook (e.g. Claude Code's PostToolUse): work out which file was just
/// edited, and which project it belongs to.
/// </summary>
public static class Hook
{
    /// <summary>The edited file from the hook's JSON input, or null if there isn't one.</summary>
    public static string? EditedFile(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("tool_input", out var input) || input.ValueKind != JsonValueKind.Object) return null;
            if (!input.TryGetProperty("file_path", out var path) || path.ValueKind != JsonValueKind.String) return null;

            var file = path.GetString()!;
            // Relative paths are relative to the session's working directory.
            return Path.IsPathRooted(file) || !root.TryGetProperty("cwd", out var cwd) || cwd.ValueKind != JsonValueKind.String
                ? file
                : Path.Combine(cwd.GetString()!, file);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// From a Stop hook's JSON input: the session's folder, and whether the agent is already continuing because this
    /// hook stopped it once (then it reports without stopping it again, so an agent that can't fix something isn't
    /// stuck in a loop).
    /// </summary>
    public static (string Folder, bool Again) StopInput(string json, string fallback)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var folder = root.TryGetProperty("cwd", out var cwd) && cwd.ValueKind == JsonValueKind.String ? cwd.GetString()! : fallback;
            var again = root.TryGetProperty("stop_hook_active", out var active) && active.ValueKind == JsonValueKind.True;
            return (folder, again);
        }
        catch (JsonException)
        {
            return (fallback, false);
        }
    }

    /// <summary>
    /// Files git sees as changed or new in the folder's repository, as full paths: whatever made the change, an edit
    /// tool or a shell command. Empty outside a repository, or without git.
    /// </summary>
    public static IReadOnlyList<string> ChangedFiles(string folder)
    {
        try
        {
            var start = new System.Diagnostics.ProcessStartInfo("git", ["status", "--porcelain=v1", "-z", "--untracked-files=all"])
            {
                WorkingDirectory = folder,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            using var git = System.Diagnostics.Process.Start(start)!;
            var output = git.StandardOutput.ReadToEnd();
            git.WaitForExit();
            if (git.ExitCode != 0) return [];

            var top = RepositoryRoot(folder);
            // Entries are "XY path\0", and a rename adds "\0old path": skip the old one, and deletions.
            var entries = output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
            var changed = new List<string>();
            for (var i = 0; i < entries.Length; i++)
            {
                var (status, path) = (entries[i][..2], entries[i][3..]);
                if (status.Contains('R') || status.Contains('C')) i++;
                var full = Path.GetFullPath(Path.Combine(top, path));
                if (File.Exists(full)) changed.Add(full);
            }
            return changed;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return [];
        }
    }

    private static string RepositoryRoot(string folder)
    {
        for (var dir = new DirectoryInfo(Path.GetFullPath(folder)); dir is not null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, ".git")) || File.Exists(Path.Combine(dir.FullName, ".git"))) return dir.FullName;
        return folder;
    }

    /// <summary>The nearest .csproj at or above the file's folder, or null.</summary>
    public static string? ProjectFor(string file)
    {
        for (var dir = Path.GetDirectoryName(Path.GetFullPath(file)); dir is not null; dir = Path.GetDirectoryName(dir))
        {
            var projects = Directory.GetFiles(dir, "*.csproj");
            if (projects.Length > 0) return projects.OrderBy(p => p, StringComparer.Ordinal).First();
        }
        return null;
    }
}
