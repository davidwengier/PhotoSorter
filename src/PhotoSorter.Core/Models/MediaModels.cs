namespace PhotoSorter.Core.Models;

public enum MediaKind
{
    Unknown,
    Image,
    Video,
    Sidecar,
}

public enum TimestampSource
{
    ExifOriginal,
    MediaCreated,
    FileName,
    FileSystem,
}

public enum MetadataConfidence
{
    Low,
    Medium,
    High,
}

public enum OffsetConfidence
{
    Unknown,
    Inferred,
    Explicit,
}

public sealed record MediaAsset
{
    public required string RelativePath { get; init; }

    public int Year { get; init; }

    public long Length { get; init; }

    public DateTimeOffset LastWriteTimeUtc { get; init; }

    public FileAttributes Attributes { get; init; }

    public MediaKind Kind { get; init; }

    public required string Extension { get; init; }

    public DateTimeOffset CapturedAt { get; init; }

    public TimestampSource TimestampSource { get; init; }

    public MetadataConfidence TimestampConfidence { get; init; }

    public OffsetConfidence OffsetConfidence { get; init; }

    public GeoPoint? Location { get; init; }

    public string? ContentIdentifier { get; init; }

    public TimeSpan? Duration { get; init; }

    public int? Width { get; init; }

    public int? Height { get; init; }

    public string? MetadataError { get; init; }

    public bool MetadataReadFailed { get; init; }

    public string FileName => Path.GetFileName(RelativePath);

    public string DirectoryRelativePath => Path.GetDirectoryName(RelativePath) ?? string.Empty;

    public string BaseName => Path.GetFileNameWithoutExtension(RelativePath);
}

public sealed class AssetBundle
{
    public AssetBundle(string id, IReadOnlyList<MediaAsset> assets)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(assets);

        if (assets.Count == 0)
        {
            throw new ArgumentException("An asset bundle must contain at least one file.", nameof(assets));
        }

        Id = id;
        Assets = assets;
        PrimaryAsset = assets
            .OrderByDescending(static asset => asset.Kind == MediaKind.Image)
            .ThenByDescending(static asset => asset.Kind == MediaKind.Video)
            .ThenByDescending(static asset => asset.TimestampConfidence)
            .ThenBy(static asset => asset.RelativePath, StringComparer.OrdinalIgnoreCase)
            .First();

        var bestTimestampAsset = assets
            .OrderByDescending(static asset => asset.TimestampConfidence)
            .ThenByDescending(asset => ReferenceEquals(asset, PrimaryAsset))
            .First();

        CapturedAt = bestTimestampAsset.CapturedAt;
        TimestampConfidence = bestTimestampAsset.TimestampConfidence;
        Location = PrimaryAsset.Location ?? assets.FirstOrDefault(static asset => asset.Location is not null)?.Location;
        Year = PrimaryAsset.Year;
    }

    public string Id { get; }

    public IReadOnlyList<MediaAsset> Assets { get; }

    public MediaAsset PrimaryAsset { get; }

    public DateTimeOffset CapturedAt { get; }

    public MetadataConfidence TimestampConfidence { get; }

    public GeoPoint? Location { get; }

    public int Year { get; }

    public bool HasGps => Location is not null;
}
