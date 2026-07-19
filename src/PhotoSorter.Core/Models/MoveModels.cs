namespace PhotoSorter.Core.Models;

public sealed record MovePlanEntry
{
    public required string BundleId { get; init; }

    public required string SourceRelativePath { get; init; }

    public required string DestinationRelativePath { get; init; }

    public long ExpectedLength { get; init; }

    public DateTimeOffset ExpectedLastWriteTimeUtc { get; init; }
}

public sealed record MovePlan
{
    public required string PicturesRoot { get; init; }

    public required string DestinationDirectoryRelativePath { get; init; }

    public required IReadOnlyList<MovePlanEntry> Entries { get; init; }
}

public sealed record MovePlanBuildResult
{
    public MovePlan? Plan { get; init; }

    public IReadOnlyList<string> Errors { get; init; } = [];

    public bool IsValid => Plan is not null && Errors.Count == 0;
}

public sealed record MovePreflightResult
{
    public required IReadOnlyList<string> Errors { get; init; }

    public IReadOnlyList<MovePlanEntry> EquivalentDestinationEntries { get; init; } = [];

    public bool IsValid => Errors.Count == 0;
}

public sealed record MoveExecutionOptions
{
    public bool DeleteEquivalentSources { get; init; }
}

public enum MoveItemStatus
{
    Moved,
    DeletedEquivalentSource,
    Failed,
    NotAttempted,
}

public sealed record MoveItemResult
{
    public required MovePlanEntry Entry { get; init; }

    public MoveItemStatus Status { get; init; }

    public string? Error { get; init; }
}

public sealed record MoveExecutionResult
{
    public required IReadOnlyList<MoveItemResult> Items { get; init; }

    public bool Succeeded => Items.Count > 0
        && Items.All(static item => item.Status is MoveItemStatus.Moved
            or MoveItemStatus.DeletedEquivalentSource);
}
