using Microsoft.Data.Sqlite;
using PhotoSorter.Core.Models;
using PhotoSorter.Infrastructure.Cache;
using PhotoSorter.Infrastructure.Tests.TestSupport;

namespace PhotoSorter.Infrastructure.Tests;

[TestClass]
public sealed class SqliteMediaCacheTests
{
    [TestMethod]
    public async Task InitializeAsync_CalledTwice_DoesNotThrowAndSchemaIsUsable()
    {
        using var temp = new TempDirectory();
        var sut = new SqliteMediaCache(Path.Combine(temp.Path, "cache.db"));

        await sut.InitializeAsync();
        await sut.InitializeAsync();

        var assets = await sut.LoadAssetsAsync();
        Assert.IsEmpty(assets);
    }

    [TestMethod]
    public async Task InitializeAsync_UnversionedLegacyCache_RebuildsDisposableSchema()
    {
        using var temp = new TempDirectory();
        var databasePath = Path.Combine(temp.Path, "cache.db");
        await using (var connection = new SqliteConnection($"Data Source={databasePath}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE media_assets(relative_path TEXT PRIMARY KEY);";
            await command.ExecuteNonQueryAsync();
        }

        var sut = new SqliteMediaCache(databasePath);
        await sut.InitializeAsync();

        Assert.IsEmpty(await sut.LoadAssetsAsync());
    }

    [TestMethod]
    public async Task InitializeAsync_NewerCacheSchema_RefusesToOverwriteIt()
    {
        using var temp = new TempDirectory();
        var databasePath = Path.Combine(temp.Path, "cache.db");
        await using (var connection = new SqliteConnection($"Data Source={databasePath}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version = 99;";
            await command.ExecuteNonQueryAsync();
        }

        var sut = new SqliteMediaCache(databasePath);

        var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => sut.InitializeAsync());
        StringAssert.Contains(exception.Message, "newer than the supported");
    }

    [TestMethod]
    public async Task ReplaceAssetsAsync_ThenLoadAssetsAsync_RoundTripsAllFieldsExactly()
    {
        using var temp = new TempDirectory();
        var sut = new SqliteMediaCache(Path.Combine(temp.Path, "cache.db"));
        await sut.InitializeAsync();
        var asset = new MediaAsset
        {
            RelativePath = @"2023\Phone Images\IMG_0001.jpg",
            Year = 2023,
            Length = 123_456,
            LastWriteTimeUtc = new DateTimeOffset(2023, 6, 1, 10, 30, 0, TimeSpan.Zero),
            Attributes = FileAttributes.ReadOnly,
            Kind = MediaKind.Image,
            Extension = ".jpg",
            CapturedAt = new DateTimeOffset(2023, 6, 1, 10, 0, 0, TimeSpan.Zero),
            TimestampSource = TimestampSource.ExifOriginal,
            TimestampConfidence = MetadataConfidence.High,
            OffsetConfidence = OffsetConfidence.Explicit,
            Location = new GeoPoint(51.5074, -0.1278),
            ContentIdentifier = "LIVE-ABC",
            Duration = TimeSpan.FromSeconds(4.5),
            Width = 4032,
            Height = 3024,
            MetadataError = null,
            MetadataReadFailed = false,
        };

        await sut.ReplaceAssetsAsync([asset]);
        var reloaded = await sut.LoadAssetsAsync();

        Assert.HasCount(1, reloaded);
        var roundTripped = reloaded[asset.RelativePath];
        Assert.AreEqual(asset.Year, roundTripped.Year);
        Assert.AreEqual(asset.Length, roundTripped.Length);
        Assert.AreEqual(asset.LastWriteTimeUtc, roundTripped.LastWriteTimeUtc);
        Assert.AreEqual(asset.Attributes, roundTripped.Attributes);
        Assert.AreEqual(asset.Kind, roundTripped.Kind);
        Assert.AreEqual(asset.Extension, roundTripped.Extension);
        Assert.AreEqual(asset.CapturedAt, roundTripped.CapturedAt);
        Assert.AreEqual(asset.TimestampSource, roundTripped.TimestampSource);
        Assert.AreEqual(asset.TimestampConfidence, roundTripped.TimestampConfidence);
        Assert.AreEqual(asset.OffsetConfidence, roundTripped.OffsetConfidence);
        Assert.AreEqual(asset.Location!.Value.Latitude, roundTripped.Location!.Value.Latitude, 1e-9);
        Assert.AreEqual(asset.Location!.Value.Longitude, roundTripped.Location!.Value.Longitude, 1e-9);
        Assert.AreEqual(asset.ContentIdentifier, roundTripped.ContentIdentifier);
        Assert.AreEqual(asset.Duration!.Value.TotalSeconds, roundTripped.Duration!.Value.TotalSeconds, 1e-6);
        Assert.AreEqual(asset.Width, roundTripped.Width);
        Assert.AreEqual(asset.Height, roundTripped.Height);
        Assert.IsNull(roundTripped.MetadataError);
        Assert.IsFalse(roundTripped.MetadataReadFailed);
    }

    [TestMethod]
    public async Task ReplaceAssetsAsync_NullableFieldsAbsent_RoundTripsAsNull()
    {
        using var temp = new TempDirectory();
        var sut = new SqliteMediaCache(Path.Combine(temp.Path, "cache.db"));
        await sut.InitializeAsync();
        var asset = new MediaAsset
        {
            RelativePath = @"2023\Phone Images\NoGps.jpg",
            Year = 2023,
            Extension = ".jpg",
            Kind = MediaKind.Image,
            CapturedAt = new DateTimeOffset(2023, 1, 1, 0, 0, 0, TimeSpan.Zero),
            MetadataError = "Some extraction error",
            MetadataReadFailed = true,
        };

        await sut.ReplaceAssetsAsync([asset]);
        var reloaded = await sut.LoadAssetsAsync();

        var roundTripped = reloaded[asset.RelativePath];
        Assert.IsNull(roundTripped.Location);
        Assert.IsNull(roundTripped.ContentIdentifier);
        Assert.IsNull(roundTripped.Duration);
        Assert.IsNull(roundTripped.Width);
        Assert.IsNull(roundTripped.Height);
        Assert.AreEqual("Some extraction error", roundTripped.MetadataError);
        Assert.IsTrue(roundTripped.MetadataReadFailed);
    }

    [TestMethod]
    public async Task ReplaceAssetsAsync_CalledAgainWithSubset_RemovesMissingAssets()
    {
        using var temp = new TempDirectory();
        var sut = new SqliteMediaCache(Path.Combine(temp.Path, "cache.db"));
        await sut.InitializeAsync();
        var keep = CreateAsset(@"2023\Phone Images\keep.jpg");
        var remove = CreateAsset(@"2023\Phone Images\remove.jpg");
        await sut.ReplaceAssetsAsync([keep, remove]);

        await sut.ReplaceAssetsAsync([keep]);
        var reloaded = await sut.LoadAssetsAsync();

        Assert.HasCount(1, reloaded);
        Assert.IsTrue(reloaded.ContainsKey(keep.RelativePath));
        Assert.IsFalse(reloaded.ContainsKey(remove.RelativePath));
    }

    [TestMethod]
    public async Task ReplaceAssetsAsync_UpdatingExistingPath_OverwritesFieldsInPlace()
    {
        using var temp = new TempDirectory();
        var sut = new SqliteMediaCache(Path.Combine(temp.Path, "cache.db"));
        await sut.InitializeAsync();
        var original = CreateAsset(@"2023\Phone Images\a.jpg", length: 100);
        await sut.ReplaceAssetsAsync([original]);

        var changed = original with { Length = 999 };
        await sut.ReplaceAssetsAsync([changed]);
        var reloaded = await sut.LoadAssetsAsync();

        Assert.HasCount(1, reloaded);
        Assert.AreEqual(999, reloaded[original.RelativePath].Length);
    }

    [TestMethod]
    public async Task GetGeocodeAsync_UnknownKey_ReturnsNull()
    {
        using var temp = new TempDirectory();
        var sut = new SqliteMediaCache(Path.Combine(temp.Path, "cache.db"));
        await sut.InitializeAsync();

        var result = await sut.GetGeocodeAsync("missing-key");

        Assert.IsNull(result);
    }

    [TestMethod]
    public async Task SetGeocodeAsync_ThenGetGeocodeAsync_RoundTripsDisplayName()
    {
        using var temp = new TempDirectory();
        var sut = new SqliteMediaCache(Path.Combine(temp.Path, "cache.db"));
        await sut.InitializeAsync();

        await sut.SetGeocodeAsync("nominatim.openstreetmap.org|51.50000|-0.12000", "London, UK");
        var result = await sut.GetGeocodeAsync("nominatim.openstreetmap.org|51.50000|-0.12000");

        Assert.AreEqual("London, UK", result);
    }

    [TestMethod]
    public async Task SetGeocodeAsync_SameKeyTwice_UpsertsInsteadOfDuplicating()
    {
        using var temp = new TempDirectory();
        var sut = new SqliteMediaCache(Path.Combine(temp.Path, "cache.db"));
        await sut.InitializeAsync();

        await sut.SetGeocodeAsync("key1", "Old Name");
        await sut.SetGeocodeAsync("key1", "New Name");
        var result = await sut.GetGeocodeAsync("key1");

        Assert.AreEqual("New Name", result);
    }

    private static MediaAsset CreateAsset(string relativePath, long length = 0) => new()
    {
        RelativePath = relativePath,
        Year = 2023,
        Length = length,
        Extension = Path.GetExtension(relativePath),
        Kind = MediaKind.Image,
        CapturedAt = new DateTimeOffset(2023, 6, 1, 0, 0, 0, TimeSpan.Zero),
    };
}
