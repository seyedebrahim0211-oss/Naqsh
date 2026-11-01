namespace Naqsh.Core.Model;

/// <summary>
/// Relative/absolute media path handling. Works with any Unicode path (Persian names included).
/// </summary>
public static class MediaPaths
{
    /// <summary>Stores <paramref name="fullPath"/> relative to <paramref name="baseFolder"/> when it is inside it.</summary>
    public static string ToStored(string fullPath, string? baseFolder)
    {
        ArgumentNullException.ThrowIfNull(fullPath);
        if (string.IsNullOrEmpty(baseFolder))
        {
            return fullPath;
        }

        var rel = Path.GetRelativePath(baseFolder, fullPath);
        return rel.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(rel) ? fullPath : rel;
    }

    /// <summary>Resolves a stored path against <paramref name="baseFolder"/>.</summary>
    public static string Resolve(string storedPath, string? baseFolder)
    {
        ArgumentNullException.ThrowIfNull(storedPath);
        if (Path.IsPathRooted(storedPath) || string.IsNullOrEmpty(baseFolder))
        {
            return storedPath;
        }

        return Path.GetFullPath(Path.Combine(baseFolder, storedPath));
    }
}
