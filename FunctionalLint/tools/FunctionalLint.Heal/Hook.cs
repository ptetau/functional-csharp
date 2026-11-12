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
