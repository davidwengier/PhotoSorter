using PhotoSorter.Core.Contracts;
using PhotoSorter.Core.Models;
using PhotoSorter.Core.Services;

namespace PhotoSorter.Infrastructure.Moves;

public sealed class FileSystemMoveExecutor : IMoveExecutor
{
    public Task<MovePreflightResult> PreflightAsync(
        MovePlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var errors = new List<string>();
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(plan.PicturesRoot));
        if (!Directory.Exists(root))
        {
            errors.Add($"Pictures folder '{root}' does not exist.");
            return Task.FromResult(new MovePreflightResult { Errors = errors });
        }

        _ = ResolveUnderRoot(
            root,
            plan.DestinationDirectoryRelativePath,
            errors,
            "Destination directory");
        var destinationDirectoryRelativePath = PathRuleMatcher.NormalizeRelativePath(
            plan.DestinationDirectoryRelativePath);
        if (!TryValidateDestinationDirectory(destinationDirectoryRelativePath, out var destinationYear))
        {
            errors.Add(
                "The destination directory must be a direct child of a four-digit year folder "
                + "and cannot be Phone Images.");
        }

        var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in plan.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = ResolveUnderRoot(root, entry.SourceRelativePath, errors, "Source");
            var destination = ResolveUnderRoot(root, entry.DestinationRelativePath, errors, "Destination");
            if (source is null || destination is null)
            {
                continue;
            }

            if (!TryReadYear(entry.SourceRelativePath, out var year)
                || !PathRuleMatcher.IsUnderPhoneImages(entry.SourceRelativePath, year))
            {
                errors.Add($"Source is not inside a year-level Phone Images folder: {entry.SourceRelativePath}");
            }
            else
            {
                if (destinationYear != year)
                {
                    errors.Add(
                        $"Destination year does not match source year for: {entry.SourceRelativePath}");
                }

                if (PathRuleMatcher.IsUnderPhoneImages(entry.DestinationRelativePath, year))
                {
                    errors.Add($"Destination cannot be inside Phone Images: {entry.DestinationRelativePath}");
                }
            }

            var entryDirectory = PathRuleMatcher.NormalizeRelativePath(
                Path.GetDirectoryName(entry.DestinationRelativePath) ?? string.Empty);
            if (!entryDirectory.Equals(
                    destinationDirectoryRelativePath,
                    StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(
                    $"Destination file is outside the planned destination folder: "
                    + $"{entry.DestinationRelativePath}");
            }

            if (!File.Exists(source))
            {
                errors.Add($"Source file no longer exists: {entry.SourceRelativePath}");
            }
            else
            {
                var info = new FileInfo(source);
                if (info.Length != entry.ExpectedLength)
                {
                    errors.Add($"Source file size changed: {entry.SourceRelativePath}");
                }

                var actualWriteTime = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero);
                if (actualWriteTime.UtcTicks != entry.ExpectedLastWriteTimeUtc.UtcTicks)
                {
                    errors.Add($"Source file timestamp changed: {entry.SourceRelativePath}");
                }
            }

            if (File.Exists(destination))
            {
                errors.Add($"Destination already exists: {entry.DestinationRelativePath}");
            }

            if (!destinations.Add(destination))
            {
                errors.Add($"More than one file would use destination: {entry.DestinationRelativePath}");
            }
        }

        return Task.FromResult(new MovePreflightResult { Errors = errors });
    }

    public async Task<MoveExecutionResult> ExecuteAsync(
        MovePlan plan,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var preflight = await PreflightAsync(plan, cancellationToken).ConfigureAwait(false);
        if (!preflight.IsValid)
        {
            return BuildPreflightFailure(plan, preflight.Errors);
        }

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(plan.PicturesRoot));
        Directory.CreateDirectory(Path.Combine(root, plan.DestinationDirectoryRelativePath));
        var results = new List<MoveItemResult>(plan.Entries.Count);

        for (var index = 0; index < plan.Entries.Count; index++)
        {
            var entry = plan.Entries[index];
            if (cancellationToken.IsCancellationRequested)
            {
                results.Add(new MoveItemResult
                {
                    Entry = entry,
                    Status = MoveItemStatus.Failed,
                    Error = "The move was cancelled before this file was attempted.",
                });
                AddNotAttempted(plan, index + 1, results);
                break;
            }

            try
            {
                var source = Path.Combine(root, entry.SourceRelativePath);
                var destination = Path.Combine(root, entry.DestinationRelativePath);
                File.Move(source, destination, overwrite: false);
                results.Add(new MoveItemResult
                {
                    Entry = entry,
                    Status = MoveItemStatus.Moved,
                });
                progress?.Report((index + 1) * 100 / plan.Entries.Count);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                results.Add(new MoveItemResult
                {
                    Entry = entry,
                    Status = MoveItemStatus.Failed,
                    Error = exception.Message,
                });
                AddNotAttempted(plan, index + 1, results);
                break;
            }
        }

        return new MoveExecutionResult { Items = results };
    }

    private static MoveExecutionResult BuildPreflightFailure(
        MovePlan plan,
        IReadOnlyList<string> errors)
    {
        var items = new List<MoveItemResult>(plan.Entries.Count);
        if (plan.Entries.Count > 0)
        {
            items.Add(new MoveItemResult
            {
                Entry = plan.Entries[0],
                Status = MoveItemStatus.Failed,
                Error = string.Join(Environment.NewLine, errors),
            });
            AddNotAttempted(plan, 1, items);
        }

        return new MoveExecutionResult { Items = items };
    }

    private static void AddNotAttempted(
        MovePlan plan,
        int startIndex,
        ICollection<MoveItemResult> results)
    {
        for (var index = startIndex; index < plan.Entries.Count; index++)
        {
            results.Add(new MoveItemResult
            {
                Entry = plan.Entries[index],
                Status = MoveItemStatus.NotAttempted,
            });
        }
    }

    private static string? ResolveUnderRoot(
        string root,
        string relativePath,
        ICollection<string> errors,
        string label)
    {
        if (!PathRuleMatcher.IsPortableRelativePath(relativePath))
        {
            errors.Add($"{label} path is not portable and relative: {relativePath}");
            return null;
        }

        var fullPath = Path.GetFullPath(Path.Combine(root, relativePath));
        if (!fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"{label} escapes the Pictures folder: {relativePath}");
            return null;
        }

        return fullPath;
    }

    private static bool TryReadYear(string relativePath, out int year)
    {
        var firstSegment = PathRuleMatcher.NormalizeRelativePath(relativePath)
            .Split(Path.DirectorySeparatorChar, 2)[0];
        return int.TryParse(
            firstSegment,
            System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture,
            out year);
    }

    private static bool TryValidateDestinationDirectory(string relativePath, out int year)
    {
        var segments = relativePath.Split(
            Path.DirectorySeparatorChar,
            StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length != 2
            || !int.TryParse(
                segments[0],
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out year)
            || segments[0].Length != 4
            || string.Equals(segments[1], "Phone Images", StringComparison.OrdinalIgnoreCase))
        {
            year = default;
            return false;
        }

        return true;
    }
}
