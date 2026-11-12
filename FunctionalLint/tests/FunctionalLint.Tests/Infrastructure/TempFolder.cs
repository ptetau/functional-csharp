using System;
using System.IO;
using System.Linq;

namespace FunctionalLint.Tests;

public static class TempFolder
{
    /// <summary>
    /// A new temp folder, by its real path. On macOS the temp folder is under /var, a link to /private/var. A restore
    /// that reaches a project by both paths writes the same files twice and fails ("... already exists").
    /// </summary>
    public static string Create(string prefix) => RealPath(Directory.CreateTempSubdirectory(prefix).FullName);

    private static string RealPath(string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full)!;
        return full[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries)
            .Aggregate(root, (folder, part) =>
            {
                var next = Path.Combine(folder, part);
                return new DirectoryInfo(next).ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? next;
            });
    }
}
