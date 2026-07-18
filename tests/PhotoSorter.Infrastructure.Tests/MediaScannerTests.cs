using PhotoSorter.Core.Models;
using PhotoSorter.Core.Services;
using PhotoSorter.Infrastructure.Media;
using PhotoSorter.Infrastructure.Tests.TestSupport;

namespace PhotoSorter.Infrastructure.Tests;

[TestClass]
public sealed class MediaScannerTests
{
    private static readonly byte[] MinimalJpeg =
    [
        0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0x01, 0x00, 0x00, 0x01,
        0x00, 0x01, 0x00, 0x00, 0xFF, 0xDB, 0x00, 0x43, 0x00, 0x08, 0x06, 0x06, 0x07, 0x06, 0x05, 0x08,
        0x07, 0x07, 0x07, 0x09, 0x09, 0x08, 0x0A, 0x0C, 0x14, 0x0D, 0x0C, 0x0B, 0x0B, 0x0C, 0x19, 0x12,
        0x13, 0x0F, 0x14, 0x1D, 0x1A, 0x1F, 0x1E, 0x1D, 0x1A, 0x1C, 0x1C, 0x20, 0x24, 0x2E, 0x27, 0x20,
        0x22, 0x2C, 0x23, 0x1C, 0x1C, 0x28, 0x37, 0x29, 0x2C, 0x30, 0x31, 0x34, 0x34, 0x34, 0x1F, 0x27,
        0x39, 0x3D, 0x38, 0x32, 0x3C, 0x2E, 0x33, 0x34, 0x32, 0xFF, 0xC0, 0x00, 0x11, 0x08, 0x00, 0x01,
        0x00, 0x01, 0x01, 0x01, 0x11, 0x00, 0xFF, 0xC4, 0x00, 0x14, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xFF, 0xDA, 0x00,
        0x08, 0x01, 0x01, 0x00, 0x00, 0x3F, 0x00, 0xD2, 0xFF, 0xD9,
    ];

    [TestMethod]
    public async Task ScanAsync_FingerprintMatchesCache_ReusesCachedAssetVerbatimWithoutReExtracting()
    {
        using var temp = new TempDirectory();
        var phoneImages = Directory.CreateDirectory(Path.Combine(temp.Path, "2023", "Phone Images")).FullName;
        var filePath = Path.Combine(phoneImages, "reuse.jpg");
        // Deliberately corrupt bytes: if the scanner actually re-reads this file, MediaMetadataReader will produce a
        // MetadataError. A successful cache reuse must return the seeded (error-free) asset unchanged instead.
        await File.WriteAllBytesAsync(filePath, [0x00, 0x01, 0x02, 0x03]);
        var info = new FileInfo(filePath);
        var relativePath = @"2023\Phone Images\reuse.jpg";
        var sentinelCapturedAt = new DateTimeOffset(2023, 6, 1, 12, 0, 0, TimeSpan.Zero);
        var cachedAsset = new MediaAsset
        {
            RelativePath = relativePath,
            Year = 2023,
            Length = info.Length,
            LastWriteTimeUtc = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero),
            Attributes = info.Attributes,
            Extension = ".jpg",
            Kind = MediaKind.Image,
            CapturedAt = sentinelCapturedAt,
            TimestampSource = TimestampSource.ExifOriginal,
            TimestampConfidence = MetadataConfidence.High,
            MetadataError = null,
        };
        var cache = new FakeMediaCache();
        cache.SeedAssets([cachedAsset]);
        var scanner = new MediaScanner(new FakeMediaCacheFactory(cache), new MediaMetadataReader(), new AssetBundler());

        var result = await scanner.ScanAsync(temp.Path);

        Assert.AreEqual(1, result.ReusedMetadataCount);
        Assert.AreEqual(0, result.ExtractedMetadataCount);
        Assert.HasCount(1, result.Assets);
        Assert.AreEqual(sentinelCapturedAt, result.Assets[0].CapturedAt);
        Assert.IsNull(result.Assets[0].MetadataError);
    }

    [TestMethod]
    public async Task ScanAsync_FingerprintMismatch_ReExtractsMetadata()
    {
        using var temp = new TempDirectory();
        var phoneImages = Directory.CreateDirectory(Path.Combine(temp.Path, "2023", "Phone Images")).FullName;
        var filePath = Path.Combine(phoneImages, "20230101090000.jpg");
        await File.WriteAllBytesAsync(filePath, MinimalJpeg);
        var relativePath = @"2023\Phone Images\20230101090000.jpg";
        var staleCachedAsset = new MediaAsset
        {
            RelativePath = relativePath,
            Year = 2023,
            Length = 1, // Deliberately wrong length so the fingerprint check fails and re-extraction is forced.
            LastWriteTimeUtc = DateTimeOffset.UtcNow,
            Attributes = FileAttributes.Normal,
            Extension = ".jpg",
            Kind = MediaKind.Image,
            CapturedAt = DateTimeOffset.UtcNow,
        };
        var cache = new FakeMediaCache();
        cache.SeedAssets([staleCachedAsset]);
        var scanner = new MediaScanner(new FakeMediaCacheFactory(cache), new MediaMetadataReader(), new AssetBundler());

        var result = await scanner.ScanAsync(temp.Path);

        Assert.AreEqual(0, result.ReusedMetadataCount);
        Assert.AreEqual(1, result.ExtractedMetadataCount);
        Assert.AreEqual(TimestampSource.FileName, result.Assets[0].TimestampSource);
        Assert.AreEqual(2023, result.Assets[0].CapturedAt.Year);
        Assert.AreEqual(1, result.Assets[0].CapturedAt.Month);
        Assert.AreEqual(1, result.Assets[0].CapturedAt.Day);
    }

    [TestMethod]
    public async Task ScanAsync_NonYearAndMissingPhoneImagesDirectories_AreSkipped()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(Path.Combine(temp.Path, "NotAYear"));
        await File.WriteAllBytesAsync(Path.Combine(temp.Path, "NotAYear", "orphan.jpg"), MinimalJpeg);
        Directory.CreateDirectory(Path.Combine(temp.Path, "2022")); // Year folder with no "Phone Images" subfolder.
        var validPhoneImages = Directory.CreateDirectory(Path.Combine(temp.Path, "2023", "Phone Images")).FullName;
        await File.WriteAllBytesAsync(Path.Combine(validPhoneImages, "valid.jpg"), MinimalJpeg);
        var scanner = CreateScanner();

        var result = await scanner.ScanAsync(temp.Path);

        Assert.HasCount(1, result.Assets);
        Assert.AreEqual(@"2023\Phone Images\valid.jpg", result.Assets[0].RelativePath);
        CollectionAssert.AreEqual(new[] { 2023 }, result.Years.ToArray());
    }

    [TestMethod]
    public async Task ScanAsync_BenignMetadataDiagnostic_IsNotAddedAsScanIssue()
    {
        using var temp = new TempDirectory();
        var phoneImages = Directory.CreateDirectory(Path.Combine(temp.Path, "2023", "Phone Images")).FullName;
        var filePath = Path.Combine(phoneImages, "cached.jpg");
        await File.WriteAllBytesAsync(filePath, MinimalJpeg);
        var info = new FileInfo(filePath);
        var cachedAsset = new MediaAsset
        {
            RelativePath = @"2023\Phone Images\cached.jpg",
            Year = 2023,
            Length = info.Length,
            LastWriteTimeUtc = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero),
            Attributes = info.Attributes,
            Extension = ".jpg",
            Kind = MediaKind.Image,
            CapturedAt = DateTimeOffset.UtcNow,
            MetadataError = "Unsupported optional metadata tag",
            MetadataReadFailed = false,
        };
        var cache = new FakeMediaCache();
        cache.SeedAssets([cachedAsset]);
        var scanner = new MediaScanner(new FakeMediaCacheFactory(cache), new MediaMetadataReader(), new AssetBundler());

        var result = await scanner.ScanAsync(temp.Path);

        Assert.IsEmpty(result.Issues);
    }

    [TestMethod]
    public async Task ScanAsync_MissingPicturesRoot_Throws()
    {
        var scanner = CreateScanner();
        var missingRoot = Path.Combine(Path.GetTempPath(), "DoesNotExist_" + Guid.NewGuid().ToString("N"));

        await Assert.ThrowsExactlyAsync<DirectoryNotFoundException>(
            () => scanner.ScanAsync(missingRoot));
    }

    private static MediaScanner CreateScanner()
    {
        var cache = new FakeMediaCache();
        return new MediaScanner(new FakeMediaCacheFactory(cache), new MediaMetadataReader(), new AssetBundler());
    }
}
