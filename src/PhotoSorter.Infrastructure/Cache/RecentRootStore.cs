using PhotoSorter.Core.Contracts;

namespace PhotoSorter.Infrastructure.Cache;

public sealed class RecentRootStore(CachePathProvider pathProvider) : IRecentRootStore
{
    private readonly string _path = Path.Combine(pathProvider.BasePath, "recent-root.txt");

    public async Task<string?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path))
        {
            return null;
        }

        var value = (await File.ReadAllTextAsync(_path, cancellationToken).ConfigureAwait(false)).Trim();
        return Directory.Exists(value) ? value : null;
    }

    public async Task SaveAsync(string picturesRoot, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(picturesRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        await File.WriteAllTextAsync(
            _path,
            Path.GetFullPath(picturesRoot) + Environment.NewLine,
            cancellationToken).ConfigureAwait(false);
    }
}
