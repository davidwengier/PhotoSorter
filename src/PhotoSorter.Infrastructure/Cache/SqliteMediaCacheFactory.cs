using PhotoSorter.Core.Contracts;

namespace PhotoSorter.Infrastructure.Cache;

public sealed class SqliteMediaCacheFactory(CachePathProvider pathProvider) : IMediaCacheFactory
{
    private readonly CachePathProvider _pathProvider = pathProvider;

    public IMediaCache Create(string picturesRoot)
    {
        var cacheRoot = _pathProvider.GetRootCachePath(picturesRoot);
        return new SqliteMediaCache(Path.Combine(cacheRoot, "photosorter.db"));
    }
}
