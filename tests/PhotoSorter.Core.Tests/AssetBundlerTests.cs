using PhotoSorter.Core.Models;
using PhotoSorter.Core.Services;

namespace PhotoSorter.Core.Tests;

[TestClass]
public sealed class AssetBundlerTests
{
    private static readonly DateTimeOffset BaseTime = new(2023, 6, 1, 10, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void Bundle_NullAssets_Throws()
    {
        var sut = new AssetBundler();

        Assert.ThrowsExactly<ArgumentNullException>(() => sut.Bundle(null!));
    }

    [TestMethod]
    public void Bundle_SingleAsset_ReturnsSingleBundle()
    {
        var sut = new AssetBundler();
        var asset = CreateAsset(@"2023\Phone Images\a.jpg", BaseTime);

        var bundles = sut.Bundle([asset]);

        Assert.HasCount(1, bundles);
        Assert.HasCount(1, bundles[0].Assets);
        Assert.AreEqual(asset.RelativePath, bundles[0].PrimaryAsset.RelativePath);
    }

    [TestMethod]
    public void Bundle_AssetsShareContentIdentifier_MergedIntoSingleBundle()
    {
        var sut = new AssetBundler();
        var image = CreateAsset(@"2023\Phone Images\IMG_0001.jpg", BaseTime, contentId: "LIVE-1", kind: MediaKind.Image);
        var video = CreateAsset(@"2023\Phone Images\IMG_0001.mov", BaseTime, contentId: "live-1", kind: MediaKind.Video);
        var unrelated = CreateAsset(@"2023\Phone Images\IMG_0002.jpg", BaseTime.AddMinutes(5));

        var bundles = sut.Bundle([image, video, unrelated]);

        Assert.HasCount(2, bundles);
        var liveBundle = bundles.Single(bundle => bundle.Assets.Count == 2);
        CollectionAssert.AreEquivalent(
            new[] { image.RelativePath, video.RelativePath },
            liveBundle.Assets.Select(static asset => asset.RelativePath).ToArray());
    }

    [TestMethod]
    public void Bundle_ImageWithSidecar_MergedIntoSingleBundle()
    {
        var sut = new AssetBundler();
        var image = CreateAsset(@"2023\Phone Images\IMG_0002.jpg", BaseTime, kind: MediaKind.Image);
        var sidecar = CreateAsset(@"2023\Phone Images\IMG_0002.xmp", BaseTime, kind: MediaKind.Sidecar);

        var bundles = sut.Bundle([image, sidecar]);

        Assert.HasCount(1, bundles);
        Assert.HasCount(2, bundles[0].Assets);
        Assert.AreEqual(image.RelativePath, bundles[0].PrimaryAsset.RelativePath);
    }

    [TestMethod]
    public void Bundle_SidecarAloneWithoutPrimary_NotMergedWithUnrelatedImage()
    {
        var sut = new AssetBundler();
        var orphanSidecar = CreateAsset(@"2023\Phone Images\IMG_0003.xmp", BaseTime, kind: MediaKind.Sidecar);
        var otherImage = CreateAsset(@"2023\Phone Images\IMG_0004.jpg", BaseTime.AddMinutes(1), kind: MediaKind.Image);

        var bundles = sut.Bundle([orphanSidecar, otherImage]);

        Assert.HasCount(2, bundles);
    }

    [TestMethod]
    public void Bundle_DifferentBaseNames_RemainSeparateBundles()
    {
        var sut = new AssetBundler();
        var first = CreateAsset(@"2023\Phone Images\IMG_0005.jpg", BaseTime);
        var second = CreateAsset(@"2023\Phone Images\IMG_0006.jpg", BaseTime.AddMinutes(1));

        var bundles = sut.Bundle([first, second]);

        Assert.HasCount(2, bundles);
    }

    [TestMethod]
    public void Bundle_ResultsOrderedByCapturedAtThenPath()
    {
        var sut = new AssetBundler();
        var later = CreateAsset(@"2023\Phone Images\Z.jpg", BaseTime.AddHours(2));
        var earlier = CreateAsset(@"2023\Phone Images\A.jpg", BaseTime);

        var bundles = sut.Bundle([later, earlier]);

        Assert.AreEqual(earlier.RelativePath, bundles[0].PrimaryAsset.RelativePath);
        Assert.AreEqual(later.RelativePath, bundles[1].PrimaryAsset.RelativePath);
    }

    private static MediaAsset CreateAsset(
        string relativePath,
        DateTimeOffset capturedAt,
        string? contentId = null,
        MediaKind kind = MediaKind.Image) => new()
        {
            RelativePath = relativePath,
            Year = capturedAt.Year,
            Extension = Path.GetExtension(relativePath),
            Kind = kind,
            CapturedAt = capturedAt,
            TimestampConfidence = MetadataConfidence.High,
            ContentIdentifier = contentId,
        };
}
