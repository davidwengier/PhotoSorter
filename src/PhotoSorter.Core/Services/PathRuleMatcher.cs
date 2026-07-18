using PhotoSorter.Core.Models;

namespace PhotoSorter.Core.Services;

public static class PathRuleMatcher
{
    private static readonly StringComparison PathComparison = StringComparison.OrdinalIgnoreCase;

    public static string NormalizeRelativePath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var normalized = path
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .Trim();

        while (normalized.StartsWith(Path.DirectorySeparatorChar))
        {
            normalized = normalized[1..];
        }

        return normalized.TrimEnd(Path.DirectorySeparatorChar);
    }

    public static bool IsPortableRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path))
        {
            return false;
        }

        var normalized = NormalizeRelativePath(path);
        return normalized
            .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries)
            .All(static segment => segment is not "." and not "..");
    }

    public static bool IsUnderPhoneImages(string relativeFilePath, int year)
    {
        var expectedPrefix = Path.Combine(year.ToString(System.Globalization.CultureInfo.InvariantCulture), "Phone Images")
            + Path.DirectorySeparatorChar;
        return NormalizeRelativePath(relativeFilePath).StartsWith(expectedPrefix, PathComparison);
    }
}
