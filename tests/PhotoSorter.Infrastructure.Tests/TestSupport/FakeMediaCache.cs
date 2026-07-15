using PhotoSorter.Core.Contracts;
using PhotoSorter.Core.Models;

namespace PhotoSorter.Infrastructure.Tests.TestSupport;

/// <summary>
/// In-memory <see cref="IMediaCache"/> test double that lets a test seed cached assets up front (to control
/// fingerprint-match "reuse" behavior in <see cref="Media.MediaScanner"/>) and inspect what was written afterwards.
/// </summary>
internal sealed class FakeMediaCache : IMediaCache
{
    private Dictionary<string, MediaAsset> _seedAssets = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, MediaAsset>? LastReplacedAssets { get; private set; }

    public int InitializeCallCount { get; private set; }

    public void SeedAssets(IEnumerable<MediaAsset> assets) =>
        _seedAssets = assets.ToDictionary(static asset => asset.RelativePath, StringComparer.OrdinalIgnoreCase);

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        InitializeCallCount++;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyDictionary<string, MediaAsset>> LoadAssetsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<string, MediaAsset>>(_seedAssets);

    public Task ReplaceAssetsAsync(IReadOnlyCollection<MediaAsset> assets, CancellationToken cancellationToken = default)
    {
        LastReplacedAssets = assets.ToDictionary(static asset => asset.RelativePath, StringComparer.OrdinalIgnoreCase);
        return Task.CompletedTask;
    }

    public Task<string?> GetGeocodeAsync(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(null);

    public Task SetGeocodeAsync(string key, string displayName, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}

internal sealed class FakeMediaCacheFactory(FakeMediaCache cache) : IMediaCacheFactory
{
    public IMediaCache Create(string picturesRoot) => cache;
}
