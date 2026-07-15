using PhotoSorter.Core.Models;

namespace PhotoSorter.Core.Services;

public static class PhotoSorterStateValidator
{
    public static IReadOnlyList<string> Validate(PhotoSorterState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var errors = new List<string>();
        if (state.SchemaVersion <= 0)
        {
            errors.Add("schemaVersion must be a positive integer.");
        }

        if (state.SchemaVersion > PhotoSorterState.CurrentSchemaVersion)
        {
            errors.Add(
                $"schemaVersion {state.SchemaVersion} is newer than the supported version "
                + $"{PhotoSorterState.CurrentSchemaVersion}.");
        }

        if (state.RoutineLocations is null)
        {
            errors.Add("routineLocations must be a JSON array.");
        }

        if (state.IgnoredFolders is null)
        {
            errors.Add("ignoredFolders must be a JSON array.");
        }

        if (state.IgnoredGroups is null)
        {
            errors.Add("ignoredGroups must be a JSON array.");
        }

        var routineLocations = state.RoutineLocations ?? [];
        var ignoredFolders = state.IgnoredFolders ?? [];
        var ignoredGroups = state.IgnoredGroups ?? [];

        ValidateUniqueIds(
            routineLocations.Select(static decision => decision.Id),
            "routineLocations",
            errors);
        ValidateUniqueIds(
            ignoredFolders.Select(static rule => rule.Id),
            "ignoredFolders",
            errors);
        ValidateUniqueIds(
            ignoredGroups.Select(static rule => rule.Id),
            "ignoredGroups",
            errors);

        foreach (var decision in routineLocations)
        {
            if (string.IsNullOrWhiteSpace(decision.Name))
            {
                errors.Add($"Routine location '{decision.Id}' must have a name.");
            }

            ValidatePoint(decision.Center, $"Routine location '{decision.Id}'", errors);
            if (decision.RadiusMeters <= 0)
            {
                errors.Add($"Routine location '{decision.Id}' must have a positive radiusMeters.");
            }
        }

        foreach (var rule in ignoredFolders)
        {
            if (!PathRuleMatcher.IsPortableRelativePath(rule.RelativePath))
            {
                errors.Add(
                    $"Ignored folder '{rule.Id}' must use a Pictures-root-relative path without '.' or '..' segments.");
            }
        }

        foreach (var rule in ignoredGroups)
        {
            if (rule.End < rule.Start)
            {
                errors.Add($"Ignored group '{rule.Id}' has an end before its start.");
            }

            if (rule.TimePaddingMinutes < 0)
            {
                errors.Add($"Ignored group '{rule.Id}' has a negative timePaddingMinutes.");
            }

            if (rule.RequiredLocationMatchFraction is < 0 or > 1)
            {
                errors.Add($"Ignored group '{rule.Id}' must have requiredLocationMatchFraction between 0 and 1.");
            }

            if (rule.Areas is null || rule.Areas.Count == 0)
            {
                errors.Add($"Ignored group '{rule.Id}' must contain at least one geographic area.");
                continue;
            }

            foreach (var area in rule.Areas)
            {
                ValidatePoint(area.Center, $"Ignored group '{rule.Id}'", errors);
                if (area.RadiusMeters <= 0)
                {
                    errors.Add($"Ignored group '{rule.Id}' must have positive area radii.");
                }
            }
        }

        return errors;
    }

    private static void ValidateUniqueIds(
        IEnumerable<string> ids,
        string collectionName,
        ICollection<string> errors)
    {
        var duplicates = ids
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .GroupBy(static id => id, StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Count() > 1)
            .Select(static group => group.Key);

        foreach (var duplicate in duplicates)
        {
            errors.Add($"{collectionName} contains duplicate id '{duplicate}'.");
        }
    }

    private static void ValidatePoint(GeoPoint point, string owner, ICollection<string> errors)
    {
        if (point.Latitude is < -90 or > 90)
        {
            errors.Add($"{owner} has a latitude outside -90 to 90.");
        }

        if (point.Longitude is < -180 or > 180)
        {
            errors.Add($"{owner} has a longitude outside -180 to 180.");
        }
    }
}
