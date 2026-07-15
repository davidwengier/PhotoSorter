using System.Text.Json.Serialization;

namespace PhotoSorter.Core.Models;

public enum RoutineLocationDisposition
{
    Routine,
    NotRoutine,
}

public sealed record RoutineLocationDecision
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    public required string Name { get; init; }

    public RoutineLocationDisposition Disposition { get; init; }

    public required GeoPoint Center { get; init; }

    public double RadiusMeters { get; init; } = 500;

    public bool SuppressCandidates { get; init; } = true;
}

public sealed record IgnoredFolderRule
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    public required string RelativePath { get; init; }

    public bool Recursive { get; init; } = true;

    public string? Label { get; init; }
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

public sealed record LegacySharedPreferences
{
    public bool? ReverseGeocodingEnabled { get; init; }

    public string ReverseGeocodingProvider { get; init; } = "nominatim";

    public string ReverseGeocodingEndpoint { get; init; } = "https://nominatim.openstreetmap.org/";

    public GroupingOptions? GroupingOverrides { get; init; }
}

public sealed record PhotoSorterState
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public List<RoutineLocationDecision> RoutineLocations { get; init; } = [];

    public List<IgnoredFolderRule> IgnoredFolders { get; init; } = [];

    public List<IgnoredGroupRule> IgnoredGroups { get; init; } = [];

    [JsonPropertyName("preferences")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LegacySharedPreferences? LegacyPreferences { get; init; }
}
