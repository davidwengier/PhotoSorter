namespace PhotoSorter.Core.Models;

public enum ScanPhase
{
    Discovering,
    ReadingMetadata,
    SavingCache,
    Grouping,
    Completed,
}

public sealed record ScanProgress(
    ScanPhase Phase,
    int Processed,
    int Total,
    string? CurrentRelativePath = null);

public sealed record ScanIssue
{
    public string? RelativePath { get; init; }

    public required string Message { get; init; }
}

public sealed record MediaScanResult
{
    public required IReadOnlyList<MediaAsset> Assets { get; init; }

    public required IReadOnlyList<AssetBundle> Bundles { get; init; }

    public required IReadOnlyList<ScanIssue> Issues { get; init; }

    public required IReadOnlyList<int> Years { get; init; }

    public int ReusedMetadataCount { get; init; }

    public int ExtractedMetadataCount { get; init; }
}
