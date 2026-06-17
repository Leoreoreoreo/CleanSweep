namespace CleanSweep.Core.Services;

/// <summary>Path-containment tests used to honour user folder exclusions.</summary>
public static class PathScope
{
    /// <summary>Absolute, separator-trimmed form; empty string if the path is blank or invalid.</summary>
    public static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        try { return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
        catch { return ""; }
    }

    /// <summary>Pre-normalised exclusion roots, with blanks and duplicates dropped.</summary>
    public static string[] Normalize(IEnumerable<string> paths)
        => paths.Select(Normalize).Where(p => p.Length > 0).Distinct().ToArray();

    /// <summary>True if <paramref name="path"/> equals, or sits under, any normalised root.</summary>
    public static bool IsUnderAny(string path, string[] normalizedRoots)
    {
        if (normalizedRoots.Length == 0) return false;
        var full = Normalize(path);
        if (full.Length == 0) return false;

        var cmp = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        foreach (var root in normalizedRoots)
            if (full.Equals(root, cmp) || full.StartsWith(root + Path.DirectorySeparatorChar, cmp))
                return true;
        return false;
    }
}
