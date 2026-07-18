namespace PhotoSorter.Core.Models;

public sealed record RoutineLocationDecision
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    public required string Name { get; init; }

    public required GeoPoint Center { get; init; }

    public double RadiusMeters { get; init; } = 500;
}

public sealed record IgnoredGroupRule
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    public string? Label { get; init; }

    public CandidateKind Kind { get; init; }

    public DateTimeOffset Start { get; init; }

    public DateTimeOffset End { get; init; }

    public int TimePaddingMinutes { get; init; } = 60;

    public double RequiredLocationMatchFraction { get; init; } = 0.4;

    public List<GeoCircle> Areas { get; init; } = [];
}

public sealed record PhotoSorterState
{
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public List<RoutineLocationDecision> RoutineLocations { get; init; } = [];

    public List<IgnoredGroupRule> IgnoredGroups { get; init; } = [];
}
