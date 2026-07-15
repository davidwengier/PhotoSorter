namespace PhotoSorter.Core.Models;

public enum CandidateKind
{
    Event,
    Trip,
}

public sealed record GroupingOptions
{
    public double EventRadiusMeters { get; init; } = 3_000;

    public double EventMaximumGapHours { get; init; } = 8;

    public int EventMinimumAnchors { get; init; } = 3;

    public double TripMaximumGapHours { get; init; } = 30;

    public double TripMinimumMovementMeters { get; init; } = 5_000;

    public double TripMinimumDurationHours { get; init; } = 12;

    public double TripRoutineDistanceMeters { get; init; } = 25_000;

    public double AttachmentPaddingHours { get; init; } = 2;
}

public sealed record CandidateGroup
{
    public required string Id { get; init; }

    public CandidateKind Kind { get; init; }

    public int Year { get; init; }

    public DateTimeOffset Start { get; init; }

    public DateTimeOffset End { get; init; }

    public required IReadOnlyList<AssetBundle> Bundles { get; init; }

    public required IReadOnlyList<GeoCircle> Areas { get; init; }

    public double Score { get; init; }

    public required IReadOnlyList<string> Reasons { get; init; }

    public string? PlaceLabel { get; init; }

    public string? FullPlaceLabel { get; init; }

    public string? PrimaryPlaceLabel { get; init; }

    public int FileCount => Bundles.Sum(static bundle => bundle.Assets.Count);

    public int GpsBundleCount => Bundles.Count(static bundle => bundle.Location is not null);

    public TimeSpan Duration => End - Start;
}

public sealed record GroupingResult
{
    public required IReadOnlyList<CandidateGroup> Candidates { get; init; }
}
