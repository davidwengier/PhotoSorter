using System.Security.Cryptography;
using System.Text;

namespace PhotoSorter.Infrastructure.Cache;

public sealed class CachePathProvider
{
    private readonly string _basePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PhotoSorter",
        "Cache");

    public string BasePath => _basePath;

    public string GetRootCachePath(string picturesRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(picturesRoot);
        var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(picturesRoot))
            .ToUpperInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Path.Combine(_basePath, Convert.ToHexString(hash.AsSpan(0, 12)).ToLowerInvariant());
    }
}
