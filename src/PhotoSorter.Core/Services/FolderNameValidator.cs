namespace PhotoSorter.Core.Services;

public static class FolderNameValidator
{
    private static readonly HashSet<string> ReservedNames = new(
        [
            "CON",
            "PRN",
            "AUX",
            "NUL",
            "COM1",
            "COM2",
            "COM3",
            "COM4",
            "COM5",
            "COM6",
            "COM7",
            "COM8",
            "COM9",
            "LPT1",
            "LPT2",
            "LPT3",
            "LPT4",
            "LPT5",
            "LPT6",
            "LPT7",
            "LPT8",
            "LPT9",
        ],
        StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> Validate(string? folderName)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(folderName))
        {
            errors.Add("Enter a destination folder name.");
            return errors;
        }

        var trimmed = folderName.Trim();
        if (!string.Equals(trimmed, folderName, StringComparison.Ordinal))
        {
            errors.Add("The folder name cannot start or end with whitespace.");
        }

        if (trimmed.EndsWith('.'))
        {
            errors.Add("The folder name cannot end with a period.");
        }

        if (trimmed.Length > 100)
        {
            errors.Add("The folder name cannot be longer than 100 characters.");
        }

        if (trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            errors.Add("The folder name contains characters that Windows does not allow.");
        }

        if (string.Equals(trimmed, "Phone Images", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("'Phone Images' cannot be used as an event destination.");
        }

        var reservedCandidate = trimmed.Split('.', 2)[0];
        if (ReservedNames.Contains(reservedCandidate))
        {
            errors.Add($"'{reservedCandidate}' is a reserved Windows name.");
        }

        return errors;
    }
}
