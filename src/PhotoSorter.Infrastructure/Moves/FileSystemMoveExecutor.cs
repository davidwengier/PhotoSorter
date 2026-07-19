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
        var equivalentDestinationEntries = new List<MovePlanEntry>();
        var conflictingDestinationEntries = new List<MovePlanEntry>();
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(plan.PicturesRoot));
        if (!Directory.Exists(root))
        {
            errors.Add($"Pictures folder '{root}' does not exist.");
            return Task.FromResult(new MovePreflightResult
            {
                Errors = errors,
                EquivalentDestinationEntries = equivalentDestinationEntries,
                ConflictingDestinationEntries = conflictingDestinationEntries,
            });
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

            FileInfo? sourceInfo = null;
            if (!File.Exists(source))
            {
                errors.Add($"Source file no longer exists: {entry.SourceRelativePath}");
            }
            else
            {
                sourceInfo = new FileInfo(source);
                if (sourceInfo.Length != entry.ExpectedLength)
                {
                    errors.Add($"Source file size changed: {entry.SourceRelativePath}");
                }

                var actualWriteTime = new DateTimeOffset(sourceInfo.LastWriteTimeUtc, TimeSpan.Zero);
                if (actualWriteTime.UtcTicks != entry.ExpectedLastWriteTimeUtc.UtcTicks)
                {
                    errors.Add($"Source file timestamp changed: {entry.SourceRelativePath}");
                }
            }

            if (File.Exists(destination))
            {
                var destinationInfo = new FileInfo(destination);
                if (sourceInfo is not null && AreEquivalent(sourceInfo, destinationInfo))
                {
                    equivalentDestinationEntries.Add(entry);
                }
                else
                {
                    conflictingDestinationEntries.Add(entry);
                }
            }

            if (!destinations.Add(destination))
            {
                errors.Add($"More than one file would use destination: {entry.DestinationRelativePath}");
            }
        }

        var conflictingBundleIds = conflictingDestinationEntries
            .Select(static entry => entry.BundleId)
            .ToHashSet(StringComparer.Ordinal);
        if (conflictingBundleIds.Count > 0)
        {
            equivalentDestinationEntries.RemoveAll(
                entry => conflictingBundleIds.Contains(entry.BundleId));
            conflictingDestinationEntries.Clear();
            conflictingDestinationEntries.AddRange(
                plan.Entries.Where(entry => conflictingBundleIds.Contains(entry.BundleId)));
        }

        return Task.FromResult(new MovePreflightResult
        {
            Errors = errors,
            EquivalentDestinationEntries = equivalentDestinationEntries,
            ConflictingDestinationEntries = conflictingDestinationEntries,
        });
    }

    public async Task<MoveExecutionResult> ExecuteAsync(
        MovePlan plan,
        MoveExecutionOptions? options = null,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        options ??= new MoveExecutionOptions();

        var preflight = await PreflightAsync(plan, cancellationToken).ConfigureAwait(false);
        if (!preflight.IsValid)
        {
            return BuildPreflightFailure(plan, preflight.Errors);
        }

        if (preflight.EquivalentDestinationEntries.Count > 0 && !options.DeleteEquivalentSources)
        {
            return BuildPreflightFailure(
                plan,
                preflight.EquivalentDestinationEntries
                    .Select(static entry =>
                        $"Destination already contains an equivalent file, but source deletion was not approved: "
                        + entry.DestinationRelativePath)
                    .ToArray());
        }

        if (preflight.ConflictingDestinationEntries.Count > 0 && !options.SkipDestinationConflicts)
        {
            return BuildPreflightFailure(
                plan,
                preflight.ConflictingDestinationEntries
                    .Select(static entry =>
                        $"Destination already exists but does not match the source name, size, and "
                        + $"modified date/time: {entry.DestinationRelativePath}")
                    .ToArray());
        }

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(plan.PicturesRoot));
        Directory.CreateDirectory(Path.Combine(root, plan.DestinationDirectoryRelativePath));
        var results = new List<MoveItemResult>(plan.Entries.Count);
        var conflictingBundleIds = preflight.ConflictingDestinationEntries
            .Select(static entry => entry.BundleId)
            .ToHashSet(StringComparer.Ordinal);
        var entriesByBundle = plan.Entries
            .GroupBy(static entry => entry.BundleId, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group.ToArray(),
                StringComparer.Ordinal);
        var checkedBundleIds = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 0; index < plan.Entries.Count; index++)
        {
            var entry = plan.Entries[index];
            if (cancellationToken.IsCancellationRequested)
            {
                results.Add(new MoveItemResult
                {
                    Entry = entry,
                    Status = MoveItemStatus.Failed,
                    Error = "The operation was cancelled before this file was attempted.",
                });
                AddNotAttempted(plan, index + 1, results);
                break;
            }

            try
            {
                if (!conflictingBundleIds.Contains(entry.BundleId)
                    && checkedBundleIds.Add(entry.BundleId)
                    && HasDestinationConflict(entriesByBundle[entry.BundleId], root))
                {
                    if (!options.SkipDestinationConflicts)
                    {
                        throw new IOException(
                            $"A destination conflict appeared while processing bundle '{entry.BundleId}'.");
                    }

                    conflictingBundleIds.Add(entry.BundleId);
                }

                var status = MoveItemStatus.SkippedDestinationConflict;
                if (!conflictingBundleIds.Contains(entry.BundleId))
                {
                    var source = Path.Combine(root, entry.SourceRelativePath);
                    var destination = Path.Combine(root, entry.DestinationRelativePath);
                    status = ProcessEntry(entry, source, destination, options);
                    if (status == MoveItemStatus.SkippedDestinationConflict)
                    {
                        conflictingBundleIds.Add(entry.BundleId);
                    }
                }

                results.Add(new MoveItemResult
                {
                    Entry = entry,
                    Status = status,
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

    private static MoveItemStatus ProcessEntry(
        MovePlanEntry entry,
        string source,
        string destination,
        MoveExecutionOptions options)
    {
        var sourceInfo = GetValidatedSourceInfo(entry, source);

        if (!File.Exists(destination))
        {
            File.Move(source, destination, overwrite: false);
            return MoveItemStatus.Moved;
        }

        var destinationInfo = new FileInfo(destination);
        if (AreEquivalent(sourceInfo, destinationInfo))
        {
            if (!options.DeleteEquivalentSources)
            {
                throw new IOException(
                    $"Destination contains an equivalent file, but source deletion was not approved: "
                    + entry.DestinationRelativePath);
            }

            File.Delete(source);
            return MoveItemStatus.DeletedEquivalentSource;
        }

        if (options.SkipDestinationConflicts)
        {
            return MoveItemStatus.SkippedDestinationConflict;
        }

        throw new IOException(
            $"Destination already exists but does not match the source name, size, and "
            + $"modified date/time: {entry.DestinationRelativePath}");
    }

    private static bool HasDestinationConflict(
        IReadOnlyList<MovePlanEntry> entries,
        string root)
    {
        foreach (var entry in entries)
        {
            var source = Path.Combine(root, entry.SourceRelativePath);
            var destination = Path.Combine(root, entry.DestinationRelativePath);
            var sourceInfo = GetValidatedSourceInfo(entry, source);
            if (File.Exists(destination)
                && !AreEquivalent(sourceInfo, new FileInfo(destination)))
            {
                return true;
            }
        }

        return false;
    }

    private static FileInfo GetValidatedSourceInfo(MovePlanEntry entry, string source)
    {
        if (!File.Exists(source))
        {
            throw new IOException($"Source file no longer exists: {entry.SourceRelativePath}");
        }

        var sourceInfo = new FileInfo(source);
        var actualWriteTime = new DateTimeOffset(sourceInfo.LastWriteTimeUtc, TimeSpan.Zero);
        if (sourceInfo.Length != entry.ExpectedLength
            || actualWriteTime.UtcTicks != entry.ExpectedLastWriteTimeUtc.UtcTicks)
        {
            throw new IOException(
                $"Source file changed after planning: {entry.SourceRelativePath}");
        }

        return sourceInfo;
    }

    private static bool AreEquivalent(FileInfo source, FileInfo destination) =>
        string.Equals(source.Name, destination.Name, StringComparison.OrdinalIgnoreCase)
        && source.Length == destination.Length
        && source.LastWriteTimeUtc.Ticks == destination.LastWriteTimeUtc.Ticks;

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
