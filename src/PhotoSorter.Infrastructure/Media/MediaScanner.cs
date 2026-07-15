using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;
using PhotoSorter.Core.Contracts;
using PhotoSorter.Core.Models;
using PhotoSorter.Core.Services;

namespace PhotoSorter.Infrastructure.Media;

public sealed partial class MediaScanner(
    IMediaCacheFactory cacheFactory,
    MediaMetadataReader metadataReader,
    AssetBundler assetBundler) : IMediaScanner
{
    private static readonly HashSet<string> SupportedExtensions = new(
        [
            ".jpg",
            ".jpeg",
            ".heic",
            ".heif",
            ".png",
            ".webp",
            ".gif",
            ".dng",
            ".avif",
            ".jp2",
            ".bmp",
            ".tif",
            ".tiff",
            ".mov",
            ".mp4",
            ".m4v",
            ".3gp",
            ".avi",
            ".xmp",
            ".nar",
        ],
        StringComparer.OrdinalIgnoreCase);

    private readonly IMediaCacheFactory _cacheFactory = cacheFactory;
    private readonly MediaMetadataReader _metadataReader = metadataReader;
    private readonly AssetBundler _assetBundler = assetBundler;

    public async Task<MediaScanResult> ScanAsync(
        string picturesRoot,
        PhotoSorterState state,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(picturesRoot);
        ArgumentNullException.ThrowIfNull(state);

        var fullRoot = Path.GetFullPath(picturesRoot);
        if (!Directory.Exists(fullRoot))
        {
            throw new DirectoryNotFoundException($"Pictures folder '{fullRoot}' does not exist.");
        }

        progress?.Report(new ScanProgress(ScanPhase.Discovering, 0, 0));
        var issues = new ConcurrentBag<ScanIssue>();
        var files = Discover(fullRoot, state.IgnoredFolders, issues, cancellationToken);
        var cache = _cacheFactory.Create(fullRoot);
        await cache.InitializeAsync(cancellationToken).ConfigureAwait(false);
        var cachedAssets = await cache.LoadAssetsAsync(cancellationToken).ConfigureAwait(false);

        var assets = new ConcurrentDictionary<string, MediaAsset>(StringComparer.OrdinalIgnoreCase);
        var reused = 0;
        var extracted = 0;
        var processed = 0;
        var parallelOptions = new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount / 2, 2, 6),
        };

        await Parallel.ForEachAsync(files, parallelOptions, (file, _) =>
        {
            MediaAsset asset;
            if (cachedAssets.TryGetValue(file.RelativePath, out var cached)
                && FingerprintMatches(file, cached))
            {
                asset = cached;
                Interlocked.Increment(ref reused);
            }
            else
            {
                asset = _metadataReader.Read(
                    file.AbsolutePath,
                    file.RelativePath,
                    file.Year,
                    file.Length,
                    file.LastWriteTimeUtc,
                    file.Attributes);
                Interlocked.Increment(ref extracted);
            }

            assets[file.RelativePath] = asset;
            if (asset.MetadataReadFailed && !string.IsNullOrWhiteSpace(asset.MetadataError))
            {
                issues.Add(new ScanIssue
                {
                    RelativePath = asset.RelativePath,
                    Message = asset.MetadataError,
                });
            }

            var completed = Interlocked.Increment(ref processed);
            progress?.Report(new ScanProgress(
                ScanPhase.ReadingMetadata,
                completed,
                files.Count,
                file.RelativePath));
            return ValueTask.CompletedTask;
        }).ConfigureAwait(false);

        var orderedAssets = assets.Values
            .OrderBy(static asset => asset.CapturedAt)
            .ThenBy(static asset => asset.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        progress?.Report(new ScanProgress(ScanPhase.SavingCache, orderedAssets.Length, orderedAssets.Length));
        await cache.ReplaceAssetsAsync(orderedAssets, cancellationToken).ConfigureAwait(false);
        var bundles = _assetBundler.Bundle(orderedAssets);
        progress?.Report(new ScanProgress(ScanPhase.Completed, orderedAssets.Length, orderedAssets.Length));

        return new MediaScanResult
        {
            Assets = orderedAssets,
            Bundles = bundles,
            Issues = issues
                .OrderBy(static issue => issue.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            Years = orderedAssets.Select(static asset => asset.Year).Distinct().Order().ToArray(),
            ReusedMetadataCount = reused,
            ExtractedMetadataCount = extracted,
        };
    }

    private static List<DiscoveredFile> Discover(
        string picturesRoot,
        IReadOnlyList<IgnoredFolderRule> ignoredFolders,
        ConcurrentBag<ScanIssue> issues,
        CancellationToken cancellationToken)
    {
        var discovered = new List<DiscoveredFile>();
        IEnumerable<string> yearDirectories;
        try
        {
            yearDirectories = Directory.EnumerateDirectories(picturesRoot).ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new IOException($"Could not enumerate '{picturesRoot}': {exception.Message}", exception);
        }

        foreach (var yearDirectory in yearDirectories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var yearName = Path.GetFileName(yearDirectory);
            if (!YearRegex().IsMatch(yearName)
                || !int.TryParse(yearName, NumberStyles.None, CultureInfo.InvariantCulture, out var year))
            {
                continue;
            }

            var phoneImages = Path.Combine(yearDirectory, "Phone Images");
            if (!Directory.Exists(phoneImages))
            {
                continue;
            }

            try
            {
                var enumerationOptions = new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true,
                    ReturnSpecialDirectories = false,
                    AttributesToSkip = FileAttributes.ReparsePoint,
                };
                foreach (var absolutePath in Directory.EnumerateFiles(phoneImages, "*", enumerationOptions))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var extension = Path.GetExtension(absolutePath);
                    if (!SupportedExtensions.Contains(extension))
                    {
                        continue;
                    }

                    var relativePath = PathRuleMatcher.NormalizeRelativePath(
                        Path.GetRelativePath(picturesRoot, absolutePath));
                    if (PathRuleMatcher.IsIgnored(relativePath, ignoredFolders))
                    {
                        continue;
                    }

                    try
                    {
                        var info = new FileInfo(absolutePath);
                        discovered.Add(new DiscoveredFile(
                            absolutePath,
                            relativePath,
                            year,
                            info.Length,
                            new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero),
                            info.Attributes));
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                    {
                        issues.Add(new ScanIssue
                        {
                            RelativePath = relativePath,
                            Message = exception.Message,
                        });
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                issues.Add(new ScanIssue
                {
                    RelativePath = Path.GetRelativePath(picturesRoot, phoneImages),
                    Message = exception.Message,
                });
            }
        }

        return discovered;
    }

    private static bool FingerprintMatches(DiscoveredFile file, MediaAsset cached) =>
        file.Length == cached.Length
        && file.LastWriteTimeUtc.UtcDateTime.Ticks == cached.LastWriteTimeUtc.UtcDateTime.Ticks
        && file.Attributes == cached.Attributes
        && file.Year == cached.Year;

    private sealed record DiscoveredFile(
        string AbsolutePath,
        string RelativePath,
        int Year,
        long Length,
        DateTimeOffset LastWriteTimeUtc,
        FileAttributes Attributes);

    [GeneratedRegex(@"^\d{4}$", RegexOptions.CultureInvariant)]
    private static partial Regex YearRegex();
}
