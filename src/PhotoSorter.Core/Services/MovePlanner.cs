using PhotoSorter.Core.Models;

namespace PhotoSorter.Core.Services;

public sealed class MovePlanner
{
    public MovePlanBuildResult Build(
        string picturesRoot,
        CandidateGroup candidate,
        IEnumerable<AssetBundle> selectedBundles,
        string destinationFolderName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(picturesRoot);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(selectedBundles);

        var errors = FolderNameValidator.Validate(destinationFolderName).ToList();
        var bundles = selectedBundles
            .DistinctBy(static bundle => bundle.Id)
            .OrderBy(static bundle => bundle.CapturedAt)
            .ToArray();
        if (bundles.Length == 0)
        {
            errors.Add("Select at least one item to move.");
        }

        if (bundles.Any(bundle => bundle.Year != candidate.Year))
        {
            errors.Add("Every selected item must belong to the candidate year.");
        }

        var destinationDirectory = Path.Combine(
            candidate.Year.ToString(System.Globalization.CultureInfo.InvariantCulture),
            destinationFolderName.Trim());
        var entries = bundles
            .SelectMany(bundle => bundle.Assets.Select(asset => new MovePlanEntry
            {
                BundleId = bundle.Id,
                SourceRelativePath = PathRuleMatcher.NormalizeRelativePath(asset.RelativePath),
                DestinationRelativePath = Path.Combine(destinationDirectory, asset.FileName),
                ExpectedLength = asset.Length,
                ExpectedLastWriteTimeUtc = asset.LastWriteTimeUtc,
            }))
            .ToArray();

        foreach (var entry in entries.Where(entry => !PathRuleMatcher.IsUnderPhoneImages(
                     entry.SourceRelativePath,
                     candidate.Year)))
        {
            errors.Add($"Source is outside {candidate.Year}\\Phone Images: {entry.SourceRelativePath}");
        }

        foreach (var duplicate in entries
                     .GroupBy(static entry => entry.DestinationRelativePath, StringComparer.OrdinalIgnoreCase)
                     .Where(static group => group.Count() > 1))
        {
            errors.Add($"More than one selected file would become '{duplicate.Key}'.");
        }

        if (errors.Count > 0)
        {
            return new MovePlanBuildResult { Errors = errors };
        }

        return new MovePlanBuildResult
        {
            Plan = new MovePlan
            {
                PicturesRoot = Path.GetFullPath(picturesRoot),
                DestinationDirectoryRelativePath = destinationDirectory,
                Entries = entries,
            },
        };
    }
}
