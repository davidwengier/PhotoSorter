using PhotoSorter.Core.Models;

namespace PhotoSorter.Core.Contracts;

public interface ISharedStateStore
{
    string GetStatePath(string picturesRoot);

    Task<PhotoSorterState> LoadAsync(string picturesRoot, CancellationToken cancellationToken = default);

    Task<PhotoSorterState> UpdateAsync(
        string picturesRoot,
        Func<PhotoSorterState, PhotoSorterState> update,
        CancellationToken cancellationToken = default);
}

public interface IMediaCache
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<string, MediaAsset>> LoadAssetsAsync(CancellationToken cancellationToken = default);

    Task ReplaceAssetsAsync(
        IReadOnlyCollection<MediaAsset> assets,
        CancellationToken cancellationToken = default);

    Task<string?> GetGeocodeAsync(string key, CancellationToken cancellationToken = default);

    Task SetGeocodeAsync(string key, string displayName, CancellationToken cancellationToken = default);
}

public interface IMediaCacheFactory
{
    IMediaCache Create(string picturesRoot);
}

public interface IMediaScanner
{
    Task<MediaScanResult> ScanAsync(
        string picturesRoot,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public interface IThumbnailService
{
    Task<byte[]?> GetThumbnailAsync(
        string absolutePath,
        long length,
        DateTimeOffset lastWriteTimeUtc,
        int pixelSize,
        CancellationToken cancellationToken = default);
}

public interface IReverseGeocoder
{
    string Attribution { get; }

    Task<PlaceName?> ReverseGeocodeAsync(
        string picturesRoot,
        GeoCircle area,
        CancellationToken cancellationToken = default);
}

public interface IMoveExecutor
{
    Task<MovePreflightResult> PreflightAsync(
        MovePlan plan,
        CancellationToken cancellationToken = default);

    Task<MoveExecutionResult> ExecuteAsync(
        MovePlan plan,
        MoveExecutionOptions? options = null,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default);
}

public interface IRecentRootStore
{
    Task<string?> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(string picturesRoot, CancellationToken cancellationToken = default);
}
