using PhotoSorter.Core.Models;
using PhotoSorter.Infrastructure.Media;
using PhotoSorter.Infrastructure.Tests.TestSupport;

namespace PhotoSorter.Infrastructure.Tests;

[TestClass]
public sealed class MediaMetadataReaderTests
{
    /// <summary>
    /// The smallest possible valid 1x1 white JPEG (no Exif/GPS data). Purely synthetic binary structure — not a photo
    /// of any person, place, or copyrighted work — used only to exercise MetadataExtractor's parser without throwing.
    /// </summary>
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
    [DataRow(".jpg", MediaKind.Image)]
    [DataRow(".JPEG", MediaKind.Image)]
    [DataRow(".heic", MediaKind.Image)]
    [DataRow(".png", MediaKind.Image)]
    [DataRow(".mp4", MediaKind.Video)]
    [DataRow(".MOV", MediaKind.Video)]
    [DataRow(".xmp", MediaKind.Sidecar)]
    [DataRow(".nar", MediaKind.Sidecar)]
    [DataRow(".txt", MediaKind.Unknown)]
    public void GetMediaKind_VariousExtensions_ReturnsExpectedKind(string extension, MediaKind expected)
    {
        var result = MediaMetadataReader.GetMediaKind(extension);

        Assert.AreEqual(expected, result);
    }

    [TestMethod]
    public async Task Read_ValidImageWithCompactDateFileName_FallsBackToFileNameTimestamp()
    {
        using var temp = new TempDirectory();
        var absolutePath = Path.Combine(temp.Path, "20230615120000.jpg");
        await File.WriteAllBytesAsync(absolutePath, MinimalJpeg);
        var lastWrite = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var sut = new MediaMetadataReader();

        var asset = sut.Read(absolutePath, "20230615120000.jpg", 2023, MinimalJpeg.Length, lastWrite, FileAttributes.Normal);

        Assert.AreEqual(TimestampSource.FileName, asset.TimestampSource);
        Assert.AreEqual(MetadataConfidence.Medium, asset.TimestampConfidence);
        Assert.AreEqual(OffsetConfidence.Inferred, asset.OffsetConfidence);
        Assert.AreEqual(2023, asset.CapturedAt.Year);
        Assert.AreEqual(6, asset.CapturedAt.Month);
        Assert.AreEqual(15, asset.CapturedAt.Day);
        Assert.AreEqual(12, asset.CapturedAt.Hour);
        Assert.AreEqual(0, asset.CapturedAt.Minute);
        Assert.AreEqual(0, asset.CapturedAt.Second);
        Assert.AreEqual(TimeZoneInfo.Local.GetUtcOffset(new DateTime(2023, 6, 15, 12, 0, 0)), asset.CapturedAt.Offset);
        Assert.IsNull(asset.MetadataError);
        Assert.AreEqual(MediaKind.Image, asset.Kind);
    }

    [TestMethod]
    [DataRow("IMG_20230615_120000.jpg")]
    [DataRow("20230615-120000.jpg")]
    public async Task Read_CompactDateFileNameWithSeparator_FallsBackToFileNameTimestamp(string fileName)
    {
        using var temp = new TempDirectory();
        var absolutePath = Path.Combine(temp.Path, fileName);
        await File.WriteAllBytesAsync(absolutePath, MinimalJpeg);
        var lastWrite = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var sut = new MediaMetadataReader();

        var asset = sut.Read(absolutePath, fileName, 2023, MinimalJpeg.Length, lastWrite, FileAttributes.Normal);

        Assert.AreEqual(TimestampSource.FileName, asset.TimestampSource);
        Assert.AreEqual(MetadataConfidence.Medium, asset.TimestampConfidence);
        Assert.AreEqual(new DateTime(2023, 6, 15, 12, 0, 0), asset.CapturedAt.DateTime);
    }

    [TestMethod]
    public async Task Read_ValidImageWithSeparatedDateFileName_FallsBackToFileNameTimestamp()
    {
        using var temp = new TempDirectory();
        var absolutePath = Path.Combine(temp.Path, "2023-06-15 12.30.45.jpg");
        await File.WriteAllBytesAsync(absolutePath, MinimalJpeg);
        var lastWrite = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var sut = new MediaMetadataReader();

        var asset = sut.Read(absolutePath, "2023-06-15 12.30.45.jpg", 2023, MinimalJpeg.Length, lastWrite, FileAttributes.Normal);

        Assert.AreEqual(TimestampSource.FileName, asset.TimestampSource);
        Assert.AreEqual(MetadataConfidence.Medium, asset.TimestampConfidence);
        Assert.AreEqual(12, asset.CapturedAt.Hour);
        Assert.AreEqual(30, asset.CapturedAt.Minute);
        Assert.AreEqual(45, asset.CapturedAt.Second);
    }

    [TestMethod]
    public async Task Read_ValidImageWithoutRecognizableFileName_FallsBackToFileSystemTimestamp()
    {
        using var temp = new TempDirectory();
        var absolutePath = Path.Combine(temp.Path, "photo.jpg");
        await File.WriteAllBytesAsync(absolutePath, MinimalJpeg);
        var lastWrite = new DateTimeOffset(2021, 3, 4, 8, 15, 0, TimeSpan.Zero);
        var sut = new MediaMetadataReader();

        var asset = sut.Read(absolutePath, "photo.jpg", 2021, MinimalJpeg.Length, lastWrite, FileAttributes.Normal);

        Assert.AreEqual(TimestampSource.FileSystem, asset.TimestampSource);
        Assert.AreEqual(MetadataConfidence.Low, asset.TimestampConfidence);
        Assert.AreEqual(OffsetConfidence.Unknown, asset.OffsetConfidence);
        Assert.AreEqual(lastWrite, asset.CapturedAt);
        Assert.IsNull(asset.MetadataError);
    }

    [TestMethod]
    public async Task Read_CorruptFile_FallsBackToFileSystemTimestampWithMetadataError()
    {
        using var temp = new TempDirectory();
        var absolutePath = Path.Combine(temp.Path, "corrupt.jpg");
        await File.WriteAllBytesAsync(absolutePath, [0x00, 0x01, 0x02, 0x03, 0x04]);
        var lastWrite = new DateTimeOffset(2019, 5, 6, 7, 8, 9, TimeSpan.Zero);
        var sut = new MediaMetadataReader();

        var asset = sut.Read(absolutePath, "corrupt.jpg", 2019, 5, lastWrite, FileAttributes.Normal);

        Assert.AreEqual(TimestampSource.FileSystem, asset.TimestampSource);
        Assert.AreEqual(MetadataConfidence.Low, asset.TimestampConfidence);
        Assert.AreEqual(OffsetConfidence.Unknown, asset.OffsetConfidence);
        Assert.AreEqual(lastWrite, asset.CapturedAt);
        Assert.IsNotNull(asset.MetadataError);
        Assert.IsNotEmpty(asset.MetadataError!);
        Assert.IsTrue(asset.MetadataReadFailed);
    }

    [TestMethod]
    public async Task Read_Sidecar_DoesNotReportAReadFailure()
    {
        using var temp = new TempDirectory();
        var absolutePath = Path.Combine(temp.Path, "IMG_0001.nar");
        await File.WriteAllBytesAsync(absolutePath, [0x00, 0x01, 0x02]);
        var lastWrite = new DateTimeOffset(2022, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var sut = new MediaMetadataReader();

        var asset = sut.Read(absolutePath, "IMG_0001.nar", 2022, 3, lastWrite, FileAttributes.Normal);

        Assert.AreEqual(MediaKind.Sidecar, asset.Kind);
        Assert.IsFalse(asset.MetadataReadFailed);
        Assert.IsNull(asset.MetadataError);
        Assert.AreEqual(TimestampSource.FileSystem, asset.TimestampSource);
    }

    [TestMethod]
    public void Read_PreservesRequestedYearLengthAndAttributes()
    {
        using var temp = new TempDirectory();
        var absolutePath = Path.Combine(temp.Path, "photo2.jpg");
        File.WriteAllBytes(absolutePath, MinimalJpeg);
        var sut = new MediaMetadataReader();

        var asset = sut.Read(
            absolutePath,
            "photo2.jpg",
            2018,
            MinimalJpeg.Length,
            DateTimeOffset.UtcNow,
            FileAttributes.ReadOnly);

        Assert.AreEqual(2018, asset.Year);
        Assert.AreEqual(MinimalJpeg.Length, asset.Length);
        Assert.AreEqual(FileAttributes.ReadOnly, asset.Attributes);
        Assert.AreEqual(".jpg", asset.Extension);
    }
}
